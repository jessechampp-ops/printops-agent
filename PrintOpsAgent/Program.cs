using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintOpsAgent.Services;

namespace PrintOpsAgent;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "PrintOps Agent";
            })
            .ConfigureServices((hostContext, services) =>
            {
                services.AddSingleton<ConfigurationService>();
                services.AddSingleton<PrinterService>();
                services.AddSingleton<WebSocketService>();
                services.AddSingleton<CommandHandler>();
                services.AddHostedService<PrintOpsAgentWorker>();
            });
}
