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

## Prerequisites

1. **.NET Framework 4.8** - [Download here](https://dotnet.microsoft.com/download/dotnet-framework/net48)
2. **Visual Studio 2022** (or MSBuild tools)

That's it. All dependencies (AOSharp.Clientless, NuGet packages) are included in the repo and restore automatically on build.

## Quick Start

### 1. Clone and Build

```bash
git clone https://github.com/lvp12345/Bankbot.git
```

Open `Bankbot.sln` in Visual Studio and build (F6), or from command line:

```bash
msbuild Bankbot.sln /restore /p:Configuration=Debug
```

Everything builds to a single output folder: `bin\Debug\`

### 2. Configure the Launcher

After building, edit `bin\Debug\launcher-config.json` with your account details:

```json
{
  "Username": "your_anarchy_online_username",
  "Password": "your_anarchy_online_password",
  "CharacterName": "your_bankbot_character_name",
  "Dimension": 1
}
```

**Dimension values:** `0` = Rubi-Ka, `1` = Rubi-Ka 2019

### 3. Configure the Bot

Edit `bin\Debug\config.json` with your bot settings:

```json
{
  "CharSettings": {
    "YourBankbotCharacterName": {
      "WebInterfaceEnabled": true,
      "WebInterfacePort": 5000,
      "WebInterfaceHost": "http://localhost",
      "AutoSortEnabled": true,
      "ItemSortingRules": {}
    }
  }
}
```

### 4. Run the Bot

```bash
cd bin\Debug
BankbotLauncher.exe
```

Or run via command line arguments:
```bash
BankbotLauncher.exe <username> <password> <characterName> [dimension]
```

## Available Commands

Send these commands via private message to your bankbot:

- `help` - Show available commands
- `list` - Display all stored items (paginated)
- `get <item name> [instance]` - Retrieve a specific item
- `view <item name>` - View item details

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

### Web Interface Configuration

```json
{
  "WebInterfaceEnabled": true,
  "WebInterfacePort": 5000,
  "WebInterfaceHost": "http://localhost"
}
```

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

### Sorting Configuration

```json
{
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
```

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
- Items that don't match any pattern are placed in any bag with available space

## Configuration Reference

### Web Interface Settings
- `WebInterfaceEnabled` - Enable/disable the web interface (default: `true`)
- `WebInterfacePort` - Port for the web interface (default: `5000`)
- `WebInterfaceHost` - Host URL for the web interface (default: `http://localhost`)

### Auto-Sorting Settings
- `AutoSortEnabled` - Enable/disable automatic item sorting (default: `true`)
- `ItemSortingRules` - Dictionary of category names and item name patterns

## Troubleshooting

### Common Issues

1. **Build fails with missing references**
   - Make sure you're building the entire solution (`Bankbot.sln`), not individual projects
   - NuGet packages restore automatically on build - no manual steps needed

2. **"Connection failed"**
   - Verify your username, password, and character name in `launcher-config.json`
   - Check that the character exists on the specified dimension

3. **"Access denied" messages**
   - Check that your org is allowed if org lockout is configured

4. **Web interface not loading**
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

- Keep your `launcher-config.json` secure (contains account credentials)
- Consider using a dedicated account for the bankbot
- Configure org lockout to restrict access to your organization
