using AOSharp.Clientless.Chat;
using AOSharp.Clientless.Common;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ChatTest
{
    internal class Program
    {
        static void Main(string[] args)
        {

            string[] test_acc = File.ReadAllText("C:\\Users\\tagyo\\Desktop\\test_acc.txt").Split('|');

            Logger logger = new LoggerConfiguration().WriteTo.Console().MinimumLevel.Debug().CreateLogger();


            ChatClient chat = new ChatClient(new Credentials(test_acc[0], test_acc[1]), test_acc[2], Dimension.RubiKa, logger);
            
            chat.PrivateMessageReceived += (e, msg) =>
            {
                logger.Information($"Received {msg.Message} from {msg.SenderName}");
            };

            chat.Init();

            Console.ReadLine();
        }
    }
}
