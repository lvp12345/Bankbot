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
        // Web Interface Settings
        public bool WebInterfaceEnabled { get; set; } = true;
        public int WebInterfacePort { get; set; } = 5000;
        public string WebInterfaceHost { get; set; } = "http://localhost";

        // Item Sorting Settings
        public bool AutoSortEnabled { get; set; } = true;
        public Dictionary<string, List<string>> ItemSortingRules { get; set; } = new Dictionary<string, List<string>>();
    }
}
