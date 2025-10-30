# Bankbot Web Interface Guide

## Overview

The Bankbot now includes a built-in web interface that allows anyone to view all stored items in real-time through a web browser. It's a simple item catalog with search, sorting, and quick GET command copying functionality.

## Features

- **Real-time Item Catalog**: View all items stored in the bankbot
- **Hierarchical Display**: Bags and their contents shown in organized structure (like in-game list)
- **Search Functionality**: Search items by name (partial matching supported)
- **Sorting Options**: Sort by name, quality level, or date added
- **Inventory Statistics**: See available inventory and bag space
- **GET Command Copy**: Click a button to copy the GET command to your clipboard
- **Auto-refresh**: Automatically updates every 30 seconds
- **Responsive Design**: Works on desktop and mobile devices

## Configuration

### Enable/Disable Web Interface

Edit your `config.json` file:

```json
{
  "CharSettings": {
    "YourBotName": {
      "WebInterfaceEnabled": true,
      "WebInterfacePort": 5000,
      "WebInterfaceHost": "http://localhost"
    }
  }
}
```

### Configuration Options

- **WebInterfaceEnabled**: Set to `true` to enable the web interface, `false` to disable (default: `true`)
- **WebInterfacePort**: The port number the web server will listen on (default: `5000`)
- **WebInterfaceHost**: The host address (default: `"http://localhost"`)

**Note:** If you get a "port already in use" error, change the port number to something else like 5001, 8080, or 9000.

## Accessing the Web Interface

### Local Access

Once the bot is running, open your web browser and navigate to:

```
http://localhost:5000/
```

Replace `5000` with your configured port if you changed it.

### Remote Access

To access the web interface from other computers on your network:

1. Find your computer's local IP address (e.g., 192.168.1.100)
2. Navigate to: `http://YOUR_IP:5000/`

**Note**: You may need to configure your firewall to allow incoming connections on the configured port.

### Public Access (Advanced)

To make the web interface accessible from the internet:

1. Configure port forwarding on your router to forward the web interface port to your computer
2. Use your public IP address or set up a dynamic DNS service
3. **Security Warning**: This will make your bankbot inventory publicly visible to anyone with the URL

## Using the Web Interface

### Main Features

1. **Search Box**: Type to filter items by name (partial matching - e.g., "symb" finds "Infantry Symbiant")
2. **Sort Dropdown**: Choose how to sort the item list:
   - Name (A-Z or Z-A)
   - Quality Level (Low-High or High-Low)
   - Date Added (Oldest or Newest)
3. **Refresh Button**: Manually refresh the item list
4. **GET Button**: Click to copy the GET command for that item to your clipboard

### Hierarchical Display

The web interface displays items in an organized structure similar to the in-game list:

- **Bags** are shown with a purple background and "BAG" badge
- **Items inside bags** are indented with a "↳" arrow showing they belong to that bag
- **Loose items** (not in bags) are shown at the bottom
- **Search filtering** shows bags when their contents match your search term

### Statistics Display

The top of the page shows:
- **Total Items**: Number of items stored in the bot
- **Free Inventory Slots**: Available main inventory space
- **Free Bag Slots**: Available space in bags

### Item Information

Each item in the table shows:
- **Item Name**: The name of the item (bags show "BAG" badge, items in bags are indented)
- **QL**: Quality Level of the item
- **Stack**: Stack count (for stackable items)
- **Action**: GET button to copy the command

### Using the GET Button

1. Click the **GET** button next to any item
2. The command will be copied to your clipboard
3. A notification will appear confirming the copy
4. Go in-game and paste the command (Ctrl+V) in the chat
5. Send the message to retrieve the item from the bot

The copied command format is:
```
/tell BotName get ItemName ItemInstance
```

## API Endpoints

The web interface also provides JSON API endpoints for developers:

### GET /api/items

Returns all items in JSON format:

```json
{
  "items": [
    {
      "id": 12345,
      "name": "Example Item",
      "quality": 300,
      "qualityLevel": 300,
      "stackCount": 1,
      "itemInstance": 67890,
      "sourceBagName": "Bag Name",
      "isContainer": false,
      "storedBy": "PlayerName",
      "storedAt": "2025-10-30 12:34:56"
    }
  ],
  "botName": "BotName"
}
```

### GET /api/stats

Returns inventory statistics in JSON format:

```json
{
  "botName": "BotName",
  "inventory": {
    "used": 25,
    "total": 29,
    "free": 4,
    "almostFull": false
  },
  "bags": {
    "used": 150,
    "total": 200,
    "free": 50
  }
}
```

## Troubleshooting

### Web Interface Won't Start

1. **Check if port is already in use**: Another application might be using the configured port
   - Try changing the port number in config.json
   - Common alternative ports: 8081, 8082, 9000

2. **Check firewall settings**: Windows Firewall might be blocking the port
   - Add an exception for the port in Windows Firewall
   - Or run the bot as Administrator (not recommended for security)

3. **Check the logs**: Look in `bin/Debug/bankbot.log` for error messages
   - Search for "[WEB SERVER]" entries

### Can't Access from Other Computers

1. **Firewall blocking**: Configure Windows Firewall to allow incoming connections
2. **Wrong IP address**: Make sure you're using the correct local IP address
3. **Network configuration**: Some networks block inter-device communication

### Items Not Showing Up

1. **Wait for initialization**: The bot needs a few seconds to scan inventory after startup
2. **Click Refresh**: Manually refresh the page
3. **Check bot logs**: Ensure the bot has successfully opened all bags

### GET Command Not Copying

1. **Browser permissions**: Some browsers require HTTPS for clipboard access
2. **Try a different browser**: Chrome, Firefox, and Edge all support clipboard API
3. **Manual copy**: You can manually select and copy the command from the notification

## Security Considerations

### Public Access Risks

- **Inventory visibility**: Anyone with the URL can see all items in your bot
- **No authentication**: The web interface is intentionally public with no login required
- **Information disclosure**: Players can see what items you have and plan accordingly
- **Read-only**: The web interface cannot modify items or execute commands - it's purely informational

### Recommendations

1. **Local network only**: Only access from your local network if possible
2. **VPN access**: Use a VPN if you need remote access
3. **Firewall rules**: Restrict access to specific IP addresses
4. **Monitor access**: Check web server logs for suspicious activity
5. **Disable when not needed**: Set `WebInterfaceEnabled: false` when you don't need it

**Note:** The web interface is designed to be a public item catalog - like a shop window for your bankbot. It's meant to help players browse what's available without needing to be in-game.

## Performance

- **Minimal overhead**: The web server runs in a background thread
- **Efficient caching**: Uses the same item cache as the in-game list command
- **Auto-refresh**: Updates every 30 seconds by default (can be changed in the HTML)
- **Concurrent access**: Multiple users can access simultaneously without issues

## Customization

The web interface HTML is embedded in `Core/WebServer.cs`. To customize:

1. Edit the `GetIndexPage()` method in `Core/WebServer.cs`
2. Modify the HTML, CSS, or JavaScript as needed
3. Rebuild the project
4. Restart the bot

### Common Customizations

- **Change colors**: Edit the CSS gradient and color values
- **Adjust auto-refresh interval**: Change `setInterval(loadData, 30000)` (value in milliseconds)
- **Add more columns**: Modify the table HTML and data rendering
- **Change layout**: Adjust the CSS grid and flexbox properties

## Support

If you encounter issues with the web interface:

1. Check the bot logs in `bin/Debug/bankbot.log`
2. Look for `[WEB SERVER]` log entries
3. Verify your configuration in `config.json`
4. Test with `http://localhost:PORT/` first before trying remote access
5. Check that the bot is running and fully initialized

## Recent Updates

### Version 2.0 Features

- **Hierarchical bag display**: Bags and their contents shown in organized structure
- **Improved search**: Partial name matching (e.g., "symb" finds all symbiants)
- **Fixed GET buttons**: Properly handles item names with special characters
- **Auto-sorting integration**: Web interface reflects auto-sorted items in real-time
- **Default port changed**: Now uses port 5000 instead of 8080

## Future Enhancements

Potential future features:
- Item filtering by type or quality
- Export to CSV/Excel
- Item statistics and charts
- Trade history viewer
- Mobile app integration

