using AOSharp.Clientless.Common;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Clientless.Orchestrator
{
    public class OrchestratorBase<TAccount, TBotDomain, TPluginTunnel, TAccountProxy>
        where TAccount : Account<TAccount, TBotDomain, TPluginTunnel, TAccountProxy>
        where TBotDomain : BotDomain<TBotDomain, TAccount, TPluginTunnel, TAccountProxy>
        where TPluginTunnel : PluginTunnel<TAccount, TAccountProxy>
        where TAccountProxy : AccountProxy<TAccount>
    {
        public List<TAccount> Accounts;

        private static UpdateLoop _updateLoop;

        public OrchestratorBase(Logger logger)
        {
            Accounts = new List<TAccount>();
        }

        public virtual void Start(bool login=true)
        {
            _updateLoop = new UpdateLoop(Update);
            _updateLoop.Start();

            if (!login)
                return;

            foreach (var account in Accounts)
                account.Login();
        }

        public virtual void Shutdown()
        {
            foreach (var account in Accounts)
                account.Logout();

            _updateLoop.Stop();
        }

        protected virtual void Update(double deltaTime)
        {
            foreach (var account in Accounts)
                account.Update(deltaTime);
        }
    }
}
