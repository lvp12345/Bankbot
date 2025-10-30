# CRAFTBOT DEVELOPMENT RULES

## CRITICAL RULES - NEVER VIOLATE THESE

### 1. DEBUG MESSAGES
- **DO NOT ADD ANY DEBUG MESSAGES INGAME**
- **ALWAYS USE THE DEBUG TXT FILE**
- Use debug logging for all debug output
- NEVER use critical logging or any ingame chat messages
- ALL debug/chat/critical messages go ONLY to txt log files with ZERO ingame chat spam
- The only ingame messages allowed are bot-to-player trade private messages

### 2. BUILD VERIFICATION
- **ALWAYS BUILD THE PROJECT AFTER CHANGES**
- Run `dotnet build` after every code modification
- Fix ALL compiler warnings before considering code complete
- Warnings may indicate potential issues that could break functionality
- Verify changes are working correctly before claiming they are perfect

### 3. CLEAR COMMUNICATION AND REPORTING
- **ALWAYS PROVIDE SPECIFIC AND ACCURATE INFORMATION**
- **When reporting changes:**
  - Specify exactly what was changed (e.g., "replaced the ProcessItem() method" not "reduced entire file")
  - Give accurate line counts and scope (e.g., "main processing method: 4 lines instead of 33" not "entire file: 4 lines")
  - Distinguish between what was modified vs. what remains unchanged
  - Explain the scope of changes clearly (e.g., "eliminated duplicate workflow logic, but all processing methods remain")
- **When describing file modifications:**
  - Be clear about which specific methods/sections were changed
  - Clarify what functionality was moved, deleted, or unified
  - Avoid misleading statements about overall file size or complexity
- **When reporting accomplishments:**
  - Focus on the actual improvements made (e.g., "unified common workflow steps")
  - Be honest about what was and wasn't changed
  - Provide context for why changes were beneficial

### Code Organization
- User prefers centralized core logic over duplicated implementations
- Remove redundant old code rather than adding new layers
- Simple solutions that eliminate rather than add to codebase complexity
- Fix all compiler warnings before considering code complete

## LOGGING AND ADMIN
- Use file-based admin management with text files in bin/Debug folder
- Admin lists and funny response lists should auto-refresh periodically
- Implement trade logging system tracking every trade with timestamps
- Admin and funny response functionality should not show in debug logs

## MEMORY
Remember these rules and refer to this file when making any changes to the project.
