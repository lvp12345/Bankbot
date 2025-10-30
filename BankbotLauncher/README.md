# Bankbot Launcher

This launcher allows you to run Bankbot in clientless mode.

## Setup

1. **Copy the launcher config example:**
   ```
   copy launcher-config.example.json launcher-config.json
   ```

2. **Edit launcher-config.json with your credentials:**
   ```json
   {
     "Username": "your_anarchy_online_username",
     "Password": "your_anarchy_online_password", 
     "CharacterName": "your_bankbot_character_name",
     "Dimension": "RubiKa2019"
   }
   ```

3. **Build the launcher:**
   ```
   dotnet build BankbotLauncher.csproj
   ```

4. **Copy required files to the output directory:**
   - Copy `Bankbot.dll` from the main Bankbot project to the launcher's output directory
   - Copy all AOSharp.Clientless DLLs to the launcher's output directory

## Usage

### Method 1: Using config file
```
BankbotLauncher.exe
```

### Method 2: Command line arguments
```
BankbotLauncher.exe <username> <password> <characterName> [dimension]
```

Example:
```
BankbotLauncher.exe myusername mypassword MyBankbot RubiKa2019
```

## Available Dimensions
- RubiKa2019 (default)
- RubiKa2020
- TestLive

## Controls
- Press any key (except 'q') to show status
- Press 'q' to quit

## Troubleshooting

1. **"Bankbot plugin not found"** - Make sure Bankbot.dll is in the same directory as BankbotLauncher.exe
2. **"Error loading config"** - Check that launcher-config.json exists and has valid JSON format
3. **Connection issues** - Verify your username, password, and character name are correct
