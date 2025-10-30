using AOSharp.Clientless.Common;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Clientless.Orchestrator
{
    public class BotDomain<TDomain, TAccount, TPluginTunnel, TAccountProxy> : ClientDomain
        where TDomain : BotDomain<TDomain, TAccount, TPluginTunnel, TAccountProxy>
        where TAccount : Account<TAccount, TDomain, TPluginTunnel, TAccountProxy>
        where TPluginTunnel : PluginTunnel<TAccount, TAccountProxy>
        where TAccountProxy : AccountProxy<TAccount>
    {
        internal TAccount Account;
        public TPluginTunnel PluginTunnel;
        public TAccountProxy AccountProxy;

        protected BotDomain(AppDomain appDomain, Logger logger) : base(appDomain, logger)
        {
            CreatePluginProxy();
            LoadCore();
        }

        internal void CreateBotTunnel()
        {
            AccountProxy = (TAccountProxy)Activator.CreateInstance(typeof(TAccountProxy), new object[] { Account });

            Type type = typeof(TPluginTunnel);
            PluginTunnel = (TPluginTunnel)_appDomain.CreateInstanceAndUnwrap(type.Assembly.FullName, type.FullName);
            PluginTunnel.Init(AccountProxy);
        }
    }
}
