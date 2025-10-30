using AOSharp.Clientless.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Clientless;
using Bankbot.Modules;
using Bankbot.Core;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles GET command messages for retrieving stored items
    /// </summary>
    public class GetMessageHandler : IMessageHandler
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
                Logger.Information($"[GET HANDLER] Processing get request from {senderName}");

                // Check org lockout first - force refresh org data to ensure accuracy
                var targetPlayer = DynelManager.Players.FirstOrDefault(p =>
                    p.Name.Equals(senderName, StringComparison.OrdinalIgnoreCase));

                if (targetPlayer != null)
                {
                    // Force refresh nearby players org info to ensure we have current data
                    OrgLockoutConfig.RefreshNearbyPlayersOrgInfo();
                    await Task.Delay(500); // Give it time to process

                    bool isAllowed = await OrgLockoutConfig.IsPlayerOrgAllowedAsync(targetPlayer.Identity.Instance, senderName);
                    if (!isAllowed)
                    {
                        Logger.Information($"[GET HANDLER] Player {senderName} organization not allowed for GET command");
                        SendPrivateMessage(senderName, "Access denied - your organization is not authorized to use this bot.");
                        return;
                    }
                }
                else
                {
                    Logger.Information($"[GET HANDLER] Player {senderName} not found nearby for org check");
                    SendPrivateMessage(senderName, "You must be near the bot to use commands. Please come closer and try again.");
                    return;
                }

                if (messageInfo.Arguments.Length == 0)
                {
                    SendPrivateMessage(senderName, "Usage: get <item name>");
                    return;
                }

                StoredItem item = null;
                string itemName;

                // Check if the last argument is a number (instance ID for bags)
                if (messageInfo.Arguments.Length >= 2 &&
                    uint.TryParse(messageInfo.Arguments[messageInfo.Arguments.Length - 1], out uint instanceId))
                {
                    // Last argument is an instance ID, so this is a bag request
                    itemName = string.Join(" ", messageInfo.Arguments.Take(messageInfo.Arguments.Length - 1));
                    Logger.Information($"[GET HANDLER] Looking for bag: '{itemName}' with instance: {instanceId}");

                    // Try with the clean name first, then try to find by searching all items
                    item = ItemTracker.FindItemByNameAndInstance(itemName, instanceId);
                    if (item == null)
                    {
                        // Search all items to find one that matches when quotes are removed
                        var allItems = ItemTracker.GetStoredItems(true);
                        item = allItems.Cast<StoredItem>().FirstOrDefault(i =>
                            i.ItemInstance == instanceId &&
                            i.Name.Replace("'", "") == itemName);

                        Logger.Information($"[GET HANDLER] Fallback search found: {(item != null ? item.Name : "nothing")}");
                    }
                }
                else
                {
                    // No instance ID, so this is a regular item request
                    itemName = string.Join(" ", messageInfo.Arguments);
                    Logger.Information($"[GET HANDLER] Looking for item: '{itemName}'");

                    // Try with the clean name first, then try to find by searching all items
                    item = ItemTracker.FindItemByName(itemName);
                    if (item == null)
                    {
                        // Search all items to find one that matches when quotes are removed
                        var allItems = ItemTracker.GetStoredItems(true);
                        item = allItems.Cast<StoredItem>().FirstOrDefault(i =>
                            i.Name.Replace("'", "") == itemName);

                        Logger.Information($"[GET HANDLER] Fallback search found: {(item != null ? item.Name : "nothing")}");
                    }
                }

                if (item == null)
                {
                    Logger.Information($"[GET HANDLER] Item '{itemName}' not found in storage");
                    SendPrivateMessage(senderName, $"Item '{itemName}' not found in storage.");
                    return;
                }

                Logger.Information($"[GET HANDLER] Found item: {item.Name}");

                // Log the GET command immediately with bag contents if it's a bag
                TradeLogger.LogGetCommand(senderName, item);

                // Initiate trade with the player
                TradingSystem.InitiateTrade(senderName, item);

                ItemTracker.LogTransaction(senderName, $"GET REQUESTED: {item.Name}");
                Logger.Information($"[GET HANDLER] GET request processed for {senderName}: {item.Name}");
            }
            catch (Exception ex)
            {
                Logger.Information($"[GET HANDLER] Error processing get request from {senderName}: {ex.Message}");
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
