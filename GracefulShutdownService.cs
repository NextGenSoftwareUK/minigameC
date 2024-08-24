using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

public class GracefulShutdownService : IHostedService
{
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly GameState _gameState;

    public GracefulShutdownService(IHostApplicationLifetime appLifetime, GameState gameState)
    {
        _appLifetime = appLifetime;
        _gameState = gameState;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _appLifetime.ApplicationStopping.Register(OnShutdown);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void OnShutdown()
    {
        System.Console.WriteLine("Application is shutting down...");
        _gameState.SaveState();
        System.Console.WriteLine("Game state saved.");
    }
}