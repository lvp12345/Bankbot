using System;
using System.IO;
using AOSharp.Clientless;

namespace Bankbot.Templates
{
    public static class BankbotScriptTemplate
    {
        public static string HelpWindow()
        {
            try
            {
                string template = GetTemplateContent("BankbotHelpTemplate");
                if (!string.IsNullOrEmpty(template))
                {
                    return template.Replace("{{ botname }}", Client.CharacterName);
                }
                else
                {
                    return CreateFallbackHelp();
                }
            }
            catch
            {
                return CreateFallbackHelp();
            }
        }

        public static string CategoryHelpWindow(string templateName)
        {
            try
            {
                string template = GetTemplateContent(templateName);
                if (!string.IsNullOrEmpty(template))
                {
                    return template.Replace("{{ botname }}", Client.CharacterName);
                }
                else
                {
                    return CreateFallbackHelp();
                }
            }
            catch
            {
                return CreateFallbackHelp();
            }
        }

        private static string CreateFallbackHelp()
        {
            string botName = Client.CharacterName;
            return $@"<a href=""text://
<font color=#00D4FF>Bankbot Help</font>
<font color=#00D4FF>Bot: {botName}</font>

<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>

<font color=#00D4FF>=== COMMANDS ===</font>
<a href='chatcmd:///tell {botName} help'><font color=#00BDBD>help</font></a><font color=#FFFFFF> - Show this help menu</font>
<a href='chatcmd:///tell {botName} list'><font color=#00BDBD>list</font></a><font color=#FFFFFF> - View stored items with GET buttons</font>
<a href='chatcmd:///tell {botName} get '><font color=#00BDBD>get &lt;item name&gt;</font></a><font color=#FFFFFF> - Retrieve a specific item</font>
<a href='chatcmd:///tell {botName} view '><font color=#00BDBD>view &lt;item name&gt;</font></a><font color=#FFFFFF> - View item details</font>
<a href='chatcmd:///tell {botName} name list'><font color=#00BDBD>name list</font></a><font color=#FFFFFF> - Show bags with custom names</font>
<a href='chatcmd:///tell {botName} orgcheck'><font color=#00BDBD>orgcheck</font></a><font color=#FFFFFF> - Check org authorization</font>

<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>

<font color=#FFFF00>HOW TO USE:</font>
<font color=#FFFFFF>1. Come stand next to me</font>
<font color=#FFFFFF>2. Open a trade and give me items to store</font>
<font color=#FFFFFF>3. Use 'list' to browse stored items</font>
<font color=#FFFFFF>4. Use 'get &lt;item&gt;' to retrieve them</font>

<img src=tdb://id:GFX_GUI_FRIENDLIST_SPLITTER>

<font color=#888888>Bankbot - Item Storage System</font>
"">Bankbot Help</a>";
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
