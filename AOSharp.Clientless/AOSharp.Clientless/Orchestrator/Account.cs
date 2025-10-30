using AOSharp.Clientless.Common;
using AOSharp.Core;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace AOSharp.Clientless.Orchestrator
{
    public class Account<TAccount, TBotDomain, TPluginTunnel, TAccountProxy>
        where TAccount : Account<TAccount, TBotDomain, TPluginTunnel, TAccountProxy>
        where TBotDomain : BotDomain<TBotDomain, TAccount, TPluginTunnel, TAccountProxy>
        where TPluginTunnel : PluginTunnel<TAccount, TAccountProxy>
        where TAccountProxy : AccountProxy<TAccount>
    {
        [NonSerialized]
        internal string Username;
        [NonSerialized]
        internal string Password;
        [NonSerialized]
        public string Character;
        [NonSerialized]
        public Dimension Dimension;
        [NonSerialized]
        internal bool UseChat;
        [NonSerialized]
        internal List<string> Plugins;

        [NonSerialized]
        protected Logger _logger;

        [NonSerialized]
        protected TBotDomain _activeClientInstance;

        public Account(string username, string password, string character, Dimension dimension, bool useChat, List<string> plugins)
        {
            Username = username;
            Password = password;
            Character = character;
            Dimension = dimension;
            UseChat = useChat;
            Plugins = plugins;

            var loggerConfig = new LoggerConfiguration();
            OnConfiguringLogger(loggerConfig);
            _logger = loggerConfig.CreateLogger();
        }

        public virtual void Login()
        {
            Log.Information($"Logging in account: {Username}");

            string character = string.IsNullOrEmpty(Character) ? "<NotYetPicked>" : Character;
            _activeClientInstance = ClientDomain.CreateDomain<TBotDomain>(Username, Password, character, Dimension.RubiKa, _logger, UseChat);
            _activeClientInstance.Account = (TAccount)this;

            foreach(string plugin in Plugins)
                _activeClientInstance.LoadPlugin(plugin);

            _activeClientInstance.CreateBotTunnel();
            _activeClientInstance.Start();
        }

        public virtual void Update(double deltaTime)
        {
        }

        public virtual void Logout()
        {
            _activeClientInstance?.Unload();
            _activeClientInstance = null;
        }

        protected virtual void OnConfiguringLogger(LoggerConfiguration loggerConfig)
        {
            loggerConfig
                .WriteTo.Console().
                MinimumLevel.Information();
        }
    }
}
