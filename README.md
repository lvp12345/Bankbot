# Bankbot - Clientless Banking Bot for Anarchy Online

Bankbot is a clientless banking and storage bot for Anarchy Online, designed to help manage items and provide banking services to players.

## Features

- **Banking Services**: Store and retrieve items for authorized players
- **Trade Management**: Automated trading system with safety checks
- **Bag Management**: Automatic bag handling and organization
- **Auto-Sorting System**: Automatically categorizes and organizes items into designated bags
- **Item Tracking**: Comprehensive logging of all transactions
- **Web Interface**: Browser-based item catalog with search and filtering
- **Private Message Commands**: Full command interface via tells
- **Clientless Operation**: Runs without a game client

## Quick Start

### 1. Build the Project
```bash
dotnet build Bankbot.sln
```

### 2. Configure Your Bot

Copy the example config and edit it:
```bash
copy Config.example.json Config.json
```

Edit `Config.json` with your settings:
```json
{
  "CharSettings": {
    "YourBankbotCharacterName": {
      "BankingEnabled": true,
      "PrivateMessageEnabled": true,
      "AuthorizedUsers": [
        "YourMainCharacter",
        "TrustedFriend1"
      ]
    }
  }
}
```

### 3. Set Up the Launcher

Copy the launcher config:
```bash
copy BankbotLauncher\launcher-config.example.json BankbotLauncher\launcher-config.json
```

Edit `BankbotLauncher\launcher-config.json`:
```json
{
  "Username": "your_anarchy_online_username",
  "Password": "your_anarchy_online_password",
  "CharacterName": "your_bankbot_character_name",
  "Dimension": "RubiKa2019"
}
```

### 4. Copy Required Files

Copy the Bankbot.dll to the launcher directory:
```bash
copy bin\Debug\Bankbot.dll BankbotLauncher\bin\Debug\
```

### 5. Run the Bot
```bash
cd BankbotLauncher\bin\Debug
BankbotLauncher.exe
```

## Available Commands

Send these commands via private message to your bankbot:

- `help` - Show available commands
- `status` - Show bot status
- `list` - Display all stored items (paginated)
- `get <item name> <instance>` - Retrieve a specific item
- `trade` - Start a trade session
- `return` - Return your saved items
- `bags` - List available bags
- `storage` - Access storage services

## Web Interface

Bankbot includes a built-in web server that provides a browser-based interface to view all stored items.

### Accessing the Web Interface

1. **Start the bot** - The web server starts automatically when the bot loads
2. **Open your browser** and navigate to: `http://localhost:5000/`
3. **Browse items** - Search, sort, and copy GET commands with one click

### Features

- **Real-time Item Catalog**: View all items stored in the bot
- **Search Functionality**: Search for items by name (partial matching supported)
- **Sorting Options**: Sort by name, quality level, or date added
- **Hierarchical Display**: Bags and their contents shown in organized structure
- **One-Click GET Commands**: Click GET button to copy the command to clipboard
- **Auto-Refresh**: Page automatically refreshes every 30 seconds
- **Inventory Stats**: See free inventory and bag slots at a glance

### Configuration

The web interface can be configured in `Config.json`:

```json
{
  "CharSettings": {
    "YourBankbotCharacterName": {
      "WebInterfaceEnabled": true,
      "WebInterfacePort": 5000,
      "WebInterfaceHost": "http://localhost"
    }
  }
}
```

**Configuration Options:**
- `WebInterfaceEnabled` - Enable/disable the web interface (default: `true`)
- `WebInterfacePort` - Port number for the web server (default: `5000`)
- `WebInterfaceHost` - Host address (default: `"http://localhost"`)

**Note:** The web interface is publicly accessible to anyone who knows the URL. It's designed as a read-only catalog for browsing items - no authentication is required.

## Auto-Sorting System

Bankbot automatically organizes items into categorized bags based on configurable rules.

### How It Works

1. **On Startup**: All loose items in inventory are automatically sorted into appropriate bags
2. **After Trades**: When players trade items to the bot, they're automatically categorized and stored
3. **Bag Consolidation**: Fills existing bags before using new ones (prevents 20 half-full bags)

### Default Categories

The bot comes with default sorting rules for common items:
- **Infantry/Artillery/Support/Control/Exterminator Symbiants**
- **Implants**
- **Nano Crystals**
- **Weapons** (pistols, rifles, swords, axes, hammers)
- **Armor** (helmets, boots, gloves, pants, sleeves)

### Configuration

Configure sorting rules in `Config.json`:

```json
{
  "CharSettings": {
    "YourBankbotCharacterName": {
      "AutoSortEnabled": true,
      "ItemSortingRules": {
        "Infantry Symbiants": ["infantry"],
        "Artillery Symbiants": ["artillery"],
        "Implants": ["implant"],
        "Nano Crystals": ["nano crystal", "nano formula"],
        "Weapons": ["pistol", "rifle", "sword"],
        "Custom Category": ["pattern1", "pattern2"]
      }
    }
  }
}
```

**Configuration Options:**
- `AutoSortEnabled` - Enable/disable automatic sorting (default: `true`)
- `ItemSortingRules` - Dictionary of category names and matching patterns

**Pattern Matching:**
- Patterns are case-insensitive
- Matches any part of the item name
- Example: Pattern `"infantry"` matches "Infantry Symbiant", "Soldier's Infantry Gear", etc.

### Setting Up Bags

1. **Create bags** with names matching your categories (e.g., "Infantry Symbiants", "Implants")
2. **Trade bags to the bot** or place them in inventory
3. **Items will automatically sort** into matching bags based on their names

**Tips:**
- Bag names should match the category names in your config
- The bot will use existing bags with space before creating/using new ones
- Items that don't match any pattern remain in main inventory

## Configuration Options

### Character Settings
- `BankingEnabled` - Enable/disable banking services
- `StorageEnabled` - Enable/disable storage services
- `TradeEnabled` - Enable/disable trading
- `PrivateMessageEnabled` - Enable/disable PM commands
- `AutoBagEnabled` - Automatically manage bags
- `BagReturnEnabled` - Allow bag return functionality

### Web Interface Settings
- `WebInterfaceEnabled` - Enable/disable web interface (default: `true`)
- `WebInterfacePort` - Port number for web server (default: `5000`)
- `WebInterfaceHost` - Host address (default: `"http://localhost"`)

### Auto-Sorting Settings
- `AutoSortEnabled` - Enable/disable automatic item sorting (default: `true`)
- `ItemSortingRules` - Dictionary of category names and item name patterns

### Security Settings
- `AuthorizedUsers` - List of players who can use the bot
- `MaxItemsPerTrade` - Maximum items per trade session
- `TradeTimeoutMinutes` - Trade timeout in minutes
- `AutoAcceptTrades` - Automatically accept trades (use with caution)

## Project Structure

```
Bankbot/
├── Bankbot.cs              # Main plugin entry point
├── Config.cs               # Configuration management
├── Config.example.json     # Example configuration
├── Core/                   # Core functionality
│   ├── ItemTracker.cs      # Item tracking and management
│   ├── ItemSorter.cs       # Automatic item sorting system
│   ├── TradeLogger.cs      # Transaction logging
│   ├── TradingSystem.cs    # Trade management
│   └── WebServer.cs        # Web interface server
├── Modules/                # Bot modules
│   └── PrivateMessageModule.cs  # PM command handling
└── BankbotLauncher/        # Clientless launcher
    ├── Program.cs          # Launcher application
    ├── launcher-config.example.json
    └── README.md           # Launcher documentation
```

## Troubleshooting

### Common Issues

1. **"Bankbot plugin not found"**
   - Ensure Bankbot.dll is in the launcher's output directory
   - Check that the build was successful

2. **"Connection failed"**
   - Verify your username, password, and character name
   - Check that the character exists on the specified dimension

3. **"Access denied" messages**
   - Make sure your character is in the AuthorizedUsers list
   - Check that PrivateMessageEnabled is true

4. **Trade issues**
   - Ensure TradeEnabled is true in config
   - Check that you're within range (for position-dependent features)

5. **Web interface not loading**
   - Check that `WebInterfaceEnabled` is `true` in config
   - Verify the port isn't already in use (change `WebInterfacePort` if needed)
   - Try accessing `http://localhost:5000/` (or your configured port)
   - Check firewall settings if accessing from another machine

6. **Items not auto-sorting**
   - Ensure `AutoSortEnabled` is `true` in config
   - Verify bag names match your category names in `ItemSortingRules`
   - Check that bags are open (bot opens all bags on startup)
   - Review bot logs for sorting activity

### Debug Mode

To enable debug logging, the bot will output detailed information to the console when running via the launcher.

## Security Notes

- Keep your launcher-config.json secure (contains account credentials)
- Only add trusted players to AuthorizedUsers
- Consider using a dedicated account for the bankbot
- Regularly backup your configuration and logs

## Support

For issues and questions, check the troubleshooting section above or review the code for specific functionality.
