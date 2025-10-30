# Organization Lockout System - Updated Guide

## Overview

The organization lockout system has been completely rewritten to use proper AOSharp functionality to detect player organization IDs. This allows you to restrict bot access to specific organizations with real org ID detection.

## Key Features

- **Real Organization Detection**: Intercepts `SimpleCharFullUpdateMessage` to get actual organization IDs
- **Multiple Organization Support**: Configure multiple org IDs that can access the bot
- **Caching System**: Caches org info for 5 minutes to reduce server requests
- **Network Message Interception**: Captures org data from network messages automatically
- **Async Processing**: Handles org info requests asynchronously to avoid blocking
- **Backward Compatibility**: Maintains the same config.json structure

## IMPORTANT: Fixed Issue

The previous issue where the bot couldn't properly detect organization IDs has been resolved. The system now:
1. Intercepts `SimpleCharFullUpdateMessage` network messages that contain real org data
2. Automatically caches org information when players come into range
3. Provides accurate org ID detection instead of defaulting to 0

## Configuration

Edit `bin/Debug/config.json` to configure allowed organizations:

### Allow All Organizations (Default)
```json
{
  "AllowedOrganizationIds": [0],
  "BankbotEnabled": true,
  "LogAllTransactions": true,
  "LogFormat": "{Time} - {PlayerName} - {ItemName}"
}
```

### Allow Specific Organizations
```json
{
  "AllowedOrganizationIds": [12345, 67890, 11111],
  "BankbotEnabled": true,
  "LogAllTransactions": true,
  "LogFormat": "{Time} - {PlayerName} - {ItemName}"
}
```

### Mixed Configuration (Specific Orgs + Allow All)
```json
{
  "AllowedOrganizationIds": [12345, 67890, 0],
  "BankbotEnabled": true,
  "LogAllTransactions": true,
  "LogFormat": "{Time} - {PlayerName} - {ItemName}"
}
```

## How It Works

1. **Automatic Detection**: When players come into range, the bot automatically captures their org info from network messages
2. **Network Interception**: The system intercepts `SimpleCharFullUpdateMessage` packets that contain real org data
3. **Caching**: Org info is cached for 5 minutes to avoid repeated requests
4. **Access Control**: Bot checks if the player's org ID is in the allowed list
5. **Fallback**: If org info can't be retrieved, access is denied (unless org ID 0 is in the allowed list)
6. **Manual Refresh**: The `orgcheck` command can trigger a manual refresh of nearby players' org data

## Testing Your Configuration

1. **Set your org ID**: Edit `config.json` and replace `[0]` with `[186370]` (your org ID)
2. **Test access**: Try trading with the bot - it should work
3. **Test restriction**: Have someone from a different org try trading - it should be denied
4. **Use orgcheck**: Use `/tell BotName orgcheck` to see your detected org info

## Commands

### Check Organization Status
Players can use the `orgcheck` command to see their organization information:

```
/tell BotName orgcheck
```

This will show:
- Player name and ID
- Organization name and ID
- Whether access is allowed
- Current configuration
- Cache age (if applicable)

## Important Notes

### Organization ID 0
- Using `0` in the `AllowedOrganizationIds` list means "allow all organizations"
- This is the default setting for backward compatibility
- Remove `0` from the list if you want to restrict to specific organizations only

### First-Time Access
- The first time a player contacts the bot, there may be a slight delay while org info is retrieved
- Subsequent interactions will be faster due to caching
- If org info retrieval fails, access will be denied for safety

### Configuration Reload
- The config file is automatically reloaded every minute
- Changes take effect without restarting the bot
- Invalid configurations will fall back to default settings

## Troubleshooting

### Player Can't Access Bot
1. Check if their org ID is in the `AllowedOrganizationIds` list
2. Have them use `orgcheck` command to see their org info
3. Check bot logs for org lockout messages
4. Verify `BankbotEnabled` is set to `true`

### Org Info Not Detected
1. Ensure the player is near the bot (within range)
2. Check if the player is actually in an organization
3. Look for timeout messages in bot logs
4. Try clearing the org cache (restart bot if needed)

### Performance Issues
1. Org info is cached for 5 minutes to reduce server load
2. Multiple simultaneous requests are handled efficiently
3. Timeouts prevent hanging on failed requests

## Technical Details

### Caching System
- Org info is cached per player ID
- Cache expires after 5 minutes
- Failed requests are not cached
- Cache can be cleared manually if needed

### Async Processing
- Org info requests are handled asynchronously
- Backward compatibility maintained with synchronous wrapper
- Timeouts prevent indefinite waiting (5 second timeout)

### Error Handling
- Network failures default to deny access
- Invalid responses are logged and handled gracefully
- Configuration errors fall back to safe defaults

## Migration from Old System

The new system is fully backward compatible:
- Existing config.json files continue to work
- Default behavior (allow all orgs) is unchanged
- No code changes needed in other modules

To take advantage of the new features:
1. Update your `AllowedOrganizationIds` with specific org IDs
2. Remove `0` from the list if you want to restrict access
3. Test with the `orgcheck` command
