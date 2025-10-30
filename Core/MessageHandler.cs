using AOSharp.Clientless.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Core;
using AOSharp.Clientless;
using AOSharp.Common.GameData;
using Bankbot.Modules;

namespace Bankbot.Core
{
    /// <summary>
    /// Central message handler for Bankbot commands
    /// </summary>
    public static class MessageHandler
    {
        private static bool _initialized = false;
        private static Dictionary<string, Action<string, string[]>> _commandHandlers;

        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                _commandHandlers = new Dictionary<string, Action<string, string[]>>(StringComparer.OrdinalIgnoreCase)
                {
                    // List command now handled by UnifiedMessageHandler -> ListMessageHandler
                };

                _initialized = true;
                ItemTracker.LogTransaction("SYSTEM", "MessageHandler initialized");
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"Error initializing MessageHandler: {ex.Message}");
            }
        }

        /// <summary>
        /// Process a command from a player
        /// </summary>
        public static void ProcessCommand(string senderName, string message)
        {
            try
            {
                if (!_initialized) Initialize();

                // Parse the command
                string[] parts = message.Trim().Split(' ');
                if (parts.Length == 0) return;

                string command = parts[0].ToLower();
                string[] args = parts.Skip(1).ToArray();

                // Check if command exists
                if (_commandHandlers.ContainsKey(command))
                {
                    _commandHandlers[command](senderName, args);
                }
                else
                {
                    // Silently ignore unknown commands
                }
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"Error processing command from {senderName}: {ex.Message}");
                SendPrivateMessage(senderName, 
                    "Error processing command. Please try again.");
            }
        }



        // List command now handled by UnifiedMessageHandler -> ListMessageHandler

        private static void HandleGetCommand(string senderName, string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    SendPrivateMessage(senderName, "Usage: get <item name>");
                    return;
                }

                string itemName = string.Join(" ", args);
                var item = ItemTracker.FindItemByName(itemName);

                if (item == null)
                {
                    SendPrivateMessage(senderName, $"Item '{itemName}' not found in storage.");
                    return;
                }

                // Initiate trade with the player
                TradingSystem.InitiateTrade(senderName, item);

                ItemTracker.LogTransaction(senderName, $"GET REQUESTED: {item.Name}");
            }
            catch (Exception ex)
            {
                ItemTracker.LogTransaction("SYSTEM", $"Error handling get command for {senderName}: {ex.Message}");
                SendPrivateMessage(senderName, "Error processing get request. Please try again.");
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
