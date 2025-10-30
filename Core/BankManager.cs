using AOSharp.Clientless.Logging;
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Bankbot.Modules;
using AOSharp.Core;
using AOSharp.Core.Inventory;
using AOSharp.Common.GameData;

namespace Bankbot.Core
{
    /// <summary>
    /// Manages automated bank operations for bag storage and retrieval
    /// </summary>
    public static class BankManager
    {
        private static BankContents _bankContents;
        private static string _bankContentsPath;
        private static bool _initialized = false;
        private static readonly object _bankLock = new object();
        
        // Track pending bank operations to avoid conflicts
        private static readonly ConcurrentQueue<BankOperation> _pendingOperations = new ConcurrentQueue<BankOperation>();
        private static bool _processingOperations = false;

        /// <summary>
        /// Initialize the bank manager system
        /// </summary>
        public static void Initialize()
        {
            try
            {
                if (_initialized) return;

                // Bank contents file is in the same directory as the DLL
                string dllDirectory = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                _bankContentsPath = Path.Combine(dllDirectory, "bank_contents.json");
                
                Logger.Information($"[BANK MANAGER] Bank contents path: {_bankContentsPath}");
                
                LoadBankContents();
                
                _initialized = true;
                Logger.Information("SYSTEM", "Bank Manager initialized");
            }
            catch (Exception ex)
            {
                Logger.Information("SYSTEM", $"Error initializing Bank Manager: {ex.Message}");
            }
        }

        /// <summary>
        /// Load bank contents from file
        /// </summary>
        private static void LoadBankContents()
        {
            try
            {
                if (File.Exists(_bankContentsPath))
                {
                    string json = File.ReadAllText(_bankContentsPath);
                    _bankContents = JsonConvert.DeserializeObject<BankContents>(json) ?? new BankContents();
                    Logger.Information($"[BANK MANAGER] Loaded {_bankContents.StoredBags.Count} bags from bank contents file");
                }
                else
                {
                    _bankContents = new BankContents();
                    SaveBankContents();
                    Logger.Information("[BANK MANAGER] Created new bank contents file");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANK MANAGER] Error loading bank contents: {ex.Message}");
                _bankContents = new BankContents();
            }
        }

        /// <summary>
        /// Save bank contents to file
        /// </summary>
        private static void SaveBankContents()
        {
            try
            {
                lock (_bankLock)
                {
                    string json = JsonConvert.SerializeObject(_bankContents, Formatting.Indented);
                    File.WriteAllText(_bankContentsPath, json);
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANK MANAGER] Error saving bank contents: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if the Rubi-Ka Banking Service Terminal is in range and accessible
        /// </summary>
        public static bool IsBankTerminalAccessible()
        {
            try
            {
                var terminal = DynelManager.NPCs.FirstOrDefault(npc => 
                    npc.Name.Contains("Rubi-Ka Banking Service Terminal") ||
                    npc.Name.Contains("Banking Service Terminal"));
                
                if (terminal == null)
                {
                    Logger.Information("[BANK MANAGER] No banking terminal found in range");
                    return false;
                }

                float distance = terminal.DistanceFrom(DynelManager.LocalPlayer);
                bool accessible = distance <= 5.0f; // Within interaction range
                
                Logger.Information($"[BANK MANAGER] Banking terminal found at distance {distance:F1}m, accessible: {accessible}");
                return accessible;
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANK MANAGER] Error checking bank terminal: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Open the bank interface
        /// </summary>
        public static async Task<bool> OpenBank()
        {
            try
            {
                if (Inventory.Bank.IsOpen)
                {
                    Logger.Information("[BANK MANAGER] Bank is already open");
                    return true;
                }

                if (!IsBankTerminalAccessible())
                {
                    Logger.Information("[BANK MANAGER] Banking terminal not accessible");
                    return false;
                }

                var terminal = DynelManager.NPCs.FirstOrDefault(npc => 
                    npc.Name.Contains("Rubi-Ka Banking Service Terminal") ||
                    npc.Name.Contains("Banking Service Terminal"));

                if (terminal == null) return false;

                Logger.Information("[BANK MANAGER] Attempting to open bank...");
                
                // Use the terminal to open bank
                terminal.Use();
                
                // Wait for bank to open (up to 3 seconds)
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(100);
                    if (Inventory.Bank.IsOpen)
                    {
                        Logger.Information("[BANK MANAGER] ✅ Bank opened successfully");
                        return true;
                    }
                }

                Logger.Information("[BANK MANAGER] ❌ Timeout waiting for bank to open");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANK MANAGER] Error opening bank: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Move a bag from inventory to bank
        /// </summary>
        public static async Task<bool> MoveBagToBank(Item bag)
        {
            try
            {
                if (!await OpenBank())
                {
                    Logger.Information("[BANK MANAGER] Cannot move bag to bank - bank not accessible");
                    return false;
                }

                Logger.Information($"[BANK MANAGER] Moving bag '{bag.Name}' to bank...");

                // Get bag contents before moving
                var bagContents = GetBagContents(bag);
                
                // Move bag to bank
                bool success = bag.MoveToBank();
                
                if (success)
                {
                    // Update bank contents tracking
                    var bankBag = new BankStoredBag
                    {
                        BagId = bag.UniqueIdentity.Instance,
                        BagName = bag.Name,
                        Items = bagContents,
                        LastUpdated = DateTime.Now
                    };
                    
                    lock (_bankLock)
                    {
                        _bankContents.StoredBags[bag.UniqueIdentity.Instance] = bankBag;
                        SaveBankContents();
                    }
                    
                    Logger.Information($"[BANK MANAGER] ✅ Bag '{bag.Name}' moved to bank with {bagContents.Count} items");
                    Logger.Information("SYSTEM", $"BANK STORE: {bag.Name} ({bagContents.Count} items)");
                    return true;
                }
                else
                {
                    Logger.Information($"[BANK MANAGER] ❌ Failed to move bag '{bag.Name}' to bank");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANK MANAGER] Error moving bag to bank: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Get contents of a bag
        /// </summary>
        private static List<BankStoredItem> GetBagContents(Item bag)
        {
            var contents = new List<BankStoredItem>();
            
            try
            {
                if (bag.UniqueIdentity.Type == IdentityType.Container)
                {
                    var backpack = new Backpack(bag.UniqueIdentity, bag.Slot);
                    foreach (var item in backpack.Items)
                    {
                        contents.Add(new BankStoredItem
                        {
                            Name = item.Name,
                            LowId = item.LowId,
                            HighId = item.HighId,
                            QualityLevel = item.QualityLevel,
                            UniqueId = item.UniqueIdentity.Instance,
                            Slot = item.Slot.Instance
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANK MANAGER] Error getting bag contents: {ex.Message}");
            }
            
            return contents;
        }

        /// <summary>
        /// Auto-process received bags by moving them to bank
        /// </summary>
        public static async Task ProcessReceivedBags(List<Item> bags)
        {
            try
            {
                if (!bags.Any()) return;

                Logger.Information($"[BANK MANAGER] Processing {bags.Count} received bags for bank storage");

                foreach (var bag in bags)
                {
                    if (bag.UniqueIdentity.Type == IdentityType.Container)
                    {
                        await MoveBagToBank(bag);
                        await Task.Delay(500); // Small delay between operations
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANK MANAGER] Error processing received bags: {ex.Message}");
            }
        }

        /// <summary>
        /// Get all items stored in bank bags
        /// </summary>
        public static List<BankStoredItem> GetAllBankItems()
        {
            var allItems = new List<BankStoredItem>();
            
            lock (_bankLock)
            {
                foreach (var bag in _bankContents.StoredBags.Values)
                {
                    allItems.AddRange(bag.Items);
                }
            }
            
            return allItems;
        }

        /// <summary>
        /// Find which bag contains a specific item
        /// </summary>
        public static BankStoredBag FindBagContainingItem(string itemName)
        {
            lock (_bankLock)
            {
                return _bankContents.StoredBags.Values.FirstOrDefault(bag =>
                    bag.Items.Any(item => item.Name.Equals(itemName, StringComparison.OrdinalIgnoreCase)));
            }
        }

        /// <summary>
        /// Get bank storage statistics
        /// </summary>
        public static string GetBankStats()
        {
            lock (_bankLock)
            {
                int totalBags = _bankContents.StoredBags.Count;
                int totalItems = _bankContents.StoredBags.Values.Sum(bag => bag.Items.Count);
                
                return $"Bank Storage: {totalBags} bags, {totalItems} items";
            }
        }
    }

    /// <summary>
    /// Represents the contents of the bank
    /// </summary>
    public class BankContents
    {
        public Dictionary<uint, BankStoredBag> StoredBags { get; set; } = new Dictionary<uint, BankStoredBag>();
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Represents a bag stored in the bank
    /// </summary>
    public class BankStoredBag
    {
        public uint BagId { get; set; }
        public string BagName { get; set; }
        public List<BankStoredItem> Items { get; set; } = new List<BankStoredItem>();
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Represents an item stored in a bank bag
    /// </summary>
    public class BankStoredItem
    {
        public string Name { get; set; }
        public int LowId { get; set; }
        public int HighId { get; set; }
        public int QualityLevel { get; set; }
        public uint UniqueId { get; set; }
        public uint Slot { get; set; }
    }

    /// <summary>
    /// Represents a pending bank operation
    /// </summary>
    public class BankOperation
    {
        public BankOperationType Type { get; set; }
        public string ItemName { get; set; }
        public uint BagId { get; set; }
        public string PlayerName { get; set; }
    }

    public enum BankOperationType
    {
        StoreBag,
        RetrieveBag,
        StoreItem,
        RetrieveItem
    }
}
