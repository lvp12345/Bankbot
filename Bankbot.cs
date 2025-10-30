using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks;
using AOSharp.Clientless;
using AOSharp.Clientless.Logging;
using AOSharp.Common.GameData;
using SmokeLounge.AOtomation.Messaging.GameData;
using SmokeLounge.AOtomation.Messaging.Messages;
using SmokeLounge.AOtomation.Messaging.Messages.N3Messages;
using SmokeLounge.AOtomation.Messaging.Messages.ChatMessages;
using Bankbot.Modules;
using static Bankbot.Modules.PrivateMessageModule;

namespace Bankbot
{
    public class Bankbot : ClientlessPluginEntry
    {
        public static Config Config { get; private set; }
        public static string PluginDir { get; private set; }

        public override void Init(string pluginDir)
        {
            try
            {
                // Set the plugin directory for access by modules
                PluginDir = pluginDir;

                // Initialize logging first so we can log everything else
                // Note: Logging is initialized in PrivateMessageModule.Initialize()

                // Load config from current directory (where launcher runs)
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                Config = Config.Load(configPath);

                // Log successful initialization start
                Logger.Information($"[BANKBOT] Initializing Bankbot plugin in directory: {pluginDir}");
                Logger.Information($"[BANKBOT] Character: {Client.CharacterName}");

                // Initialize Private Message module (always active) - this also initializes logging
                Logger.Information("[BANKBOT] About to initialize PrivateMessageModule...");
                PrivateMessageModule.Initialize();
                Logger.Information("[BANKBOT] PrivateMessageModule initialization completed");

                // Initialize storage and logging systems
                Core.ItemTracker.Initialize();
                Core.TradeLogger.Initialize();
                Core.TradingSystem.Initialize();
                Core.OrgLockoutConfig.Initialize();
                Core.ItemSorter.Initialize();

                // Initialize web server if enabled
                var charSettings = Config.CharSettings.ContainsKey(Client.CharacterName)
                    ? Config.CharSettings[Client.CharacterName]
                    : new CharacterSettings();

                if (charSettings.WebInterfaceEnabled)
                {
                    Logger.Information($"[BANKBOT] Starting web interface on port {charSettings.WebInterfacePort}");
                    Core.WebServer.Initialize(Client.CharacterName, charSettings.WebInterfacePort);
                }

                // Pre-cache the storage window for instant list commands
                Templates.StorageHelpTemplate.PreCacheStorageWindow();

                // Set up network message handling for Private Message trade events
                Client.MessageReceived += Network_N3MessageReceived;
                Logger.Information("[BANKBOT] Network message handler registered");

                // Make the bot stand up on startup
                Logger.Information("[BANKBOT] 🤖 Making bot stand up...");
                StandUp();
                Logger.Information("[BANKBOT] Stand up command sent");

                // Schedule bag opening after character is fully loaded
                Logger.Information("[BANKBOT] 🎒 Scheduling bag opening...");
                Task.Run(async () =>
                {
                    // Wait for character to be fully loaded first
                    await Task.Delay(3000); // Wait 3 seconds for character to load

                    // Now open all bags
                    Logger.Information("[BANKBOT] Opening all bags in inventory...");
                    await OpenAllBagsAsync();
                    Logger.Information("[BANKBOT] ✅ All bags opened and ready");

                    // Wait for bags to open and contents to load
                    await Task.Delay(2000); // Wait 2 seconds for bags to open
                    Logger.Information("[BANKBOT] Bag opening completed - inventory ready for trading");

                    // Auto-sort items on startup if enabled
                    var sortSettings = Config.CharSettings.ContainsKey(Client.CharacterName)
                        ? Config.CharSettings[Client.CharacterName]
                        : new CharacterSettings();

                    if (sortSettings.AutoSortEnabled)
                    {
                        Logger.Information("[BANKBOT] 📦 Starting auto-sort on startup...");
                        await Core.ItemSorter.SortAllItems();
                        Logger.Information("[BANKBOT] ✅ Startup auto-sort complete");
                    }
                });

                Logger.Information("[BANKBOT] Bankbot plugin initialization completed successfully");
                Logger.Information("[BANKBOT] Running in event-driven mode - no game update handler needed");

                Logger.Information("=== BANKBOT READY ===");
            }
            catch (Exception ex)
            {
                // Log initialization errors
                try
                {
                    Logger.Information($"[BANKBOT] Error during initialization: {ex.Message}");
                    Logger.Information($"[BANKBOT] Stack trace: {ex.StackTrace}");
                }
                catch
                {
                    // If logging fails, we can't do much
                }
            }
        }

        public override void Teardown()
        {
            try
            {
                Logger.Information("[BANKBOT] Starting plugin teardown...");

                Core.WebServer.Shutdown();
                PrivateMessageModule.Cleanup();
                Core.TradingSystem.Cleanup();
                Client.MessageReceived -= Network_N3MessageReceived;

                Logger.Information("[BANKBOT] Plugin teardown completed successfully");
            }
            catch (Exception ex)
            {
                try
                {
                    Logger.Information($"[BANKBOT] Error during teardown: {ex.Message}");
                }
                catch
                {
                    // Silent error handling if logging fails
                }
            }
        }

        // No settings window - automatic processing only

        // No game update handler needed - event-driven processing only

        /// <summary>
        /// Open all bags in inventory with small delays between each
        /// </summary>
        private async Task OpenAllBagsAsync()
        {
            try
            {
                // Check if inventory is available
                if (Inventory.Items == null)
                {
                    Logger.Information("[BANKBOT] Inventory not available yet, skipping bag opening");
                    return;
                }

                // Find all bags in inventory
                var bags = Inventory.Items.Where(item =>
                    item.UniqueIdentity.Type == IdentityType.Container).ToList();

                Logger.Information($"[BANKBOT] Found {bags.Count} bags to open");

                foreach (var bag in bags)
                {
                    try
                    {
                        Logger.Information($"[BANKBOT] Opening bag: {bag.Name}");
                        bag.Use(); // Open the bag

                        // Small delay between bag openings (50ms as requested)
                        await Task.Delay(50);
                    }
                    catch (Exception ex)
                    {
                        Logger.Information($"[BANKBOT] Error opening bag {bag.Name}: {ex.Message}");
                    }
                }

                Logger.Information($"[BANKBOT] Finished opening {bags.Count} bags");
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANKBOT] Error in OpenAllBagsAsync: {ex.Message}");
            }
        }

        private void Network_N3MessageReceived(object s, Message msg)
        {
            try
            {
                // Trade handling is now done through TradeStatusChanged events
                // Only log important messages, not spam
                if (msg.Body is TradeMessage tradeMsg)
                {
                    Logger.Information($"[NETWORK] Received trade message: {tradeMsg.Action}");
                }

                // Only log private messages if they come through N3 system
                if (msg.Body.GetType().Name.Contains("Private") || msg.Body.GetType().Name.Contains("Tell"))
                {
                    Logger.Information($"[NETWORK DEBUG] FOUND PRIVATE MESSAGE: {msg.Body.GetType().Name}");
                    Logger.Information($"[NETWORK DEBUG] Message details: {msg.Body}");
                }
            }
            catch (Exception ex)
            {
                Logger.Information($"[NETWORK] Error handling network message: {ex.Message}");
            }
        }

        /// <summary>
        /// Makes the bot stand up using the same method as Craftbot
        /// </summary>
        private async void StandUp()
        {
            try
            {
                // Wait a moment for LocalPlayer to be fully initialized
                await Task.Delay(1000);

                if (DynelManager.LocalPlayer == null)
                {
                    Logger.Information("[BANKBOT] LocalPlayer not initialized yet, retrying in 3 seconds...");
                    _ = Task.Delay(3000).ContinueWith(_ => StandUp());
                    return;
                }

                // Use the same method as Craftbot for standing up
                Logger.Information("[BANKBOT] Attempting to stand up using clientless method...");

                DynelManager.LocalPlayer.MovementComponent.ChangeMovement(MovementAction.LeaveSit);

                Logger.Information("[BANKBOT] ✅ Stand up command sent successfully using MovementComponent.ChangeMovement");
            }
            catch (Exception ex)
            {
                Logger.Information($"[BANKBOT] Error during stand up: {ex.Message}");
                Logger.Information("[BANKBOT] Will retry stand up in 5 seconds...");
                _ = Task.Delay(5000).ContinueWith(_ => StandUp());
            }
        }


    }
}
