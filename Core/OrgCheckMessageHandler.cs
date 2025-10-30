using AOSharp.Clientless.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Clientless;
using Bankbot.Modules;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles orgcheck command messages for debugging organization lockout
    /// </summary>
    public class OrgCheckMessageHandler : IMessageHandler
    {
        public void HandleMessage(string senderName, MessageInfo messageInfo)
        {
            // Handle async operation
            _ = Task.Run(async () => await HandleMessageAsync(senderName, messageInfo));
        }

        private async Task HandleMessageAsync(string senderName, MessageInfo messageInfo)
        {
            try
            {
                Logger.Information($"[ORG CHECK] Processing orgcheck request from {senderName}");

                // Find the player
                var player = DynelManager.Players.FirstOrDefault(p =>
                    p.Name.Equals(senderName, StringComparison.OrdinalIgnoreCase));

                if (player == null)
                {
                    SendPrivateMessage(senderName, "Error: Could not find your player data. Please come closer to the bot and try again.");
                    return;
                }

                int playerId = player.Identity.Instance;

                // Send initial response
                SendPrivateMessage(senderName, "Checking your organization information, please wait...");

                // Get cached org info first
                var cachedOrgInfo = OrgLockoutConfig.GetCachedOrgInfo(playerId);

                // If no cached info, try to refresh nearby players first
                if (cachedOrgInfo == null || !cachedOrgInfo.IsValid)
                {
                    SendPrivateMessage(senderName, "No cached org data found, requesting fresh data...");
                    OrgLockoutConfig.RefreshNearbyPlayersOrgInfo();
                    await Task.Delay(1000); // Give it a moment to process
                }

                // Get fresh org info (this will use cache if recent, or request new data)
                bool isAllowed = await OrgLockoutConfig.IsPlayerOrgAllowedAsync(playerId, senderName);

                // Get updated cached info after the async call
                var orgInfo = OrgLockoutConfig.GetCachedOrgInfo(playerId);

                // Get config info
                string configInfo = OrgLockoutConfig.GetConfigInfo();

                // Build response
                string response = $"=== ORG CHECK RESULTS ===\n";
                response += $"Player: {senderName}\n";
                response += $"Player ID: {playerId}\n";

                if (orgInfo != null && orgInfo.IsValid)
                {
                    response += $"Organization: {orgInfo.OrgName}\n";
                    response += $"Org ID: {orgInfo.OrgId}\n";
                    response += $"Cache Age: {(DateTime.Now - orgInfo.LastUpdated).TotalMinutes:F1} minutes\n";
                }
                else
                {
                    response += $"Organization: Unable to retrieve\n";
                    response += $"Org ID: Unknown\n";
                    response += $"Status: Org info request failed or timed out\n";
                }

                response += $"Access Allowed: {(isAllowed ? "YES" : "NO")}\n";
                response += $"Config: {configInfo}\n";

                // Add helpful info about multiple org support
                response += $"\nNOTE: You can configure multiple allowed org IDs in config.json\n";
                response += $"Example: \"AllowedOrganizationIds\": [12345, 67890, 0]\n";
                response += $"Use 0 to allow all organizations.\n\n";

                // Add debug info about all cached org data
                response += $"=== DEBUG: ALL CACHED ORG DATA ===\n";
                response += OrgLockoutConfig.GetAllCachedOrgInfo();

                SendPrivateMessage(senderName, response);

                string logOrgInfo = orgInfo?.IsValid == true ? $"OrgID={orgInfo.OrgId} ({orgInfo.OrgName})" : "OrgID=Unknown";
                Logger.Information($"[ORG CHECK] Sent org info to {senderName}: {logOrgInfo}, Allowed={isAllowed}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[ORG CHECK] Error processing orgcheck from {senderName}: {ex.Message}");
                SendPrivateMessage(senderName, "Error checking organization status: " + ex.Message);
            }
        }

        private static void SendPrivateMessage(string playerName, string message)
        {
            try
            {
                var targetPlayer = DynelManager.Players
                    .FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

                if (targetPlayer != null)
                {
                    Client.SendPrivateMessage((uint)targetPlayer.Identity.Instance, message);
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"Error sending private message: {ex.Message}");
            }
        }
    }
}
