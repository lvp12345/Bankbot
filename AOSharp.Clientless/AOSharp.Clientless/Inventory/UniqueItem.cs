using System.Collections.Generic;
using AOSharp.Common.GameData;

namespace AOSharp.Clientless
{
    public class UniqueItem : Item
    {
        public Dictionary<Stat, int> Stats;

        public UniqueItem(Identity slot, SimpleItem simpleItem) : base(slot, simpleItem.Identity, simpleItem.ACGItem.LowId, simpleItem.ACGItem.HighId, simpleItem.ACGItem.QL, 1)
        {
            Stats = simpleItem.Stats;
        }

        public UniqueItem(Identity slot, Identity identity, int lowId, int highId, int ql, Dictionary<Stat, int> stats) : base(slot, identity, lowId, highId, ql, 1)
        {
            Stats = stats;
        }
    }
}