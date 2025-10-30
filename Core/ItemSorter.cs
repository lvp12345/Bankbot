using AOSharp.Clientless;
using AOSharp.Clientless.Logging;
using AOSharp.Common.GameData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles automatic sorting of items into categorized bags
    /// </summary>
    public static class ItemSorter
    {
        private static bool _initialized = false;
        private static Dictionary<string, List<string>> _sortingRules = new Dictionary<string, List<string>>();
        private static bool _isSorting = false;

        public static void Initialize()
        {
            if (_initialized) return;

            Logger.Information("[ITEM SORTER] Initializing item sorting system...");

            // Load sorting rules from config
            LoadSortingRules();

            _initialized = true;
            Logger.Information($"[ITEM SORTER] Initialized with {_sortingRules.Count} sorting categories");
        }

        /// <summary>
        /// Load sorting rules from configuration
        /// </summary>
        private static void LoadSortingRules()
        {
            try
            {
                var charSettings = Bankbot.Config.CharSettings.ContainsKey(Client.CharacterName)
                    ? Bankbot.Config.CharSettings[Client.CharacterName]
                    : new CharacterSettings();

                if (charSettings.ItemSortingRules != null && charSettings.ItemSortingRules.Any())
                {
                    _sortingRules = charSettings.ItemSortingRules;
                    Logger.Information($"[ITEM SORTER] Loaded {_sortingRules.Count} sorting rules from config");
                }
                else
                {
                    // Default sorting rules if none configured
                    _sortingRules = new Dictionary<string, List<string>>
                    {
                        { "Infantry Symbiants", new List<string> { "infantry" } },
                        { "Artillery Symbiants", new List<string> { "artillery" } },
                        { "Support Symbiants", new List<string> { "support" } },
                        { "Control Symbiants", new List<string> { "control" } },
                        { "Exterminator Symbiants", new List<string> { "exterminator" } },
                        { "Implants", new List<string> { "implant" } },
                        { "Nano Crystals", new List<string> { "nano crystal", "nano formula" } },
                        { "Weapons", new List<string> { "pistol", "rifle", "sword", "axe", "hammer" } },
                        { "Armor", new List<string> { "armor", "helmet", "boots", "gloves", "pants", "sleeves" } }
                    };
                    Logger.Information("[ITEM SORTER] Using default sorting rules");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ITEM SORTER] Error loading sorting rules: {ex.Message}");
                _sortingRules = new Dictionary<string, List<string>>();
            }
        }

        /// <summary>
        /// Sort all loose items in inventory into appropriate bags
        /// </summary>
        public static async Task SortAllItems()
        {
            if (!_initialized) Initialize();
            if (_isSorting)
            {
                Logger.Information("[ITEM SORTER] Sorting already in progress, skipping...");
                return;
            }

            _isSorting = true;

            try
            {
                Logger.Information("[ITEM SORTER] Starting full inventory sort...");

                // Get all loose items (not in bags, not containers themselves)
                var looseItems = Inventory.Items.Where(item =>
                    item.Slot.Type == IdentityType.Inventory &&
                    item.UniqueIdentity.Type != IdentityType.Container).ToList();

                Logger.Information($"[ITEM SORTER] Found {looseItems.Count} loose items to sort");

                if (!looseItems.Any())
                {
                    Logger.Information("[ITEM SORTER] No loose items to sort");
                    return;
                }

                // Sort each item
                foreach (var item in looseItems)
                {
                    await SortItem(item);
                    await Task.Delay(300); // Delay between moves to avoid overwhelming the server
                }

                Logger.Information("[ITEM SORTER] ✅ Sorting complete");
            }
            catch (Exception ex)
            {
                Logger.Error($"[ITEM SORTER] Error during sorting: {ex.Message}");
            }
            finally
            {
                _isSorting = false;
            }
        }

        /// <summary>
        /// Sort a single item into the appropriate bag
        /// </summary>
        private static async Task SortItem(Item item)
        {
            try
            {
                // Determine which category this item belongs to
                string targetBagName = DetermineItemCategory(item);

                if (string.IsNullOrEmpty(targetBagName))
                {
                    Logger.Information($"[ITEM SORTER] No category match for '{item.Name}' - leaving in inventory");
                    return;
                }

                Logger.Information($"[ITEM SORTER] Item '{item.Name}' matches category '{targetBagName}'");

                // Find or create a bag for this category
                var targetBag = FindOrCreateBagForCategory(targetBagName);

                if (targetBag == null)
                {
                    Logger.Information($"[ITEM SORTER] Could not find/create bag for category '{targetBagName}'");
                    return;
                }

                // Move item to the bag
                Logger.Information($"[ITEM SORTER] Moving '{item.Name}' to bag '{targetBag.Item?.Name ?? "Unknown"}'");
                item.MoveToContainer(targetBag.Identity);

                // Update storage tracking
                ItemTracker.UpdateItemBagLocation(item, targetBag.Item?.Name ?? targetBagName, 
                    (uint)targetBag.Identity.Instance, "AutoSort");

                await Task.Delay(200); // Small delay after move
            }
            catch (Exception ex)
            {
                Logger.Error($"[ITEM SORTER] Error sorting item '{item.Name}': {ex.Message}");
            }
        }

        /// <summary>
        /// Determine which category an item belongs to based on sorting rules
        /// </summary>
        private static string DetermineItemCategory(Item item)
        {
            string itemNameLower = item.Name.ToLower();

            foreach (var rule in _sortingRules)
            {
                string categoryName = rule.Key;
                List<string> patterns = rule.Value;

                foreach (var pattern in patterns)
                {
                    if (itemNameLower.Contains(pattern.ToLower()))
                    {
                        return categoryName;
                    }
                }
            }

            return null; // No category match
        }

        /// <summary>
        /// Find an existing bag for a category, or create a new one if needed
        /// Prioritizes filling existing bags before creating new ones
        /// </summary>
        private static Container FindOrCreateBagForCategory(string categoryName)
        {
            try
            {
                // First, try to find an existing bag with this exact name that has space
                var existingBag = FindBagWithSpace(categoryName);
                if (existingBag != null)
                {
                    Logger.Information($"[ITEM SORTER] Found existing bag '{existingBag.Item?.Name}' with space");
                    return existingBag;
                }

                // If no existing bag with space, try to find ANY bag with this name (even if full)
                // to check if we need to create a numbered variant
                var allMatchingBags = Inventory.Containers.Where(c =>
                    c.IsOpen &&
                    c.Item != null &&
                    c.Item.Name.StartsWith(categoryName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (allMatchingBags.Any())
                {
                    Logger.Information($"[ITEM SORTER] Found {allMatchingBags.Count} existing bags for category '{categoryName}', but all are full");
                    // All bags for this category are full - would need to create a new bag here
                    // For now, just return null since we can't create bags programmatically
                    return null;
                }

                // No bags exist for this category - try to find an empty generic bag to rename/use
                var emptyBag = FindEmptyBag();
                if (emptyBag != null)
                {
                    Logger.Information($"[ITEM SORTER] Found empty bag to use for category '{categoryName}'");
                    // Note: We can't rename bags programmatically, so just use it as-is
                    return emptyBag;
                }

                Logger.Information($"[ITEM SORTER] No suitable bag found for category '{categoryName}'");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"[ITEM SORTER] Error finding/creating bag for category '{categoryName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find a bag with the given name that has free space
        /// </summary>
        private static Container FindBagWithSpace(string bagName)
        {
            try
            {
                foreach (var container in Inventory.Containers)
                {
                    if (!container.IsOpen || container.Item == null)
                        continue;

                    // Check if bag name matches (case-insensitive, starts with)
                    if (container.Item.Name.StartsWith(bagName, StringComparison.OrdinalIgnoreCase))
                    {
                        int estimatedCapacity = ItemTracker.EstimateBagCapacity(container.Item.Name);
                        int currentItems = container.Items.Count;

                        if (currentItems < estimatedCapacity - 1) // Leave at least 1 slot free
                        {
                            Logger.Information($"[ITEM SORTER] Bag '{container.Item.Name}' has space: {currentItems}/{estimatedCapacity}");
                            return container;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"[ITEM SORTER] Error finding bag with space: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find an empty bag that can be used for a new category
        /// </summary>
        private static Container FindEmptyBag()
        {
            try
            {
                foreach (var container in Inventory.Containers)
                {
                    if (container.IsOpen && container.Items.Count == 0)
                    {
                        return container;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error($"[ITEM SORTER] Error finding empty bag: {ex.Message}");
                return null;
            }
        }
    }
}

