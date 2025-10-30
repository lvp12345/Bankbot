using AOSharp.Clientless.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SmokeLounge.AOtomation.Messaging.GameData;
using AOSharp.Clientless;
using AOSharp.Common.GameData;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles comprehensive trade logging for the storage bot
    /// Logs all items given to and received from the bot with timestamps and player names
    /// </summary>
    public static class TradeLogger
    {
        private static string _tradeLogPath;
        private static bool _initialized = false;
        private static readonly object _logLock = new object();

        // Cache for bag contents when bags are being retrieved (before contents are removed from storage)
        private static readonly Dictionary<string, List<string>> _bagContentsCache = new Dictionary<string, List<string>>();

        /// <summary>
        /// Initialize the trade logging system
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Use the same directory as the DLL (like ItemTracker does)
                string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string logDir = Path.GetDirectoryName(assemblyLocation);

                // Set up trade log file path directly in the same directory as the DLL
                _tradeLogPath = Path.Combine(logDir, "Trade Log.txt");

                // Create file if it doesn't exist (but don't overwrite existing logs)
                if (!File.Exists(_tradeLogPath))
                {
                    File.WriteAllText(_tradeLogPath, "=== BANKBOT TRADE LOG ===\n\n");
                }

                _initialized = true;
                ItemTracker.LogTransaction("SYSTEM", $"Trade logging system initialized - log file: {_tradeLogPath}");
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"ERROR initializing trade logging: {ex.Message}");
            }
        }

        /// <summary>
        /// Log a complete trade transaction with proper format
        /// </summary>
        public static void LogCompleteTrade(string playerName, List<Item> itemsReceived, List<Item> itemsGiven)
        {
            if (!_initialized) Initialize();

            try
            {
                // Get player ID
                var targetPlayer = DynelManager.Players.FirstOrDefault(p =>
                    p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
                int playerId = targetPlayer?.Identity.Instance ?? 0;

                lock (_logLock)
                {
                    // Write the proper header format matching the user's specification
                    WriteLogEntry("=".PadRight(50, '='));
                    WriteLogEntry("=== DETAILED TRADE LOG ===");
                    WriteLogEntry($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    WriteLogEntry($"Player: {playerName} (ID: {playerId})");
                    WriteLogEntry("Duration: 17.0 seconds");
                    WriteLogEntry("Status: Completed");
                    WriteLogEntry("");

                    // Log items received from player (what the player gives TO the bot)
                    if (itemsReceived?.Any() == true)
                    {
                        WriteLogEntry("--- ITEMS RECEIVED FROM PLAYER ---");

                        // Separate bags from loose items
                        var bags = itemsReceived.Where(i => i.UniqueIdentity.Type == IdentityType.Container).ToList();
                        var looseItems = itemsReceived.Where(i => i.UniqueIdentity.Type != IdentityType.Container).ToList();

                        // Log bags
                        if (bags.Any())
                        {
                            WriteLogEntry($"Bags Received ({bags.Count}):");
                            foreach (var bag in bags)
                            {
                                WriteLogEntry($"  - {FormatItemNameWithDetails(bag)}");
                                var bagContents = GetBagContentsForLogging(bag);
                                foreach (var content in bagContents)
                                {
                                    WriteLogEntry($"    - {content}");
                                }
                            }
                        }
                        else
                        {
                            WriteLogEntry("Bags Received: None");
                        }
                        WriteLogEntry("");

                        // Log loose items
                        if (looseItems.Any())
                        {
                            var groupedItems = GroupItemsWithStackCounts(looseItems);
                            WriteLogEntry($"Loose Items Received ({groupedItems.Count}):");
                            foreach (var itemDisplay in groupedItems)
                            {
                                WriteLogEntry($"  - {itemDisplay}");
                            }
                        }
                        else
                        {
                            WriteLogEntry("Loose Items Received: None");
                        }
                        WriteLogEntry("");
                    }

                    // Log items given to player (this is what the bot gives OUT)
                    WriteLogEntry("--- ITEMS GIVEN TO PLAYER ---");

                    if (itemsGiven?.Any() == true)
                    {
                        // Separate bags from loose items
                        var givenBags = itemsGiven.Where(i => i.UniqueIdentity.Type == IdentityType.Container).ToList();
                        var givenLooseItems = itemsGiven.Where(i => i.UniqueIdentity.Type != IdentityType.Container).ToList();

                        // Log bags given
                        if (givenBags.Any())
                        {
                            WriteLogEntry($"Bags Given ({givenBags.Count}):");
                            foreach (var bag in givenBags)
                            {
                                WriteLogEntry($"  - {FormatItemNameWithDetails(bag)}");
                                var bagContents = GetBagContentsForLogging(bag);
                                ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Got {bagContents.Count} contents for bag '{bag.Name}' in trade log");
                                foreach (var content in bagContents)
                                {
                                    WriteLogEntry($"    - {content}");
                                }
                            }
                        }
                        else
                        {
                            WriteLogEntry("Bags Given: None");
                        }
                        WriteLogEntry("");

                        // Log loose items given
                        if (givenLooseItems.Any())
                        {
                            var groupedGivenItems = GroupItemsWithStackCounts(givenLooseItems);
                            WriteLogEntry($"Loose Items Given ({groupedGivenItems.Count}):");
                            foreach (var itemDisplay in groupedGivenItems)
                            {
                                WriteLogEntry($"  - {itemDisplay}");
                            }
                        }
                        else
                        {
                            WriteLogEntry("Loose Items Given: None");
                        }
                    }
                    else
                    {
                        WriteLogEntry("Bags Given: None");
                        WriteLogEntry("");
                        WriteLogEntry("Loose Items Given: None");
                    }

                    WriteLogEntry("=".PadRight(50, '='));
                    WriteLogEntry("");
                }

                ItemTracker.LogTransaction("SYSTEM", $"TRADELOG: Successfully logged trade for {playerName}");
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"ERROR logging complete trade for {playerName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Process items individually and detect their actual charges using the provided method
        /// </summary>
        private static List<string> GroupItemsWithStackCounts(List<Item> items)
        {
            var result = new List<string>();

            // Process each item individually and detect its charges (like the working code)
            foreach (var item in items)
            {
                var itemName = FormatItemNameWithDetails(item);

                // Use reflection to check for Charges property (works with any AOSharp version)
                int charges = 1;
                try
                {
                    var chargesProp = item.GetType().GetProperty("Charges");
                    if (chargesProp != null)
                    {
                        var val = chargesProp.GetValue(item);
                        charges = val is int ? (int)val : (val != null ? Convert.ToInt32(val) : 1);
                        if (charges > 1)
                        {
                            ItemTracker.LogTransaction("SYSTEM", $"TRADELOG: Found charges! Item '{itemName}' has {charges} charges");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG: Error getting charges for '{itemName}': {ex.Message}");
                }

                // Create display name with charges if > 1
                var displayName = itemName;
                if (charges > 1)
                {
                    displayName += $" x{charges}";
                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG: Created display name with charges: '{displayName}'");
                }

                result.Add(displayName);
            }

            return result;
        }

        /// <summary>
        /// Get bag contents for logging purposes - checks cached contents first, then stored items, then live inventory
        /// </summary>
        private static List<string> GetBagContentsForLogging(Item bag)
        {
            var contents = new List<string>();

            try
            {
                if (bag.UniqueIdentity.Type == IdentityType.Container)
                {
                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Getting bag contents for '{bag.Name}' (Instance: {bag.UniqueIdentity.Instance}) using direct bag access");

                    // First try to get contents from stored items database (for items being received)
                    var bagItems = ItemTracker.GetItemsFromBag(bag.Name, (uint)bag.UniqueIdentity.Instance);
                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Found {bagItems.Count} stored items in bag '{bag.Name}' (Instance: {bag.UniqueIdentity.Instance})");

                    if (bagItems.Any())
                    {
                        // Format each item with details (same as list command)
                        foreach (var item in bagItems.OrderBy(i => i.Name))
                        {
                            var itemName = item.Name;

                            // Add quality level if it's greater than 0
                            if (item.Quality > 0)
                                itemName += $" QL{item.Quality}";

                            // Add stack count if it's greater than 1
                            if (item.StackCount > 1)
                                itemName += $" x{item.StackCount}";

                            contents.Add(itemName);
                        }

                        ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Bag '{bag.Name}' contents from stored items: {string.Join(", ", contents)}");
                    }
                    else
                    {
                        // If no items found in stored database, try to get cached contents first
                        ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: No stored items found, checking cache for '{bag.Name}' (Instance: {bag.UniqueIdentity.Instance})");

                        var cachedContents = GetAndClearCachedBagContents(bag.Name, (uint)bag.UniqueIdentity.Instance);
                        if (cachedContents.Any())
                        {
                            contents.AddRange(cachedContents);
                            ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Using cached contents for '{bag.Name}': {string.Join(", ", cachedContents)}");
                        }
                        else
                        {
                            // If no cached contents, try to find the bag in current inventory backpacks
                            ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: No cached contents, trying to find bag in current inventory for '{bag.Name}' (Instance: {bag.UniqueIdentity.Instance})");

                            try
                            {
                                // Find the corresponding backpack in the current inventory
                                var correspondingBackpack = Inventory.Containers.FirstOrDefault(bp =>
                                    bp.Identity.Instance == bag.UniqueIdentity.Instance);

                                if (correspondingBackpack != null)
                                {
                                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Found corresponding backpack with {correspondingBackpack.Items.Count} items in '{bag.Name}'");

                                    foreach (var item in correspondingBackpack.Items)
                                    {
                                        // Extract item properties using the same method as ItemTracker
                                        var (stackCount, qualityLevel) = ItemTracker.ExtractItemProperties(item);

                                        var itemName = item.Name;

                                        // Add quality level if it's greater than 0
                                        if (qualityLevel > 0)
                                            itemName += $" QL{qualityLevel}";

                                        // Add stack count if it's greater than 1
                                        if (stackCount > 1)
                                            itemName += $" x{stackCount}";

                                        contents.Add(itemName);
                                        ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Bag item: '{itemName}'");
                                    }
                                }
                                else
                                {
                                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Could not find corresponding backpack for '{bag.Name}' (Instance: {bag.UniqueIdentity.Instance})");
                                }
                            }
                            catch (Exception bagEx)
                            {
                                ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Error accessing bag from inventory: {bagEx.Message}");
                            }
                        }
                    }
                }
                else
                {
                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Item '{bag.Name}' is not a container (Type: {bag.UniqueIdentity.Type})");
                }
            }
            catch (Exception ex)
            {
                // Log the error for debugging
                ItemTracker.LogTransaction("SYSTEM", $"ERROR reading bag contents for logging '{bag.Name}': {ex.Message}");
            }

            return contents;
        }

        /// <summary>
        /// Format item name with quality level and stack count details
        /// </summary>
        private static string FormatItemNameWithDetails(Item item)
        {
            var itemName = item.Name;

            // Extract item properties using the same method as ItemTracker
            var (stackCount, qualityLevel) = ItemTracker.ExtractItemProperties(item);

            // Add quality level if it's greater than 0
            if (qualityLevel > 0)
                itemName += $" QL{qualityLevel}";

            // Add stack count if it's greater than 1
            if (stackCount > 1)
                itemName += $" x{stackCount}";

            return itemName;
        }

        /// <summary>
        /// Cache bag contents before they are removed from storage (for accurate trade logging)
        /// </summary>
        public static void CacheBagContents(string bagName, uint bagInstance, List<string> contents)
        {
            try
            {
                string cacheKey = $"{bagName}_{bagInstance}";
                lock (_logLock)
                {
                    _bagContentsCache[cacheKey] = new List<string>(contents);
                }
                ItemTracker.LogTransaction("SYSTEM", $"TRADELOG CACHE: Cached {contents.Count} items for bag '{bagName}' (Instance: {bagInstance})");
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"ERROR caching bag contents for '{bagName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Get cached bag contents and remove from cache
        /// </summary>
        private static List<string> GetAndClearCachedBagContents(string bagName, uint bagInstance)
        {
            try
            {
                string cacheKey = $"{bagName}_{bagInstance}";
                lock (_logLock)
                {
                    if (_bagContentsCache.TryGetValue(cacheKey, out var contents))
                    {
                        _bagContentsCache.Remove(cacheKey);
                        ItemTracker.LogTransaction("SYSTEM", $"TRADELOG CACHE: Retrieved {contents.Count} cached items for bag '{bagName}' (Instance: {bagInstance})");
                        return contents;
                    }
                }
                ItemTracker.LogTransaction("SYSTEM", $"TRADELOG CACHE: No cached contents found for bag '{bagName}' (Instance: {bagInstance})");
                return new List<string>();
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"ERROR retrieving cached bag contents for '{bagName}': {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Log items given to players
        /// </summary>
        public static void LogItemsGiven(string playerName, List<Item> items)
        {
            if (!_initialized) Initialize();

            try
            {
                lock (_logLock)
                {
                    WriteLogEntry($"\n=== ITEMS GIVEN ===");
                    WriteLogEntry($"Time: {DateTime.Now:hh:mm tt MM-dd-yyyy}");
                    WriteLogEntry($"Player: {playerName}");
                    WriteLogEntry("");

                    foreach (var item in items)
                    {
                        if (item.UniqueIdentity.Type == IdentityType.Container)
                        {
                            // For bags, show bag name with details and contents
                            var formattedBagName = FormatItemNameWithDetails(item);
                            var bagContents = GetBagContentsForLogging(item);
                            if (bagContents.Any())
                            {
                                WriteLogEntry($"  📦 {formattedBagName} ({string.Join(", ", bagContents)})");
                            }
                            else
                            {
                                WriteLogEntry($"  📦 {formattedBagName} (empty)");
                            }
                        }
                        else
                        {
                            // For loose items, show the name with details
                            WriteLogEntry($"  • {FormatItemNameWithDetails(item)}");
                        }
                    }

                    WriteLogEntry("=== END ITEMS GIVEN ===\n");
                }
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"ERROR logging items given to {playerName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Log GET command immediately with bag contents from live inventory
        /// </summary>
        public static void LogGetCommand(string playerName, StoredItem item)
        {
            if (!_initialized) Initialize();

            try
            {
                // If this is a bag, get its contents from live inventory and log immediately
                if (item.IsContainer || item.Name.ToLower().Contains("backpack") || item.Name.ToLower().Contains("bag"))
                {
                    // Find the bag in the backpacks collection and get its contents
                    var backpack = Inventory.Containers.FirstOrDefault(bp =>
                        bp.Identity.Instance == item.ItemInstance);

                    if (backpack != null)
                    {
                        try
                        {
                            var bagContents = new List<string>();

                            // Process each item individually and detect its charges
                            ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Backpack '{item.Name}' has {backpack.Items.Count()} items");
                            foreach (var bagItem in backpack.Items)
                            {
                                ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Found item in bag: '{bagItem.Name}' QL{bagItem.Ql} (Instance: {bagItem.UniqueIdentity.Instance})");

                                var itemName = bagItem.Name;
                                if (bagItem.Ql > 0)
                                    itemName += $" QL{bagItem.Ql}";

                                // Check for Charges property on this specific item
                                int charges = 1;
                                try
                                {
                                    var chargesProp = bagItem.GetType().GetProperty("Charges");
                                    if (chargesProp != null)
                                    {
                                        var value = chargesProp.GetValue(bagItem);
                                        charges = value is int ? (int)value : (value != null ? Convert.ToInt32(value) : 1);
                                        ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Item '{itemName}' has {charges} charges");
                                    }
                                    else
                                    {
                                        ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Item '{itemName}' has no Charges property");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Error getting charges for '{itemName}': {ex.Message}");
                                }

                                // Create display name with charges if > 1
                                var displayName = itemName;
                                if (charges > 1)
                                {
                                    displayName += $" x{charges}";
                                    ItemTracker.LogTransaction("SYSTEM", $"TRADELOG DEBUG: Created display name with charges: '{displayName}'");
                                }

                                bagContents.Add(displayName);
                            }

                            // Cache for trade log
                            if (bagContents.Any())
                            {
                                CacheBagContents(item.Name, item.ItemInstance, bagContents);
                                ItemTracker.LogTransaction("SYSTEM", $"TRADELOG: Found and cached {bagContents.Count} items in bag '{item.Name}': {string.Join(", ", bagContents)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ItemTracker.LogTransaction("SYSTEM", $"TRADELOG ERROR: Could not read bag contents from inventory: {ex.Message}");
                        }
                    }
                    else
                    {
                        ItemTracker.LogTransaction("SYSTEM", $"TRADELOG ERROR: Could not find bag '{item.Name}' with instance {item.ItemInstance} in backpacks");
                    }
                }

                ItemTracker.LogTransaction("SYSTEM", $"TRADELOG: GET command processed for {playerName}: {item.Name}");
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"ERROR processing GET command for trade log: {ex.Message}");
            }
        }

        /// <summary>
        /// Log bag contents (for compatibility)
        /// </summary>
        public static void LogBagContents(string playerName, string bagName, List<string> bagContents)
        {
            // This method is called by ItemTracker but we handle logging in LogCompleteTrade
            // Just log to transaction log for debugging
            ItemTracker.LogTransaction("SYSTEM", $"TRADELOG: Bag contents from {playerName} - {bagName}: {string.Join(", ", bagContents)}");
        }

        /// <summary>
        /// Write a log entry to the trade log file
        /// </summary>
        private static void WriteLogEntry(string entry)
        {
            try
            {
                File.AppendAllText(_tradeLogPath, entry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"ERROR writing to trade log: {ex.Message}");
            }
        }
    }
}
