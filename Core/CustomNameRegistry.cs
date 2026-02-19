using AOSharp.Clientless;
using AOSharp.Clientless.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Bankbot.Core
{
    /// <summary>
    /// Intercepts SetName N3 packets from raw bytes and maintains a mapping
    /// of item Instance -> custom name. Persists to custom_names.json.
    /// </summary>
    public static class CustomNameRegistry
    {
        private static ConcurrentDictionary<int, string> _customNames = new ConcurrentDictionary<int, string>();
        private static string _persistencePath;
        private static bool _initialized = false;

        // N3MessageType.SetName = 1934514811 (0x734E616D)
        private const int SetNameN3Type = 1934514811;

        public static void Initialize()
        {
            if (_initialized) return;

            _persistencePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_names.json");
            LoadFromDisk();

            Client.RawPacketReceived += OnRawPacketReceived;

            _initialized = true;
            Logger.Information($"[CUSTOM NAMES] Initialized with {_customNames.Count} persisted names");
        }

        /// <summary>
        /// Get the custom name for an item instance, or null if none set
        /// </summary>
        public static string GetCustomName(int instance)
        {
            return _customNames.TryGetValue(instance, out string name) ? name : null;
        }

        /// <summary>
        /// Get custom name if set, otherwise return the default name
        /// </summary>
        public static string GetDisplayName(int instance, string defaultName)
        {
            return _customNames.TryGetValue(instance, out string name) ? name : defaultName;
        }

        /// <summary>
        /// Manually set a custom name (for the bot command)
        /// </summary>
        public static void SetCustomName(int instance, string name)
        {
            _customNames[instance] = name;
            SaveToDisk();
            InvalidateItemCaches();
            Logger.Information($"[CUSTOM NAMES] Manually set name for instance {instance}: '{name}'");
        }

        /// <summary>
        /// Remove a custom name
        /// </summary>
        public static bool RemoveCustomName(int instance)
        {
            bool removed = _customNames.TryRemove(instance, out _);
            if (removed)
            {
                SaveToDisk();
                InvalidateItemCaches();
                Logger.Information($"[CUSTOM NAMES] Removed custom name for instance {instance}");
            }
            return removed;
        }

        /// <summary>
        /// Get all custom name entries (instance -> name)
        /// </summary>
        public static Dictionary<int, string> GetAllCustomNames()
        {
            return new Dictionary<int, string>(_customNames);
        }

        /// <summary>
        /// Check if a search term matches any custom name, return matching instance or -1
        /// </summary>
        public static int FindInstanceByCustomName(string searchName)
        {
            foreach (var kvp in _customNames)
            {
                if (kvp.Value.Equals(searchName, StringComparison.OrdinalIgnoreCase))
                    return kvp.Key;
            }
            return -1;
        }

        private static void OnRawPacketReceived(object sender, byte[] packet)
        {
            try
            {
                // Minimum packet size: 16 header + 4 N3Type + 8 Identity + 1 unknown + 2 name length = 31
                if (packet == null || packet.Length < 31)
                    return;

                // Check if this is an N3 message by looking at packet type in header
                // Header layout: type(2) + unknown(2) + size(2) + sender(4) + receiver(4) + messageId(2) = 16 bytes
                // PacketType for N3Message is 0x0005
                ushort packetType = (ushort)((packet[0] << 8) | packet[1]);
                if (packetType != 0x0005)
                    return;

                // N3 body starts at offset 16 (after the header)
                // N3MessageType is at bytes 16-19
                // Try big-endian first (network byte order)
                int n3TypeBE = (packet[16] << 24) | (packet[17] << 16) | (packet[18] << 8) | packet[19];
                // Also try little-endian
                int n3TypeLE = packet[16] | (packet[17] << 8) | (packet[18] << 16) | (packet[19] << 24);

                bool isBigEndian = (n3TypeBE == SetNameN3Type);
                bool isLittleEndian = (n3TypeLE == SetNameN3Type);

                if (!isBigEndian && !isLittleEndian)
                    return;

                Logger.Information($"[CUSTOM NAMES] Detected SetName packet! Length={packet.Length}, Endian={( isBigEndian ? "BE" : "LE")}");

                // Log raw bytes for diagnostic validation
                LogDiagnosticBytes(packet);

                // Parse Identity (type + instance)
                // Identity starts at offset 20
                int identityType, identityInstance;
                if (isBigEndian)
                {
                    identityType = (packet[20] << 24) | (packet[21] << 16) | (packet[22] << 8) | packet[23];
                    identityInstance = (packet[24] << 24) | (packet[25] << 16) | (packet[26] << 8) | packet[27];
                }
                else
                {
                    identityType = packet[20] | (packet[21] << 8) | (packet[22] << 16) | (packet[23] << 24);
                    identityInstance = packet[24] | (packet[25] << 8) | (packet[26] << 16) | (packet[27] << 24);
                }

                Logger.Information($"[CUSTOM NAMES] Identity: Type={identityType}, Instance={identityInstance}");

                // Skip unknown byte at offset 28
                // Name string length at offset 29 (ushort)
                if (packet.Length < 31)
                    return;

                ushort nameLength;
                if (isBigEndian)
                {
                    nameLength = (ushort)((packet[29] << 8) | packet[30]);
                }
                else
                {
                    nameLength = (ushort)(packet[29] | (packet[30] << 8));
                }

                Logger.Information($"[CUSTOM NAMES] Name length: {nameLength}");

                if (nameLength == 0 || packet.Length < 31 + nameLength)
                {
                    Logger.Information($"[CUSTOM NAMES] Invalid name length {nameLength} for packet of size {packet.Length}");
                    return;
                }

                // Extract name string (UTF-8)
                string customName = Encoding.UTF8.GetString(packet, 31, nameLength);

                Logger.Information($"[CUSTOM NAMES] Captured custom name: Instance={identityInstance}, Name='{customName}'");

                // Store the mapping
                _customNames[identityInstance] = customName;
                SaveToDisk();
                InvalidateItemCaches();
            }
            catch (Exception ex)
            {
                Logger.Information($"[CUSTOM NAMES] Error parsing raw packet: {ex.Message}");
            }
        }

        private static void LogDiagnosticBytes(byte[] packet)
        {
            // Log bytes at key offsets for first-run validation
            var sb = new StringBuilder("[CUSTOM NAMES] Diagnostic bytes: ");
            int maxLog = Math.Min(packet.Length, 50);
            for (int i = 16; i < maxLog; i++)
            {
                sb.Append($"[{i}]=0x{packet[i]:X2} ");
            }
            Logger.Information(sb.ToString());
        }

        private static void InvalidateItemCaches()
        {
            try
            {
                ItemTracker.InvalidateStoredItemsCache();
            }
            catch
            {
                // ItemTracker may not be initialized yet
            }
        }

        private static void LoadFromDisk()
        {
            try
            {
                if (File.Exists(_persistencePath))
                {
                    string json = File.ReadAllText(_persistencePath);
                    var data = JsonConvert.DeserializeObject<Dictionary<int, string>>(json);
                    if (data != null)
                    {
                        _customNames = new ConcurrentDictionary<int, string>(data);
                        Logger.Information($"[CUSTOM NAMES] Loaded {_customNames.Count} names from {_persistencePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[CUSTOM NAMES] Error loading from disk: {ex.Message}");
            }
        }

        private static void SaveToDisk()
        {
            try
            {
                string json = JsonConvert.SerializeObject(new Dictionary<int, string>(_customNames), Formatting.Indented);
                File.WriteAllText(_persistencePath, json);
            }
            catch (Exception ex)
            {
                Logger.Information($"[CUSTOM NAMES] Error saving to disk: {ex.Message}");
            }
        }
    }
}
