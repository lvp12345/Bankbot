using AOSharp.Clientless.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using AOSharp.Core;
using AOSharp.Clientless;
using AOSharp.Common.GameData;
using Bankbot.Modules;
// using Bankbot.Recipes; // COMMENTED OUT - Recipe functionality disabled

namespace Bankbot.Core
{
    /// <summary>
    /// Unified message handling system that routes all player communications through a single entry point
    /// Eliminates duplicate messages and provides consistent response handling
    /// </summary>
    public static class UnifiedMessageHandler
    {
        private static Dictionary<string, IMessageHandler> _handlers;
        private static bool _initialized = false;

        /// <summary>
        /// Initialize the unified message system
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            _handlers = new Dictionary<string, IMessageHandler>(StringComparer.OrdinalIgnoreCase)
            {
                { "help", new HelpMessageHandler() },
                { "list", new ListMessageHandler() },
                { "get", new GetMessageHandler() },
                { "view", new ViewMessageHandler() },
                { "orgcheck", new OrgCheckMessageHandler() }
            };

            _initialized = true;
        }

        /// <summary>
        /// UNIFIED ENTRY POINT: Process all incoming tell messages
        /// </summary>
        /// <param name="senderName">Player name or ID</param>
        /// <param name="message">Message content</param>
        public static void ProcessMessage(string senderName, string message)
        {
            try
            {
                Logger.Information($"[UNIFIED MSG] Processing message '{message}' from {senderName}");

                if (!_initialized)
                {
                    Logger.Information($"[UNIFIED MSG] Not initialized, calling Initialize()");
                    Initialize();
                }

                // Parse the message to determine type and extract arguments
                var messageInfo = ParseMessage(message);
                Logger.Information($"[UNIFIED MSG] Parsed command: '{messageInfo.Command}'");

                // Route to appropriate handler
                if (_handlers.ContainsKey(messageInfo.Command))
                {
                    Logger.Information($"[UNIFIED MSG] Found handler for command '{messageInfo.Command}'");
                    var handler = _handlers[messageInfo.Command];
                    handler.HandleMessage(senderName, messageInfo);
                }
                else
                {
                    // Unknown command - silently ignore (following existing pattern)
                    Logger.Information($"[UNIFIED MSG] Unknown command '{messageInfo.Command}' from {senderName}");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[UNIFIED MSG] Error processing message from {senderName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Parse incoming message to determine command type and arguments
        /// </summary>
        private static MessageInfo ParseMessage(string message)
        {
            var parts = message.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length == 0)
            {
                return new MessageInfo { Command = "unknown", Arguments = new string[0] };
            }

            string command = parts[0].ToLower();
            string[] arguments = parts.Skip(1).ToArray();



            return new MessageInfo 
            { 
                Command = command, 
                Arguments = arguments 
            };
        }

        /// <summary>
        /// UNIFIED RESPONSE: Send messages to players with consistent formatting
        /// </summary>
        public static void SendResponse(string targetName, string message, MessageType messageType = MessageType.Standard)
        {
            try
            {
                // Apply consistent formatting based on message type
                string formattedMessage = FormatMessage(message, messageType);
                
                // Send through existing infrastructure
                SendPrivateMessage(targetName, formattedMessage);
            }
            catch (Exception ex)
            {
                Logger.Information($"[UNIFIED MSG] Error sending response to {targetName}: {ex.Message}");
            }
        }

        private static void SendPrivateMessage(string playerName, string message)
        {
            try
            {
                var targetPlayer = AOSharp.Clientless.DynelManager.Players
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

        /// <summary>
        /// Format messages consistently based on type
        /// </summary>
        private static string FormatMessage(string message, MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.Error:
                    return $"Error: {message}";
                case MessageType.Success:
                    return message; // Success messages are already formatted
                case MessageType.Info:
                    return message;
                case MessageType.Standard:
                default:
                    return message;
            }
        }
    }

    /// <summary>
    /// Message information parsed from incoming tell
    /// </summary>
    public class MessageInfo
    {
        public string Command { get; set; }
        public string[] Arguments { get; set; }
        public string OriginalCommand { get; set; }
    }

    /// <summary>
    /// Message type for consistent formatting
    /// </summary>
    public enum MessageType
    {
        Standard,
        Info,
        Success,
        Error
    }

    /// <summary>
    /// Interface for message handlers
    /// </summary>
    public interface IMessageHandler
    {
        void HandleMessage(string senderName, MessageInfo messageInfo);
    }
}
