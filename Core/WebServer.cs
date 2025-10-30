using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AOSharp.Clientless;
using AOSharp.Clientless.Logging;
using Newtonsoft.Json;

namespace Bankbot.Core
{
    public class WebServer
    {
        private static HttpListener _listener;
        private static bool _isRunning = false;
        private static Thread _listenerThread;
        private static string _botName;
        private static int _port;

        public static void Initialize(string botName, int port = 5000)
        {
            try
            {
                _botName = botName;
                _port = port;

                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://+:{port}/");
                _listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous; // No authentication required

                Logger.Information($"[WEB SERVER] Starting web server on port {port}");

                _listener.Start();
                _isRunning = true;

                _listenerThread = new Thread(HandleRequests);
                _listenerThread.IsBackground = true;
                _listenerThread.Start();

                Logger.Information($"[WEB SERVER] Web interface available at http://localhost:{port}/");
                ItemTracker.LogTransaction("SYSTEM", $"Web server started on port {port}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WEB SERVER] Failed to start web server: {ex.Message}");
                ItemTracker.LogTransaction("SYSTEM", $"Web server failed to start: {ex.Message}");
            }
        }

        public static void Shutdown()
        {
            try
            {
                _isRunning = false;
                _listener?.Stop();
                _listener?.Close();
                Logger.Information("[WEB SERVER] Web server stopped");
            }
            catch (Exception ex)
            {
                Logger.Error($"[WEB SERVER] Error stopping web server: {ex.Message}");
            }
        }

        private static void HandleRequests()
        {
            while (_isRunning)
            {
                try
                {
                    var context = _listener.GetContext();
                    Task.Run(() => ProcessRequest(context));
                }
                catch (HttpListenerException)
                {
                    // Listener was stopped
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Error($"[WEB SERVER] Error handling request: {ex.Message}");
                }
            }
        }

        private static void ProcessRequest(HttpListenerContext context)
        {
            try
            {
                var request = context.Request;
                var response = context.Response;

                Logger.Information($"[WEB SERVER] Request: {request.HttpMethod} {request.Url.AbsolutePath}");

                // Set CORS headers to allow access from anywhere
                response.Headers.Add("Access-Control-Allow-Origin", "*");
                response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (request.HttpMethod == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                string responseString = "";
                string contentType = "text/html";

                switch (request.Url.AbsolutePath)
                {
                    case "/":
                        responseString = GetIndexPage();
                        break;
                    case "/api/items":
                        responseString = GetItemsJson();
                        contentType = "application/json";
                        break;
                    case "/api/stats":
                        responseString = GetStatsJson();
                        contentType = "application/json";
                        break;
                    default:
                        response.StatusCode = 404;
                        responseString = "<html><body><h1>404 - Not Found</h1></body></html>";
                        break;
                }

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentType = contentType + "; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Logger.Error($"[WEB SERVER] Error processing request: {ex.Message}");
            }
        }

        private static string GetItemsJson()
        {
            try
            {
                var items = ItemTracker.GetStoredItems(true); // Include bags
                var allItems = new List<object>();
                var bags = new List<object>();
                var looseItems = new List<object>();

                foreach (var item in items)
                {
                    // Use reflection to get properties since StoredItem is nested
                    var itemType = item.GetType();
                    var idProp = itemType.GetProperty("Id");
                    var nameProp = itemType.GetProperty("Name");
                    var qualityProp = itemType.GetProperty("Quality");
                    var qualityLevelProp = itemType.GetProperty("QualityLevel");
                    var stackCountProp = itemType.GetProperty("StackCount");
                    var itemInstanceProp = itemType.GetProperty("ItemInstance");
                    var sourceBagNameProp = itemType.GetProperty("SourceBagName");
                    var sourceBagInstanceProp = itemType.GetProperty("SourceBagInstance");
                    var isContainerProp = itemType.GetProperty("IsContainer");
                    var storedByProp = itemType.GetProperty("StoredBy");
                    var storedAtProp = itemType.GetProperty("StoredAt");

                    if (nameProp != null)
                    {
                        var itemData = new
                        {
                            id = idProp?.GetValue(item) ?? 0,
                            name = nameProp.GetValue(item)?.ToString() ?? "",
                            quality = qualityProp?.GetValue(item) ?? 0,
                            qualityLevel = qualityLevelProp?.GetValue(item) ?? 0,
                            stackCount = stackCountProp?.GetValue(item) ?? 1,
                            itemInstance = itemInstanceProp?.GetValue(item) ?? 0,
                            sourceBagName = sourceBagNameProp?.GetValue(item)?.ToString(),
                            sourceBagInstance = sourceBagInstanceProp?.GetValue(item),
                            isContainer = isContainerProp?.GetValue(item) ?? false,
                            storedBy = storedByProp?.GetValue(item)?.ToString() ?? "",
                            storedAt = ((DateTime)(storedAtProp?.GetValue(item) ?? DateTime.Now)).ToString("yyyy-MM-dd HH:mm:ss")
                        };

                        allItems.Add(itemData);

                        // Separate bags from regular items
                        if ((bool)(isContainerProp?.GetValue(item) ?? false))
                        {
                            bags.Add(itemData);
                        }
                        else if (string.IsNullOrEmpty(sourceBagNameProp?.GetValue(item)?.ToString()))
                        {
                            looseItems.Add(itemData);
                        }
                    }
                }

                Logger.Information($"[WEB SERVER] Returning {allItems.Count} total items, {bags.Count} bags, {looseItems.Count} loose items");

                return JsonConvert.SerializeObject(new {
                    items = allItems,
                    bags = bags,
                    looseItems = looseItems,
                    botName = _botName
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[WEB SERVER] Error getting items JSON: {ex.Message}");
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }

        private static string GetStatsJson()
        {
            try
            {
                var inventoryStats = ItemTracker.GetInventorySpaceStats();
                var bagStats = ItemTracker.GetBagSpaceStats();

                return JsonConvert.SerializeObject(new
                {
                    botName = _botName,
                    inventory = new
                    {
                        used = inventoryStats.usedSlots,
                        total = inventoryStats.totalSlots,
                        free = inventoryStats.totalSlots - inventoryStats.usedSlots,
                        almostFull = inventoryStats.isAlmostFull
                    },
                    bags = new
                    {
                        used = bagStats.usedBagSlots,
                        total = bagStats.totalBagSlots,
                        free = bagStats.totalBagSlots - bagStats.usedBagSlots
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"[WEB SERVER] Error getting stats JSON: {ex.Message}");
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }

        private static string GetIndexPage()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Bankbot - Item Catalog</title>
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #1e3c72 0%, #2a5298 100%);
            color: #fff;
            padding: 20px;
            min-height: 100vh;
        }
        
        .container {
            max-width: 1400px;
            margin: 0 auto;
            background: rgba(0, 0, 0, 0.3);
            border-radius: 10px;
            padding: 30px;
            box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
        }
        
        h1 {
            text-align: center;
            margin-bottom: 10px;
            font-size: 2.5em;
            text-shadow: 2px 2px 4px rgba(0, 0, 0, 0.5);
        }
        
        .subtitle {
            text-align: center;
            margin-bottom: 30px;
            opacity: 0.8;
            font-size: 1.1em;
        }
        
        .stats-bar {
            display: flex;
            justify-content: space-around;
            margin-bottom: 30px;
            padding: 20px;
            background: rgba(255, 255, 255, 0.1);
            border-radius: 8px;
        }
        
        .stat-item {
            text-align: center;
        }
        
        .stat-value {
            font-size: 2em;
            font-weight: bold;
            color: #4CAF50;
        }
        
        .stat-label {
            font-size: 0.9em;
            opacity: 0.8;
            margin-top: 5px;
        }
        
        .controls {
            display: flex;
            gap: 15px;
            margin-bottom: 20px;
            flex-wrap: wrap;
        }
        
        .search-box {
            flex: 1;
            min-width: 250px;
            padding: 12px 20px;
            font-size: 16px;
            border: 2px solid rgba(255, 255, 255, 0.3);
            border-radius: 25px;
            background: rgba(255, 255, 255, 0.1);
            color: #fff;
            outline: none;
            transition: all 0.3s;
        }
        
        .search-box:focus {
            border-color: #4CAF50;
            background: rgba(255, 255, 255, 0.15);
        }
        
        .search-box::placeholder {
            color: rgba(255, 255, 255, 0.5);
        }
        
        .sort-select {
            padding: 12px 20px;
            font-size: 16px;
            border: 2px solid rgba(255, 255, 255, 0.3);
            border-radius: 25px;
            background: rgba(255, 255, 255, 0.1);
            color: #fff;
            outline: none;
            cursor: pointer;
        }
        
        .sort-select option {
            background: #2a5298;
            color: #fff;
        }
        
        .refresh-btn {
            padding: 12px 30px;
            font-size: 16px;
            border: none;
            border-radius: 25px;
            background: #4CAF50;
            color: white;
            cursor: pointer;
            transition: all 0.3s;
            font-weight: bold;
        }
        
        .refresh-btn:hover {
            background: #45a049;
            transform: translateY(-2px);
            box-shadow: 0 4px 8px rgba(0, 0, 0, 0.3);
        }
        
        .items-table {
            width: 100%;
            border-collapse: collapse;
            background: rgba(255, 255, 255, 0.05);
            border-radius: 8px;
            overflow: hidden;
        }
        
        .items-table thead {
            background: rgba(0, 0, 0, 0.3);
        }
        
        .items-table th {
            padding: 15px;
            text-align: left;
            font-weight: 600;
            border-bottom: 2px solid rgba(255, 255, 255, 0.1);
            cursor: pointer;
            user-select: none;
        }
        
        .items-table th:hover {
            background: rgba(255, 255, 255, 0.1);
        }
        
        .items-table td {
            padding: 12px 15px;
            border-bottom: 1px solid rgba(255, 255, 255, 0.05);
        }
        
        .items-table tbody tr {
            transition: all 0.2s;
        }
        
        .items-table tbody tr:hover {
            background: rgba(255, 255, 255, 0.1);
        }
        
        .get-btn {
            padding: 8px 20px;
            background: #2196F3;
            color: white;
            border: none;
            border-radius: 20px;
            cursor: pointer;
            font-weight: bold;
            transition: all 0.3s;
        }
        
        .get-btn:hover {
            background: #0b7dda;
            transform: scale(1.05);
        }
        
        .get-btn:active {
            transform: scale(0.95);
        }
        
        .loading {
            text-align: center;
            padding: 40px;
            font-size: 1.2em;
        }
        
        .error {
            text-align: center;
            padding: 40px;
            color: #ff6b6b;
            font-size: 1.2em;
        }
        
        .toast {
            position: fixed;
            bottom: 30px;
            right: 30px;
            background: #4CAF50;
            color: white;
            padding: 15px 25px;
            border-radius: 8px;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
            opacity: 0;
            transition: opacity 0.3s;
            z-index: 1000;
        }
        
        .toast.show {
            opacity: 1;
        }
        
        .bag-badge {
            display: inline-block;
            padding: 4px 10px;
            background: rgba(255, 152, 0, 0.3);
            border-radius: 12px;
            font-size: 0.85em;
            border: 1px solid rgba(255, 152, 0, 0.5);
        }
        
        .container-badge {
            display: inline-block;
            padding: 4px 10px;
            background: rgba(156, 39, 176, 0.3);
            border-radius: 12px;
            font-size: 0.85em;
            border: 1px solid rgba(156, 39, 176, 0.5);
        }
    </style>
</head>
<body>
    <div class=""container"">
        <h1 id=""botName"">Bankbot Item Catalog</h1>
        <div class=""subtitle"">Real-time inventory viewer</div>
        
        <div class=""stats-bar"" id=""statsBar"">
            <div class=""stat-item"">
                <div class=""stat-value"" id=""totalItems"">-</div>
                <div class=""stat-label"">Total Items</div>
            </div>
            <div class=""stat-item"">
                <div class=""stat-value"" id=""inventoryFree"">-</div>
                <div class=""stat-label"">Free Inventory Slots</div>
            </div>
            <div class=""stat-item"">
                <div class=""stat-value"" id=""bagsFree"">-</div>
                <div class=""stat-label"">Free Bag Slots</div>
            </div>
        </div>
        
        <div class=""controls"">
            <input type=""text"" class=""search-box"" id=""searchBox"" placeholder=""Search items..."">
            <select class=""sort-select"" id=""sortSelect"">
                <option value=""name-asc"">Name (A-Z)</option>
                <option value=""name-desc"">Name (Z-A)</option>
                <option value=""ql-asc"">Quality Level (Low-High)</option>
                <option value=""ql-desc"">Quality Level (High-Low)</option>
                <option value=""date-asc"">Date Added (Oldest)</option>
                <option value=""date-desc"">Date Added (Newest)</option>
            </select>
            <button class=""refresh-btn"" onclick=""loadData()"">Refresh</button>
        </div>
        
        <div id=""content"">
            <div class=""loading"">Loading items...</div>
        </div>
    </div>
    
    <div class=""toast"" id=""toast""></div>
    
    <script>
        let allItems = [];
        let allBags = [];
        let looseItems = [];
        let currentBotName = '';

        async function loadData() {
            try {
                const [itemsResponse, statsResponse] = await Promise.all([
                    fetch('/api/items'),
                    fetch('/api/stats')
                ]);

                const itemsData = await itemsResponse.json();
                const statsData = await statsResponse.json();

                allItems = itemsData.items || [];
                allBags = itemsData.bags || [];
                looseItems = itemsData.looseItems || [];
                currentBotName = itemsData.botName || 'Bankbot';

                document.getElementById('botName').textContent = currentBotName + ' - Item Catalog';
                document.getElementById('totalItems').textContent = allItems.length;
                document.getElementById('inventoryFree').textContent = statsData.inventory.free + '/' + statsData.inventory.total;
                document.getElementById('bagsFree').textContent = statsData.bags.free + '/' + statsData.bags.total;

                renderItems();
            } catch (error) {
                document.getElementById('content').innerHTML = '<div class=""error"">Error loading data: ' + error.message + '</div>';
            }
        }
        
        function renderItems() {
            const searchTerm = document.getElementById('searchBox').value.toLowerCase();
            const sortBy = document.getElementById('sortSelect').value;

            let html = '<table class=""items-table""><thead><tr>';
            html += '<th>Item Name</th>';
            html += '<th>QL</th>';
            html += '<th>Stack</th>';
            html += '<th>Action</th>';
            html += '</tr></thead><tbody>';

            let hasResults = false;

            // Sort bags
            let sortedBags = sortItems([...allBags], sortBy);

            // Render bags with their contents
            sortedBags.forEach(bag => {
                // Get all items in this bag
                let allItemsInBag = allItems.filter(item =>
                    !item.isContainer &&
                    item.sourceBagName === bag.name &&
                    item.sourceBagInstance === bag.itemInstance
                );

                console.log('Bag: ' + bag.name + ' (Instance: ' + bag.itemInstance + ') has ' + allItemsInBag.length + ' items');

                // Filter by search term if provided
                let matchingItems = searchTerm === '' ? allItemsInBag :
                    allItemsInBag.filter(item => item.name.toLowerCase().includes(searchTerm));

                // Show bag if it matches search OR if any of its items match OR if no search term
                let bagMatches = searchTerm === '' || bag.name.toLowerCase().includes(searchTerm);

                if (bagMatches || matchingItems.length > 0) {
                    hasResults = true;

                    // Bag row
                    html += '<tr style=""background: rgba(156, 39, 176, 0.2);"">';
                    html += '<td><strong>' + escapeHtml(bag.name) + '</strong> <span class=""container-badge"">BAG</span></td>';
                    html += '<td>' + (bag.qualityLevel || '-') + '</td>';
                    html += '<td>-</td>';
                    html += '<td><button class=""get-btn"" onclick=""copyGetCommand(\'' + escapeHtml(bag.name).replace(/'/g, '\\\'') + '\', ' + bag.itemInstance + ')"">GET BAG</button></td>';
                    html += '</tr>';

                    // Show items - all items if no search or bag matches, otherwise only matching items
                    let displayItems = (searchTerm === '' || bagMatches) ? allItemsInBag : matchingItems;
                    displayItems = sortItems(displayItems, sortBy);

                    displayItems.forEach(item => {
                        html += '<tr style=""background: rgba(255, 255, 255, 0.03);"">';
                        html += '<td style=""padding-left: 40px;"">↳ ' + escapeHtml(item.name) + '</td>';
                        html += '<td>' + (item.qualityLevel || '-') + '</td>';
                        html += '<td>' + (item.stackCount || '-') + '</td>';
                        html += '<td><button class=""get-btn"" onclick=""copyGetCommand(\'' + escapeHtml(item.name).replace(/'/g, '\\\'') + '\', ' + item.itemInstance + ')"">GET</button></td>';
                        html += '</tr>';
                    });
                }
            });

            // Render loose items (not in bags)
            let filteredLoose = looseItems.filter(item =>
                item.name.toLowerCase().includes(searchTerm)
            );

            if (filteredLoose.length > 0) {
                hasResults = true;
                filteredLoose = sortItems(filteredLoose, sortBy);

                filteredLoose.forEach(item => {
                    html += '<tr>';
                    html += '<td>' + escapeHtml(item.name) + '</td>';
                    html += '<td>' + (item.qualityLevel || '-') + '</td>';
                    html += '<td>' + (item.stackCount || '-') + '</td>';
                    html += '<td><button class=""get-btn"" onclick=""copyGetCommand(\'' + escapeHtml(item.name).replace(/'/g, '\\\'') + '\', ' + item.itemInstance + ')"">GET</button></td>';
                    html += '</tr>';
                });
            }

            html += '</tbody></table>';

            if (!hasResults) {
                document.getElementById('content').innerHTML = '<div class=""loading"">No items found matching &quot;' + escapeHtml(searchTerm) + '&quot;</div>';
            } else {
                document.getElementById('content').innerHTML = html;
            }
        }
        
        function sortItems(items, sortBy) {
            const sorted = [...items];
            
            switch(sortBy) {
                case 'name-asc':
                    sorted.sort((a, b) => a.name.localeCompare(b.name));
                    break;
                case 'name-desc':
                    sorted.sort((a, b) => b.name.localeCompare(a.name));
                    break;
                case 'ql-asc':
                    sorted.sort((a, b) => (a.qualityLevel || 0) - (b.qualityLevel || 0));
                    break;
                case 'ql-desc':
                    sorted.sort((a, b) => (b.qualityLevel || 0) - (a.qualityLevel || 0));
                    break;
                case 'date-asc':
                    sorted.sort((a, b) => new Date(a.storedAt) - new Date(b.storedAt));
                    break;
                case 'date-desc':
                    sorted.sort((a, b) => new Date(b.storedAt) - new Date(a.storedAt));
                    break;
            }
            
            return sorted;
        }
        
        function copyGetCommand(itemName, itemInstance) {
            const command = '/tell ' + currentBotName + ' get ' + itemName + ' ' + itemInstance;

            // Try modern clipboard API first
            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(command).then(() => {
                    showToast('Copied: ' + command);
                }).catch(err => {
                    // Fallback to old method
                    fallbackCopy(command);
                });
            } else {
                // Use fallback for older browsers or HTTP
                fallbackCopy(command);
            }
        }

        function fallbackCopy(text) {
            const textArea = document.createElement('textarea');
            textArea.value = text;
            textArea.style.position = 'fixed';
            textArea.style.left = '-999999px';
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();

            try {
                const successful = document.execCommand('copy');
                if (successful) {
                    showToast('Copied: ' + text);
                } else {
                    showToast('Failed to copy. Command: ' + text);
                }
            } catch (err) {
                showToast('Failed to copy. Command: ' + text);
            }

            document.body.removeChild(textArea);
        }
        
        function showToast(message) {
            const toast = document.getElementById('toast');
            toast.textContent = message;
            toast.classList.add('show');
            
            setTimeout(() => {
                toast.classList.remove('show');
            }, 3000);
        }
        
        function escapeHtml(text) {
            const div = document.createElement('div');
            div.textContent = text;
            return div.innerHTML;
        }
        
        document.getElementById('searchBox').addEventListener('input', renderItems);
        document.getElementById('sortSelect').addEventListener('change', renderItems);
        
        loadData();
        setInterval(loadData, 30000); // Auto-refresh every 30 seconds
    </script>
</body>
</html>";
        }
    }
}

