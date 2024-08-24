using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

public class GameLoop : IHostedService, IDisposable
{
    private System.Timers.Timer _gameTimer;
    private System.Timers.Timer _matchTimer;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly GameState _gameState;

    private const double MoveSpeed = 20; // units per second
    private const int HealDuration = 3000; // 3 seconds to heal

    public GameLoop(IHubContext<GameHub> hubContext, GameState gameState)
    {
        _hubContext = hubContext;
        _gameState = gameState;
        _gameTimer = new System.Timers.Timer(100);
        _matchTimer = new System.Timers.Timer(1000);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _gameTimer.Elapsed += UpdateGameState;
        _gameTimer.Start();

        _matchTimer.Elapsed += UpdateMatchTime;
        _matchTimer.Start();

        StartIntermission();

        return Task.CompletedTask;
    }

    private async void UpdateGameState(object? sender, ElapsedEventArgs e)
    {
        if (_gameState.IsIntermission) return;

        var deltaTime = 0.1; // 100ms interval
        var currentTime = DateTime.UtcNow;

        foreach (var tank in _gameState.Tanks)
        {
            if (tank.RespawnTime.HasValue && currentTime >= tank.RespawnTime.Value)
            {
                // Respawn the tank
                if (_gameState.HQs.TryGetValue(tank.HQ, out var hq))
                {
                    tank.Position = new Position { X = hq.Position.X, Y = hq.Position.Y };
                    tank.Health = 100;
                    tank.Visible = true;
                    tank.RespawnTime = null;
                    await _hubContext.Clients.All.SendAsync("tankSpawned", new { walletAddress = tank.Owner, hq = tank.HQ, timestamp = currentTime });
                }
            }

            if (tank.MovingTo != null && tank.Health > 0)
            {
                Position? targetPoint = null;
                if (tank.MovingTo is string movingToBaseString && movingToBaseString == "base")
                {
                    if (_gameState.HQs.TryGetValue(tank.HQ, out var hq))
                    {
                        targetPoint = hq.Position;
                    }
                }
                else if (tank.MovingTo is int movingToInt)
                {
                    var capturePoint = _gameState.CapturePoints.Find(cp => cp.Id == movingToInt);
                    targetPoint = capturePoint?.Position;
                }

                if (targetPoint != null)
                {
                    var dx = targetPoint.X - tank.Position.X;
                    var dy = targetPoint.Y - tank.Position.Y;
                    var distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance <= MoveSpeed * deltaTime)
                    {
                        if (tank.MovingTo is string movingToBaseString2 && movingToBaseString2 == "base")
                        {
                            // Tank has arrived at base, start healing
                            tank.Position = new Position { X = targetPoint.X, Y = targetPoint.Y };
                            tank.MovingTo = null;
                            tank.HealingStartTime = currentTime;
                            UpdateHQTankCount(tank.HQ, 1);
                        }
                        else if (tank.MovingTo is int movingToInt2)
                        {
                            var capturePoint = _gameState.CapturePoints.Find(cp => cp.Id == movingToInt2);
                            if (capturePoint != null)
                            {
                                ArriveTank(tank, capturePoint);
                            }
                        }
                    }
                    else
                    {
                        var ratio = MoveSpeed * deltaTime / distance;
                        tank.Position.X += dx * ratio;
                        tank.Position.Y += dy * ratio;
                    }
                }
            }

            // Heal tank at base
            if (tank.HealingStartTime.HasValue)
            {
                var healingTime = (currentTime - tank.HealingStartTime.Value).TotalMilliseconds;
                if (healingTime >= HealDuration)
                {
                    tank.Health = 100;
                    tank.HealingStartTime = null;
                    await _hubContext.Clients.All.SendAsync("tankHealed", new { tankId = tank.Id, owner = tank.Owner, timestamp = currentTime });
                }
                else
                {
                    tank.Health = Math.Min(100, tank.Health + (100.0 / HealDuration) * deltaTime * 1000);
                }
            }
        }

        ReconcileTankCounts();

        foreach (var capturePoint in _gameState.CapturePoints)
        {
            _gameState.UpdateCapturePointStatus(capturePoint);
        }

        await _hubContext.Clients.All.SendAsync("gameUpdate", GetSimplifiedGameState());
    }

    private async void UpdateMatchTime(object? sender, ElapsedEventArgs e)
    {
        var currentTime = DateTime.UtcNow;
        if (_gameState.IsIntermission)
        {
            if (_gameState.IntermissionStartTime.HasValue)
            {
                var elapsedTime = (currentTime - _gameState.IntermissionStartTime.Value).TotalMilliseconds;
                var remainingTime = Math.Max(0, GameState.IntermissionDuration - elapsedTime);
                await _hubContext.Clients.All.SendAsync("intermissionTimeUpdate", new { remainingTime, isIntermission = true, timestamp = currentTime });
            }
        }
        else if (_gameState.MatchInProgress)
        {
            if (_gameState.MatchStartTime.HasValue)
            {
                var elapsedTime = (currentTime - _gameState.MatchStartTime.Value).TotalMilliseconds;
                var remainingTime = Math.Max(0, GameState.MatchDuration - elapsedTime);
                var matchStatistics = GetMatchStatistics();
                await _hubContext.Clients.All.SendAsync("matchTimeUpdate", new { remainingTime, statistics = matchStatistics, isIntermission = false, timestamp = currentTime });
            }
        }
    }

    private void StartIntermission()
    {
        _gameState.IsIntermission = true;
        _gameState.MatchInProgress = false;
        _gameState.IntermissionStartTime = DateTime.UtcNow;
        _hubContext.Clients.All.SendAsync("intermissionStart", new { timestamp = _gameState.IntermissionStartTime });
        Console.WriteLine("Intermission started");

        _gameState.ResetGameState();

        Task.Delay(GameState.IntermissionDuration).ContinueWith(_ => StartMatch());
    }

    private void StartMatch()
    {
        _gameState.IsIntermission = false;
        _gameState.MatchInProgress = true;
        _gameState.MatchStartTime = DateTime.UtcNow;
        _hubContext.Clients.All.SendAsync("matchStart", new { timestamp = _gameState.MatchStartTime });
        Console.WriteLine("Match started");

        foreach (var player in _gameState.Players.Values)
        {
            player.ArtilleryStrikesAvailable = 1;
        }

        Task.Delay(GameState.MatchDuration).ContinueWith(_ => EndMatch());
    }

    private void EndMatch()
    {
        var winner = DetermineWinner();
        _hubContext.Clients.All.SendAsync("matchEnd", new { winner, timestamp = DateTime.UtcNow });
        Console.WriteLine($"Match ended. Winner: {(winner == 0 ? "Draw" : $"HQ {winner}")}");

        StartIntermission();
    }

    private int DetermineWinner()
    {
        var scores = CalculateScores();
        if (scores[1] > scores[2]) return 1;
        if (scores[2] > scores[1]) return 2;
        return 0; // Draw
    }

    private Dictionary<int, int> CalculateScores()
    {
        var scores = new Dictionary<int, int> { { 1, 0 }, { 2, 0 } };
        foreach (var point in _gameState.CapturePoints)
        {
            if (point.ControlledBy.HasValue)
            {
                scores[point.ControlledBy.Value]++;
            }
        }
        return scores;
    }

    private object GetMatchStatistics()
    {
        var scores = CalculateScores();
        var tankCounts = new Dictionary<int, int> { { 1, 0 }, { 2, 0 } };
        foreach (var tank in _gameState.Tanks)
        {
            tankCounts[tank.HQ]++;
        }

        return new
        {
            scores,
            tankCounts,
            capturePoints = _gameState.CapturePoints.Select(point => new
            {
                point.Id,
                point.ControlledBy,
                point.Tanks
            }),
            timestamp = DateTime.UtcNow
        };
    }

    private void UpdateHQTankCount(int hqId, int change)
    {
        if (_gameState.HQs.TryGetValue(hqId, out var hq))
        {
            hq.Tanks = Math.Max(0, hq.Tanks + change);
        }
    }

    private void ArriveTank(Tank tank, CapturePoint capturePoint)
    {
        tank.Position = new Position { X = capturePoint.Position.X, Y = capturePoint.Position.Y };
        tank.CapturePointId = capturePoint.Id;
        tank.MovingTo = null;
        tank.Visible = false;  // Make the tank invisible when it arrives at the capture point

        capturePoint.Tanks[tank.HQ] = Math.Max(1, capturePoint.Tanks[tank.HQ] + 1);

        _gameState.UpdateCapturePointStatus(capturePoint);
    }

    private void ApplyDamageToTanks(List<Tank> tanks, double damage, List<Tank> enemyTanks, CapturePoint capturePoint)
    {
        foreach (var tank in tanks.ToList()) // Use ToList to avoid collection modification issues
        {
            tank.Health -= damage;
            Console.WriteLine($"HQ{tank.HQ} tank {tank.Id} health: {tank.Health}");
            if (tank.Health <= 0)
            {
                var killerTank = enemyTanks.Count > 0 ? enemyTanks[new Random().Next(enemyTanks.Count)] : null;
                DestroyTank(tank, killerTank, capturePoint);
                tanks.Remove(tank);
                capturePoint.Tanks[tank.HQ]--;
            }
        }
    }

    private void DestroyTank(Tank tank, Tank? killerTank, CapturePoint capturePoint)
    {
        tank.Health = 0;
        tank.Visible = false;
        tank.RespawnTime = DateTime.UtcNow.AddSeconds(3);

        if (tank.CapturePointId != null)
        {
            capturePoint.Tanks[tank.HQ] = Math.Max(0, capturePoint.Tanks[tank.HQ] - 1);
        }

        tank.CapturePointId = null;
        tank.MovingTo = null;

        if (killerTank != null)
        {
            if (killerTank.HQ == tank.HQ)
            {
                killerTank.FriendlyKills++;
            }
            else
            {
                killerTank.EnemyKills++;
            }
        }

        _hubContext.Clients.All.SendAsync("tankDestroyed", new
        {
            tankId = tank.Id,
            owner = tank.Owner,
            killerTankId = killerTank?.Id,
            killerOwner = killerTank?.Owner,
            timestamp = DateTime.UtcNow
        });
        Console.WriteLine($"Tank {tank.Id} destroyed at position ({tank.Position.X}, {tank.Position.Y})");
    }

    private void ReconcileTankCounts()
    {
        foreach (var capturePoint in _gameState.CapturePoints)
        {
            var hq1Tanks = _gameState.Tanks.Count(t => t.CapturePointId == capturePoint.Id && t.HQ == 1);
            var hq2Tanks = _gameState.Tanks.Count(t => t.CapturePointId == capturePoint.Id && t.HQ == 2);

            if (capturePoint.Tanks[1] != hq1Tanks || capturePoint.Tanks[2] != hq2Tanks)
            {
                capturePoint.Tanks[1] = hq1Tanks;
                capturePoint.Tanks[2] = hq2Tanks;
                Console.WriteLine($"Reconciled tank counts for capture point {capturePoint.Id}: HQ1: {hq1Tanks}, HQ2: {hq2Tanks}");
            }
        }
    }

    private object GetSimplifiedGameState()
    {
        return new
        {
            players = _gameState.Players.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    kvp.Value.WalletAddress,
                    kvp.Value.HQ,
                    kvp.Value.TankId,
                    kvp.Value.Scans,
                    kvp.Value.ArtilleryStrikesAvailable
                }
            ),
            hqs = _gameState.HQs.ToDictionary(
                kvp => kvp.Key,
                kvp => new
                {
                    kvp.Value.Position,
                    kvp.Value.Tanks
                }
            ),
            tanks = _gameState.Tanks.Select(tank => new
            {
                tank.Id,
                tank.Owner,
                tank.HQ,
                tank.Position,
                tank.CapturePointId,
                tank.MovingTo,
                tank.Rotation,
                tank.Visible,
                tank.Health,
                tank.HealingStartTime,
                tank.FriendlyKills,
                tank.EnemyKills
            }),
            capturePoints = _gameState.CapturePoints.Select(cp => new
            {
                cp.Id,
                cp.Position,
                cp.ControlledBy,
                cp.Tanks,
                cp.CaptureProgress,
                cp.DefenseBoost,
                CaptureTime = cp.CaptureTime.HasValue ? cp.CaptureTime.Value.ToString("o") : null
            }),
            timestamp = DateTime.UtcNow.ToString("o")
        };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gameTimer?.Stop();
        _matchTimer?.Stop();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _gameTimer?.Dispose();
        _matchTimer?.Dispose();
    }
}