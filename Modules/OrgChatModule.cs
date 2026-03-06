using System;
using System.Linq;
using AOSharp.Clientless;
using AOSharp.Clientless.Chat;
using AOSharp.Clientless.Logging;
using Bankbot.Templates;

namespace Bankbot.Modules
{
    public static class OrgChatModule
    {
        private static bool _initialized = false;
        private static string _commandPrefix = "!";

        public static void Initialize(string commandPrefix)
        {
            if (_initialized) return;

            try
            {
                _commandPrefix = commandPrefix ?? "!";

                if (Client.Chat == null)
                {
                    Logger.Error("[ORG CHAT] Client.Chat is null - cannot register org chat handler");
                    return;
                }

                Client.Chat.GroupMessageReceived += (e, msg) => OnGroupMessageReceived(msg);
                Logger.Information($"[ORG CHAT] Org chat module initialized with prefix '{_commandPrefix}'");

                _initialized = true;
            }
            catch (Exception ex)
            {
                Logger.Error($"[ORG CHAT] Error initializing: {ex.Message}");
            }
        }

        private static void OnGroupMessageReceived(GroupMsg msg)
        {
            try
            {
                // Only process org chat messages
                if (msg.ChannelId != DynelManager.LocalPlayer.OrgId)
                    return;

                // Ignore messages from ourselves
                if (msg.SenderId == Client.Chat.CharId)
                    return;

                string message = msg.Message?.Trim();
                if (string.IsNullOrEmpty(message))
                    return;

                // Check if message starts with command prefix
                if (!message.StartsWith(_commandPrefix, StringComparison.OrdinalIgnoreCase))
                    return;

                // Strip prefix and parse command
                string commandText = message.Substring(_commandPrefix.Length).Trim();
                if (string.IsNullOrEmpty(commandText))
                    return;

                string[] parts = commandText.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                string command = parts[0].ToLower();
                string[] arguments = parts.Skip(1).ToArray();

                Logger.Information($"[ORG CHAT] Command '{command}' from {msg.SenderName}");

                // Only allow help and list - explicitly block get and everything else
                switch (command)
                {
                    case "help":
                        HandleHelpCommand(msg.SenderName);
                        break;
                    case "list":
                        HandleListCommand(msg.SenderName, arguments);
                        break;
                    default:
                        Logger.Information($"[ORG CHAT] Ignoring command '{command}' from {msg.SenderName}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ORG CHAT] Error processing org message: {ex.Message}");
            }
        }

        private static void HandleHelpCommand(string senderName)
        {
            try
            {
                string helpContent = BankbotScriptTemplate.HelpWindow();
                Client.SendOrgMessage(helpContent);
                Logger.Information($"[ORG CHAT] Sent help to org chat (requested by {senderName})");
            }
            catch (Exception ex)
            {
                Logger.Error($"[ORG CHAT] Error handling help command: {ex.Message}");
            }
        }

        private static void HandleListCommand(string senderName, string[] arguments)
        {
            try
            {
                int pageNumber = 1;
                string searchFilter = null;

                if (arguments.Length > 0)
                {
                    if (int.TryParse(arguments[0], out int parsedPage))
                    {
                        pageNumber = Math.Max(1, parsedPage);
                    }
                    else
                    {
                        searchFilter = string.Join(" ", arguments);
                    }
                }

                string windowContent;
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    windowContent = StorageHelpTemplate.GenerateStorageWindowFiltered(searchFilter);
                }
                else
                {
                    windowContent = StorageHelpTemplate.GenerateStorageWindowPaginated(pageNumber);
                }

                Client.SendOrgMessage(windowContent);
                Logger.Information($"[ORG CHAT] Sent list to org chat (requested by {senderName}, filter: {searchFilter ?? "none"}, page: {pageNumber})");
            }
            catch (Exception ex)
            {
                Logger.Error($"[ORG CHAT] Error handling list command: {ex.Message}");
            }
        }

        public static void Cleanup()
        {
            _initialized = false;
        }
    }
}
