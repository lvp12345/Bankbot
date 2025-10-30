using System;
using System.IO;
using AOSharp.Clientless;

namespace Bankbot.Templates
{
    // REMOVED: Help functionality no longer needed - only using list command
    public static class BankbotScriptTemplate
    {
        // REMOVED: Help functionality no longer needed
        /*
        public static string HelpWindow()
        {
            try
            {
                string template = GetTemplateContent("BankbotHelpTemplate");
                if (!string.IsNullOrEmpty(template))
                {
                    // Simple string replacement for botname
                    return template.Replace("{{ botname }}", Client.CharacterName);
                }
                else
                {
                    return CreateFallbackHelp();
                }
            }
            catch
            {
                // Silent error handling
                return CreateFallbackHelp();
            }
        }
        */

        // REMOVED: Help functionality no longer needed
        /*
        public static string CategoryHelpWindow(string templateName)
        {
            try
            {
                string template = GetTemplateContent(templateName);
                if (!string.IsNullOrEmpty(template))
                {
                    // Simple string replacement for botname
                    return template.Replace("{{ botname }}", Client.CharacterName);
                }
                else
                {
                    return CreateFallbackHelp();
                }
            }
            catch
            {
                // Silent error handling
                return CreateFallbackHelp();
            }
        }
        */

        private static string CreateFallbackHelp()
        {
            string botName = Client.CharacterName;
            return $@"<a href=""text://
<font color=#00D4FF>Storage Bot Help Menu</font>
<font color=#00D4FF>Bot: {botName}</font>

<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>

<font color=#00D4FF>=== STORAGE COMMANDS ===</font>
<a href='chatcmd:///tell {botName} trade'><font color=#00BDBD>trade</font></a><font color=#FFFFFF> - Give me items to store in catalog</font>
<a href='chatcmd:///tell {botName} list'><font color=#00BDBD>list</font></a><font color=#FFFFFF> - View stored items with GET buttons</font>
<a href='chatcmd:///tell {botName} get <item name>'><font color=#00BDBD>get <item name></font></a><font color=#FFFFFF> - Retrieve a specific item</font>
<a href='chatcmd:///tell {botName} queue'><font color=#00BDBD>queue</font></a><font color=#FFFFFF> - Check your position in the trade queue</font>

<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>

<font color=#00D4FF>=== HELP CATEGORIES ===</font>
<a href='chatcmd:///tell {botName} help storage'><font color=#00BDBD>help storage</font></a><font color=#FFFFFF> - Show storage catalog window</font>
<a href='chatcmd:///tell {botName} help carb'><font color=#00BDBD>help carb</font></a><font color=#FFFFFF> - Show carb armor help</font>
<a href='chatcmd:///tell {botName} help implants'><font color=#00BDBD>help implants</font></a><font color=#FFFFFF> - Implant processing help</font>
<a href='chatcmd:///tell {botName} help clusters'><font color=#00BDBD>help clusters</font></a><font color=#FFFFFF> - Cluster processing help</font>
<a href='chatcmd:///tell {botName} help bags'><font color=#00BDBD>help bags</font></a><font color=#FFFFFF> - Bag processing help</font>

<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>

<font color=#FFFF00>HOW TO USE:</font>
<font color=#FFFFFF>1. Come stand next to me</font>
<font color=#FFFFFF>2. Send: /tell {botName} trade</font>
<font color=#FFFFFF>3. Put items/bags in trade and give them to me</font>
<font color=#FFFFFF>4. Use 'list' to see stored items and 'get <item>' to retrieve them</font>

<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>

<font color=#888888>Storage Bot V2.0 - Multi-Function Storage System</font>
"">Storage Bot Help Menu</a>";
        }

        private static string GetTemplateContent(string templateFile)
        {
            try
            {
                string templatePath = $"{Bankbot.PluginDir}\\Templates\\{templateFile}.txt";
                if (File.Exists(templatePath))
                {
                    return File.ReadAllText(templatePath);
                }
                else
                {
                    // Silent template loading //$"[TEMPLATE] Template file not found: {templatePath}");
                    return null;
                }
            }
            catch (Exception)
            {
                // Silent template loading //$"[TEMPLATE] Error loading template {templateFile}: {ex.Message}");
                return null;
            }
        }
    }
}
