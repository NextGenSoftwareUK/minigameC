using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.SignalR;
using System;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        });

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                var origins = Configuration["AllowedOrigins"];
                if (!string.IsNullOrEmpty(origins))
                {
                    builder.WithOrigins(origins.Split(','))
                           .AllowAnyMethod()
                           .AllowAnyHeader()
                           .AllowCredentials();
                }
            });
        });

        services.AddSingleton<GameState>();
        services.AddHostedService<GameLoop>();

        // Add logging
        services.AddLogging();

        // Add health checks
        services.AddHealthChecks();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, GameState gameState)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        else
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseCors();

        app.UseAuthorization();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
            await next();
        });

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHub<GameHub>("/gameHub");
            endpoints.MapGet("/", async context =>
            {
                await context.Response.WriteAsync("Welcome to the Mars Minigame Server!");
            });
            endpoints.MapHealthChecks("/health");
        });

        // Load the game state
        gameState.LoadState();

        // Graceful shutdown
        var applicationLifetime = app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>();
        applicationLifetime.ApplicationStopping.Register(() =>
        {
            gameState.SaveState();
        });
    }
}