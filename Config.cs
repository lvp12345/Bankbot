using System;
using System.Collections.Generic;
using System.IO;
using AOSharp.Clientless;
using Newtonsoft.Json;

namespace Bankbot
{
    public class Config
    {
        public Dictionary<string, CharacterSettings> CharSettings { get; set; }

        protected string _path;

        public static Config Load(string path)
        {
            Config config;

            try
            {
                config = JsonConvert.DeserializeObject<Config>(File.ReadAllText(path));
                config._path = path;
            }
            catch
            {
                // Silent config loading //$"No config file found.");
                // Silent config loading //$"Using default settings");

                config = new Config
                {
                    CharSettings = new Dictionary<string, CharacterSettings>()
                    {
                        { Client.CharacterName, new CharacterSettings() }
                    }
                };

                config._path = path;
                config.Save();
            }

            return config;
        }

        public void Save()
        {
            // Save to current directory (bin\Debug)
            File.WriteAllText(_path, JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented));
        }
    }

    public class CharacterSettings
    {
        // Bankbot specific settings - Banking enabled by default
        public bool BankingEnabled { get; set; } = true;
        public bool StorageEnabled { get; set; } = true;
        public bool TradeEnabled { get; set; } = true;
        public bool PrivateMessageEnabled { get; set; } = true;
        public bool AutoBagEnabled { get; set; } = false;
        public bool BagReturnEnabled { get; set; } = true;

        // Banking Module Settings
        public List<string> AuthorizedUsers { get; set; } = new List<string>();
        public Dictionary<string, string> PrivateMessageCommands { get; set; } = new Dictionary<string, string>
        {
            { "status", "Show bankbot status" },
            { "help", "Show available commands" },
            { "trade", "Start a trade session" },
            { "return", "Return your saved items" },
            { "bags", "List available bags" },
            { "storage", "Access storage services" }
        };

        // Storage settings
        public int MaxItemsPerTrade { get; set; } = 20;
        public int TradeTimeoutMinutes { get; set; } = 5;
        public bool LogTransactions { get; set; } = true;
        public bool AutoAcceptTrades { get; set; } = false;

        // Web Interface Settings
        public bool WebInterfaceEnabled { get; set; } = true;
        public int WebInterfacePort { get; set; } = 5000;
        public string WebInterfaceHost { get; set; } = "http://localhost";

        // Item Sorting Settings
        public bool AutoSortEnabled { get; set; } = true;
        public Dictionary<string, List<string>> ItemSortingRules { get; set; } = new Dictionary<string, List<string>>();
    }
}
