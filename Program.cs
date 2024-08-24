using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel(options =>
                {
                    options.ListenAnyIP(5000); // HTTP port
                });
                webBuilder.UseStartup<Startup>();
            })
            .UseConsoleLifetime()
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<GracefulShutdownService>();

                // Error handling
                AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
                {
                    Console.Error.WriteLine($"Unhandled exception: {eventArgs.ExceptionObject}");
                };
            });
}
