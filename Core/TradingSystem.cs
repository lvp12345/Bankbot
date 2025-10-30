using AOSharp.Clientless.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Clientless;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using Bankbot.Modules;
using Bankbot.Templates;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles automated trading system for Bankbot
    /// </summary>
    public static class TradingSystem
    {
        private static bool _initialized = false;
        private static Dictionary<uint, PendingTrade> _pendingTrades = new Dictionary<uint, PendingTrade>();

        // Track items given by bot during current trade
        private static Dictionary<uint, List<string>> _itemsGivenByBot = new Dictionary<uint, List<string>>();

        // Track inventory before each trade to detect what was actually received
        private static List<uint> _inventoryBeforeTrade = new List<uint>();

        // Track items given to player during current trade (for trade logging)
        private static List<Item> _itemsGivenInCurrentTrade = new List<Item>();

        // Lock to prevent concurrent item movements
        private static readonly object _itemMovementLock = new object();

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Subscribe to trade events for auto-acceptance
                Trade.TradeOpened += OnTradeOpened;
                Trade.TradeStatusChanged += OnTradeStatusChanged;

                // Try to subscribe to trade item events if they exist
                try
                {
                    // Check if there are any item-related trade events we can hook into
                    var tradeType = typeof(Trade);
                    var events = tradeType.GetEvents();
                    Logger.Information($"[TRADING SYSTEM] Available Trade events: {string.Join(", ", events.Select(e => e.Name))}");
                }
                catch (Exception ex)
                {
                    Logger.Information($"[TRADING SYSTEM] Error checking Trade events: {ex.Message}");
                }

                _initialized = true;
                Logger.Information("[TRADING SYSTEM] TradingSystem initialized with trade auto-acceptance");
                ItemTracker.LogTransaction("SYSTEM", "TradingSystem initialized with trade auto-acceptance");
            }
            catch (Exception ex)
            {
                Logger.Information($"Error initializing TradingSystem: {ex.Message}");
            }
        }

        public static void Cleanup()
        {
            try
            {
                Trade.TradeOpened -= OnTradeOpened;
                Trade.TradeStatusChanged -= OnTradeStatusChanged;
                _initialized = false;
                Logger.Information("[TRADING SYSTEM] TradingSystem cleanup completed");
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error during cleanup: {ex.Message}");
            }
        }



        /// <summary>
        /// Handle trade opened events
        /// </summary>
        private static void OnTradeOpened(Identity target)
        {
            try
            {
                Logger.Information($"[TRADING SYSTEM] Trade opened with {target.Instance}");

                // Check org restrictions for trading
                var tradeTarget = Trade.CurrentTarget;
                if (tradeTarget != Identity.None)
                {
                    var targetPlayer = DynelManager.Players.FirstOrDefault(p => p.Identity.Instance == tradeTarget.Instance);
                    if (targetPlayer != null)
                    {
                        // Use synchronous org check for trade opening
                        bool isAllowed = OrgLockoutConfig.IsPlayerOrgAllowed(targetPlayer.Identity.Instance, targetPlayer.Name);
                        if (!isAllowed)
                        {
                            Logger.Information($"[TRADING SYSTEM] Player {targetPlayer.Name} organization not allowed for trading");
                            Trade.Decline();
                            return;
                        }
                        Logger.Information($"[TRADING SYSTEM] Player {targetPlayer.Name} organization allowed for trading");
                    }
                }

                // Capture inventory before trade to detect received items later
                CaptureInventoryBeforeTrade();

                // Clear items given tracking for new trade
                _itemsGivenInCurrentTrade.Clear();
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error in OnTradeOpened: {ex.Message}");
            }
        }



        /// <summary>
        /// Handle trade status changed events - this is where auto-acceptance happens
        /// </summary>
        private static void OnTradeStatusChanged(TradeStatus status)
        {
            try
            {
                Logger.Information($"[TRADING SYSTEM] Trade status changed to: {status}");
                ItemTracker.LogTransaction("SYSTEM", $"Trade status changed to: {status}");

                switch (status)
                {
                    case TradeStatus.Accept:
                        Logger.Information("[TRADING SYSTEM] Player accepted trade - checking for items in trade window");

                        // Check for items in trade window and apply Charges detection
                        CheckTradeWindowItems();

                        // Check if we have enough inventory space for incoming items
                        // Reserve at least 1 slot for GET command operations (moving items from bags)
                        int incomingItemCount = Trade.TargetWindowCache.Items.Count;
                        int freeSlots = Inventory.NumFreeSlots;
                        int reservedSlots = 1; // Always keep 1 slot free for GET operations
                        int availableSlots = freeSlots - reservedSlots;

                        Logger.Information($"[TRADING SYSTEM] Incoming items: {incomingItemCount}, Free inventory slots: {freeSlots}, Available (after reserve): {availableSlots}");

                        if (incomingItemCount > availableSlots)
                        {
                            Logger.Information($"[TRADING SYSTEM] NOT ENOUGH SPACE! Need {incomingItemCount} slots but only have {availableSlots} available (reserving {reservedSlots} for operations)");
                            ItemTracker.LogTransaction("SYSTEM", $"Trade declined - insufficient space: need {incomingItemCount} slots, have {availableSlots} available");

                            // Decline the trade
                            Trade.Decline();

                            // Notify the player
                            var targetPlayer = DynelManager.Players.FirstOrDefault(p => p.Identity == Trade.CurrentTarget);
                            if (targetPlayer != null)
                            {
                                SendPrivateMessage(targetPlayer.Name, $"Trade declined - insufficient inventory space. I need {incomingItemCount} free slots but only have {availableSlots} available (keeping {reservedSlots} reserved). Please remove some items from the trade.");
                            }

                            return;
                        }

                        Logger.Information($"[TRADING SYSTEM] Inventory space check passed - proceeding with trade");

                        // For "get" trades (bot giving items to player), use Trade.Confirm() like Craftbot
                        Task.Run(async () =>
                        {
                            await Task.Delay(200); // Reduced delay
                            try
                            {
                                Trade.Confirm();
                                Logger.Information("[TRADING SYSTEM] Bot confirmed trade successfully");

                                // After confirm, we need to accept again to complete the trade
                                await Task.Delay(200);
                                Trade.Accept();
                                Logger.Information("[TRADING SYSTEM] Bot accepted trade to complete");
                            }
                            catch (Exception ex)
                            {
                                Logger.Information($"[TRADING SYSTEM] Error confirming trade: {ex.Message}");
                            }
                        });
                        break;

                    case TradeStatus.Finished:
                        Logger.Information("[TRADING SYSTEM] Trade completed successfully");
                        ItemTracker.LogTransaction("SYSTEM", "Trade completed successfully");

                        // Process completed trade like Craftbot does
                        Task.Run(async () =>
                        {
                            await Task.Delay(1000); // Wait for items to appear in inventory
                            try
                            {
                                ItemTracker.LogTransaction("SYSTEM", "Starting trade completion processing");

                                // Check for any new items received in trade and process them
                                await Task.Delay(1000); // Additional delay for items to settle
                                ItemTracker.LogTransaction("SYSTEM", "About to call ProcessReceivedItemsFromTrade");
                                ProcessReceivedItemsFromTrade();

                                // Find the completed trade (for GET commands)
                                var completedTrade = _pendingTrades.Values.FirstOrDefault();
                                if (completedTrade != null)
                                {
                                    Logger.Information($"[TRADING SYSTEM] Processing completed GET trade for {completedTrade.PlayerName}");

                                    // Capture items that were received from the player
                                    var itemsReceived = GetItemsReceivedFromTrade();

                                    // For GET trades, we gave items to the player - use ALL items given during this trade
                                    var itemsGiven = new List<Item>(_itemsGivenInCurrentTrade);
                                    ItemTracker.LogTransaction("SYSTEM", $"Trade completion: Found {itemsGiven.Count} items given during this trade");

                                    // Log the complete trade with both received and given items
                                    TradeLogger.LogCompleteTrade(completedTrade.PlayerName, itemsReceived, itemsGiven);

                                    // Invalidate both caches since items were given away
                                    ItemTracker.InvalidateStoredItemsCache();
                                    StorageHelpTemplate.InvalidateCache();
                                }
                                else
                                {
                                    // No pending trade - this means player gave items to bot without GET command
                                    Logger.Information("[TRADING SYSTEM] Processing trade where player gave items to bot (no GET command)");

                                    // Get the actual player name from the trade target
                                    string playerName = "Unknown Player";
                                    try
                                    {
                                        if (Trade.CurrentTarget != Identity.None)
                                        {
                                            var targetPlayer = DynelManager.Players.FirstOrDefault(p =>
                                                p.Identity.Instance == Trade.CurrentTarget.Instance);
                                            if (targetPlayer != null)
                                            {
                                                playerName = targetPlayer.Name;
                                                ItemTracker.LogTransaction("SYSTEM", $"Found trade partner: {playerName} (ID: {targetPlayer.Identity.Instance})");
                                            }
                                            else
                                            {
                                                ItemTracker.LogTransaction("SYSTEM", $"Trade target found but player not in DynelManager: {Trade.CurrentTarget.Instance}");
                                            }
                                        }
                                        else
                                        {
                                            ItemTracker.LogTransaction("SYSTEM", "No current trade target found");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        ItemTracker.LogTransaction("SYSTEM", $"Error getting trade partner name: {ex.Message}");
                                    }

                                    // Capture items that were received from the player
                                    var itemsReceived = GetItemsReceivedFromTrade();

                                    if (itemsReceived.Any())
                                    {
                                        // Log trade with received items only (no items given by bot)
                                        TradeLogger.LogCompleteTrade(playerName, itemsReceived, new List<Item>());
                                        Logger.Information($"[TRADING SYSTEM] Logged trade with {itemsReceived.Count} items received from {playerName}");

                                        // Invalidate the storage list cache since items were received
                                        StorageHelpTemplate.InvalidateCache();

                                        // Auto-sort received items if enabled
                                        var charSettings = Bankbot.Config.CharSettings.ContainsKey(Client.CharacterName)
                                            ? Bankbot.Config.CharSettings[Client.CharacterName]
                                            : new CharacterSettings();

                                        if (charSettings.AutoSortEnabled)
                                        {
                                            Logger.Information("[TRADING SYSTEM] Starting auto-sort after trade...");
                                            await Task.Delay(1000); // Wait for items to settle
                                            await ItemSorter.SortAllItems();
                                            Logger.Information("[TRADING SYSTEM] Auto-sort complete");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Information($"[TRADING SYSTEM] Error processing completed trade: {ex.Message}");
                            }
                        });

                        ClearStalePendingTrades();
                        ClearTradeTracking();
                        break;

                    case TradeStatus.None:
                        Logger.Information("[TRADING SYSTEM] Trade cancelled/declined");
                        ClearStalePendingTrades();
                        ClearTradeTracking();
                        break;

                    case TradeStatus.Confirm:
                        Logger.Information("[TRADING SYSTEM] Trade confirm status received - trade should complete");
                        // Don't call Trade.Confirm() here as it creates endless loop!
                        // This status means the confirmation is happening
                        break;

                    default:
                        Logger.Information($"[TRADING SYSTEM] Unhandled trade status: {status}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error in OnTradeStatusChanged: {ex.Message}");
            }
        }

        /// <summary>
        /// Initiate a trade with a player to give them an item
        /// </summary>
        public static void InitiateTrade(string playerName, object item)
        {
            try
            {
                Logger.Information($"[TRADING SYSTEM] Initiating GET trade for {playerName}");

                // Clear any stale pending trades first
                ClearStalePendingTrades();

                // Find the player
                var targetPlayer = DynelManager.Players.FirstOrDefault(p =>
                    p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

                if (targetPlayer == null)
                {
                    Logger.Information($"[TRADING SYSTEM] Player {playerName} not found for GET trade");
                    SendPrivateMessage(playerName, "You must be near the bot to retrieve items. Please come closer and try again.");
                    return;
                }

                // Check distance
                // float distance = targetPlayer.Position.DistanceFrom(DynelManager.LocalPlayer.Position); // Position not available in clientless
                // if (distance > 10f) // Distance check disabled in clientless mode
                {
                    Logger.Information($"[TRADING SYSTEM] Player {playerName} distance check skipped in clientless mode");
                    // SendPrivateMessage(playerName, $"You're too far away ({distance:F1}m). Please come closer and try again."); // Distance check disabled in clientless
                    // return; // Distance check disabled in clientless mode
                }

                // Note: Org lockout check is handled by the calling code (GET handler) using async method

                // Cast the item to StoredItem to get the actual Item
                if (item is StoredItem storedItem)
                {
                    uint playerId = (uint)targetPlayer.Identity.Instance;

                    // Check if there's already an open trade with this specific player
                    // Use the actual trade window state, not just pending trade records
                    bool tradeAlreadyOpen = Trade.CurrentTarget != Identity.None &&
                                          Trade.CurrentTarget.Instance == targetPlayer.Identity.Instance;

                    Logger.Information($"[TRADING SYSTEM] Trade status check - TradeTarget.HasValue: {Trade.CurrentTarget != Identity.None}, " +
                                                 $"Target matches player: {(Trade.CurrentTarget != Identity.None ? Trade.CurrentTarget.Instance == targetPlayer.Identity.Instance : false)}, " +
                                                 $"Trade window open with player: {tradeAlreadyOpen}");

                    if (tradeAlreadyOpen)
                    {
                        Logger.Information($"[TRADING SYSTEM] Trade already open with {playerName}, adding item directly");



                        // Add item to existing trade immediately
                        Task.Run(async () =>
                        {
                            try
                            {
                                Item actualItem = FindActualItemInInventory(storedItem);
                                if (actualItem != null)
                                {
                                    Logger.Information($"[TRADING SYSTEM] Adding {actualItem.Name} to existing trade");
                                    await Task.Delay(200); // Small delay to ensure item movement

                                    // Add item to trade
                                    try
                                    {
                                        Trade.AddItem(actualItem.Slot);
                                        Logger.Information($"[TRADING SYSTEM] Successfully added {actualItem.Name} to trade");

                                        // Track item for trade logging
                                        _itemsGivenInCurrentTrade.Add(actualItem);
                                    }
                                    catch (Exception addEx)
                                    {
                                        Logger.Information($"[TRADING SYSTEM] Error adding item to trade: {addEx.Message}");
                                        SendPrivateMessage(playerName, "Error adding item to trade. Please try again.");
                                        return;
                                    }

                                    // Track item given by bot for comprehensive logging
                                    TrackItemGivenByBot(playerId, actualItem.Name);

                                    // Update the pending trade with the actual item that was added
                                    if (_pendingTrades.ContainsKey(playerId))
                                    {
                                        _pendingTrades[playerId].ActualItem = actualItem;
                                    }

                                    Logger.Information($"[TRADING SYSTEM] Item {actualItem.Name} added to existing trade");
                                    SendPrivateMessage(playerName, $"Added {actualItem.Name} to trade.");
                                }
                                else
                                {
                                    SendPrivateMessage(playerName, "Item not found.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Information($"[TRADING SYSTEM] Error adding item to existing trade: {ex.Message}");
                                SendPrivateMessage(playerName, "Error adding item to trade.");
                            }
                        });
                    }
                    else
                    {
                        Logger.Information($"[TRADING SYSTEM] No existing trade, opening new trade with {playerName}");

                        // Clear any stale pending trade record for this player first
                        if (_pendingTrades.ContainsKey(playerId))
                        {
                            Logger.Information($"[TRADING SYSTEM] Clearing stale pending trade record for {playerName}");
                            _pendingTrades.Remove(playerId);
                        }



                        // Create a pending trade record for new trade
                        var pendingTrade = new PendingTrade
                        {
                            PlayerName = playerName,
                            PlayerId = playerId,
                            RequestedItem = storedItem.Name,
                            ActualItem = storedItem,
                            InitiatedAt = DateTime.Now
                        };

                        _pendingTrades[playerId] = pendingTrade;

                        // Open new trade with the player
                        Trade.Open(targetPlayer.Identity);
                        Logger.Information($"[TRADING SYSTEM] New GET trade opened with {playerName} for {storedItem.Name}");
                        SendPrivateMessage(playerName, $"Opening trade to give you: {storedItem.Name}");

                        // Trade acceptance is now handled by TradeStatusChanged events

                        // Add item to the new trade after a short delay
                        Task.Run(async () =>
                        {
                            try
                            {
                                await Task.Delay(500); // Wait for trade window to fully open
                                Item actualItem = FindActualItemInInventory(storedItem);
                                if (actualItem != null)
                                {
                                    Logger.Information($"[TRADING SYSTEM] Adding {actualItem.Name} to new trade");

                                    try
                                    {
                                        Trade.AddItem(actualItem.Slot);
                                        Logger.Information($"[TRADING SYSTEM] Successfully added {actualItem.Name} to new trade");

                                        // Track item given by bot for comprehensive logging
                                        TrackItemGivenByBot(playerId, actualItem.Name);

                                        // Track item for trade logging
                                        _itemsGivenInCurrentTrade.Add(actualItem);

                                        // Update the pending trade with the actual item that was added
                                        if (_pendingTrades.ContainsKey(playerId))
                                        {
                                            _pendingTrades[playerId].ActualItem = actualItem;
                                        }

                                        SendPrivateMessage(playerName, $"Added {actualItem.Name} to trade. Please accept when ready.");
                                    }
                                    catch (Exception addEx)
                                    {
                                        Logger.Information($"[TRADING SYSTEM] Error adding item to new trade: {addEx.Message}");
                                        SendPrivateMessage(playerName, "Error adding item to trade. Please try again.");
                                    }
                                }
                                else
                                {
                                    Logger.Information($"[TRADING SYSTEM] Could not find actual item {storedItem.Name} in inventory");
                                    SendPrivateMessage(playerName, "Item not found in inventory. Please try again.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Information($"[TRADING SYSTEM] Error in new trade item addition: {ex.Message}");
                                SendPrivateMessage(playerName, "Error setting up trade. Please try again.");
                            }
                        });
                    }
                }
                else
                {
                    Logger.Information($"[TRADING SYSTEM] Invalid item type for GET trade: {item?.GetType().Name}");
                    SendPrivateMessage(playerName, "Error retrieving item. Please try again.");
                }

                ItemTracker.LogTransaction(playerName, $"GET TRADE INITIATED: {item}");
            }
            catch (Exception ex)
            {
                Logger.Information($"Error initiating trade with {playerName}: {ex.Message}");
                SendPrivateMessage(playerName, "Error opening trade. Please try again.");
            }
        }

        /// <summary>
        /// Initiate a return trade to give a specific item back to a player
        /// </summary>
        public static void InitiateReturnTrade(uint playerId, string playerName, Item item)
        {
            try
            {
                Logger.Information($"[TRADING SYSTEM] Initiating return trade for {item.Name} to {playerName}");

                // Create a pending trade record
                var pendingTrade = new PendingTrade
                {
                    PlayerName = playerName,
                    PlayerId = playerId,
                    RequestedItem = item.Name,
                    ActualItem = item,
                    InitiatedAt = DateTime.Now
                };

                _pendingTrades[playerId] = pendingTrade;

                // Find the player
                var targetPlayer = DynelManager.Players.FirstOrDefault(p =>
                    p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

                if (targetPlayer != null)
                {
                    // Open trade with the player
                    Trade.Open(targetPlayer.Identity);
                    Logger.Information($"[TRADING SYSTEM] Return trade opened with {playerName}");
                }
                else
                {
                    Logger.Information($"[TRADING SYSTEM] Player {playerName} not found for return trade");
                    _pendingTrades.Remove(playerId);
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"Error initiating return trade with {playerName}: {ex.Message}");
                _pendingTrades.Remove(playerId);
            }
        }

        /// <summary>
        /// Check if there's a pending return trade for a player
        /// </summary>
        public static bool HasPendingReturnTrade(uint playerId)
        {
            return _pendingTrades.ContainsKey(playerId);
        }

        /// <summary>
        /// Clear any stale pending trades (for debugging)
        /// </summary>
        public static void ClearStalePendingTrades()
        {
            try
            {
                var staleTrades = _pendingTrades.Where(kvp =>
                    DateTime.Now - kvp.Value.InitiatedAt > TimeSpan.FromMinutes(5)).ToList();

                foreach (var staleTrade in staleTrades)
                {
                    Logger.Information($"[TRADING SYSTEM] Clearing stale pending trade for {staleTrade.Value.PlayerName}");
                    _pendingTrades.Remove(staleTrade.Key);
                }

                if (staleTrades.Any())
                {
                    Logger.Information($"[TRADING SYSTEM] Cleared {staleTrades.Count} stale pending trades");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error clearing stale trades: {ex.Message}");
            }
        }

        /// <summary>
        /// Get items that were received from the player in the trade
        /// </summary>
        private static List<Item> GetItemsReceivedFromTrade()
        {
            try
            {
                // Simple approach: just get loose items in inventory that aren't in bags
                var looseItems = Inventory.Items.Where(item =>
                    item.Slot.Type == IdentityType.Inventory &&
                    item.UniqueIdentity.Type != IdentityType.Container).ToList();

                Logger.Information($"[TRADING SYSTEM] Found {looseItems.Count} loose items in inventory");

                foreach (var item in looseItems)
                {
                    Logger.Information($"[TRADING SYSTEM] Loose item: {item.Name} (QL {item.Ql})");
                }

                return looseItems;
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error getting received items: {ex.Message}");
                return new List<Item>();
            }
        }

        /// <summary>
        /// Process any items received from trade and move them to bags
        /// </summary>
        private static void ProcessReceivedItemsFromTrade()
        {
            try
            {
                Logger.Information("[TRADING SYSTEM] Checking for received items to move to bags");
                ItemTracker.LogTransaction("SYSTEM", "ProcessReceivedItemsFromTrade started");

                // SCAN ALL INVENTORY ITEMS FOR CHARGES DETECTION
                Logger.Information("[TRADING SYSTEM] Scanning full inventory for items with charges:");
                ItemTracker.LogTransaction("SYSTEM", "Scanning full inventory for items with charges:");

                try
                {
                    ItemTracker.LogTransaction("SYSTEM", "Attempting to access Inventory.Items...");
                    var allInventoryItems = Inventory.Items?.ToList();

                    if (allInventoryItems == null)
                    {
                        ItemTracker.LogTransaction("SYSTEM", "ERROR: Inventory.Items is null!");
                        return;
                    }

                    ItemTracker.LogTransaction("SYSTEM", $"Found {allInventoryItems.Count} items in inventory");

                    foreach (var invItem in allInventoryItems)
                    {
                        try
                        {
                            ItemTracker.LogTransaction("SYSTEM", $"Processing item: {invItem?.Name ?? "NULL"}");

                            if (invItem == null)
                            {
                                ItemTracker.LogTransaction("SYSTEM", "  - Item is null, skipping");
                                continue;
                            }

                            // Use reflection to check for Charges property
                            var chargesProp = invItem.GetType().GetProperty("Charges");
                            int itemCharges = 1;
                            if (chargesProp != null)
                            {
                                var val = chargesProp.GetValue(invItem);
                                itemCharges = val is int ? (int)val : (val != null ? Convert.ToInt32(val) : 1);
                                ItemTracker.LogTransaction("SYSTEM", $"  - {invItem.Name} (QL {invItem.Ql}) with {itemCharges} charges (Slot {invItem.Slot.Instance})");
                            }
                            else
                            {
                                ItemTracker.LogTransaction("SYSTEM", $"  - {invItem.Name} (QL {invItem.Ql}) - NO CHARGES PROPERTY (Slot {invItem.Slot.Instance})");
                            }
                        }
                        catch (Exception ex)
                        {
                            ItemTracker.LogTransaction("SYSTEM", $"  - ERROR scanning item: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    ItemTracker.LogTransaction("SYSTEM", $"ERROR accessing inventory: {ex.Message}");
                }

                // Get all items currently in main inventory (not in bags)
                ItemTracker.LogTransaction("SYSTEM", "Getting loose items for bag movement...");
                var inventoryItems = Inventory.Items?.Where(item =>
                    item.Slot.Type == IdentityType.Inventory &&
                    item.UniqueIdentity.Type != IdentityType.Container)?.ToList() ?? new List<Item>();

                if (inventoryItems.Any())
                {
                    Logger.Information($"[TRADING SYSTEM] Found {inventoryItems.Count} loose items in inventory - moving to bags");

                    // Use ItemTracker to process and move these items
                    ItemTracker.ProcessReceivedItems(inventoryItems, "TradePartner");
                }
                else
                {
                    Logger.Information("[TRADING SYSTEM] No loose items found in inventory");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error processing received items: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear trade tracking data after trade completion
        /// </summary>
        private static void ClearTradeTracking()
        {
            try
            {
                _itemsGivenInCurrentTrade.Clear();
                _inventoryBeforeTrade.Clear();
                Logger.Information("[TRADING SYSTEM] Cleared trade tracking data");
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error clearing trade tracking: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the pending return trade for a player
        /// </summary>
        public static PendingTrade GetPendingReturnTrade(uint playerId)
        {
            return _pendingTrades.TryGetValue(playerId, out var trade) ? trade : null;
        }

        /// <summary>
        /// Complete a return trade
        /// </summary>
        public static void CompleteReturnTrade(uint playerId)
        {
            try
            {
                if (_pendingTrades.TryGetValue(playerId, out var trade))
                {
                    Logger.Information($"[TRADING SYSTEM] Completing return trade for {trade.PlayerName}");
                    _pendingTrades.Remove(playerId);
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"Error completing return trade: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle trade state changes for GET commands
        /// </summary>
//         private static void OnTradeStatusChanged(TradeStatus status)
//         {
//             try
//             {
//                 Logger.Information($"[TRADING SYSTEM] TradeStateChanged: status={status}"); // state and trader not available in clientless
// 
//                 if (status == TradeStatus.Finished) //                 if (state == TradeStatus.Opened && trader.Type == IdentityType.SimpleChar)                if (state == TradeStatus.Opened && trader.Type == IdentityType.SimpleChar) trader.Type == IdentityType.SimpleChar - trader not available in clientless
//                 {
//                     // uint playerId = (uint)trader.Instance; // trader not available in clientless
// 
//                     // Check if this is a GET trade
//                     // uint playerId = (uint)trader.Instance; // trader not available in clientless
//                     {
//                     // if (_pendingTrades.TryGetValue(playerId, out var pendingTrade)) // playerId not available in clientless
// 
//                         // Find the item and add it to trade
//                         // Logger.Information($"[TRADING SYSTEM] ActualItem type: {pendingTrade.ActualItem?.GetType().Name ?? "null"}"); // pendingTrade not available
// 
//                         // if (pendingTrade.ActualItem is StoredItem actualItem) // pendingTrade not available in clientless
//                         {
//                             Logger.Information($"[TRADING SYSTEM] ActualItem is StoredItem, calling FindActualItemInInventory");
// 
//                             // Use async approach to handle item movement and trade addition properly
//                             Task.Run(async () =>
//                             {
//                                 try
//                                 {
//                             // var foundItem = FindItemInInventory(storedItem); // storedItem not available in clientless
// 
//                             // if (foundItem != null) // actualItem not available in clientless
//                                     {
//                                 // Logger.Information($"[TRADING SYSTEM] Found item {actualItem.Name}, adding to trade"); // actualItem not available
// 
//                                         // Add delay to ensure item movement is complete before adding to trade
//                             // else // Commented out because corresponding if statement was commented out
// 
//                                         // Add item to trade using the same pattern as working code
//                         // Trade.AddItem(actualItem.Slot); // Method signature issue in clientless
// 
//                                         // Track item given by bot for comprehensive logging
//                         // SendPrivateMessage(pendingTrade.PlayerName, $"Added {actualItem.Name} to trade. Please accept when ready."); // playerId not available
//                         // TrackItemGivenByBot(playerId, actualItem.Name); // playerId not available in clientless
// 
//                                 // Logger.Information($"[TRADING SYSTEM] Item {actualItem.Name} added to trade"); // actualItem not available
//                         // Logger.Information($"[TRADING SYSTEM] Item {actualItem.Name} added to trade"); // pendingTrade not available
//                             // else // Commented out because corresponding if statement was commented out
//                                     else
//                                     {
//                                         Logger.Information($"[TRADING SYSTEM] Item not found");
//                         // Logger.Information($"[TRADING SYSTEM] Removing pending trade for {pendingTrade.PlayerName}"); // pendingTrade not available
//                                     }
//                                 }
//                                 catch (Exception ex)
//                                 {
//                                     Logger.Information($"[TRADING SYSTEM] Error in async trade addition: {ex.Message}");
//                         // Logger.Information($"[TRADING SYSTEM] No pending trade found for {pendingTrade.PlayerName}"); // pendingTrade not available
//                                 }
//                             });
//                         }
//                         // else // Commented out because corresponding if statement was commented out
//                         {
//                             // Logger.Information($"[TRADING SYSTEM] Error: ActualItem is not a valid StoredItem object - it's {pendingTrade.ActualItem?.GetType().Name ?? "null"}"); // pendingTrade not available
//                             // SendPrivateMessage(pendingTrade.PlayerName, "Sorry, there was an error processing your request."); // pendingTrade not available
//                         }
//                     }
//                 }
//                 else if (status == TradeStatus.Finished) //                 else if (state == TradeStatus.Finished && trader.Type == IdentityType.SimpleChar)                else if (state == TradeStatus.Finished && trader.Type == IdentityType.SimpleChar) trader.Type == IdentityType.SimpleChar - trader not available in clientless
//                 {
//                     // uint playerId = (uint)trader.Instance; // trader not available in clientless
// 
//                     // Complete the GET trade
//                     // uint playerId = (uint)trader.Instance; // trader not available in clientless
//                     {
//                     // if (_pendingTrades.TryGetValue(playerId, out var completedTrade)) // playerId not available in clientless
//                         // Logger.Information($"[TRADING SYSTEM] GET trade completed for {completedTrade.PlayerName}"); // completedTrade not available
// 
//                         // Note: Trade logging is handled by StorageBot_ProcessTradeItems using LogCompleteTrade
//                         // which only shows sections with items (no empty sections)
// 
//                         // Clean up tracking
//                         // _itemsGivenByBot.Remove(playerId); // playerId not available in clientless
//                         // _pendingTrades.Remove(playerId); // playerId not available in clientless
//                     }
//                 }
//                 else if (status == TradeStatus.None) // Declined trade - trader not available in clientless
//                 {
//                     // uint playerId = (uint)trader.Instance; // trader not available in clientless
// 
//                     // Clean up pending GET trade
//                     // if (_pendingTrades.TryGetValue(playerId, out var cancelledTrade)) // playerId not available in clientless
//                     {
//                         // Logger.Information($"[TRADING SYSTEM] GET trade cancelled/declined for {cancelledTrade.PlayerName}"); // cancelledTrade not available
//                         // _pendingTrades.Remove(playerId); // playerId not available in clientless
//                         // _itemsGivenByBot.Remove(playerId); // playerId not available in clientless
//                     }
//                 }
//             }
//             catch (Exception ex)
//             {
//                 Logger.Information($"Error in trade state change handler: {ex.Message}");
//             }
//         }
// 
//         // Simplified trade handling methods - TODO: Implement when AOSharp Trade API is available
// 
        // HandleTradeMessage removed - now using proper TradeStatusChanged events

        // Old monitoring methods removed - now using proper TradeStatusChanged events

        /// <summary>
        /// Capture inventory state before trade starts
        /// </summary>
        private static void CaptureInventoryBeforeTrade()
        {
            try
            {
                _inventoryBeforeTrade.Clear();

                // Capture all item instance IDs currently in inventory
                foreach (var item in Inventory.Items.Where(i => i.Slot.Type == IdentityType.Inventory))
                {
                    _inventoryBeforeTrade.Add((uint)item.UniqueIdentity.Instance);
                }

                Logger.Information($"[TRADING SYSTEM] Captured {_inventoryBeforeTrade.Count} items in inventory before trade");

                // Debug: Log first few item IDs
                if (_inventoryBeforeTrade.Count > 0)
                {
                    var firstFew = _inventoryBeforeTrade.Take(3).Select(id => id.ToString()).ToArray();
                    Logger.Information($"[TRADING SYSTEM] Sample item IDs: {string.Join(", ", firstFew)}");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error capturing inventory before trade: {ex.Message}");
            }
        }

        /// <summary>
        /// Get items that were actually received from the player in the trade
        /// Compares current inventory against pre-trade inventory
        /// </summary>
        private static List<Item> GetReceivedItemsFromInventory()
        {
            try
            {
                Logger.Information("[TRADING SYSTEM] Detecting received items from trade");

                // Get current inventory
                var currentInventory = Inventory.Items.Where(item =>
                    item.Slot.Type == IdentityType.Inventory).ToList();

                // Find items that are NEW (not in the before-trade list)
                var receivedItems = currentInventory.Where(item =>
                    !_inventoryBeforeTrade.Contains((uint)item.UniqueIdentity.Instance)).ToList();

                Logger.Information($"[TRADING SYSTEM] Found {receivedItems.Count} NEW items received in trade (out of {currentInventory.Count} total inventory items)");

                foreach (var item in receivedItems)
                {
                    Logger.Information($"[TRADING SYSTEM] Received: {item.Name} (ID: {item.UniqueIdentity.Instance})");
                }

                return receivedItems;
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error detecting received items: {ex.Message}");
                return new List<Item>();
            }
        }

        /// <summary>
        /// Find the actual Item object in inventory or bags that matches the StoredItem
        /// If found in a bag, move it to main inventory first
        /// </summary>
        private static Item FindActualItemInInventory(StoredItem storedItem)
        {
            try
            {
                Logger.Information($"[TRADING SYSTEM] Looking for item: {storedItem.Name} (Instance: {storedItem.ItemInstance})");

                // First, check if we have a direct reference to the actual item
                if (storedItem.ActualItem != null)
                {
                    Logger.Information($"[TRADING SYSTEM] Using direct ActualItem reference for {storedItem.Name}");

                    // Verify the item still exists and is valid
                    if (storedItem.ActualItem.Slot.Type == IdentityType.Inventory)
                    {
                        Logger.Information($"[TRADING SYSTEM] ActualItem is in main inventory, ready for trade");
                        return storedItem.ActualItem;
                    }
                    else
                    {
                        Logger.Information($"[TRADING SYSTEM] ActualItem is in a bag, need to move to main inventory");

                        // Find an empty inventory slot
                        var emptySlot = FindEmptyInventorySlot();
                        if (emptySlot == -1)
                        {
                            Logger.Information($"[TRADING SYSTEM] No empty inventory slots available");
                            return null;
                        }

                        // Move item to main inventory
                        if (MoveItemToMainInventory(storedItem.ActualItem, emptySlot))
                        {
                            // Wait for the move to complete
                            System.Threading.Thread.Sleep(200);
                            Logger.Information($"[TRADING SYSTEM] Successfully moved {storedItem.Name} to main inventory");
                            return storedItem.ActualItem;
                        }
                        else
                        {
                            Logger.Information($"[TRADING SYSTEM] Failed to move {storedItem.Name} to main inventory");
                            return null;
                        }
                    }
                }

                Logger.Information($"[TRADING SYSTEM] No ActualItem reference, falling back to instance search");

                // Fallback: check if the item is already in main inventory
                var inventoryItem = Inventory.Items.FirstOrDefault(item =>
                    item.UniqueIdentity.Instance == storedItem.ItemInstance &&
                    item.Name.Equals(storedItem.Name, StringComparison.OrdinalIgnoreCase) &&
                    item.Slot.Type == IdentityType.Inventory);

                if (inventoryItem != null)
                {
                    Logger.Information($"[TRADING SYSTEM] Item found in main inventory, ready for trade");
                    return inventoryItem;
                }

                // Search all bags for the item
                Logger.Information($"[TRADING SYSTEM] Item not in main inventory, searching bags...");
                foreach (var backpack in Inventory.Containers)
                {
                    Logger.Information($"[TRADING SYSTEM] Searching bag: {backpack.Item?.Name ?? "Unknown"} ({backpack.Items.Count} items)");

                    var bagItem = backpack.Items.FirstOrDefault(item =>
                        item.UniqueIdentity.Instance == storedItem.ItemInstance &&
                        item.Name.Equals(storedItem.Name, StringComparison.OrdinalIgnoreCase));

                    if (bagItem != null)
                    {
                        Logger.Information($"[TRADING SYSTEM] Found item in bag '{backpack.Item?.Name ?? "Unknown"}', moving to main inventory");

                        // Find an empty inventory slot
                        var emptySlot = FindEmptyInventorySlot();
                        if (emptySlot == -1)
                        {
                            Logger.Information($"[TRADING SYSTEM] No empty inventory slots available");
                            return null;
                        }

                        // Move item to main inventory using proper slot targeting
                        if (MoveItemToMainInventory(bagItem, emptySlot))
                        {
                            // Wait for the move to complete
                            System.Threading.Thread.Sleep(200);

                            // Find the moved item in main inventory
                            var movedItem = Inventory.Items.FirstOrDefault(item =>
                                item.UniqueIdentity.Instance == storedItem.ItemInstance &&
                                item.Name.Equals(storedItem.Name, StringComparison.OrdinalIgnoreCase) &&
                                item.Slot.Type == IdentityType.Inventory);

                            if (movedItem != null)
                            {
                                Logger.Information($"[TRADING SYSTEM] Item successfully moved to main inventory");
                                return movedItem;
                            }
                            else
                            {
                                Logger.Information($"[TRADING SYSTEM] Item move completed but item not found in main inventory");
                                return bagItem; // Return original item as fallback
                            }
                        }
                        else
                        {
                            Logger.Information($"[TRADING SYSTEM] Failed to move item to main inventory");
                            return null;
                        }
                    }
                }

                Logger.Information($"[TRADING SYSTEM] Item '{storedItem.Name}' not found in any bags or main inventory");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Move an item from a bag to the main inventory - using the working pattern from auto-bagging
        /// </summary>
        private static bool MoveItemToMainInventory(Item item, int targetSlot)
        {
            try
            {
                Logger.Information($"[TRADING SYSTEM] Attempting to move {item.Name} from bag to main inventory");

                // Use the same approach as the working auto-bagging code
                // Move to main inventory by using the player's inventory identity
                var inventoryIdentity = DynelManager.LocalPlayer.Identity;
                item.MoveToContainer(inventoryIdentity);

                Logger.Information($"[TRADING SYSTEM] Move command sent for {item.Name} to main inventory");
                return true;
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error moving item to main inventory: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Overload for backward compatibility - finds empty slot automatically
        /// </summary>
        private static bool MoveItemToMainInventory(Item item)
        {
            var emptySlot = FindEmptyInventorySlot();
            if (emptySlot == -1)
            {
                Logger.Information($"[TRADING SYSTEM] No empty inventory slots available");
                return false;
            }
            return MoveItemToMainInventory(item, emptySlot);
        }

        /// <summary>
        /// Find an empty slot in the main inventory
        /// </summary>
        private static int FindEmptyInventorySlot()
        {
            try
            {
                // Get all inventory slots (typically 0-29 for main inventory)
                for (int slotIndex = 0; slotIndex < 30; slotIndex++)
                {
                    // Check if this slot is empty (no item occupying it)
                    var itemInSlot = Inventory.Items.FirstOrDefault(item =>
                        item.Slot.Type == IdentityType.Inventory &&
                        item.Slot.Instance == slotIndex);

                    if (itemInSlot == null)
                    {
                        Logger.Information($"[TRADING SYSTEM] Found empty inventory slot: {slotIndex}");
                        return slotIndex;
                    }
                }

                Logger.Information($"[TRADING SYSTEM] No empty inventory slots found");
                return -1;
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error finding empty inventory slot: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Track an item given by the bot to a player
        /// </summary>
        private static void TrackItemGivenByBot(uint playerId, string itemName)
        {
            try
            {
                if (!_itemsGivenByBot.ContainsKey(playerId))
                {
                    _itemsGivenByBot[playerId] = new List<string>();
                }

                _itemsGivenByBot[playerId].Add(itemName);
                Logger.Information($"[TRADING SYSTEM] Tracked item given by bot: {itemName} to player {playerId}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error tracking item given by bot: {ex.Message}");
            }
        }

        /// <summary>
        /// Send a private message to a player
        /// </summary>
        private static void SendPrivateMessage(string playerName, string message)
        {
            try
            {
                // Try to find the player by name
                var targetPlayer = DynelManager.Players
                    .FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

                if (targetPlayer != null)
                {
                    Client.SendPrivateMessage((uint)targetPlayer.Identity.Instance, message);
                }
                else
                {
                    Logger.Information($"[TRADING SYSTEM] Player {playerName} not found for private message");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error sending private message: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for items in trade window and detect their charges
        /// </summary>
        private static void CheckTradeWindowItems()
        {
            try
            {
                Logger.Information("[TRADING SYSTEM] Checking trade window for items with charges");

                // Try to access trade window items through various methods
                try
                {
                    // Method 1: Check if Trade has an Items property
                    var tradeType = typeof(Trade);
                    var itemsProperty = tradeType.GetProperty("Items");
                    if (itemsProperty != null)
                    {
                        var tradeItems = itemsProperty.GetValue(null);
                        Logger.Information($"[TRADING SYSTEM] Found Trade.Items property: {tradeItems?.GetType().Name}");

                        if (tradeItems is IEnumerable<Item> items)
                        {
                            foreach (var item in items)
                            {
                                CheckItemCharges(item, "Trade Window");
                            }
                        }
                    }
                    else
                    {
                        Logger.Information("[TRADING SYSTEM] No Trade.Items property found");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Information($"[TRADING SYSTEM] Error accessing trade items: {ex.Message}");
                }

                // Method 2: Try other possible trade item access methods
                try
                {
                    var tradeType = typeof(Trade);
                    var methods = tradeType.GetMethods().Where(m => m.Name.Contains("Item") || m.Name.Contains("Get")).ToArray();
                    Logger.Information($"[TRADING SYSTEM] Available Trade methods: {string.Join(", ", methods.Select(m => m.Name))}");
                }
                catch (Exception ex)
                {
                    Logger.Information($"[TRADING SYSTEM] Error checking Trade methods: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error in CheckTradeWindowItems: {ex.Message}");
            }
        }

        /// <summary>
        /// Check charges on a specific item using the provided method
        /// </summary>
        private static void CheckItemCharges(Item item, string context)
        {
            try
            {
                Logger.Information($"[TRADING SYSTEM] Checking charges for '{item.Name}' in {context}");

                // Use the exact code provided by the user
                int charges = 0;
                try
                {
                    var chargesProp = item.GetType().GetProperty("Charges");
                    if (chargesProp != null)
                    {
                        var value = chargesProp.GetValue(item);
                        charges = value is int ? (int)value : (value != null ? Convert.ToInt32(value) : 0);
                        Logger.Information($"[TRADING SYSTEM] Item '{item.Name}' has {charges} charges (from {context})");
                    }
                    else
                    {
                        Logger.Information($"[TRADING SYSTEM] Item '{item.Name}' has no Charges property (from {context})");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Information($"[TRADING SYSTEM] Error getting charges for '{item.Name}': {ex.Message}");
                }

                if (charges > 1)
                {
                    Logger.Information($"[TRADING SYSTEM] *** FOUND STACKABLE ITEM: '{item.Name}' with {charges} charges in {context} ***");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error checking item charges: {ex.Message}");
            }
        }

        /// <summary>
        /// Track item given by bot (simplified version)
        /// </summary>
        private static void TrackItemGivenByBot(int playerId, Item item)
        {
            try
            {
                Logger.Information($"[TRADING SYSTEM] Tracked item given by bot: {item.Name} to player {playerId}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[TRADING SYSTEM] Error tracking item: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Represents a pending trade operation
    /// </summary>
    public class PendingTrade
    {
        public string PlayerName { get; set; }
        public uint PlayerId { get; set; }
        public object RequestedItem { get; set; }
        public object ActualItem { get; set; } // Simplified for now
        public DateTime InitiatedAt { get; set; }
    }
}
