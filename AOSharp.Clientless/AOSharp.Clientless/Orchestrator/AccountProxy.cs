using System;
using System.Security.Permissions;

namespace AOSharp.Clientless.Orchestrator
{
    public class AccountProxy<TAccount> : MarshalByRefObject
    {
        [NonSerialized]
        protected TAccount _account;

        public AccountProxy(TAccount account)
        {
            _account = account;
        }

        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.Infrastructure)]
        public override object InitializeLifetimeService()
        {
            return null;
        }
    }
}
