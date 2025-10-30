using Serilog.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Serilog;
using AOSharp.Clientless.Net;
using AOSharp.Clientless.Common;
using AOSharp.Clientless.Chat;
using System.Security.Permissions;
using System.Globalization;

namespace AOSharp.Clientless
{
    public class ClientDomain
    {
        protected Logger _logger;
        protected AppDomain _appDomain;
        private PluginProxy _pluginProxy;

        protected ClientDomain(AppDomain appDomain, Logger logger)
        {
            _appDomain = appDomain;
            _logger = logger;
        }

        internal static ClientDomain CreateDomain(string username, string password, string characterName, Dimension dimension, Logger logger, bool useChat = true)
        {
            return CreateDomain<ClientDomain>(username, password, characterName, dimension, logger, useChat);
        }

        internal static T CreateDomain<T>(string username, string password, string characterName, Dimension dimension, Logger logger, bool useChat = true) where T : ClientDomain
        {
            AppDomainSetup setup = new AppDomainSetup()
            {
                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory
            };

            AppDomain appDomain = AppDomain.CreateDomain("plugins", null, setup);
            appDomain.SetData("username", username);
            appDomain.SetData("password", password);
            appDomain.SetData("character", characterName);
            appDomain.SetData("dimension", dimension);
            appDomain.SetData("useChat", useChat);

            T clientDomain = (T)Activator.CreateInstance(typeof(T), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { appDomain, logger }, CultureInfo.InvariantCulture);
            clientDomain.CreatePluginProxy();
            clientDomain.LoadCore();

            return clientDomain;
        }

        protected void CreatePluginProxy()
        {
            Type type = typeof(PluginProxy);
            _pluginProxy = (PluginProxy)_appDomain.CreateInstanceAndUnwrap(type.Assembly.FullName, type.FullName);
            _pluginProxy.Initialize(new HostProxy(_logger));
        }

        public void Start()
        {
            _pluginProxy.Start();
        }

        public void Unload()
        {
            AppDomain.Unload(_appDomain);
        }

        protected void LoadCore()
        {
            _pluginProxy.LoadCoreAssembly(_appDomain.BaseDirectory + "\\AOSharp.Clientless.DLL");
        }

        public void LoadPlugin(string pluginPath)
        {
            try
            {
                _pluginProxy.LoadPlugin(pluginPath);
            }
            catch (Exception e)
            {
                _logger.Error($"Error when loading plugin {pluginPath}:\n{e}");
            }
        }
    }

    internal class HostProxy : MarshalByRefObject
    {
        private Logger _logger;

        public HostProxy(Logger logger)
        {
            _logger = logger;
        }

        public void Debug(string message) => _logger.Debug(message);

        public void Warning(string message) => _logger.Warning(message);

        public void Error(string message) => _logger.Error(message);

        public void Information(string message) => _logger.Information(message);

        [SecurityPermission(SecurityAction.Demand, Flags = SecurityPermissionFlag.Infrastructure)]
        public override object InitializeLifetimeService()
        {
            return null;
        }
    }

    internal class PluginProxy : MarshalByRefObject
    {
        public void Initialize(HostProxy hostProxy)
        {
            Client.Credentials = new Credentials((string)AppDomain.CurrentDomain.GetData("username"), (string)AppDomain.CurrentDomain.GetData("password"));
            Client.CharacterName = (string)AppDomain.CurrentDomain.GetData("character");
            Client.Dimension = (Dimension)AppDomain.CurrentDomain.GetData("dimension");
            Client.HostProxy = hostProxy;
            Client.Logger = new LoggerConfiguration().WriteTo.DomainProxySink().MinimumLevel.Debug().CreateLogger();

            if ((bool)AppDomain.CurrentDomain.GetData("useChat"))
                Client.CreateChatClient();

            AppDomain.CurrentDomain.DomainUnload += (s, e) =>
            {
                Client.Teardown();
            };
        }

        public void Start()
        {
            Client.Init();
        }

        internal void LoadCoreAssembly(string assemblyPath)
        {
            //Load main assembly
            var assembly = Assembly.LoadFrom(assemblyPath);

            //Load references
            //foreach (var reference in assembly.GetReferencedAssemblies())
            //    Assembly.Load(reference);
        }

        internal void LoadPlugin(string assemblyPath)
        {
            //Load main assembly
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            IPCChannel.LoadMessages(assembly);

            //Load references
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (reference.Name == "AOSharp.Common" ||
                    reference.Name == "AOSharp.Clientless")
                    continue;

                try
                {
                    Assembly.Load(reference);
                }
                catch (FileNotFoundException)
                {
                    Assembly.LoadFrom($"{Path.GetDirectoryName(assemblyPath)}\\{reference.Name}.dll");
                }
            }

            // Find the first AOSharp.Clientless.IClientlessPluginEntry
            foreach (Type type in assembly.GetExportedTypes())
            {
                if (type.GetInterface("AOSharp.Clientless.IClientlessPluginEntry") == null)
                    continue;

                MethodInfo runMethod = type.GetMethod("Init", BindingFlags.Public | BindingFlags.Instance);

                if (runMethod == null)
                    continue;

                MethodInfo teardownMethod = type.GetMethod("Teardown", BindingFlags.Public | BindingFlags.Instance);

                if (teardownMethod == null)
                    continue;

                ConstructorInfo constructor = type.GetConstructor(Type.EmptyTypes);

                if (constructor == null)
                    continue;

                object instance = constructor.Invoke(null);

                if (instance == null) //Is this even possible?
                    continue;

                runMethod.Invoke(instance, new object[] { Path.GetDirectoryName(assemblyPath) });
            }
        }

        private T CreateDelegate<T>(Assembly assembly, string className, string methodName) where T : class
        {
            Type t = assembly.GetType(className);

            if (t == null)
                return default(T);

            MethodInfo m = t.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);

            if (m == null)
                return default(T);

            return Delegate.CreateDelegate(typeof(T), m) as T;
        }
    }
}
