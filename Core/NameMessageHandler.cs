using AOSharp.Clientless;
using AOSharp.Clientless.Logging;
using System;
using System.Linq;
using System.Text;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles the "name" bot command for manual custom name management
    /// Usage:
    ///   name list                    - Show all bags with their instances and current names
    ///   name &lt;instance_id&gt; &lt;custom name&gt; - Set a custom name for an item
    ///   name clear &lt;instance_id&gt;     - Remove a custom name
    /// </summary>
    public class NameMessageHandler : IMessageHandler
    {
        public void HandleMessage(string senderName, MessageInfo messageInfo)
        {
            try
            {
                if (messageInfo.Arguments.Length == 0)
                {
                    SendPrivateMessage(senderName, "Usage: name list | name <instance> <name> | name clear <instance>");
                    return;
                }

                string subCommand = messageInfo.Arguments[0].ToLower();

                switch (subCommand)
                {
                    case "list":
                        HandleList(senderName);
                        break;
                    case "clear":
                        HandleClear(senderName, messageInfo.Arguments);
                        break;
                    default:
                        HandleSetName(senderName, messageInfo.Arguments);
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[NAME HANDLER] Error: {ex.Message}");
                SendPrivateMessage(senderName, "Error processing name command.");
            }
        }

        private void HandleList(string senderName)
        {
            var customNames = CustomNameRegistry.GetAllCustomNames();
            var bags = Inventory.Items
                .Where(i => i.UniqueIdentity.Type == AOSharp.Common.GameData.IdentityType.Container)
                .ToList();

            if (bags.Count == 0)
            {
                SendPrivateMessage(senderName, "No bags found in inventory.");
                return;
            }

            var sb = new StringBuilder("Bags in inventory:\n");
            foreach (var bag in bags)
            {
                int instance = bag.UniqueIdentity.Instance;
                string customName = CustomNameRegistry.GetCustomName(instance);
                string displayLine = customName != null
                    ? $"  [{instance}] {customName} (default: {bag.Name})"
                    : $"  [{instance}] {bag.Name}";
                sb.AppendLine(displayLine);
            }

            if (customNames.Count > 0)
            {
                sb.AppendLine($"\n{customNames.Count} custom name(s) set.");
            }

            SendPrivateMessage(senderName, sb.ToString());
        }

        private void HandleClear(string senderName, string[] args)
        {
            if (args.Length < 2 || !int.TryParse(args[1], out int instance))
            {
                SendPrivateMessage(senderName, "Usage: name clear <instance_id>");
                return;
            }

            if (CustomNameRegistry.RemoveCustomName(instance))
            {
                SendPrivateMessage(senderName, $"Custom name removed for instance {instance}.");
            }
            else
            {
                SendPrivateMessage(senderName, $"No custom name found for instance {instance}.");
            }
        }

        private void HandleSetName(string senderName, string[] args)
        {
            // First argument should be instance ID, rest is the name
            if (args.Length < 2 || !int.TryParse(args[0], out int instance))
            {
                SendPrivateMessage(senderName, "Usage: name <instance_id> <custom name>");
                return;
            }

            string customName = string.Join(" ", args.Skip(1));
            CustomNameRegistry.SetCustomName(instance, customName);
            SendPrivateMessage(senderName, $"Custom name set: [{instance}] = '{customName}'");
        }

        private static void SendPrivateMessage(string playerName, string message)
        {
            try
            {
                var targetPlayer = DynelManager.Players
                    .FirstOrDefault(p => p.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));

                if (targetPlayer != null)
                {
                    Client.SendPrivateMessage((uint)targetPlayer.Identity.Instance, message);
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"Error sending private message: {ex.Message}");
            }
        }
    }
}
