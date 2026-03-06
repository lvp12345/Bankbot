using AOSharp.Clientless.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using AOSharp.Clientless;
using Bankbot.Modules;
using Bankbot.Templates;

namespace Bankbot.Core
{
    /// <summary>
    /// Handles list command messages
    /// </summary>
    public class ListMessageHandler : IMessageHandler
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
                Logger.Information("[LIST HANDLER] Processing list request from " + senderName);

                // LIST command has no org restrictions - anyone can view the catalog
                Logger.Information($"[LIST HANDLER] Player {senderName} requesting list - no org restrictions for list command");

                // Parse arguments: numeric = page number, text = search filter
                int pageNumber = 1;
                string searchFilter = null;

                if (messageInfo.Arguments.Length > 0)
                {
                    if (int.TryParse(messageInfo.Arguments[0], out int parsedPage))
                    {
                        pageNumber = Math.Max(1, parsedPage);
                    }
                    else
                    {
                        searchFilter = string.Join(" ", messageInfo.Arguments);
                    }
                }

                // Generate content based on whether we have a search filter or page number
                string windowContent;
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    windowContent = StorageHelpTemplate.GenerateStorageWindowFiltered(searchFilter);
                    Logger.Information($"[LIST HANDLER] Generated filtered list for '{searchFilter}', length: {windowContent.Length}");
                }
                else
                {
                    windowContent = StorageHelpTemplate.GenerateStorageWindowPaginated(pageNumber);
                    Logger.Information($"[LIST HANDLER] Generated page {pageNumber} content length: {windowContent.Length}");
                }

                SendPrivateMessage(senderName, windowContent);

                Logger.Information($"[LIST HANDLER] Sent storage list to {senderName} (filter: {searchFilter ?? "none"}, page: {pageNumber})");

                var storedItems = ItemTracker.GetStoredItems(true); // Include bags for count
                Logger.Information($"[LIST HANDLER] LIST REQUESTED by {senderName} - {storedItems.Count} items total");
            }
            catch (Exception ex)
            {
                Logger.Information($"[LIST HANDLER] Error sending list to {senderName}: {ex.Message}");
                SendPrivateMessage(senderName, "Error retrieving item list. Please try again.");
            }
        }

        private static void SendPrivateMessage(string playerName, string message)
        {
            try
            {
                // Use ChatClient to send message to remote players by name
                Client.Chat.SendPrivateMessage(playerName, message);
            }
            catch (Exception ex)
            {
                Logger.Information($"Error sending private message: {ex.Message}");
            }
        }
    }
}
