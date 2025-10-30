using System;
using System.IO;
using AOSharp.Clientless;
using AOSharp.Clientless.Common;
using AOSharp.Common.GameData;
using Serilog;

namespace BankbotLauncher
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== Bankbot Clientless Launcher ===");
            Console.WriteLine("Starting Bankbot in clientless mode...");

            // Setup simple console logging (avoiding Serilog dependency issues)
            var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Debug()
                .CreateLogger();

            try
            {
                // Get credentials from command line or config
                string username, password, characterName;
                Dimension dimension = Dimension.RubiKa2019;

                if (args.Length >= 3)
                {
                    username = args[0];
                    password = args[1];
                    characterName = args[2];
                    
                    if (args.Length >= 4 && Enum.TryParse<Dimension>(args[3], out var dim))
                    {
                        dimension = dim;
                    }
                }
                else
                {
                    // Try to load from config file
                    var config = LoadConfig();
                    if (config != null)
                    {
                        username = config.Username;
                        password = config.Password;
                        characterName = config.CharacterName;
                        dimension = config.Dimension;
                    }
                    else
                    {
                        Console.WriteLine("Usage: BankbotLauncher.exe <username> <password> <characterName> [dimension]");
                        Console.WriteLine("Or create a launcher-config.json file with credentials");
                        Console.WriteLine("Press any key to exit...");
                        Console.ReadKey();
                        return;
                    }
                }

                Console.WriteLine($"Launching Bankbot for character: {characterName} on {dimension}");

                // Create clientless domain
                var clientDomain = Client.CreateInstance(username, password, characterName, dimension, logger);

                // Load the Bankbot plugin
                string pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bankbot.dll");
                if (!File.Exists(pluginPath))
                {
                    Console.WriteLine($"ERROR: Bankbot plugin not found at: {pluginPath}");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return;
                }

                Console.WriteLine($"Loading Bankbot plugin from: {pluginPath}");
                clientDomain.LoadPlugin(pluginPath);

                // Start the client
                Console.WriteLine("Starting clientless client...");
                clientDomain.Start();

                Console.WriteLine("Bankbot is now running in clientless mode!");
                Console.WriteLine("Press 'q' to quit or any other key for status...");

                // Main loop
                while (true)
                {
                    var key = Console.ReadKey(true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        break;
                    }
                    else
                    {
                        Console.WriteLine($"Status: Running - Character: {characterName}");
                        Console.WriteLine("Press 'q' to quit...");
                    }
                }

                Console.WriteLine("Shutting down Bankbot...");
                clientDomain.Unload();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FATAL ERROR in Bankbot launcher: {ex.Message}");
                Console.WriteLine($"Details: {ex}");
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            finally
            {
                logger?.Dispose();
            }
        }

        private static LauncherConfig LoadConfig()
        {
            try
            {
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "launcher-config.json");

                // Create default config if it doesn't exist
                if (!File.Exists(configPath))
                {
                    CreateDefaultLauncherConfig(configPath);
                }

                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath);
                    return Newtonsoft.Json.JsonConvert.DeserializeObject<LauncherConfig>(json);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading config: {ex.Message}");
            }
            return null;
        }

        private static void CreateDefaultLauncherConfig(string configPath)
        {
            try
            {
                var defaultConfig = new LauncherConfig
                {
                    Username = "your_username",
                    Password = "your_password",
                    CharacterName = "your_bankbot_character_name",
                    Dimension = Dimension.RubiKa2019
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(defaultConfig, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(configPath, json);

                Console.WriteLine($"Created default launcher config at: {configPath}");
                Console.WriteLine("Please edit launcher-config.json with your credentials before running again.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating default config: {ex.Message}");
            }
        }
    }

    public class LauncherConfig
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string CharacterName { get; set; }
        public Dimension Dimension { get; set; } = Dimension.RubiKa2019;
    }
}
