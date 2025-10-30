using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AOSharp.Clientless.Logging;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;

namespace AOSharp.Clientless
{
    public class LocalPlayer : PlayerChar
    {
        public bool IsCasting { get; internal set; } = false;

        public new readonly LocalPlayerMovementComponent MovementComponent;

        public IReadOnlyDictionary<Stat, Cooldown> Cooldowns => _cooldowns;

        private readonly Dictionary<Stat, Cooldown> _cooldowns = new Dictionary<Stat, Cooldown>();

        public int[] SpellList;

        public LocalPlayer(SimpleCharFullUpdateMessage simpleCharMsg) : base(simpleCharMsg)
        {
            MovementComponent = new LocalPlayerMovementComponent
            {
                Position = simpleCharMsg.Position,
                Heading = simpleCharMsg.Heading,
            };
        }

        public void Attack(SimpleChar target) => Attack(target.Identity);

        public void Attack(Identity target)
        {
            Client.Send(new AttackMessage
            {
                Target = target
            });
        }

        public void StopAttack()
        {
            Client.Send(new StopFightMessage());
        }


        internal bool SetCastState(bool state) => IsCasting = state;

        public void Cast(int nanoId)
        {
            Targeting.SetTarget(Identity);
            CastNano(Identity, nanoId);
        }

        public void Cast(SimpleChar target, int nanoId)
        {
            Targeting.SetTarget(target);
            CastNano(target.Identity, nanoId);
        }

        public void Cast(Identity target, int nanoId)
        {
            Targeting.SetTarget(target);
            CastNano(target, nanoId);
        }

        private void CastNano(Identity target, int nanoId)
        {
            Client.Send(new CharacterActionMessage()
            {
                Action = CharacterActionType.CastNano,
                Target = target,
                Parameter1 = (int)IdentityType.NanoProgram,
                Parameter2 = nanoId
            });
        }

        internal void ApplyFullCharacter(FullCharacterMessage fullChar)
        {
            foreach (var stat in fullChar.Stats1)
                SetStat((Stat)stat.Value1, (int)stat.Value2);

            foreach (var stat in fullChar.Stats2)
                SetStat((Stat)stat.Value1, (int)stat.Value2);

            SpellList = fullChar.UploadedNanoIds;
        }

        public override int GetStat(Stat stat)
        {
            if (Inventory.Items == null)
                return 0;

            int equippedValue = Inventory.Items.Where(x => x.Slot.Instance <= (int)EquipSlot.Imp_Feet && x.Modifiers.TryGetValue(SpellListType.Wear, out var wearModifiers) && wearModifiers.ContainsKey(stat)).Sum(x => x.Modifiers[SpellListType.Wear][stat]);

            return base.GetStat(stat) + equippedValue;
        }

        internal bool TryGetCooldown(Stat stat, out Cooldown cooldown) => _cooldowns.TryGetValue(stat, out cooldown);
      
        internal bool RemoveCooldown(Stat stat) => _cooldowns.Remove(stat);

        internal void RegisterCooldown(Stat stat, int timeInSeconds)
        {
            if (!_cooldowns.TryGetValue(stat, out Cooldown cooldown))
            {
                cooldown = new Cooldown();
                _cooldowns.Add(stat, cooldown);
            }

            cooldown.SetExpireTime(timeInSeconds);
        }

        internal override void OnTeamLeft()
        {
            Client.Chat?.RemoveChannelId(TeamId);

            base.OnTeamLeft();
        }
    }
}