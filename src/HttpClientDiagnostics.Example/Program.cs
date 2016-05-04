using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using HttpClientDiagnostics.Logging;
using Serilog;

namespace HttpClientDiagnostics.Example
{
    class Program
    {
        static void Main(string[] args)
        {
            // Can use any logging implementaiton with this library, see https://github.com/damianh/LibLog
            var log = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.RollingFile("debug.txt")
                .WriteTo.ColoredConsole(outputTemplate: "{Timestamp:HH:mm} [{Level}] ({Name:l}) {Message}{NewLine}{Exception}")
                .CreateLogger();
            Log.Logger = log;


            MainAsync().Wait();
        }

        static async Task MainAsync()
        {
            Log.Debug("test");
            var client = new HttpClient(new HttpClientDiagnosticsHandler());

            await client.GetStringAsync("https://api.duckduckgo.com/?q=apple&format=json");
        }
    }
}
