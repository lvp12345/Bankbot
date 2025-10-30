using System;
using AOSharp.Clientless;
using AOSharp.Clientless.Logging;
using AOSharp.Clientless.Chat;
using Bankbot.Core;

namespace Bankbot.Modules
{
    public static class PrivateMessageModule
    {
        public static void Initialize()
        {
            try
            {
                Logger.Information("[PM] Starting PrivateMessageModule initialization...");

                // Check if Client.Chat is available
                if (Client.Chat == null)
                {
                    Logger.Error("[PM] Client.Chat is null - cannot register private message handler");
                    return;
                }

                Logger.Information("[PM] Client.Chat is available, registering event handler...");

                // Register for private messages in clientless mode
                Client.Chat.PrivateMessageReceived += (e, msg) => OnPrivateMessageReceived(msg);
                Logger.Information("[PM] Private message handler registered for clientless mode");

                // Also initialize the UnifiedMessageHandler
                Logger.Information("[PM] Initializing UnifiedMessageHandler...");
                UnifiedMessageHandler.Initialize();
                Logger.Information("[PM] UnifiedMessageHandler initialized");
            }
            catch (Exception ex)
            {
                Logger.Error($"[PM] Error initializing: {ex.Message}");
                Logger.Error($"[PM] Stack trace: {ex.StackTrace}");
            }
        }

        private static void OnPrivateMessageReceived(PrivateMessage msg)
        {
            try
            {
                string senderName = msg.SenderName;
                string content = msg.Message;

                if (!string.IsNullOrEmpty(senderName) && !string.IsNullOrEmpty(content))
                {
                    Logger.Information($"[PM] Received from {senderName}: {content}");
                    ProcessCommand(senderName, content);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[PM] Error processing message: {ex.Message}");
            }
        }

        private static void ProcessCommand(string senderName, string content)
        {
            try
            {
                // Route to unified message handler
                UnifiedMessageHandler.ProcessMessage(senderName, content);
            }
            catch (Exception ex)
            {
                Logger.Error($"[PM] Error processing command: {ex.Message}");
            }
        }

        public static void Cleanup()
        {
            try
            {
                Client.Chat.PrivateMessageReceived -= (e, msg) => OnPrivateMessageReceived(msg);
                Logger.Information("[PM] Private message handler unregistered");
            }
            catch (Exception ex)
            {
                Logger.Error($"[PM] Error during cleanup: {ex.Message}");
            }
        }
    }
}
