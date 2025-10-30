using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Clientless;
using AOSharp.Clientless.Logging;
using Bankbot.Core;
using Bankbot.Modules;

namespace Bankbot.Templates
{
    /// <summary>
    /// Generates dynamic storage help templates showing stored items with GET buttons
    /// </summary>
    public static class StorageHelpTemplate
    {
        // Cache for the generated storage window content
        private static string _cachedStorageWindow = null;
        private static DateTime _cacheLastUpdated = DateTime.MinValue;
        private static readonly object _cacheLock = new object();
        private static bool _startupCacheCompleted = false;

        // Cache the actual item data so we don't call ItemTracker.GetStoredItems() every time
        private static List<StoredItem> _cachedItemData = null;

        /// <summary>
        /// Invalidate the cached storage window, forcing regeneration on next request
        /// </summary>
        public static void InvalidateCache()
        {
            lock (_cacheLock)
            {
                _cachedStorageWindow = null;
                _cachedItemData = null;
                _cacheLastUpdated = DateTime.MinValue;
                _startupCacheCompleted = false;
                Logger.Information("[STORAGE TEMPLATE] 🗑️ Cache invalidated");
            }
        }

        /// <summary>
        /// Pre-generate and cache the storage window on startup for instant access
        /// </summary>
        public static void PreCacheStorageWindow()
        {
            Task.Run(async () =>
            {
                try
                {
                    Logger.Information("🚀 Starting startup cache generation...");

                    // Wait for bag re-indexing to complete (happens around 5 seconds after startup)
                    await Task.Delay(10000);

                    Logger.Information("🔄 Generating startup cache...");

                    // Generate the cache
                    var cachedContent = GenerateStorageWindow();

                    Logger.Information($"✅ Startup cache completed! Storage window ready for instant access ({cachedContent.Length} chars)");
                }
                catch (Exception ex)
                {
                    Logger.Error($"❌ Error during startup cache generation: {ex.Message}");
                }
            });
        }



        /// <summary>
        /// Generate the main storage window showing all stored items
        /// </summary>
        /// <returns>HTML-formatted storage window content</returns>
        public static string GenerateStorageWindow()
        {
            try
            {
                // Check if we have a valid cached version
                lock (_cacheLock)
                {
                    if (_cachedStorageWindow != null)
                    {
                        var cacheAge = DateTime.Now - _cacheLastUpdated;
                        Logger.Information($"[STORAGE TEMPLATE] ⚡ Returning cached storage window (age: {cacheAge.TotalSeconds:F1}s)");
                        return _cachedStorageWindow;
                    }
                }

                Logger.Information("[STORAGE TEMPLATE] 🔄 Starting GenerateStorageWindow - cache miss");

                // Get cached item data or force a fresh scan if cache is empty
                List<StoredItem> allItems;
                lock (_cacheLock)
                {
                    if (_cachedItemData != null)
                    {
                        Logger.Information($"[STORAGE TEMPLATE] ⚡ Using cached item data ({_cachedItemData.Count} items)");
                        allItems = new List<StoredItem>(_cachedItemData);
                    }
                    else
                    {
                        Logger.Information("[STORAGE TEMPLATE] 🔄 No cached item data, forcing fresh scan");
                        var storedItems = ItemTracker.GetStoredItems(true); // Include bags
                        allItems = storedItems.Cast<StoredItem>().ToList();
                        _cachedItemData = new List<StoredItem>(allItems);
                        Logger.Information($"[STORAGE TEMPLATE] 💾 Cached {allItems.Count} items for future use");
                    }
                }

                var botName = Client.CharacterName;
                Logger.Information($"[STORAGE TEMPLATE] Bot name: {botName}");

                // Separate bags and bag contents
                var bags = allItems.Where(item => IsContainer(item)).OrderBy(b => b.Name).ThenBy(b => b.ItemInstance).ToList();
                var bagContents = allItems.Where(item => !IsContainer(item) && !string.IsNullOrEmpty(item.SourceBagName)).ToList();
                var looseItems = allItems.Where(item => !IsContainer(item) && string.IsNullOrEmpty(item.SourceBagName)).OrderBy(i => i.Name).ToList();

                Logger.Information($"[STORAGE TEMPLATE] Found {bags.Count} bags, {bagContents.Count} bag contents, {looseItems.Count} loose items");

                // Debug: Log bag and bag content details
                foreach (var bag in bags)
                {
                    Logger.Information($"[STORAGE TEMPLATE] Bag: '{bag.Name}' Instance: {bag.ItemInstance} IsContainer: {bag.IsContainer}");
                }
                foreach (var bagContent in bagContents)
                {
                    Logger.Information($"[STORAGE TEMPLATE] Bag Content: '{bagContent.Name}' SourceBag: '{bagContent.SourceBagName}' SourceInstance: {bagContent.SourceBagInstance}");
                }

                // Build the proper Anarchy Online formatted window content
                var content = new StringBuilder();

                // Header with proper formatting
                content.AppendLine("<font color=#00D4FF>STORAGE BOT - ITEM CATALOG</font>");
                content.AppendLine($"<font color=#00D4FF>Bot: {botName}</font>");
                content.AppendLine();
                content.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                content.AppendLine();

                // Add inventory space information above the stored bags
                var inventoryStats = ItemTracker.GetInventorySpaceStats();
                var bagStats = ItemTracker.GetBagSpaceStats();

                content.AppendLine("<font color=#00D4FF>=== INVENTORY SPACE ===</font>");
                content.AppendLine($"Total inventory space: {inventoryStats.totalSlots - inventoryStats.usedSlots}/{inventoryStats.totalSlots} slots available");
                content.AppendLine($"Total bag space: {bagStats.totalBagSlots - bagStats.usedBagSlots}/{bagStats.totalBagSlots} slots available");
                content.AppendLine();
                content.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                content.AppendLine();

                // Display bags with their contents in hierarchical format
                if (bags.Any())
                {
                    content.AppendLine("<font color=#00D4FF>=== STORED BAGS ===</font>");

                    foreach (var bag in bags)
                    {
                        // Count items in this bag
                        var itemsInBag = bagContents.Where(item =>
                            item.SourceBagName == bag.Name &&
                            item.SourceBagInstance == bag.ItemInstance).ToList();

                        Logger.Information($"[STORAGE TEMPLATE] Bag '{bag.Name}' (Instance: {bag.ItemInstance}) has {itemsInBag.Count} items");

                        // Debug: Show which bag contents we're trying to match against
                        if (itemsInBag.Count == 0 && bagContents.Any())
                        {
                            Logger.Information($"[STORAGE TEMPLATE] No items found for bag '{bag.Name}' (Instance: {bag.ItemInstance})");
                            Logger.Information($"[STORAGE TEMPLATE] Available bag contents:");
                            foreach (var availableContent in bagContents.Take(5)) // Show first 5 for debugging
                            {
                                Logger.Information($"[STORAGE TEMPLATE]   - '{availableContent.Name}' from bag '{availableContent.SourceBagName}' (Instance: {availableContent.SourceBagInstance})");
                            }
                        }

                        // Clean the bag name for both command and display if it has single quotes
                        string cleanBagName = bag.Name;
                        if (cleanBagName.Contains("'"))
                        {
                            cleanBagName = cleanBagName.Replace("'", "");
                        }

                        string getBagCommand = $"get {cleanBagName} {bag.ItemInstance}";

                        // Bag name with GET button - no color formatting as per user preference
                        content.AppendLine($"{cleanBagName} - <a href='chatcmd:///tell {botName} {getBagCommand}'>[GET BAG]</a>");

                        // Show items in this bag with indentation
                        foreach (var item in itemsInBag.OrderBy(i => i.Name))
                        {
                            // Create item reference link with proper escaping for text:// content
                            string itemRefLink = $"<a href='itemref://{item.Id}/{item.ItemInstance}/{item.QualityLevel}'>{item.Name}</a>";

                            // Add quality level if it's greater than 0
                            if (item.QualityLevel > 0)
                                itemRefLink += $" QL{item.QualityLevel}";

                            // Add stack count if it's greater than 1
                            if (item.StackCount > 1)
                                itemRefLink += $" x{item.StackCount}";

                            // Clean the base item name for the command (without QL/stack info)
                            string cleanItemName = CleanItemNameForCommand(item.Name);

                            string getItemCommand = $"get {cleanItemName}";
                            string viewItemCommand = $"view {item.Id} {item.ItemInstance}";

                            Logger.Information($"[STORAGE TEMPLATE] Item: '{item.Name}' -> ItemRef: '{itemRefLink}' -> CleanName: '{cleanItemName}' -> Command: '{getItemCommand}'");

                            content.AppendLine($"  - {itemRefLink} <a href='chatcmd:///tell {botName} {getItemCommand}'>[GET]</a> <a href='chatcmd:///tell {botName} {viewItemCommand}'>[VIEW]</a>");
                        }

                        content.AppendLine(); // Empty line between bags
                    }
                }

                // Show loose items (not in bags) if any
                if (looseItems.Any())
                {
                    content.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                    content.AppendLine("<font color=#00D4FF>=== LOOSE ITEMS ===</font>");
                    foreach (var item in looseItems)
                    {
                        // Create item reference link with proper escaping for text:// content
                        string itemRefLink = $"<a href='itemref://{item.Id}/{item.ItemInstance}/{item.QualityLevel}'>{item.Name}</a>";

                        // Add quality level if it's greater than 0
                        if (item.QualityLevel > 0)
                            itemRefLink += $" QL{item.QualityLevel}";

                        // Add stack count if it's greater than 1
                        if (item.StackCount > 1)
                            itemRefLink += $" x{item.StackCount}";

                        // Clean the base item name for the command (without QL/stack info)
                        string cleanItemName = CleanItemNameForCommand(item.Name);

                        string getItemCommand = $"get {cleanItemName}";
                        string viewItemCommand = $"view {item.Id} {item.ItemInstance}";
                        content.AppendLine($"{itemRefLink} <a href='chatcmd:///tell {botName} {getItemCommand}'>[GET]</a> <a href='chatcmd:///tell {botName} {viewItemCommand}'>[VIEW]</a>");
                    }
                    content.AppendLine();
                }

                // Add inventory space info
                content.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                content.AppendLine($"<font color=#888888>Total Items Stored: {allItems.Count}</font>");

                // Now wrap the content in the proper text:// link structure
                string windowContent = $@"<a href=""text://{content.ToString()}"">Storage Bot - Item Catalog</a>";

                Logger.Information($"[STORAGE TEMPLATE] Final window content length: {windowContent.Length}");
                Logger.Information($"[STORAGE TEMPLATE] Window preview: {windowContent.Substring(0, Math.Min(100, windowContent.Length))}");

                // Cache the generated content
                lock (_cacheLock)
                {
                    _cachedStorageWindow = windowContent;
                    _cacheLastUpdated = DateTime.Now;
                    Logger.Information("[STORAGE TEMPLATE] 💾 Storage window cached successfully");
                }

                return windowContent;
            }
            catch (Exception ex)
            {
                Logger.Information($"[STORAGE TEMPLATE] Error in GenerateStorageWindow: {ex.Message}");
                return "Error generating storage window";
            }
        }

        /// <summary>
        /// Generate a paginated storage window showing stored items
        /// </summary>
        /// <param name="pageNumber">Page number to display (1-based)</param>
        /// <returns>HTML-formatted storage window content for the specified page</returns>
        public static string GenerateStorageWindowPaginated(int pageNumber)
        {
            try
            {
                Logger.Information($"[STORAGE TEMPLATE] 🔄 Starting GenerateStorageWindowPaginated - page {pageNumber}");

                // Get all items (same as original method)
                var allItems = ItemTracker.GetStoredItems(true).Cast<StoredItem>().ToList();
                if (allItems == null || allItems.Count == 0)
                {
                    return "<a href=\"text://No items found in storage.\">No items found in storage.</a>";
                }

                string botName = DynelManager.LocalPlayer?.Name ?? "Bankbot";

                // Separate bags and bag contents (EXACT SAME AS ORIGINAL)
                var bags = allItems.Where(item => item.IsContainer).ToList();
                var bagContents = allItems.Where(item => !item.IsContainer && item.IsFromBag).ToList();
                var looseItems = allItems.Where(item => !item.IsContainer && !item.IsFromBag).ToList();

                // Build content sections (bags with their items)
                var contentSections = new List<string>();

                // Build bag sections
                foreach (var bag in bags)
                {
                    var sectionContent = new StringBuilder();

                    var itemsInBag = bagContents.Where(item =>
                        item.SourceBagName == bag.Name &&
                        item.SourceBagInstance == bag.ItemInstance).ToList();

                    string cleanBagName = bag.Name.Replace("'", "");
                    string getBagCommand = $"get {cleanBagName} {bag.ItemInstance}";

                    sectionContent.AppendLine($"{cleanBagName} - <a href='chatcmd:///tell {botName} {getBagCommand}'>[GET BAG]</a>");

                    foreach (var item in itemsInBag.OrderBy(i => i.Name))
                    {
                        string itemRefLink = $"<a href='itemref://{item.Id}/{item.ItemInstance}/{item.QualityLevel}'>{item.Name}</a>";
                        if (item.QualityLevel > 0)
                            itemRefLink += $" QL{item.QualityLevel}";
                        if (item.StackCount > 1)
                            itemRefLink += $" x{item.StackCount}";

                        string cleanItemName = CleanItemNameForCommand(item.Name);
                        string getItemCommand = $"get {cleanItemName}";
                        string viewItemCommand = $"view {item.Id} {item.ItemInstance}";

                        sectionContent.AppendLine($"  - {itemRefLink} <a href='chatcmd:///tell {botName} {getItemCommand}'>[GET]</a> <a href='chatcmd:///tell {botName} {viewItemCommand}'>[VIEW]</a>");
                    }
                    sectionContent.AppendLine();

                    contentSections.Add(sectionContent.ToString());
                }

                // Add loose items section if any
                if (looseItems.Any())
                {
                    var looseSection = new StringBuilder();
                    looseSection.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                    looseSection.AppendLine("<font color=#00D4FF>=== LOOSE ITEMS ===</font>");
                    foreach (var item in looseItems)
                    {
                        string itemRefLink = $"<a href='itemref://{item.Id}/{item.ItemInstance}/{item.QualityLevel}'>{item.Name}</a>";
                        if (item.QualityLevel > 0)
                            itemRefLink += $" QL{item.QualityLevel}";
                        if (item.StackCount > 1)
                            itemRefLink += $" x{item.StackCount}";

                        string cleanItemName = CleanItemNameForCommand(item.Name);
                        string getItemCommand = $"get {cleanItemName}";
                        string viewItemCommand = $"view {item.Id} {item.ItemInstance}";
                        looseSection.AppendLine($"{itemRefLink} <a href='chatcmd:///tell {botName} {getItemCommand}'>[GET]</a> <a href='chatcmd:///tell {botName} {viewItemCommand}'>[VIEW]</a>");
                    }
                    looseSection.AppendLine();
                    contentSections.Add(looseSection.ToString());
                }

                // Paginate sections (not individual items)
                const int sectionsPerPage = 10; // Show 10 bags per page
                int totalPages = (int)Math.Ceiling((double)contentSections.Count / sectionsPerPage);
                pageNumber = Math.Max(1, Math.Min(pageNumber, totalPages));

                int startSection = (pageNumber - 1) * sectionsPerPage;
                var pageSections = contentSections.Skip(startSection).Take(sectionsPerPage);

                // Build final content
                var content = new StringBuilder();
                content.AppendLine("<font color=#00D4FF>STORAGE BOT - ITEM CATALOG</font>");
                content.AppendLine($"<font color=#00D4FF>Bot: {botName}</font>");
                content.AppendLine($"<font color=#FFFF00>Page {pageNumber} of {totalPages}</font>");
                content.AppendLine();
                content.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                content.AppendLine();

                var inventoryStats = ItemTracker.GetInventorySpaceStats();
                var bagStats = ItemTracker.GetBagSpaceStats();
                content.AppendLine("<font color=#00D4FF>=== INVENTORY SPACE ===</font>");
                content.AppendLine($"Total inventory space: {inventoryStats.totalSlots - inventoryStats.usedSlots}/{inventoryStats.totalSlots} slots available");
                content.AppendLine($"Total bag space: {bagStats.totalBagSlots - bagStats.usedBagSlots}/{bagStats.totalBagSlots} slots available");
                content.AppendLine();
                content.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                content.AppendLine();
                content.AppendLine("<font color=#00D4FF>=== STORED BAGS ===</font>");

                foreach (var section in pageSections)
                {
                    content.Append(section);
                }

                // Navigation
                content.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                if (pageNumber > 1)
                {
                    content.AppendLine($"<a href='chatcmd:///tell {botName} list {pageNumber - 1}'>← Previous Page</a>");
                }
                if (pageNumber < totalPages)
                {
                    content.AppendLine($"<a href='chatcmd:///tell {botName} list {pageNumber + 1}'>Next Page →</a>");
                }

                content.AppendLine("<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>");
                content.AppendLine($"<font color=#888888>Total Items Stored: {allItems.Count}</font>");

                string windowContent = $@"<a href=""text://{content.ToString()}"">Storage Bot - Item Catalog (Page {pageNumber}/{totalPages})</a>";

                Logger.Information($"[STORAGE TEMPLATE] ✅ Generated page {pageNumber}");
                return windowContent;
            }
            catch (Exception ex)
            {
                Logger.Information($"[STORAGE TEMPLATE] Error in GenerateStorageWindowPaginated: {ex.Message}");
                return "Error generating paginated storage window";
            }
        }

        /// <summary>
        /// Check if an item is a container/bag
        /// </summary>
        private static bool IsContainer(StoredItem item)
        {
            // Use the proper IsContainer property that's set based on game's IdentityType.Container
            return item.IsContainer;
        }

        /// <summary>
        /// Clean item name for GET command by removing quality level prefixes
        /// </summary>
        private static string CleanItemNameForCommand(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return itemName;

            // Remove quality level prefixes like "16 - 31 " from "16 - 31 NCU Memory"
            // Pattern: digits, optional space, dash, optional space, digits, space
            var cleanName = System.Text.RegularExpressions.Regex.Replace(itemName, @"^\d+\s*-\s*\d+\s+", "");

            // Remove single quotes that might cause command issues
            if (cleanName.Contains("'"))
            {
                cleanName = cleanName.Replace("'", "");
            }

            return cleanName;
        }
    }
}
