using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace AOSharp.Clientless
{
    public static class DynelManager
    {
        public static EventHandler<Dynel> DynelSpawned;
        public static EventHandler<Dynel> DynelDespawned;
        public static Action<Identity, Identity> DynelUsed;

        public static ReadOnlyCollection<Dynel> AllDynels => _dynels.Values.ToList().AsReadOnly();
        public static ReadOnlyCollection<NpcChar> Npcs => _dynels.Values.Where(x => x is NpcChar).Cast<NpcChar>().ToList().AsReadOnly();
        public static ReadOnlyCollection<PlayerChar> Players => _dynels.Values.Where(x => x is PlayerChar).Cast<PlayerChar>().ToList().AsReadOnly();

        public static ReadOnlyCollection<SimpleChar> Characters => _dynels.Values.Where(x => x is SimpleChar).Cast<SimpleChar>().ToList().AsReadOnly();
        public static ReadOnlyCollection<VendingMachine> VendingMachines => _dynels.Values.Where(x => x is VendingMachine).Cast<VendingMachine>().ToList().AsReadOnly();

        internal static LocalPlayerProxy LocalPlayerProxy = new LocalPlayerProxy();

        public static LocalPlayer LocalPlayer => LocalPlayerProxy.LocalPlayer;

        private static Dictionary<Identity, Dynel> _dynels = new Dictionary<Identity, Dynel>();

        public static bool Find<T>(Identity identity, out T dynel) where T : Dynel
        {
            return (dynel = (T)AllDynels.FirstOrDefault(x => x is T && x.Identity == identity)) != null;
        }

        public static bool Find<T>(string name, out T dynel) where T : SimpleChar
        {
            return (dynel = (T)AllDynels.Cast<SimpleChar>().FirstOrDefault(x => x is T && x.Name == name)) != null;
        }

        internal static void OnDynelUsed(Identity user, Identity target)
        {
            DynelUsed?.Invoke(user, target);
        }

        internal static void OnDynelSpawned(VendingMachineFullUpdateMessage vendMachineMsg)
        {
            if (vendMachineMsg.Position != null)
                OnDynelSpawned(new VendingMachine(vendMachineMsg.Identity, vendMachineMsg.Position.Value, vendMachineMsg.Rotation.Value, vendMachineMsg.Stats));
        }

        internal static void OnDynelSpawned(SimpleCharFullUpdateMessage simpleCharMsg)
        {
            Dynel dynel;

            if (simpleCharMsg.Identity.Instance == Client.LocalDynelId)
            {
                LocalPlayerProxy.ApplySimpleCharFullUpdate(simpleCharMsg);
                dynel = LocalPlayer;
            }
            else
            {
                dynel = simpleCharMsg.Flags.HasFlag(SimpleCharFullUpdateFlags.IsNpc) ? new NpcChar(simpleCharMsg) : (Dynel)new PlayerChar(simpleCharMsg);
            }

            OnDynelSpawned(dynel);
        }

        internal static void OnDynelSpawned(Dynel dynel)
        {
            _dynels.Add(dynel.Identity, dynel);
            DynelSpawned?.Invoke(null, dynel);
        }

        internal static void OnDynelDespawned(Identity identity)
        {
            if (_dynels.TryGetValue(identity, out Dynel dynel))
            {
                DynelDespawned?.Invoke(null, dynel);
                _dynels.Remove(identity);
            }
        }

        internal static void OnDynelMovementChanged(Identity identity, Vector3 pos, Quaternion heading, MovementAction moveAction)
        {
            if (_dynels.TryGetValue(identity, out Dynel dynel))
            {
                dynel.Transform.Position = pos;
                dynel.Transform.Heading = heading;
            }
        }

        internal static void OnOrgInfoPacket(OrgInfoPacketMessage orgInfoPacketMsg)
        {
            if (_dynels.TryGetValue(orgInfoPacketMsg.Identity, out Dynel dynel) && dynel is PlayerChar player)
            {
                //player.SetStat(Stat.Clan, orgInfoPacketMsg.OrgId);
                player.OrgName = orgInfoPacketMsg.Name;
            }
        }

        internal static void InitStaticDynels(PlayfieldId playfieldId)
        {
            if (!StaticDynelData.GetDynels(playfieldId, out List<StaticDynel> dynels))
                return;

            foreach (var dynel in dynels)
            {
                _dynels.Add(dynel.Identity, dynel);
            }
        }

        internal static void Reset()
        {
            _dynels.Clear();
        }
    }
}
