﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Clientless.Logging;
using AOSharp.Common.GameData;
using Newtonsoft.Json;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Clientless
{
    public class ItemBase
    {
        public string Name;
        public int Icon;
        public int Id;
        public int Ql;
        public Dictionary<ItemActionInfo, List<RequirementCriterion>> Criteria = new Dictionary<ItemActionInfo, List<RequirementCriterion>>();
        public Dictionary<SpellListType, Dictionary<Stat, int>> Modifiers = new Dictionary<SpellListType, Dictionary<Stat, int>>();

        public ItemBase(int id, int ql)
        {
            Id = id;
            Ql = ql;
        }
    }
}