using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AOSharp.Clientless.Orchestrator
{
    //Host->Plugin Tunnel
    public class PluginTunnel<TAccount, TAccountProxy> : MarshalByRefObject where TAccountProxy : AccountProxy<TAccount>
    {
        public static TAccountProxy AccountProxy { get; private set; }

        public void Init(TAccountProxy accountProxy)
        {
            AccountProxy = accountProxy;
        }
    }
}
