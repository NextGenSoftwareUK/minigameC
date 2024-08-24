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
                webBuilder.UseStartup<Startup>();
                webBuilder.UseUrls("http://localhost:5000"); // Explicitly set the URL
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