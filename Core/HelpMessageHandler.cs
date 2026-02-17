using AOSharp.Clientless.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Clientless;
using Bankbot.Templates;

namespace Bankbot.Core
{
    public class HelpMessageHandler : IMessageHandler
    {
        public void HandleMessage(string senderName, MessageInfo messageInfo)
        {
            _ = Task.Run(async () => await HandleMessageAsync(senderName, messageInfo));
        }

        private async Task HandleMessageAsync(string senderName, MessageInfo messageInfo)
        {
            try
            {
                Logger.Information($"[HELP HANDLER] Processing help request from {senderName}");

                var targetPlayer = DynelManager.Players.FirstOrDefault(p =>
                    p.Name.Equals(senderName, StringComparison.OrdinalIgnoreCase));

                if (targetPlayer != null)
                {
                    bool isAllowed = await OrgLockoutConfig.IsPlayerOrgAllowedAsync(targetPlayer.Identity.Instance, senderName);
                    if (!isAllowed)
                    {
                        Logger.Information($"[HELP HANDLER] Player {senderName} organization not allowed");
                        SendPrivateMessage(senderName, "Access denied - your organization is not authorized to use this bot.");
                        return;
                    }
                }

                string helpContent;

                if (messageInfo.Arguments.Length > 0)
                {
                    string category = messageInfo.Arguments[0].ToLower();
                    if (category == "storage")
                    {
                        helpContent = StorageHelpTemplate.GenerateStorageWindowPaginated(1);
                    }
                    else
                    {
                        helpContent = BankbotScriptTemplate.CategoryHelpWindow(category);
                    }
                }
                else
                {
                    helpContent = BankbotScriptTemplate.HelpWindow();
                }

                SendPrivateMessage(senderName, helpContent);
                Logger.Information($"[HELP HANDLER] Help sent to {senderName}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[HELP HANDLER] Error processing help request from {senderName}: {ex.Message}");
                SendPrivateMessage(senderName, "Error processing help request. Please try again.");
            }
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
