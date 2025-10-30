using AOSharp.Clientless.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Clientless;
using Bankbot.Modules;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles view command messages for viewing and retrieving specific items by instance ID
    /// </summary>
    public class ViewMessageHandler : IMessageHandler
    {
        public void HandleMessage(string senderName, MessageInfo messageInfo)
        {
            // Handle async operation
            _ = Task.Run(async () => await HandleMessageAsync(senderName, messageInfo));
        }

        private async Task HandleMessageAsync(string senderName, MessageInfo messageInfo)
        {
            try
            {
                Logger.Information($"[VIEW HANDLER] Processing view request from {senderName}");

                // Check org lockout first
                var targetPlayer = DynelManager.Players.FirstOrDefault(p =>
                    p.Name.Equals(senderName, StringComparison.OrdinalIgnoreCase));

                if (targetPlayer != null)
                {
                    bool isAllowed = await OrgLockoutConfig.IsPlayerOrgAllowedAsync(targetPlayer.Identity.Instance, senderName);
                    if (!isAllowed)
                    {
                        Logger.Information($"[VIEW HANDLER] Player {senderName} organization not allowed for VIEW command");
                        SendPrivateMessage(senderName, "Access denied - your organization is not authorized to use this bot.");
                        return;
                    }
                }

                // Parse arguments - expecting item ID and instance ID
                if (messageInfo.Arguments.Length < 2)
                {
                    Logger.Information($"[VIEW HANDLER] Invalid view command format from {senderName}");
                    SendPrivateMessage(senderName, "Usage: view <itemId> <instanceId>");
                    return;
                }

                // Try to parse the item ID and instance ID
                if (!uint.TryParse(messageInfo.Arguments[0], out uint itemId) || 
                    !uint.TryParse(messageInfo.Arguments[1], out uint instanceId))
                {
                    Logger.Information($"[VIEW HANDLER] Invalid item ID or instance ID from {senderName}");
                    SendPrivateMessage(senderName, "Invalid item ID or instance ID format.");
                    return;
                }

                Logger.Information($"[VIEW HANDLER] Looking for item with ID: {itemId}, Instance: {instanceId}");

                // Find the item by ID and instance
                var storedItems = ItemTracker.GetStoredItems(true);
                var item = storedItems.Cast<StoredItem>().FirstOrDefault(i => 
                    i.Id == itemId && i.ItemInstance == instanceId);

                if (item == null)
                {
                    Logger.Information($"[VIEW HANDLER] Item with ID {itemId} and instance {instanceId} not found");
                    SendPrivateMessage(senderName, "Item not found in storage.");
                    return;
                }

                Logger.Information($"[VIEW HANDLER] Found item: {item.Name}");

                // Log the VIEW command immediately with bag contents if it's a bag
                TradeLogger.LogGetCommand(senderName, item);

                // Initiate trade with the player (same as GET command)
                TradingSystem.InitiateTrade(senderName, item);

                ItemTracker.LogTransaction(senderName, $"VIEW REQUESTED: {item.Name} (ID: {itemId}, Instance: {instanceId})");
                Logger.Information($"[VIEW HANDLER] VIEW request processed for {senderName}: {item.Name}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[VIEW HANDLER] Error processing view request from {senderName}: {ex.Message}");
                SendPrivateMessage(senderName, "Error processing view request. Please try again.");
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
