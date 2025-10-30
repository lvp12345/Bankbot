using AOSharp.Clientless.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Clientless;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.GameData;
using Newtonsoft.Json;
using Bankbot.Templates;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles item tracking, storage, and logging for the Bankbot
    /// </summary>
    public static class ItemTracker
    {
        private static Dictionary<string, StoredItem> _storedItems = new Dictionary<string, StoredItem>();
        private static string _logFilePath;
        private static bool _initialized = false;

        // Cache for GetStoredItems to improve performance
        private static List<object> _cachedStoredItems = null;
        private static DateTime _storedItemsCacheLastUpdated = DateTime.MinValue;
        private static readonly object _storedItemsCacheLock = new object();

        // Cache for space stats to improve performance
        private static (int usedSlots, int totalSlots, bool isAlmostFull) _cachedInventoryStats;
        private static (int usedBagSlots, int totalBagSlots) _cachedBagStats;
        private static bool _spaceStatsCached = false;

        /// <summary>
        /// Invalidate the stored items cache, forcing regeneration on next request
        /// </summary>
        public static void InvalidateStoredItemsCache()
        {
            lock (_storedItemsCacheLock)
            {
                _cachedStoredItems = null;
                _storedItemsCacheLastUpdated = DateTime.MinValue;
                _spaceStatsCached = false; // Also invalidate space stats cache
                LogTransaction("SYSTEM", "[CACHE DEBUG] CACHE INVALIDATED");
            }
        }

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Set up log file path in same directory as DLL
                string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string logDir = Path.GetDirectoryName(assemblyLocation);
                _logFilePath = Path.Combine(logDir, "bankbot.log");

                // Wipe the log file on startup for fresh logging
                File.WriteAllText(_logFilePath, "");

                // Load existing stored items if any
                LoadStoredItems();

                // Re-index all items in bags to fix any missing bag associations
                Task.Run(async () =>
                {
                    await Task.Delay(8000); // Wait longer for bags to be opened
                    ReindexBagContents();
                });

                _initialized = true;
                LogTransaction("SYSTEM", "Bankbot ItemTracker initialized");
            }
            catch (Exception ex)
            {
                // Silent error handling but log to file if possible
                try
                {
                    File.AppendAllText(_logFilePath ?? "bankbot_error.log", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - SYSTEM - ERROR: {ex.Message}\n");
                }
                catch { }
            }
        }

        /// <summary>
        /// Process items received in trade
        /// </summary>
        public static void ProcessReceivedItems(List<Item> items, string playerName)
        {
            if (!_initialized) Initialize();

            try
            {
                // Initialize TradeLogger to ensure it's ready
                TradeLogger.Initialize();

                // Log all items given TO the bot
                TradeLogger.LogItemsGiven(playerName, items);

                // Move items to bags with free space (like Craftbot does)
                Task.Run(async () =>
                {
                    await Task.Delay(2000); // Wait for items to appear in inventory
                    MoveItemsToBags(items, playerName);
                });

                // REACTIVATED - Bankbot storage functionality needed for storage bot
                foreach (var item in items)
                {
                    if (item.UniqueIdentity.Type == IdentityType.Container)
                    {
                        // Handle bags - open and index contents
                        ProcessBag(item, playerName);
                    }
                    else
                    {
                        // Handle individual items
                        ProcessIndividualItem(item, playerName);
                    }
                }

                // Invalidate both caches since items were added
                InvalidateStoredItemsCache();
                StorageHelpTemplate.InvalidateCache();
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR processing items from {playerName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract item properties safely from an Item object
        /// </summary>
        public static (int stackCount, int qualityLevel) ExtractItemProperties(Item item)
        {
            try
            {
                int stackCount = 1;
                int qualityLevel = 0;



                LogTransaction("SYSTEM", $"STACK COUNT DETECTION:");

                // Use reflection to check for Charges property (works with any AOSharp version)
                try
                {
                    var chargesProp = item.GetType().GetProperty("Charges");
                    if (chargesProp != null)
                    {
                        var value = chargesProp.GetValue(item);
                        int charges = value is int ? (int)value : (value != null ? Convert.ToInt32(value) : 1);
                        LogTransaction("SYSTEM", $"  CHARGES PROPERTY FOUND: {charges}");
                        if (charges > 1)
                        {
                            stackCount = charges;
                            LogTransaction("SYSTEM", $"  *** USING STACK COUNT FROM CHARGES PROPERTY: {charges} ***");
                        }
                    }
                    else
                    {
                        LogTransaction("SYSTEM", $"  CHARGES PROPERTY NOT FOUND - using default stack count 1");
                    }
                }
                catch (Exception ex)
                {
                    LogTransaction("SYSTEM", $"  ERROR GETTING CHARGES PROPERTY: {ex.Message}");
                }



                // Now try to extract quality level from the most likely candidates
                string[] qualityCandidates = { "Ql", "QualityLevel", "QL", "Quality", "ItemLevel", "Level" };
                LogTransaction("SYSTEM", $"QUALITY LEVEL DETECTION:");
                foreach (var propName in qualityCandidates)
                {
                    // Try property first
                    try
                    {
                        var property = item.GetType().GetProperty(propName);
                        if (property != null)
                        {
                            var value = property.GetValue(item);
                            if (value != null && int.TryParse(value.ToString(), out int ql))
                            {
                                LogTransaction("SYSTEM", $"  QUALITY CANDIDATE PROP: {propName} = {ql}");
                                if (ql > 0 && qualityLevel == 0) // Use first valid quality level > 0
                                {
                                    qualityLevel = ql;
                                    LogTransaction("SYSTEM", $"  *** USING QUALITY LEVEL FROM PROPERTY '{propName}': {ql} ***");
                                }
                            }
                        }
                    }
                    catch { }

                    // Try field
                    try
                    {
                        var field = item.GetType().GetField(propName);
                        if (field != null)
                        {
                            var value = field.GetValue(item);
                            if (value != null && int.TryParse(value.ToString(), out int ql))
                            {
                                LogTransaction("SYSTEM", $"  QUALITY CANDIDATE FIELD: {propName} = {ql}");
                                if (ql > 0 && qualityLevel == 0) // Use first valid quality level > 0
                                {
                                    qualityLevel = ql;
                                    LogTransaction("SYSTEM", $"  *** USING QUALITY LEVEL FROM FIELD '{propName}': {ql} ***");
                                }
                            }
                        }
                    }
                    catch { }
                }

                LogTransaction("SYSTEM", $"=== FINAL RESULT FOR '{item.Name}': StackCount={stackCount}, QualityLevel={qualityLevel} ===");
                return (stackCount, qualityLevel);
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR extracting item properties for '{item.Name}': {ex.Message}");
                return (1, 0); // Default values
            }
        }

        /// <summary>
        /// Process a bag item - open it and index contents
        /// </summary>
        private static void ProcessBag(Item bag, string playerName)
        {
            // REACTIVATED - Bankbot storage functionality needed for storage bot
            try
            {
                // Extract item properties
                var (stackCount, qualityLevel) = ExtractItemProperties(bag);

                // First, store the bag itself as a container
                var bagStoredItem = new StoredItem
                {
                    Id = (uint)bag.Id,
                    Name = bag.Name,
                    Quality = 0, // Default quality for bags
                    Quantity = 1,
                    StoredBy = playerName,
                    StoredAt = DateTime.Now,
                    ItemInstance = (uint)bag.UniqueIdentity.Instance,
                    IsContainer = true,
                    StackCount = stackCount,
                    QualityLevel = qualityLevel
                };

                string bagKey = $"{bag.Name}_{bag.Id}_{bag.UniqueIdentity.Instance}";
                _storedItems[bagKey] = bagStoredItem;

                LogTransaction(playerName, $"BAG: {bag.Name}"); // Log the bag itself

                // Open the bag immediately so it's available for catalog/list commands
                Task.Run(async () =>
                {
                    await Task.Delay(1000); // Wait for bag to be fully processed
                    try
                    {
                        LogTransaction("SYSTEM", $"Opening received bag: {bag.Name}");
                        bag.Use(); // Open the bag
                        LogTransaction("SYSTEM", $"✅ Bag opened: {bag.Name}");
                    }
                    catch (Exception ex)
                    {
                        LogTransaction("SYSTEM", $"ERROR opening received bag {bag.Name}: {ex.Message}");
                    }
                });

                // Note: Bag contents will be processed separately when the bag is opened in the trading system
                // This method stores the bag itself, and bag contents are logged when they're actually processed
                LogTransaction(playerName, $"Stored bag: {bag.Name} (contents will be processed when opened)");

                // Save updated storage
                SaveStoredItems();
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR processing bag {bag.Name} from {playerName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Process an item from inside a bag and store it with bag tracking
        /// </summary>
        private static void ProcessBagItem(Item item, string playerName, string bagName, uint bagInstance)
        {
            try
            {
                // Extract item properties
                var (stackCount, qualityLevel) = ExtractItemProperties(item);

                var storedItem = new StoredItem
                {
                    Id = (uint)item.Id,
                    Name = item.Name,
                    Quality = 0, // Default quality for bag items
                    Quantity = 1, // Default quantity for bag items
                    StoredBy = playerName,
                    StoredAt = DateTime.Now,
                    ItemInstance = (uint)item.UniqueIdentity.Instance,
                    SourceBagName = bagName,
                    SourceBagInstance = bagInstance,
                    IsContainer = false,
                    StackCount = stackCount,
                    QualityLevel = qualityLevel
                };

                // Use a unique key for storage
                string itemKey = $"{item.Name}_{item.Id}_{item.UniqueIdentity.Instance}";
                _storedItems[itemKey] = storedItem;

                // Log the transaction with bag info
                LogTransaction(playerName, $"{item.Name} (from {bagName})");
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR storing bag item {item.Name} from {bagName} for {playerName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Process an individual item and store it
        /// </summary>
        private static void ProcessIndividualItem(Item item, string playerName)
        {
            // REACTIVATED - Bankbot storage functionality needed for storage bot
            try
            {
                // Extract item properties
                var (stackCount, qualityLevel) = ExtractItemProperties(item);

                var storedItem = new StoredItem
                {
                    Id = (uint)item.Id,
                    Name = item.Name,
                    Quality = 0, // Default quality for loose items
                    Quantity = 1, // Default quantity for loose items
                    StoredBy = playerName,
                    StoredAt = DateTime.Now,
                    ItemInstance = (uint)item.UniqueIdentity.Instance,
                    SourceBagName = null, // This is a loose item, not from a bag
                    SourceBagInstance = null,
                    IsContainer = false, // Default to false, will be determined by name heuristic
                    StackCount = stackCount,
                    QualityLevel = qualityLevel
                };

                // Use a unique key for storage
                string itemKey = $"{item.Name}_{item.Id}_{item.UniqueIdentity.Instance}";
                _storedItems[itemKey] = storedItem;

                // Log the transaction
                LogTransaction(playerName, item.Name);

                // Save updated storage
                SaveStoredItems();
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR storing item {item.Name} from {playerName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Process and log bag contents when a bag is opened
        /// </summary>
        /// <param name="bagName">Name of the bag</param>
        /// <param name="bagContents">List of items found in the bag</param>
        /// <param name="playerName">Name of the player who gave the bag</param>
        public static void ProcessAndLogBagContents(string bagName, List<Item> bagContents, string playerName)
        {
            if (!_initialized) Initialize();

            try
            {
                if (bagContents == null || !bagContents.Any()) return;

                var itemNames = new List<string>();
                foreach (var item in bagContents)
                {
                    // Store each item from the bag with bag tracking
                    ProcessBagItem(item, playerName, bagName, 0); // Using 0 as placeholder for bag instance
                    itemNames.Add(item.Name);
                }

                // Log the bag contents to the trade log (this logs the individual items from the bag)
                TradeLogger.LogBagContents(playerName, bagName, itemNames);

                // Save updated storage
                SaveStoredItems();
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR processing bag contents for {bagName} from {playerName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all current inventory items (live inventory, not stored catalog)
        /// Focus on items inside bags since that's where most items will be
        /// </summary>
        public static List<object> GetStoredItems(bool includeBags = false)
        {
            try
            {
                // Check if we have a valid cached version
                lock (_storedItemsCacheLock)
                {
                    LogTransaction("SYSTEM", $"[CACHE DEBUG] Checking cache: _cachedStoredItems is {(_cachedStoredItems == null ? "NULL" : "NOT NULL")}");
                    if (_cachedStoredItems != null)
                    {
                        LogTransaction("SYSTEM", $"[CACHE DEBUG] CACHE HIT! Returning {_cachedStoredItems.Count} cached items");
                        return new List<object>(_cachedStoredItems); // Return a copy to prevent modification
                    }
                    LogTransaction("SYSTEM", "[CACHE DEBUG] CACHE MISS - proceeding with live scan");
                }
                var currentItems = new List<object>();

                // PRIMARY FOCUS: Get items from bags (where most items will be)
                // LogTransaction("SYSTEM", $"DEBUG: Scanning {Inventory.Containers.Count()} backpacks");
                foreach (var backpack in Inventory.Containers)
                {
                    // ALWAYS get the bag name from the main inventory item, not the backpack object
                    // This ensures consistency between bag containers and their contents
                    string bagName = null;
                    var bagInInventory = Inventory.Items.FirstOrDefault(item =>
                        item.UniqueIdentity.Instance == backpack.Identity.Instance &&
                        item.UniqueIdentity.Type == IdentityType.Container);

                    if (bagInInventory != null)
                    {
                        bagName = bagInInventory.Name;
                    }
                    else
                    {
                        // Fallback to backpack name if not found in inventory
                        bagName = backpack.Item?.Name ?? "Unknown" ?? $"Unknown Bag {backpack.Identity.Instance}";
                    }

                    LogTransaction("SYSTEM", $"DEBUG: Backpack '{bagName}' (ID: {backpack.Identity.Instance}) has {backpack.Items.Count} items");
                    foreach (var bagItem in backpack.Items)
                    {
                        // Extract item properties
                        var (stackCount, qualityLevel) = ExtractItemProperties(bagItem);

                        var storedItem = new StoredItem
                        {
                            Id = (uint)bagItem.Id,
                            Name = bagItem.Name,
                            Quality = 0,
                            Quantity = 1,
                            StoredBy = "Player", // Generic since we don't track individual players in live mode
                            StoredAt = DateTime.Now,
                            ItemInstance = (uint)bagItem.UniqueIdentity.Instance,
                            SourceBagName = bagName,
                            SourceBagInstance = (uint)backpack.Identity.Instance,
                            IsContainer = false,
                            ActualItem = bagItem, // Store reference to actual Item object for trading
                            StackCount = stackCount,
                            QualityLevel = qualityLevel
                        };

                        LogTransaction("SYSTEM", $"DEBUG: Adding item '{bagItem.Name}' from bag '{bagName}'");
                        currentItems.Add(storedItem);
                    }
                }

                // SECONDARY: Get loose items from main inventory (including bags)
                var inventoryItems = Inventory.Items.Where(item =>
                    item.Slot.Type == IdentityType.Inventory).ToList();

                LogTransaction("SYSTEM", $"DEBUG: Found {inventoryItems.Count} items in main inventory");
                foreach (var item in inventoryItems)
                {
                    bool isContainer = item.UniqueIdentity.Type == IdentityType.Container;
                    LogTransaction("SYSTEM", $"DEBUG: Item '{item.Name}' - IsContainer: {isContainer}");

                    // For containers, use the backpack identity instance to match with bag contents
                    uint itemInstance = (uint)item.UniqueIdentity.Instance;
                    if (isContainer)
                    {
                        // Find the corresponding backpack to get the correct instance ID
                        var correspondingBackpack = Inventory.Containers.FirstOrDefault(bp =>
                            bp.Identity.Instance == item.UniqueIdentity.Instance);
                        if (correspondingBackpack != null)
                        {
                            itemInstance = (uint)correspondingBackpack.Identity.Instance;
                            LogTransaction("SYSTEM", $"DEBUG: Container '{item.Name}' - Using backpack instance: {itemInstance}");
                        }
                    }

                    // Extract item properties
                    var (stackCount, qualityLevel) = ExtractItemProperties(item);

                    // Create a StoredItem representation for display
                    var storedItem = new StoredItem
                    {
                        Id = (uint)item.Id,
                        Name = item.Name,
                        Quality = 0,
                        Quantity = 1,
                        StoredBy = "Player",
                        StoredAt = DateTime.Now,
                        ItemInstance = itemInstance,
                        SourceBagName = null, // Loose item
                        SourceBagInstance = null,
                        IsContainer = isContainer,
                        ActualItem = item, // Store reference to actual Item object for trading
                        StackCount = stackCount,
                        QualityLevel = qualityLevel
                    };

                    LogTransaction("SYSTEM", $"DEBUG: Created StoredItem: '{item.Name}' with instance {itemInstance} (IsContainer: {isContainer})");

                    currentItems.Add(storedItem);
                }

                var sortedItems = currentItems.OrderBy(item => ((StoredItem)item).Name).ToList();

                // Cache the result and calculate space stats while we have all the data
                lock (_storedItemsCacheLock)
                {
                    _cachedStoredItems = new List<object>(sortedItems);
                    _storedItemsCacheLastUpdated = DateTime.Now;

                    // Calculate and cache space stats from the data we already have
                    CalculateAndCacheSpaceStats(sortedItems);

                    LogTransaction("SYSTEM", $"[CACHE DEBUG] CACHED {sortedItems.Count} items successfully");
                }

                return sortedItems;
            }
            catch (Exception)
            {
                // LogTransaction("SYSTEM", $"ERROR getting current inventory items: {ex.Message}");
                return new List<object>();
            }
        }

        /// <summary>
        /// Remove an item from storage (when given to player) - for live inventory mode, just log the transaction
        /// </summary>
        public static bool RemoveItem(string itemKey, string playerName)
        {
            try
            {
                // In live inventory mode, we don't actually remove from a catalog
                // The item will be removed from inventory when it's traded to the player
                LogTransaction(playerName, $"RETRIEVED: {itemKey}");
                return true;
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR logging retrieval {itemKey} for {playerName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Find an item by name and instance ID in current inventory (for bag disambiguation)
        /// </summary>
        public static StoredItem FindItemByNameAndInstance(string itemName, uint instanceId)
        {
            try
            {
                LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** Searching for item: '{itemName}' with instance: {instanceId}");

                // PRIMARY SEARCH: Look in main inventory for bags with specific instance
                LogTransaction("SYSTEM", $"*** SEARCH DEBUG *** Looking in main inventory for '{itemName}' with instance {instanceId}");
                foreach (var item in Inventory.Items.Where(i => i.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)))
                {
                    LogTransaction("SYSTEM", $"*** SEARCH DEBUG *** Found item '{item.Name}' with instance {item.UniqueIdentity.Instance} (looking for {instanceId})");
                }

                var inventoryItem = Inventory.Items.FirstOrDefault(item =>
                    item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase) &&
                    item.UniqueIdentity.Instance == instanceId);

                if (inventoryItem != null)
                {
                    LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** MATCH FOUND: Requested '{itemName}' with instance {instanceId} found in main inventory");

                    // Extract item properties
                    var (stackCount, qualityLevel) = ExtractItemProperties(inventoryItem);

                    return new StoredItem
                    {
                        Id = (uint)inventoryItem.Id,
                        Name = inventoryItem.Name,
                        Quality = 0,
                        Quantity = 1,
                        StoredBy = "Player",
                        StoredAt = DateTime.Now,
                        ItemInstance = (uint)inventoryItem.UniqueIdentity.Instance,
                        SourceBagName = null,
                        SourceBagInstance = null,
                        IsContainer = inventoryItem.UniqueIdentity.Type == IdentityType.Container,
                        ActualItem = inventoryItem, // Store reference to actual Item object
                        StackCount = stackCount,
                        QualityLevel = qualityLevel
                    };
                }

                // SECONDARY SEARCH: Look in bags for items with specific instance
                foreach (var backpack in Inventory.Containers)
                {
                    var bagItem = backpack.Items.FirstOrDefault(item =>
                        item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase) &&
                        item.UniqueIdentity.Instance == instanceId);

                    if (bagItem != null)
                    {
                        LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** MATCH FOUND: Requested '{itemName}' with instance {instanceId} found in bag '{backpack.Item?.Name ?? "Unknown"}'");

                        // Extract item properties
                        var (stackCount, qualityLevel) = ExtractItemProperties(bagItem);

                        return new StoredItem
                        {
                            Id = (uint)bagItem.Id,
                            Name = bagItem.Name,
                            Quality = 0,
                            Quantity = 1,
                            StoredBy = "Player",
                            StoredAt = DateTime.Now,
                            ItemInstance = (uint)bagItem.UniqueIdentity.Instance,
                            SourceBagName = backpack.Item?.Name ?? "Unknown",
                            SourceBagInstance = (uint)backpack.Identity.Instance,
                            IsContainer = false,
                            ActualItem = bagItem, // Store reference to actual Item object
                            StackCount = stackCount,
                            QualityLevel = qualityLevel
                        };
                    }
                }

                LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** NO MATCH: Item '{itemName}' with instance {instanceId} not found");
                return null;
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR finding item by name and instance '{itemName}' {instanceId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find an item by name in current inventory (prioritize bags since most items are there)
        /// </summary>
        public static StoredItem FindItemByName(string itemName)
        {
            try
            {
                LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** Searching for item: '{itemName}'");

                // PRIMARY SEARCH: Look in bags first (where most items will be)
                foreach (var backpack in Inventory.Containers)
                {
                    LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** Searching in bag: {backpack.Item?.Name ?? "Unknown"} ({backpack.Items.Count} items)");

                    // Log all items in this bag
                    foreach (var item in backpack.Items)
                    {
                        var cleanName = CleanItemNameForSearch(item.Name);
                        LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** Bag item: '{item.Name}' -> Clean: '{cleanName}' (exact match: {item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)}, clean match: {cleanName.Equals(itemName, StringComparison.OrdinalIgnoreCase)})");
                    }

                    var bagItem = backpack.Items.FirstOrDefault(item =>
                        item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase) ||
                        CleanItemNameForSearch(item.Name).Equals(itemName, StringComparison.OrdinalIgnoreCase));

                    if (bagItem != null)
                    {
                        LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** MATCH FOUND: Requested '{itemName}' and found '{bagItem.Name}' in bag '{backpack.Item?.Name ?? "Unknown"}'");

                        // Extract item properties
                        var (stackCount, qualityLevel) = ExtractItemProperties(bagItem);

                        return new StoredItem
                        {
                            Id = (uint)bagItem.Id,
                            Name = bagItem.Name,
                            Quality = 0,
                            Quantity = 1,
                            StoredBy = "Player",
                            StoredAt = DateTime.Now,
                            ItemInstance = (uint)bagItem.UniqueIdentity.Instance,
                            SourceBagName = backpack.Item?.Name ?? "Unknown",
                            SourceBagInstance = (uint)backpack.Identity.Instance,
                            IsContainer = false,
                            ActualItem = bagItem, // Store reference to actual Item object
                            StackCount = stackCount,
                            QualityLevel = qualityLevel
                        };
                    }
                }

                // SECONDARY SEARCH: Look in main inventory (for loose items)
                LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** Searching main inventory ({Inventory.Items.Count()} items)");

                // Log all items in main inventory
                foreach (var item in Inventory.Items)
                {
                    LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** Main inventory item: '{item.Name}' (matches '{itemName}'? {item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)})");
                }

                var inventoryItem = Inventory.Items.FirstOrDefault(item =>
                    item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase) ||
                    CleanItemNameForSearch(item.Name).Equals(itemName, StringComparison.OrdinalIgnoreCase));

                if (inventoryItem != null)
                {
                    LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** MATCH FOUND: Requested '{itemName}' and found '{inventoryItem.Name}' in main inventory");

                    // Extract item properties
                    var (stackCount, qualityLevel) = ExtractItemProperties(inventoryItem);

                    return new StoredItem
                    {
                        Id = (uint)inventoryItem.Id,
                        Name = inventoryItem.Name,
                        Quality = 0,
                        Quantity = 1,
                        StoredBy = "Player",
                        StoredAt = DateTime.Now,
                        ItemInstance = (uint)inventoryItem.UniqueIdentity.Instance,
                        SourceBagName = null,
                        SourceBagInstance = null,
                        IsContainer = false,
                        ActualItem = inventoryItem, // Store reference to actual Item object
                        StackCount = stackCount,
                        QualityLevel = qualityLevel
                    };
                }

                LogTransaction("SYSTEM", $"*** CRITICAL DEBUG *** NO MATCH: Item '{itemName}' not found in any bags or main inventory");
                return null;
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR finding item by name '{itemName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Calculate and cache space statistics from stored items data
        /// </summary>
        private static void CalculateAndCacheSpaceStats(List<object> sortedItems)
        {
            try
            {
                // Calculate inventory stats from cached data
                var inventoryItems = Inventory.Items.Where(item =>
                    item.Slot.Type == IdentityType.Inventory).ToList();
                int usedSlots = inventoryItems.Count;
                int totalSlots = 29; // Standard AO inventory size -1 (reserve 1 slot for trading)
                bool isAlmostFull = (totalSlots - usedSlots) <= 1;
                _cachedInventoryStats = (usedSlots, totalSlots, isAlmostFull);

                // Calculate bag stats from cached data
                int totalUsed = 0;
                int totalCapacity = 0;
                var storedItems = sortedItems.Cast<StoredItem>().ToList();
                var bags = storedItems.Where(item => IsContainer(item)).ToList();

                foreach (var bag in bags)
                {
                    var itemsInBag = storedItems.Where(item =>
                        !IsContainer(item) &&
                        item.SourceBagName == bag.Name &&
                        item.SourceBagInstance == bag.ItemInstance).Count();

                    totalUsed += itemsInBag;
                    int estimatedCapacity = EstimateBagCapacity(bag.Name);
                    totalCapacity += estimatedCapacity;
                }
                _cachedBagStats = (totalUsed, totalCapacity);
                _spaceStatsCached = true;

                LogTransaction("SYSTEM", $"[CACHE DEBUG] CACHED space stats - Inventory: {usedSlots}/{totalSlots}, Bags: {totalUsed}/{totalCapacity}");
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"Error calculating space stats: {ex.Message}");
                _spaceStatsCached = false;
            }
        }

        /// <summary>
        /// Get inventory space statistics
        /// </summary>
        public static (int usedSlots, int totalSlots, bool isAlmostFull) GetInventorySpaceStats()
        {
            // Use cached stats if available
            lock (_storedItemsCacheLock)
            {
                if (_spaceStatsCached)
                {
                    LogTransaction("SYSTEM", "[CACHE DEBUG] Using cached inventory stats");
                    return _cachedInventoryStats;
                }
            }

            try
            {
                LogTransaction("SYSTEM", "[CACHE DEBUG] Live calculating inventory stats");
                // Count items in main inventory (excluding containers)
                var inventoryItems = Inventory.Items.Where(item =>
                    item.Slot.Type == IdentityType.Inventory).ToList();

                int usedSlots = inventoryItems.Count;
                int totalSlots = 29; // Standard AO inventory size -1 (reserve 1 slot for trading)
                bool isAlmostFull = (totalSlots - usedSlots) <= 1; // Only 1 slot left or less

                return (usedSlots, totalSlots, isAlmostFull);
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR getting inventory stats: {ex.Message}");
                return (0, 29, false);
            }
        }

        /// <summary>
        /// Get total available space in all bags
        /// </summary>
        public static (int usedBagSlots, int totalBagSlots) GetBagSpaceStats()
        {
            // Use cached stats if available
            lock (_storedItemsCacheLock)
            {
                if (_spaceStatsCached)
                {
                    LogTransaction("SYSTEM", "[CACHE DEBUG] Using cached bag stats");
                    return _cachedBagStats;
                }
            }

            try
            {
                LogTransaction("SYSTEM", "[CACHE DEBUG] Live calculating bag stats");
                int totalUsed = 0;
                int totalCapacity = 0;

                foreach (var backpack in Inventory.Containers)
                {
                    if (backpack.IsOpen)
                    {
                        totalUsed += backpack.Items.Count;
                        // Estimate bag capacity based on common AO bag sizes
                        // Most bags are 21 slots, some are 42 or 63
                        int estimatedCapacity = EstimateBagCapacity(backpack.Item?.Name ?? "Unknown");
                        totalCapacity += estimatedCapacity;
                    }
                }

                return (totalUsed, totalCapacity);
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR getting bag stats: {ex.Message}");
                return (0, 0);
            }
        }

        /// <summary>
        /// Estimate bag capacity based on bag name/type
        /// </summary>
        public static int EstimateBagCapacity(string bagName)
        {
            if (string.IsNullOrEmpty(bagName))
                return 21; // Default

            string lowerName = bagName.ToLower();

            // Large bags typically have more slots
            if (lowerName.Contains("large") || lowerName.Contains("big"))
                return 42;

            // Huge/massive bags
            if (lowerName.Contains("huge") || lowerName.Contains("massive"))
                return 63;

            // Default to standard bag size
            return 21;
        }

        /// <summary>
        /// Get items grouped by their container (loose items or specific bags)
        /// </summary>
        public static Dictionary<string, List<StoredItem>> GetItemsGroupedByContainer()
        {
            try
            {
                var groupedItems = new Dictionary<string, List<StoredItem>>();
                var currentItems = GetStoredItems(true).Cast<StoredItem>().ToList();

                // Debug logging
                LogTransaction("SYSTEM", $"DEBUG: GetItemsGroupedByContainer found {currentItems.Count} total items");
                foreach (var item in currentItems)
                {
                    LogTransaction("SYSTEM", $"DEBUG: Item '{item.Name}' - SourceBagName: '{item.SourceBagName ?? "NULL"}'");
                }

                // Group loose items (main inventory) - include bags as gettable items
                var looseItems = currentItems.Where(item => string.IsNullOrEmpty(item.SourceBagName)).ToList();
                if (looseItems.Any())
                {
                    groupedItems["Main Inventory"] = looseItems;
                    LogTransaction("SYSTEM", $"DEBUG: Added {looseItems.Count} items to Main Inventory group (including bags)");
                }

                // Group items by bag
                var bagGroups = currentItems
                    .Where(item => !string.IsNullOrEmpty(item.SourceBagName))
                    .GroupBy(item => item.SourceBagName)
                    .OrderBy(group => group.Key);

                foreach (var bagGroup in bagGroups)
                {
                    groupedItems[bagGroup.Key] = bagGroup.ToList();
                    LogTransaction("SYSTEM", $"DEBUG: Added {bagGroup.Count()} items to bag group '{bagGroup.Key}'");
                }

                return groupedItems;
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR grouping items by container: {ex.Message}");
                return new Dictionary<string, List<StoredItem>>();
            }
        }

        /// <summary>
        /// Move items to bags with free space (like Craftbot does)
        /// </summary>
        private static void MoveItemsToBags(List<Item> items, string playerName)
        {
            try
            {
                LogTransaction("SYSTEM", $"Moving {items.Count} items to bags for {playerName}");

                foreach (var item in items)
                {
                    // Skip containers (bags) - don't move bags to other bags
                    if (item.UniqueIdentity.Type == IdentityType.Container)
                    {
                        LogTransaction("SYSTEM", $"Skipping bag movement for {item.Name}");
                        continue;
                    }

                    // Find a bag with free space
                    var targetBag = FindBagWithFreeSpace();
                    if (targetBag != null)
                    {
                        LogTransaction("SYSTEM", $"Moving {item.Name} to bag {targetBag.Item?.Name ?? "Unknown"}");

                        // Move item to the bag using Craftbot's approach
                        item.MoveToContainer(targetBag.Identity);

                        // Update storage record to reflect the item is now in the bag
                        UpdateItemBagLocation(item, targetBag.Item?.Name ?? "Unknown", (uint)targetBag.Identity.Instance, playerName);

                        // Small delay between moves
                        System.Threading.Thread.Sleep(200);
                    }
                    else
                    {
                        LogTransaction("SYSTEM", $"No bag space available for {item.Name} - leaving in main inventory");
                    }
                }
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR moving items to bags: {ex.Message}");
            }
        }

        /// <summary>
        /// Re-index all items in bags to fix missing bag associations
        /// </summary>
        public static void ReindexBagContents()
        {
            try
            {
                LogTransaction("SYSTEM", "Starting bag content re-indexing...");
                LogTransaction("SYSTEM", $"Found {Inventory.Containers.Count()} containers to check");

                bool madeChanges = false;

                foreach (var container in Inventory.Containers)
                {
                    if (container.IsOpen && container.Item != null)
                    {
                        string bagName = container.Item.Name;
                        uint bagInstance = (uint)container.Identity.Instance;

                        LogTransaction("SYSTEM", $"Re-indexing bag: {bagName} (Instance: {bagInstance}) with {container.Items.Count} items");

                        foreach (var item in container.Items)
                        {
                            string itemKey = $"{item.Name}_{item.Id}_{item.UniqueIdentity.Instance}";

                            if (_storedItems.TryGetValue(itemKey, out var storedItem))
                            {
                                // Update bag association if missing
                                if (string.IsNullOrEmpty(storedItem.SourceBagName))
                                {
                                    storedItem.SourceBagName = bagName;
                                    storedItem.SourceBagInstance = bagInstance;
                                    LogTransaction("SYSTEM", $"Fixed bag association for {item.Name} -> {bagName}");
                                    madeChanges = true;
                                }
                            }
                            else
                            {
                                // Item in bag but not in storage - add it
                                var (stackCount, qualityLevel) = ExtractItemProperties(item);
                                var newStoredItem = new StoredItem
                                {
                                    Id = (uint)item.Id,
                                    Name = item.Name,
                                    Quality = 0,
                                    Quantity = 1,
                                    StoredBy = "SYSTEM",
                                    StoredAt = DateTime.Now,
                                    ItemInstance = (uint)item.UniqueIdentity.Instance,
                                    SourceBagName = bagName,
                                    SourceBagInstance = bagInstance,
                                    IsContainer = false,
                                    StackCount = stackCount,
                                    QualityLevel = qualityLevel
                                };

                                _storedItems[itemKey] = newStoredItem;
                                LogTransaction("SYSTEM", $"Added missing item to storage: {item.Name} in {bagName}");
                                madeChanges = true;
                            }
                        }
                    }
                }

                SaveStoredItems();

                // Only invalidate the caches if we actually made changes
                if (madeChanges)
                {
                    InvalidateStoredItemsCache();
                    StorageHelpTemplate.InvalidateCache();
                    LogTransaction("SYSTEM", "Bag content re-indexing completed - caches invalidated due to changes");
                }
                else
                {
                    LogTransaction("SYSTEM", "Bag content re-indexing completed - no changes made, caches preserved");
                }
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR during bag re-indexing: {ex.Message}");
            }
        }

        /// <summary>
        /// Clean item name for search by removing quality level prefixes
        /// </summary>
        private static string CleanItemNameForSearch(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return itemName;

            // Remove quality level prefixes like "16 - 31 " from "16 - 31 NCU Memory"
            // Pattern: digits, optional space, dash, optional space, digits, space
            return System.Text.RegularExpressions.Regex.Replace(itemName, @"^\d+\s*-\s*\d+\s+", "");
        }

        /// <summary>
        /// Update an item's storage record to reflect it's now in a bag
        /// </summary>
        public static void UpdateItemBagLocation(Item item, string bagName, uint bagInstance, string playerName)
        {
            try
            {
                string itemKey = $"{item.Name}_{item.Id}_{item.UniqueIdentity.Instance}";

                if (_storedItems.TryGetValue(itemKey, out var storedItem))
                {
                    // Update the stored item to reflect it's now in a bag
                    storedItem.SourceBagName = bagName;
                    storedItem.SourceBagInstance = bagInstance;

                    LogTransaction("SYSTEM", $"Updated {item.Name} location to bag {bagName}");
                    SaveStoredItems();
                }
                else
                {
                    LogTransaction("SYSTEM", $"Could not find stored item record for {item.Name} to update bag location");
                }
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR updating bag location for {item.Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Find a bag with free space
        /// </summary>
        private static Container FindBagWithFreeSpace()
        {
            try
            {
                foreach (var backpack in Inventory.Containers)
                {
                    if (backpack.IsOpen)
                    {
                        // Estimate bag capacity and check if there's space
                        int estimatedCapacity = EstimateBagCapacity(backpack.Item?.Name ?? "Unknown");
                        int currentItems = backpack.Items.Count;

                        if (currentItems < estimatedCapacity - 1) // Leave at least 1 slot free
                        {
                            LogTransaction("SYSTEM", $"Found bag with space: {backpack.Item?.Name ?? "Unknown"} ({currentItems}/{estimatedCapacity})");
                            return backpack;
                        }
                    }
                }

                LogTransaction("SYSTEM", "No bags with free space found");
                return null;
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR finding bag with free space: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Log a transaction to the log file
        /// </summary>
        public static void LogTransaction(string playerName, string itemName)
        {
            if (!_initialized) Initialize();

            try
            {
                string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {playerName} - {itemName}\n";
                File.AppendAllText(_logFilePath, logEntry);
            }
            catch (Exception)
            {
                // Silent error handling for logging
            }
        }

        /// <summary>
        /// Save stored items to file
        /// </summary>
        private static void SaveStoredItems()
        {
            try
            {
                string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string storageDir = Path.GetDirectoryName(assemblyLocation);
                string storageFile = Path.Combine(storageDir, "stored_items.json");
                File.WriteAllText(storageFile, JsonConvert.SerializeObject(_storedItems, Formatting.Indented));
            }
            catch (Exception)
            {
                // Silent error handling
            }
        }

        /// <summary>
        /// Load stored items from file
        /// </summary>
        private static void LoadStoredItems()
        {
            try
            {
                string assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string storageDir = Path.GetDirectoryName(assemblyLocation);
                string storageFile = Path.Combine(storageDir, "stored_items.json");
                if (File.Exists(storageFile))
                {
                    var loaded = JsonConvert.DeserializeObject<Dictionary<string, StoredItem>>(File.ReadAllText(storageFile));
                    if (loaded != null)
                    {
                        _storedItems = loaded;
                    }
                }
            }
            catch (Exception)
            {
                // Silent error handling - start with empty storage
                _storedItems = new Dictionary<string, StoredItem>();
            }
        }

        /// <summary>
        /// Check if an item is a container/bag
        /// </summary>
        private static bool IsContainer(StoredItem item)
        {
            // Simple heuristic - bags typically have "bag", "backpack", "container" in name
            string name = item.Name.ToLower();
            return name.Contains("bag") || name.Contains("backpack") || name.Contains("container") ||
                   name.Contains("pouch") || name.Contains("satchel");
        }

        /// <summary>
        /// Get all items from a specific bag for reconstruction
        /// </summary>
        public static List<StoredItem> GetItemsFromBag(string bagName, uint? bagInstance = null)
        {
            if (!_initialized) Initialize();

            try
            {
                var bagItems = _storedItems.Values
                    .Where(item => item.IsFromBag &&
                                  item.SourceBagName.Equals(bagName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (bagInstance.HasValue)
                {
                    bagItems = bagItems.Where(item => item.SourceBagInstance == bagInstance.Value).ToList();
                }

                return bagItems;
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR getting items from bag '{bagName}': {ex.Message}");
                return new List<StoredItem>();
            }
        }

        /// <summary>
        /// Check if a bag can be reconstructed (has multiple items from same bag)
        /// </summary>
        public static bool CanReconstructBag(string bagName, uint? bagInstance = null)
        {
            try
            {
                var bagItems = GetItemsFromBag(bagName, bagInstance);
                return bagItems.Count > 1;
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR checking bag reconstruction for '{bagName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Remove all items from a bag for reconstruction
        /// </summary>
        public static List<StoredItem> RemoveAllItemsFromBag(string bagName, uint? bagInstance, string playerName)
        {
            if (!_initialized) Initialize();

            try
            {
                var bagItems = GetItemsFromBag(bagName, bagInstance);
                var removedItems = new List<StoredItem>();

                // Cache bag contents for trade logging before removing them
                if (bagItems.Any() && bagInstance.HasValue)
                {
                    var formattedContents = new List<string>();
                    foreach (var item in bagItems)
                    {
                        var itemName = item.Name;

                        // Add quality level if it's greater than 0
                        if (item.Quality > 0)
                            itemName += $" QL{item.Quality}";

                        // Add stack count if it's greater than 1
                        if (item.StackCount > 1)
                            itemName += $" x{item.StackCount}";

                        formattedContents.Add(itemName);
                    }

                    // Cache the formatted contents for trade logging
                    TradeLogger.CacheBagContents(bagName, bagInstance.Value, formattedContents);
                }

                foreach (var item in bagItems)
                {
                    string itemKey = item.GetItemKey();
                    if (_storedItems.ContainsKey(itemKey))
                    {
                        _storedItems.Remove(itemKey);
                        removedItems.Add(item);
                        LogTransaction(playerName, $"RETRIEVED FROM BAG: {item.Name} (from {bagName})");
                    }
                }

                if (removedItems.Any())
                {
                    SaveStoredItems();
                }

                return removedItems;
            }
            catch (Exception ex)
            {
                LogTransaction("SYSTEM", $"ERROR removing items from bag '{bagName}' for {playerName}: {ex.Message}");
                return new List<StoredItem>();
            }
        }
    }

    /// <summary>
    /// Represents a stored item in the bank
    /// </summary>
    public class StoredItem
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public int Quality { get; set; }
        public int Quantity { get; set; }
        public string StoredBy { get; set; }
        public DateTime StoredAt { get; set; }
        public uint ItemInstance { get; set; }

        // Enhanced properties for bag tracking
        public string SourceBagName { get; set; } = null; // Name of bag this item came from
        public uint? SourceBagInstance { get; set; } = null; // Instance of bag this item came from
        public bool IsFromBag => !string.IsNullOrEmpty(SourceBagName);
        public bool IsContainer { get; set; } = false; // True if this item itself is a bag/container

        // New properties for stack count and quality level tracking
        public int StackCount { get; set; } = 1; // Number of items in the stack
        public int QualityLevel { get; set; } = 0; // Quality level/QL of the item

        // Reference to the actual Item object for trading
        public Item ActualItem { get; set; } = null;

        public string GetItemKey()
        {
            return $"{Name}_{Id}_{ItemInstance}";
        }

        public string GetDisplayName()
        {
            var displayName = Name;

            // Add quality level if it's greater than 0
            if (QualityLevel > 0)
                displayName += $" QL{QualityLevel}";

            // Add stack count if it's greater than 1
            if (StackCount > 1)
                displayName += $" x{StackCount}";

            // Add bag source if applicable
            if (IsFromBag)
                displayName += $" (from {SourceBagName})";

            return displayName;
        }
    }

    /// <summary>
    /// Represents a bag reconstruction option
    /// </summary>
    public class BagReconstructionOption
    {
        public string BagName { get; set; }
        public uint? BagInstance { get; set; }
        public int ItemCount { get; set; }
        public List<StoredItem> Items { get; set; } = new List<StoredItem>();
        public string StoredBy { get; set; }

        public string GetDisplayText()
        {
            return $"{BagName} ({ItemCount} items) - stored by {StoredBy}";
        }
    }
}
