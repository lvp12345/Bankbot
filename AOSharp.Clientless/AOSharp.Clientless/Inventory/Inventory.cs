using AOSharp.Clientless.Logging;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AOSharp.Clientless
{
    public static class Inventory
    {
        public const int INVENTORY_CAPACITY = 30;
        public const int INVENTORY_START = 0x40;
        public const int INVENTORY_END = INVENTORY_START + INVENTORY_CAPACITY;
        public static int NumFreeSlots => 30 - _items.Where(x => x.Slot.Type == IdentityType.Inventory).Count();
        public static bool NoAvailableSlots => GetNextAvailableSlot() == null;

        public static Bank Bank = new Bank();

        public static IReadOnlyList<Container> Containers => _containers;
        public static IReadOnlyList<Item> Items => _items;
        public static IReadOnlyList<UniqueItem> UniqueItems => _items.OfType<UniqueItem>().ToList();
        public static IReadOnlyList<TempItem> TempItems => _items.OfType<TempItem>().ToList();
        private static Item _lastProxyItem;
        private static List<Item> _items;
        private static List<Container> _containers = new List<Container>();

        public static Action<Container> ContainerOpened;
        public static Action<Item> ItemAdded;
        public static Action<Item> ItemRemoved;
        public static Action<Item> BankItemAdded;
        public static Action<Item> BankItemRemoved;
        public static Action<Container, Item> ContainerItemAdded;
        public static Action<Container, Item> ContainerItemRemoved;

        internal static int? GetNextAvailableSlot(int invStart, int invEnd, IEnumerable<Item> items) => Enumerable.Range(invStart, invEnd).Except(items.Select(x => (x.Slot.Instance & 0xFF))).FirstOrDefault();
  
        public static int? GetNextAvailableSlot() => GetNextAvailableSlot(INVENTORY_START, INVENTORY_CAPACITY, _items);

        public static bool Find(Identity slot, out Item item) => (item = Items?.FirstOrDefault(x => x.Slot == slot)) != null;

        public static bool Find(string name, out Item item) => (item = Items?.FirstOrDefault(x => x.Name == name)) != null;

        public static bool Find(int id, out Item item) => (item = Items?.FirstOrDefault(x => x.Id == id || x.HighId == id)) != null;

        public static bool Find(int lowId, int highId, out Item item) => (item = Items?.FirstOrDefault(x => x.Id == lowId && x.HighId == highId)) != null;

        public static List<Item> FindAll(int id) => Items?.Where(x => x.Id == id || x.HighId == id).ToList();

        public static List<Item> FindAll(IEnumerable<int> ids) => Items?.Where(x => ids.Contains(x.Id)).ToList();

        public static List<Item> FindAll(int lowId, int highId) => Items?.Where(x => x.Id == lowId && x.HighId == highId).ToList();

        public static List<Item> FindAll(string name) => Items?.Where(x => x.Name == name).ToList();

        internal static void OnFullCharacterMessage(InventorySlot[] inventorySlots)
        {
            _items = new List<Item>();
            _containers = new List<Container>();
            RegisterItems(_items, inventorySlots);
        }

        internal static void OnContainerUpdate(Identity invIdentity, InventorySlot[] inventorySlots, int handle)
        {
            if (_containers.Find(invIdentity, out Container container))
                _containers.Remove(container);

            container = new Container(invIdentity, handle);
            _containers.Add(container);

            container.RegisterItems(inventorySlots);

            ContainerOpened?.Invoke(container);
        }

        internal static void ResetContainers()
        {
            foreach (Container container in _containers)
            {
                container.Items = new List<Item>();
                container.Handle = 0;
            }

            Bank.IsOpen = false;
        }

        internal static void OnAddTemplateMessage(int lowId, int highId, int ql, int charges)
        {
            var item = new Item(Identity.None, Identity.None, lowId, highId, ql, charges);
            AddToNextAvailableSlot(item);
            RegisterLastItem(item);
        }

        internal static void OnTemplateMessage(int lowId, int highId, int ql, int charges)
        {
            RegisterLastItem(lowId, highId, ql, charges);
        }

        internal static void OnBankUpdate(BankMessage bankMsg)
        {
            Bank.RegisterItems(bankMsg.BankSlots);
            Bank.IsOpen = true;
        }

        internal static void SplitItem(Identity source, int charges)
        {
            switch (source.Type)
            {
                case IdentityType.Backpack:
                    if (_containers.Find(source, out Item bagItem))
                    {
                        if (!_containers.Find((byte)(source.Instance >> 8), out Container sourceContainer))
                        {
                            Logger.Information($"Split failed on '{bagItem.Name} | {bagItem.Slot}' (Couldn't find source container)");
                        }

                        var firstAvailSlot = sourceContainer.NextAvailableSlot;

                        if (!firstAvailSlot.HasValue)
                        {
                            Logger.Information($"Split failed on '{bagItem.Name} | {bagItem.Slot}' (Container is full)");
                            return;
                        }

                        bagItem.Charges = bagItem.Charges - charges;
                        Item splitItem = new Item(bagItem.Slot, bagItem.UniqueIdentity, bagItem.Id, bagItem.HighId, bagItem.Ql, charges);
                        OnContainerAction(sourceContainer.Identity, splitItem, firstAvailSlot.Value);
                    }
                    break;
                case IdentityType.BankByRef:
                    if (Bank.Items.Find(source, out Item bankItem))
                    {
                        var firstAvailSlot = Bank.NextAvailableSlot;

                        if (!firstAvailSlot.HasValue)
                        {
                            Logger.Information($"Split failed on '{bankItem.Name} | {bankItem.Slot}' (Container is full)");
                            return;
                        }

                        bankItem.Charges = bankItem.Charges - charges;
                        Item splitItem = new Item(bankItem.Slot, bankItem.UniqueIdentity, bankItem.Id, bankItem.HighId, bankItem.Ql, charges);
                        OnContainerAction(new Identity(IdentityType.Bank, 0), splitItem, firstAvailSlot.Value);
                    }
                    break;
                case IdentityType.Inventory:
                    if (Find(source, out Item invItem))
                    {
                        var firstAvailSlot = GetNextAvailableSlot();

                        if (!firstAvailSlot.HasValue)
                        {
                            Logger.Information($"Split failed on '{invItem.Name} | {invItem.Slot}' (Container is full)");
                            return;
                        }

                        invItem.Charges = invItem.Charges - charges;
                        Item splitItem = new Item(invItem.Slot, invItem.UniqueIdentity, invItem.Id, invItem.HighId, invItem.Ql, charges);
                        OnContainerAction(DynelManager.LocalPlayer.Identity, splitItem, firstAvailSlot.Value);
                    }
                    break;
                default:
                    Logger.Information($"OnMoveItemAction IdentityType {source.Type} not mapped. This shouldn't happen.");
                    break;
            }
        }

        internal static void OnContainerAddItem(Identity source, Identity target, int slot)
        {
            // We will map these as needed
            if (source.Type == IdentityType.OverflowWindow && source.Instance == 0 &&
                target.Type == IdentityType.OverflowWindow && target.Instance == DynelManager.LocalPlayer.Identity.Instance)
            {
                OnSpawnItemAction();
            }
            else
            {
                OnMoveItemAction(source, target, slot);
            }
        }

        private static void OnMoveItemAction(Identity source, Identity target, int slot)
        {
            switch (source.Type)
            {
                case IdentityType.Inventory:
                case IdentityType.ArmorPage:
                case IdentityType.WeaponPage:
                case IdentityType.ImplantPage:
                case IdentityType.SocialPage:
                    if (Find(source, out Item invItem))
                    {
                        _items.Remove(invItem);
                        ItemRemoved?.Invoke(invItem);
                        OnContainerAction(target, invItem, slot);
                    }
                    break;
                case IdentityType.Backpack:
                    if (_containers.Find(source, out Item bagItem))
                    {
                        _containers.RemoveItem(bagItem, out Container container);
                        ContainerItemRemoved?.Invoke(container, bagItem);
                        OnContainerAction(target, bagItem, slot);
                    }
                    break;
                case IdentityType.BankByRef:
                    if (Bank.Items.Find(source, out Item bankItem))
                    {
                        Bank.RemoveItem(bankItem);
                        BankItemRemoved?.Invoke(bankItem);
                        OnContainerAction(target, bankItem, slot);
                    }
                    break;
                default:
                    Logger.Information($"OnMoveItemAction IdentityType {source.Type} not mapped. This shouldn't happen.");
                    break;
            }
        }

        private static void OnContainerAction(Identity target, Item sourceItem, int slot)
        {
            //Logger.Information($"OnContainerAction({target}, {sourceItem.Id}, {slot}");

            if (target.Type == IdentityType.Container)
            {
                if (_containers.Find(target, out Container container))
                {
                    container.AddItem(sourceItem);
                    ContainerItemAdded?.Invoke(container, sourceItem);
                }
            }
            else if (target.Type == IdentityType.Bank)
            {
                Bank.AddItemToAvailableSlot(sourceItem);
                BankItemAdded?.Invoke(sourceItem);
            }
            else if (target == DynelManager.LocalPlayer.Identity)
            {
                sourceItem.Slot = new Identity(GetSlotType(slot), slot == 0x6F ? GetNextAvailableSlot().Value : slot);
                _items.Add(sourceItem);
                ItemAdded?.Invoke(sourceItem);
            }
        }

        private static void OnSpawnItemAction()
        {
            AddToNextAvailableSlot(_lastProxyItem);
        }

        private static void RegisterItems(List<Item> items, InventorySlot[] inventorySlots)
        {
            for (int i = 0; i < inventorySlots.Length; i++)
            {
                var invSlot = inventorySlots[i];
                Item item = new Item(new Identity(GetSlotType(invSlot.Placement), invSlot.Placement), invSlot.Identity, invSlot.ItemLowId, invSlot.ItemHighId, invSlot.Quality, invSlot.Count);

                if (invSlot.Identity.Type == IdentityType.Container)
                    OnContainerUpdate(invSlot.Identity, new InventorySlot[0], 0);

                items.Add(item);
                UpdateItem(item);
            }
        }

        private static IdentityType GetSlotType(int slot)
        {
            IdentityType slotType = IdentityType.Inventory;

            if (slot <= (int)EquipSlot.Weap_Hud2)
                slotType = IdentityType.WeaponPage;
            else if (slot <= (int)EquipSlot.Cloth_LeftFinger)
                slotType = IdentityType.ArmorPage;
            else if (slot <= (int)EquipSlot.Imp_Feet)
                slotType = IdentityType.ImplantPage;
            else if (slot <= (int)EquipSlot.Social_LeftWeap)
                slotType = IdentityType.SocialPage;

            return slotType;
        }

        internal static void UpdateItem(Item item)
        {
            if (item.UniqueIdentity == Identity.None)
                return;

            if (!FullUpdateProxy.Find(item.UniqueIdentity, out SimpleItem simpleItem))
                return;

            _items[_items.IndexOf(item)] = CreateUniqueItem(item.Slot, simpleItem);
        }

        private static Item CreateUniqueItem(Identity slot, SimpleItem simpleItem)
        {
            Item result = null;

            //We will create more variations of UniqueItem types as we need them
            if (simpleItem is WeaponItem)
            {
                result = new UniqueItem(slot, simpleItem);
            }
            else if (simpleItem is ChestItem)
            {
                result = new UniqueItem(slot, simpleItem);
            }
            else if (simpleItem is SimpleItem)
            {
                if (simpleItem.Stats.TryGetValue(Stat.TimeExist, out var remainingTime) && remainingTime > 0)
                {
                    result = new TempItem(slot, simpleItem);
                }
                else
                {
                    result = new UniqueItem(slot, simpleItem);
                }
            }

            if (result == null)
            {
                Logger.Error("UpdateItem Null Item. This shouldnt happen.");
                return null;
            }

            return result;
        }

        internal static void AddToNextAvailableSlot(Item item, bool updateContainer = true)
        {
            item.Slot = new Identity(IdentityType.Inventory, (int)GetNextAvailableSlot());

            if (updateContainer)
            {
                if (item.UniqueIdentity.Type == IdentityType.Container)
                    OnContainerUpdate(item.UniqueIdentity, new InventorySlot[0], 0);
            }

            _items.Add(item);
        }

        internal static void RemoveItem(Item item, bool wipeContainer = true)
        {
            _items.Remove(item);

            if (wipeContainer)
            {
                if (item.UniqueIdentity.Type == IdentityType.Container)
                {
                    if (!_containers.Find(item.UniqueIdentity, out Container container))
                        return;

                    _containers.Remove(container);
                }
            }
        }

        internal static void RemoveItem(Identity slot) => _items.Remove(_items.FirstOrDefault(x => x.Slot == slot));

        internal static void RegisterLastItem(int lowId, int highId, int ql, int charges)
        {
            _lastProxyItem = new Item(Identity.None, Identity.None, lowId, highId, ql, charges);
        }

        internal static void RegisterLastItem(SimpleItem simpleItem)
        {
            _lastProxyItem = CreateUniqueItem(Identity.None, simpleItem);
        }

        internal static void RegisterLastItem(Item item)
        {
            _lastProxyItem = item;
        }
    }
}