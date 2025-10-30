using AOSharp.Clientless.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Bankbot.Modules;
using AOSharp.Clientless;
using AOSharp.Clientless.Logging;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles organization lockout configuration from config.json
    /// </summary>
    public static class OrgLockoutConfig
    {
        private static BankbotConfig _config;
        private static string _configPath;
        private static DateTime _lastConfigLoad = DateTime.MinValue;
        private static readonly TimeSpan ConfigCacheTime = TimeSpan.FromMinutes(1); // Reload config every minute

        // Cache for player org IDs to avoid repeated requests
        private static readonly ConcurrentDictionary<int, PlayerOrgInfo> _playerOrgCache = new ConcurrentDictionary<int, PlayerOrgInfo>();
        private static readonly ConcurrentDictionary<int, TaskCompletionSource<PlayerOrgInfo>> _pendingOrgRequests = new ConcurrentDictionary<int, TaskCompletionSource<PlayerOrgInfo>>();
        private static readonly TimeSpan OrgCacheTime = TimeSpan.FromMinutes(5); // Cache org info for 5 minutes

        // Track which player requested info to correlate responses
        private static readonly ConcurrentDictionary<int, DateTime> _recentInfoRequests = new ConcurrentDictionary<int, DateTime>();

        /// <summary>
        /// Initialize the org lockout system
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // Config file is in the same directory as the DLL
                string dllDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                _configPath = Path.Combine(dllDirectory, "config.json");

                Logger.Information($"[ORG LOCKOUT] Config path: {_configPath}");

                LoadConfig();

                // Subscribe to network messages to capture InfoPacket messages with org data
                Client.MessageReceived += OnN3MessageReceived;

                Logger.Information("Org lockout system initialized with proper AOSharp org detection");
            }
            catch (Exception ex)
            {
                Logger.Information($"Error initializing org lockout: {ex.Message}");
            }
        }

        /// <summary>
        /// Load configuration from config.json
        /// </summary>
        public static void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    Logger.Information($"[ORG LOCKOUT] Config file not found, creating default: {_configPath}");
                    CreateDefaultConfig();
                    return;
                }

                // Check if we need to reload config
                if (DateTime.Now - _lastConfigLoad < ConfigCacheTime && _config != null)
                {
                    return; // Use cached config
                }

                string configJson = File.ReadAllText(_configPath);
                _config = JsonConvert.DeserializeObject<BankbotConfig>(configJson);
                _lastConfigLoad = DateTime.Now;

                Logger.Information($"[ORG LOCKOUT] Config loaded. Allowed orgs: [{string.Join(", ", _config.AllowedOrganizationIds)}]");
                Logger.Information($"[ORG LOCKOUT] Bankbot enabled: {_config.BankbotEnabled}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG LOCKOUT] Error loading config: {ex.Message}");
                CreateDefaultConfig();
            }
        }

        /// <summary>
        /// Handle N3 messages to capture InfoPacket messages with org data
        /// </summary>
        private static void OnN3MessageReceived(object sender, Message message)
        {
            try
            {
                if (message.Body is InfoPacketMessage infoMsg)
                {
                    Logger.Information($"[ORG LOCKOUT] 📨 InfoPacket received - Type: {infoMsg.Type}");

                    // Check if this is a character info packet with org data
                    if (infoMsg.Type == InfoPacketType.CharacterOrg ||
                        infoMsg.Type == InfoPacketType.CharacterOrgSite ||
                        infoMsg.Type == InfoPacketType.CharacterOrgSiteTower)
                    {
                        if (infoMsg.Info is CharacterInfoPacket charInfo)
                        {
                            Logger.Information($"[ORG LOCKOUT] 🎯 CharacterInfoPacket with org data received");
                            Logger.Information($"[ORG LOCKOUT] 📊 OrgId: {charInfo.OrganizationId}, FirstName: '{charInfo.FirstName}', LastName: '{charInfo.LastName}'");

                            if (charInfo.OrganizationId.HasValue)
                            {
                                // The InfoPacket doesn't contain the player's name, so we need to correlate
                                // it with recent requests. Find the most recent pending request.
                                var recentRequests = _recentInfoRequests
                                    .Where(kvp => DateTime.Now - kvp.Value < TimeSpan.FromSeconds(10))
                                    .OrderByDescending(kvp => kvp.Value)
                                    .ToList();

                                Logger.Information($"[ORG LOCKOUT] 🔗 Found {recentRequests.Count} recent info requests to correlate with OrgID {charInfo.OrganizationId.Value}");

                                foreach (var request in recentRequests)
                                {
                                    int playerId = request.Key;
                                    var player = DynelManager.Players.FirstOrDefault(p => p.Identity.Instance == playerId);

                                    if (player != null && _pendingOrgRequests.ContainsKey(playerId))
                                    {
                                        var orgInfo = new PlayerOrgInfo
                                        {
                                            PlayerId = playerId,
                                            OrgId = charInfo.OrganizationId.Value,
                                            OrgName = charInfo.OrganizationRank ?? "Unknown Org",
                                            LastUpdated = DateTime.Now,
                                            IsValid = true
                                        };

                                        _playerOrgCache.AddOrUpdate(playerId, orgInfo, (key, old) => orgInfo);

                                        // Complete the pending request for this player
                                        if (_pendingOrgRequests.TryRemove(playerId, out var tcs))
                                        {
                                            tcs.SetResult(orgInfo);
                                            _recentInfoRequests.TryRemove(playerId, out _);

                                            Logger.Information($"[ORG LOCKOUT] ✅ Updated org info for {player.Name} (PlayerID: {playerId}): OrgID={charInfo.OrganizationId.Value}");
                                            break; // Only complete one request per response
                                        }
                                    }
                                }

                                if (recentRequests.Count == 0)
                                {
                                    Logger.Information($"[ORG LOCKOUT] ⚠️ No recent requests to correlate with OrgID {charInfo.OrganizationId.Value}");
                                }
                            }
                            else
                            {
                                Logger.Information($"[ORG LOCKOUT] ⚠️ CharacterInfoPacket has no OrganizationId");
                            }
                        }
                        else
                        {
                            Logger.Information($"[ORG LOCKOUT] ⚠️ InfoPacket.Info is not CharacterInfoPacket: {infoMsg.Info?.GetType().Name ?? "null"}");
                        }
                    }
                }
                else if (message.Body is OrgInfoPacketMessage orgPacket)
                {
                    Logger.Information($"[ORG LOCKOUT] 🏢 OrgInfoPacket received - OrgId: {orgPacket.OrgId}, Name: '{orgPacket.Name}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG LOCKOUT] ❌ Error handling N3 message: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle org info responses from the server (legacy method)
        /// </summary>
        private static void OnOrgInfoReceived(OrganizationInfo orgInfo)
        {
            try
            {
                // This method is kept for compatibility but the SimpleCharFullUpdate method above is more reliable
                Logger.Information($"[ORG LOCKOUT] Received org info response: {orgInfo.OrganizationName}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG LOCKOUT] Error handling org info response: {ex.Message}");
            }
        }

        /// <summary>
        /// Create default configuration file
        /// </summary>
        private static void CreateDefaultConfig()
        {
            try
            {
                _config = new BankbotConfig
                {
                    AllowedOrganizationIds = new List<int> { 0 }, // 0 = allow all orgs by default
                    BankbotEnabled = true,
                    LogAllTransactions = true,
                    LogFormat = "{Time} - {PlayerName} - {ItemName}"
                };

                string configJson = JsonConvert.SerializeObject(_config, Formatting.Indented);
                File.WriteAllText(_configPath, configJson);

                Logger.Information($"[ORG LOCKOUT] Created default config file: {_configPath}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG LOCKOUT] Error creating default config: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a player's organization is allowed to trade (async version)
        /// </summary>
        /// <param name="playerId">Player ID to check</param>
        /// <param name="playerName">Player name for logging</param>
        /// <returns>True if allowed, false if blocked</returns>
        public static async Task<bool> IsPlayerOrgAllowedAsync(int playerId, string playerName)
        {
            try
            {
                LoadConfig(); // Refresh config if needed

                if (_config == null || !_config.BankbotEnabled)
                {
                    Logger.Information($"[ORG LOCKOUT] Bankbot disabled, declining trade from {playerName}");
                    return false;
                }

                // If org ID 0 is in the list, allow all organizations
                if (_config.AllowedOrganizationIds.Contains(0))
                {
                    Logger.Information($"[ORG LOCKOUT] Org ID 0 in allowed list - allowing all orgs for {playerName}");
                    return true;
                }

                // Get player org info (with caching and async request)
                var orgInfo = await GetPlayerOrgInfoAsync(playerId, playerName);
                if (orgInfo == null || !orgInfo.IsValid)
                {
                    Logger.Information($"[ORG LOCKOUT] Could not get org info for {playerName}, denying access");
                    return false;
                }

                bool isAllowed = _config.AllowedOrganizationIds.Contains(orgInfo.OrgId);
                Logger.Information($"[ORG LOCKOUT] Player {playerName} org ID: {orgInfo.OrgId} ({orgInfo.OrgName}), allowed: {isAllowed}");

                return isAllowed;
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG LOCKOUT] Error checking org for {playerName}: {ex.Message}");
                return false; // Default to deny on error
            }
        }

        /// <summary>
        /// Synchronous wrapper for org checking (for backward compatibility)
        /// </summary>
        /// <param name="playerId">Player ID to check</param>
        /// <param name="playerName">Player name for logging</param>
        /// <returns>True if allowed, false if blocked</returns>
        public static bool IsPlayerOrgAllowed(int playerId, string playerName)
        {
            try
            {
                Logger.Information($"[ORG LOCKOUT] 🔍 Checking org access for {playerName} (PlayerID: {playerId})");

                // For immediate checks, use cached data if available
                if (_playerOrgCache.TryGetValue(playerId, out var cachedInfo))
                {
                    Logger.Information($"[ORG LOCKOUT] 📋 Found cached info for {playerName}: OrgID={cachedInfo.OrgId}, OrgName='{cachedInfo.OrgName}', Age={(DateTime.Now - cachedInfo.LastUpdated).TotalMinutes:F1}min");

                    if (DateTime.Now - cachedInfo.LastUpdated < OrgCacheTime && cachedInfo.IsValid)
                    {
                        LoadConfig();
                        if (_config?.AllowedOrganizationIds.Contains(0) == true)
                        {
                            Logger.Information($"[ORG LOCKOUT] ✅ Allowing {playerName} - org ID 0 in allowed list (allow all)");
                            return true;
                        }

                        bool isAllowed = _config?.AllowedOrganizationIds.Contains(cachedInfo.OrgId) == true;
                        Logger.Information($"[ORG LOCKOUT] 🎯 Player {playerName} org ID: {cachedInfo.OrgId} ({cachedInfo.OrgName}) - allowed: {isAllowed}");
                        Logger.Information($"[ORG LOCKOUT] 📝 Allowed org IDs: [{string.Join(", ", _config?.AllowedOrganizationIds ?? new List<int>())}]");
                        return isAllowed;
                    }
                    else
                    {
                        Logger.Information($"[ORG LOCKOUT] ⏰ Cached info for {playerName} is stale or invalid");
                    }
                }
                else
                {
                    Logger.Information($"[ORG LOCKOUT] ❌ No cached org info found for {playerName} (PlayerID: {playerId})");
                }

                // If no cached data, try to get org info with a short wait
                Logger.Information($"[ORG LOCKOUT] No cached org info for {playerName}, attempting to retrieve...");

                try
                {
                    // Wait up to 3 seconds for org info to be retrieved
                    var orgInfoTask = GetPlayerOrgInfoAsync(playerId, playerName);
                    if (orgInfoTask.Wait(3000)) // 3 second timeout
                    {
                        var orgInfo = orgInfoTask.Result;
                        if (orgInfo != null && orgInfo.IsValid)
                        {
                            LoadConfig();
                            bool isAllowed = _config?.AllowedOrganizationIds.Contains(orgInfo.OrgId) == true;
                            Logger.Information($"[ORG LOCKOUT] Retrieved org info for {playerName}: OrgID={orgInfo.OrgId} ({orgInfo.OrgName}), allowed: {isAllowed}");
                            return isAllowed;
                        }
                    }
                    else
                    {
                        Logger.Information($"[ORG LOCKOUT] Timeout waiting for org info for {playerName}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Information($"[ORG LOCKOUT] Error waiting for org info for {playerName}: {ex.Message}");
                }

                LoadConfig();
                // Default to allowing if org ID 0 is in the list, otherwise deny for safety
                if (_config?.AllowedOrganizationIds.Contains(0) == true)
                {
                    Logger.Information($"[ORG LOCKOUT] ⚠️ Org ID 0 in allowed list - allowing {playerName} (org info failed/timeout)");
                    return true;
                }

                Logger.Information($"[ORG LOCKOUT] 🚫 Could not get org info for {playerName}, denying access for safety");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG LOCKOUT] ❌ Error in sync org check for {playerName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get player organization information with caching
        /// </summary>
        private static async Task<PlayerOrgInfo> GetPlayerOrgInfoAsync(int playerId, string playerName)
        {
            try
            {
                Logger.Information($"[ORG LOCKOUT] 🔍 GetPlayerOrgInfoAsync called for {playerName} (PlayerID: {playerId})");

                // Check cache first
                if (_playerOrgCache.TryGetValue(playerId, out var cachedInfo))
                {
                    if (DateTime.Now - cachedInfo.LastUpdated < OrgCacheTime && cachedInfo.IsValid)
                    {
                        Logger.Information($"[ORG LOCKOUT] 📋 Using cached info for {playerName}");
                        return cachedInfo;
                    }
                }

                // Check if there's already a pending request
                if (_pendingOrgRequests.TryGetValue(playerId, out var existingTcs))
                {
                    Logger.Information($"[ORG LOCKOUT] ⏳ Waiting for existing request for {playerName}");
                    return await existingTcs.Task;
                }

                // Find the player
                var player = DynelManager.Players.FirstOrDefault(p => p.Identity.Instance == playerId);
                if (player == null)
                {
                    Logger.Information($"[ORG LOCKOUT] ❌ Player {playerName} not found for org lookup");
                    return new PlayerOrgInfo { PlayerId = playerId, IsValid = false };
                }

                // Create new request
                var tcs = new TaskCompletionSource<PlayerOrgInfo>();
                _pendingOrgRequests.TryAdd(playerId, tcs);

                // Track this request so we can correlate the response
                _recentInfoRequests.AddOrUpdate(playerId, DateTime.Now, (key, old) => DateTime.Now);

                // Request fresh player data by requesting character info (which includes org data)
                Logger.Information($"[ORG LOCKOUT] 📡 Requesting character info for {playerName} (PlayerID: {playerId})");
                Client.InfoRequest(player.Identity);

                // Wait for response with timeout
                var timeoutTask = Task.Delay(5000); // 5 second timeout
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // Timeout - clean up and return invalid info
                    _pendingOrgRequests.TryRemove(playerId, out _);
                    Logger.Information($"[ORG LOCKOUT] ⏰ Timeout waiting for org info for {playerName}");
                    return new PlayerOrgInfo { PlayerId = playerId, IsValid = false };
                }

                var result = await tcs.Task;
                Logger.Information($"[ORG LOCKOUT] ✅ Successfully got org info for {playerName}: OrgID={result.OrgId}, OrgName='{result.OrgName}'");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG LOCKOUT] ❌ Error getting org info for {playerName}: {ex.Message}");
                _pendingOrgRequests.TryRemove(playerId, out _);
                return new PlayerOrgInfo { PlayerId = playerId, IsValid = false };
            }
        }

        /// <summary>
        /// Get current configuration for debugging
        /// </summary>
        public static string GetConfigInfo()
        {
            try
            {
                LoadConfig();
                if (_config == null)
                    return "Config not loaded";

                return $"Enabled: {_config.BankbotEnabled}, Allowed Orgs: [{string.Join(", ", _config.AllowedOrganizationIds)}]";
            }
            catch (Exception ex)
            {
                return $"Error getting config: {ex.Message}";
            }
        }

        /// <summary>
        /// Get current configuration object
        /// </summary>
        public static BankbotConfig GetConfig()
        {
            LoadConfig();
            return _config;
        }

        /// <summary>
        /// Get cached org info for a player (for debugging)
        /// </summary>
        public static PlayerOrgInfo GetCachedOrgInfo(int playerId)
        {
            _playerOrgCache.TryGetValue(playerId, out var info);
            return info;
        }

        /// <summary>
        /// Clear org cache for a specific player
        /// </summary>
        public static void ClearPlayerOrgCache(int playerId)
        {
            _playerOrgCache.TryRemove(playerId, out _);
            _pendingOrgRequests.TryRemove(playerId, out _);
        }

        /// <summary>
        /// Clear all org cache
        /// </summary>
        public static void ClearAllOrgCache()
        {
            _playerOrgCache.Clear();
            _pendingOrgRequests.Clear();
        }

        /// <summary>
        /// Manually refresh org info for all nearby players
        /// </summary>
        public static void RefreshNearbyPlayersOrgInfo()
        {
            try
            {
                var nearbyPlayers = DynelManager.Players.Where(p =>
                    p.DistanceFrom(DynelManager.LocalPlayer) <= 50f).ToList();

                Logger.Information($"[ORG LOCKOUT] 🔄 Found {nearbyPlayers.Count} nearby players to refresh");

                foreach (var player in nearbyPlayers)
                {
                    // Track this request so we can correlate the response
                    _recentInfoRequests.AddOrUpdate(player.Identity.Instance, DateTime.Now, (key, old) => DateTime.Now);

                    // Request character info (which includes org data) for each nearby player
                    // CharacterAction.InfoRequest(player.Identity);
                    Logger.Information($"[ORG LOCKOUT] 📡 Requesting character info for: {player.Name} (PlayerID: {player.Identity.Instance})");
                }

                Logger.Information($"[ORG LOCKOUT] ✅ Requested org refresh for {nearbyPlayers.Count} nearby players");
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG LOCKOUT] ❌ Error refreshing nearby players org info: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug method to show all cached org info
        /// </summary>
        public static string GetAllCachedOrgInfo()
        {
            try
            {
                if (_playerOrgCache.IsEmpty)
                {
                    return "No cached org information available.";
                }

                var result = "=== CACHED ORG INFORMATION ===\n";
                foreach (var kvp in _playerOrgCache)
                {
                    var info = kvp.Value;
                    var age = (DateTime.Now - info.LastUpdated).TotalMinutes;
                    result += $"PlayerID: {info.PlayerId} | OrgID: {info.OrgId} | OrgName: '{info.OrgName}' | Age: {age:F1}min | Valid: {info.IsValid}\n";
                }
                return result;
            }
            catch (Exception ex)
            {
                return $"Error getting cached org info: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Cached player organization information
    /// </summary>
    public class PlayerOrgInfo
    {
        public int PlayerId { get; set; }
        public int OrgId { get; set; }
        public string OrgName { get; set; } = "Unknown";
        public DateTime LastUpdated { get; set; }
        public bool IsValid { get; set; }
    }

    /// <summary>
    /// Configuration structure for bankbot
    /// </summary>
    public class BankbotConfig
    {
        public List<int> AllowedOrganizationIds { get; set; } = new List<int>();
        public bool BankbotEnabled { get; set; } = true;
        public bool LogAllTransactions { get; set; } = true;
        public string LogFormat { get; set; } = "{Time} - {PlayerName} - {ItemName}";
    }
}
