using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

[EnableCors]
public class GameHub : Hub
{
    private readonly GameState _gameState;
    private readonly ILogger<GameHub> _logger;
    private static readonly Random _random = new Random();

    public GameHub(GameState gameState, ILogger<GameHub> logger)
    {
        _gameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task Ping()
    {
        return Task.CompletedTask;
    }

    public async Task SpawnTank(string walletAddress, int hq)
    {
        if (_gameState.IsIntermission)
        {
            _logger.LogWarning("Cannot spawn tank for {WalletAddress} during intermission", walletAddress);
            await Clients.Caller.SendAsync("spawnError", new { walletAddress, message = "Cannot spawn during intermission", timestamp = DateTime.UtcNow });
            return;
        }

        if (_gameState.HQs.TryGetValue(hq, out var hqData))
        {
            var existingTank = _gameState.Tanks.FirstOrDefault(tank => tank.Owner == walletAddress);
            if (existingTank != null)
            {
                _logger.LogWarning("Tank already exists for {WalletAddress}", walletAddress);
                return;
            }

            var spawnPosition = CalculateRandomSpawnPosition(hqData.Position, hq);

            var newTank = new Tank
            {
                Id = _gameState.Tanks.Count + 1,
                Owner = walletAddress,
                HQ = hq,
                Position = spawnPosition,
                CapturePointId = null,
                MovingTo = null,
                Rotation = 0,
                Visible = true,
                Health = 100,
                RespawnTime = null,
                HealingStartTime = null,
                FriendlyKills = 0,
                EnemyKills = 0
            };

            _gameState.Tanks.Add(newTank);
            _gameState.Players[walletAddress] = new Player(_gameState.HoloNETClient)
            {
                WalletAddress = walletAddress,
                HQ = hq,
                TankId = newTank.Id,
                Scans = 3,
                ArtilleryStrikesAvailable = 1
            };

            await Clients.All.SendAsync("gameUpdate", GetSimplifiedGameState());
            await Clients.All.SendAsync("tankSpawned", new { walletAddress, hq, position = spawnPosition, timestamp = DateTime.UtcNow });
            _logger.LogInformation("Tank spawned for {WalletAddress} in HQ {HQ} at position ({X}, {Y})", walletAddress, hq, spawnPosition.X, spawnPosition.Y);
        }
        else
        {
            _logger.LogError("Invalid player address or HQ: {WalletAddress}, HQ: {HQ}", walletAddress, hq);
        }
    }

    private Position CalculateRandomSpawnPosition(Position hqPosition, int hq)
    {
        double radius = 10; // Adjust this value to change the spawn area size
        double angle = hq == 1 ? Math.PI / 4 : 5 * Math.PI / 4; // Angle facing away from the corner
        double randomAngle = angle + (_random.NextDouble() - 0.5) * Math.PI / 2; // Random angle within a quarter circle
        double randomRadius = radius * Math.Sqrt(_random.NextDouble()); // Random radius for uniform distribution

        double x = hqPosition.X + randomRadius * Math.Cos(randomAngle);
        double y = hqPosition.Y + randomRadius * Math.Sin(randomAngle);

        return new Position { X = Math.Clamp(x, 0, GameState.MapSize), Y = Math.Clamp(y, 0, GameState.MapSize) };
    }

    public async Task MoveTank(string walletAddress, int capturePointId, DateTime actionTimestamp)
    {
        if (_gameState.IsIntermission)
        {
            _logger.LogWarning("Cannot move tank for {WalletAddress} during intermission", walletAddress);
            await Clients.Caller.SendAsync("moveError", new { walletAddress, message = "Cannot move during intermission", timestamp = DateTime.UtcNow });
            return;
        }

        var tank = _gameState.Tanks.FirstOrDefault(t => t.Owner == walletAddress);
        if (tank != null && tank.Health > 0)
        {
            var capturePoint = _gameState.CapturePoints.FirstOrDefault(cp => cp.Id == capturePointId);
            if (capturePoint != null)
            {
                // Remove tank from current capture point or HQ if it's already moving
                if (tank.CapturePointId != null)
                {
                    var currentPoint = _gameState.CapturePoints.FirstOrDefault(cp => cp.Id == tank.CapturePointId);
                    if (currentPoint != null)
                    {
                        currentPoint.Tanks[tank.HQ] = Math.Max(0, currentPoint.Tanks[tank.HQ] - 1);
                        _logger.LogDebug("Removed tank from capture point {CapturePointId}. New count: {TankCount}", currentPoint.Id, currentPoint.Tanks[tank.HQ]);
                    }
                }
                else if (tank.CapturePointId == null && tank.MovingTo == null)
                {
                    UpdateHQTankCount(tank.HQ, -1);
                    _logger.LogDebug("Removed tank from HQ {HQ}", tank.HQ);
                }

                tank.MovingTo = capturePointId;
                tank.CapturePointId = null;  // The tank is not at the capture point yet
                tank.Visible = true;  // Keep the tank visible while moving
                tank.HealingStartTime = null;

                var dx = capturePoint.Position.X - tank.Position.X;
                var dy = capturePoint.Position.Y - tank.Position.Y;
                tank.Rotation = Math.Atan2(dy, dx);

                _logger.LogInformation("Tank owned by {WalletAddress} moving to capture point: {CapturePointId}", walletAddress, capturePointId);

                await Clients.All.SendAsync("gameUpdate", GetSimplifiedGameState());
                await Clients.All.SendAsync("tankMoved", new { walletAddress, capturePointId, actionTimestamp, serverTimestamp = DateTime.UtcNow });
            }
            else
            {
                _logger.LogWarning("Attempt to move tank to non-existent capture point: {CapturePointId}", capturePointId);
                await Clients.Caller.SendAsync("moveError", new { walletAddress, message = "Invalid capture point", timestamp = DateTime.UtcNow });
            }
        }
        else
        {
            _logger.LogWarning("Invalid move attempt for tank owned by {WalletAddress}", walletAddress);
            await Clients.Caller.SendAsync("moveError", new { walletAddress, message = "Invalid move", timestamp = DateTime.UtcNow });
        }
    }

    public async Task ReturnTankToBase(string walletAddress, DateTime actionTimestamp)
    {
        if (_gameState.IsIntermission)
        {
            _logger.LogWarning("Cannot return tank to base for {WalletAddress} during intermission", walletAddress);
            await Clients.Caller.SendAsync("moveError", new { walletAddress, message = "Cannot move during intermission", timestamp = DateTime.UtcNow });
            return;
        }

        var tank = _gameState.Tanks.FirstOrDefault(t => t.Owner == walletAddress);
        if (tank != null && tank.Health > 0)
        {
            if (_gameState.HQs.TryGetValue(tank.HQ, out var hq))
            {
                if (tank.CapturePointId != null)
                {
                    var currentPoint = _gameState.CapturePoints.FirstOrDefault(cp => cp.Id == tank.CapturePointId);
                    if (currentPoint != null)
                    {
                        currentPoint.Tanks[tank.HQ] = Math.Max(0, currentPoint.Tanks[tank.HQ] - 1);
                    }
                    tank.CapturePointId = null;
                }

                tank.MovingTo = "base";
                tank.Visible = true;

                var dx = hq.Position.X - tank.Position.X;
                var dy = hq.Position.Y - tank.Position.Y;
                tank.Rotation = Math.Atan2(dy, dx);

                _logger.LogInformation("Tank owned by {WalletAddress} returning to base", walletAddress);
                await Clients.All.SendAsync("gameUpdate", GetSimplifiedGameState());
                await Clients.All.SendAsync("tankReturningToBase", new { walletAddress, actionTimestamp, serverTimestamp = DateTime.UtcNow });
            }
        }
    }

    public async Task ScanObjective(string walletAddress, int capturePointId, DateTime actionTimestamp)
    {
        if (_gameState.IsIntermission)
        {
            _logger.LogWarning("Cannot scan objective for {WalletAddress} during intermission", walletAddress);
            await Clients.Caller.SendAsync("scanError", new { walletAddress, message = "Cannot scan during intermission", timestamp = DateTime.UtcNow });
            return;
        }

        if (!_gameState.Players.TryGetValue(walletAddress, out var player) || player.Scans <= 0)
        {
            _logger.LogWarning("Player {WalletAddress} has no scans available", walletAddress);
            return;
        }

        var capturePoint = _gameState.CapturePoints.FirstOrDefault(cp => cp.Id == capturePointId);
        if (capturePoint == null)
        {
            _logger.LogError("Invalid capture point ID: {CapturePointId}", capturePointId);
            return;
        }

        player.Scans--;

        var enemyHQ = player.HQ == 1 ? 2 : 1;
        var enemyTanksCount = capturePoint.Tanks[enemyHQ];

        var playersInHQ = _gameState.Players.Values.Where(p => p.HQ == player.HQ);
        foreach (var p in playersInHQ)
        {
            await Clients.All.SendAsync("scanResult", new
            {
                walletAddress = p.WalletAddress,
                capturePointId,
                enemyTanksCount,
                timestamp = DateTime.UtcNow,
                performedBy = walletAddress,
                actionTimestamp
            });
        }

        _logger.LogInformation("Player {WalletAddress} scanned objective {CapturePointId}. Enemy tanks: {EnemyTanksCount}", walletAddress, capturePointId, enemyTanksCount);

        await Clients.All.SendAsync("scansUpdated", new { walletAddress, scans = player.Scans, actionTimestamp, serverTimestamp = DateTime.UtcNow });
    }

    public async Task SetScans(string walletAddress, int scans)
    {
        if (_gameState.Players.TryGetValue(walletAddress, out var player))
        {
            player.Scans = Math.Max(0, scans);
            _logger.LogInformation("Set scans for player {WalletAddress} to {Scans}", walletAddress, player.Scans);
            await Clients.All.SendAsync("scansUpdated", new { walletAddress, scans = player.Scans, timestamp = DateTime.UtcNow });
        }
        else
        {
            _logger.LogWarning("Player {WalletAddress} not found", walletAddress);
        }
    }

    public async Task HandleArtilleryStrike(string walletAddress, Position position, DateTime actionTimestamp)
    {
        if (_gameState.IsIntermission)
        {
            _logger.LogWarning("Cannot use artillery strike for {WalletAddress} during intermission", walletAddress);
            await Clients.Caller.SendAsync("artilleryError", new { walletAddress, message = "Cannot use artillery during intermission", timestamp = DateTime.UtcNow });
            return;
        }

        if (!_gameState.Players.TryGetValue(walletAddress, out var player) || player.ArtilleryStrikesAvailable <= 0)
        {
            _logger.LogWarning("Player {WalletAddress} has no artillery strikes available", walletAddress);
            await Clients.Caller.SendAsync("artilleryError", new { walletAddress, message = "No artillery strikes available", timestamp = DateTime.UtcNow });
            return;
        }

        player.ArtilleryStrikesAvailable--;

        var affectedTanks = new List<object>();
        var strikerTank = _gameState.Tanks.FirstOrDefault(tank => tank.Owner == walletAddress);

        foreach (var tank in _gameState.Tanks)
        {
            var tankDistance = Distance(tank.Position, position);

            if (tankDistance <= GameState.ArtilleryStrikeRadius)
            {
                var damageMultiplier = 1 - (tankDistance / GameState.ArtilleryStrikeRadius);
                var damage = (int)(GameState.ArtilleryStrikeDamage * damageMultiplier);
                var oldHealth = tank.Health;
                tank.Health = Math.Max(0, tank.Health - damage);
                affectedTanks.Add(new
                {
                    id = tank.Id,
                    owner = tank.Owner,
                    remainingHealth = tank.Health,
                    damageTaken = oldHealth - tank.Health,
                    capturePointId = tank.CapturePointId
                });

                _logger.LogInformation("Artillery strike damaged tank {TankId}. Health: {OldHealth} -> {NewHealth}", tank.Id, oldHealth, tank.Health);

                if (tank.Health <= 0)
                {
                    if (strikerTank != null)
                    {
                        if (strikerTank.HQ == tank.HQ)
                            strikerTank.FriendlyKills++;
                        else
                            strikerTank.EnemyKills++;
                    }
                    await DestroyTank(tank, strikerTank);
                }
                else
                {
                    UpdateTankPosition(tank);
                }
            }
        }

        foreach (var point in _gameState.CapturePoints)
        {
            var pointDistance = Distance(point.Position, position);

            if (pointDistance <= GameState.ArtilleryStrikeRadius)
            {
                foreach (var hq in point.Tanks.Keys.ToList())
                {
                    var tanksAtPoint = point.Tanks[hq];
                    var damageMultiplier = 1 - (pointDistance / GameState.ArtilleryStrikeRadius);
                    var damage = (int)(GameState.ArtilleryStrikeDamage * damageMultiplier);
                    var killedTanks = 0;

                    for (int i = 0; i < tanksAtPoint; i++)
                    {
                        var tankHealth = 100 - damage;
                        if (tankHealth <= 0)
                        {
                            killedTanks++;
                            if (strikerTank != null)
                            {
                                if (strikerTank.HQ == hq)
                                    strikerTank.FriendlyKills++;
                                else
                                    strikerTank.EnemyKills++;
                            }
                        }
                    }

                    point.Tanks[hq] = Math.Max(0, point.Tanks[hq] - killedTanks);

                    for (int i = 0; i < killedTanks; i++)
                    {
                        affectedTanks.Add(new
                        {
                            id = (int?)null,
                            owner = (string?)null,
                            remainingHealth = 0,
                            damageTaken = 100,
                            capturePointId = point.Id
                        });
                    }

                    UpdateCapturePointStatus(point);
                }
            }
        }

        var affectedCapturePoints = affectedTanks.Select(t => (t as dynamic).capturePointId).Where(id => id != null).Distinct().ToList();
        foreach (var capturePointId in affectedCapturePoints)
        {
            var capturePoint = _gameState.CapturePoints.FirstOrDefault(cp => cp.Id == capturePointId);
            if (capturePoint != null)
            {
                UpdateCapturePointStatus(capturePoint);
            }
        }

        await Clients.All.SendAsync("artilleryStrikeResult", new
        {
            position,
            radius = GameState.ArtilleryStrikeRadius,
            affectedTanks,
            timestamp = DateTime.UtcNow,
            actionTimestamp
        });

        _logger.LogInformation("Artillery strike by {WalletAddress} at position ({X}, {Y})", walletAddress, position.X, position.Y);
    }

    private void UpdateHQTankCount(int hqId, int change)
    {
        if (_gameState.HQs.TryGetValue(hqId, out var hq))
        {
            hq.Tanks = Math.Max(0, hq.Tanks + change);
        }
    }

    private async Task DestroyTank(Tank tank, Tank? killerTank)
    {
        tank.Health = 0;
        tank.Visible = false;
        tank.RespawnTime = DateTime.UtcNow.AddSeconds(3);

        if (tank.CapturePointId != null)
        {
            var capturePoint = _gameState.CapturePoints.FirstOrDefault(cp => cp.Id == tank.CapturePointId);
            if (capturePoint != null)
            {
                capturePoint.Tanks[tank.HQ] = Math.Max(0, capturePoint.Tanks[tank.HQ] - 1);
            }
        }

        tank.CapturePointId = null;
        tank.MovingTo = null;

        await Clients.All.SendAsync("tankDestroyed", new
        {
            tankId = tank.Id,
            owner = tank.Owner,
            killerTankId = killerTank?.Id,
            killerOwner = killerTank?.Owner,
            timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Tank {TankId} destroyed at position ({X}, {Y})", tank.Id, tank.Position.X, tank.Position.Y);
    }

    private void UpdateTankPosition(Tank tank)
    {
        if (tank.CapturePointId.HasValue)
        {
            var capturePoint = _gameState.CapturePoints.FirstOrDefault(cp => cp.Id == tank.CapturePointId.Value);
            if (capturePoint != null)
            {
                capturePoint.Tanks[tank.HQ] = Math.Max(1, capturePoint.Tanks[tank.HQ]);
            }
        }
    }

    private void UpdateCapturePointStatus(CapturePoint capturePoint)
    {
        _gameState.UpdateCapturePointStatus(capturePoint);
    }

    private void ApplyDamageToTanks(List<Tank> tanks, double damage, List<Tank> enemyTanks, CapturePoint capturePoint)
    {
        foreach (var tank in tanks.ToList()) // Use ToList to avoid collection modification issues
        {
            tank.Health -= damage;
            _logger.LogDebug("HQ{HQ} tank {TankId} health: {Health}", tank.HQ, tank.Id, tank.Health);
            if (tank.Health <= 0)
            {
                var killerTank = enemyTanks.Count > 0 ? enemyTanks[new Random().Next(enemyTanks.Count)] : null;
                DestroyTank(tank, killerTank).Wait();
                tanks.Remove(tank);
                capturePoint.Tanks[tank.HQ]--;
            }
        }
    }

    private double Distance(Position point1, Position point2)
    {
        return Math.Sqrt(Math.Pow(point1.X - point2.X, 2) + Math.Pow(point1.Y - point2.Y, 2));
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
            hqs = _gameState.HQs,
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
            capturePoints = _gameState.CapturePoints,
            timestamp = DateTime.UtcNow
        };
    }
}