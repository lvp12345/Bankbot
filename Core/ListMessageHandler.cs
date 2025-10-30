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

                // Parse page number from command (default to page 1)
                int pageNumber = 1;
                if (messageInfo.Arguments.Length > 0 && int.TryParse(messageInfo.Arguments[0], out int parsedPage))
                {
                    pageNumber = Math.Max(1, parsedPage); // Ensure page is at least 1
                }

                // Generate paginated content
                string windowContent = StorageHelpTemplate.GenerateStorageWindowPaginated(pageNumber);

                Logger.Information($"[LIST HANDLER] Generated page {pageNumber} content length: " + windowContent.Length);
                Logger.Information("[LIST HANDLER] Window content preview: " + windowContent.Substring(0, Math.Min(100, windowContent.Length)));

                SendPrivateMessage(senderName, windowContent);

                Logger.Information($"[LIST HANDLER] Sent storage list page {pageNumber} to " + senderName);

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
