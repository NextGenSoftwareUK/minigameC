using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;

public class GameState
{
    public const int MapSize = 100;
    public const int ArtilleryStrikeRadius = 15;
    public const int ArtilleryStrikeDamage = 50;
    public const int MatchDuration = 9 * 60 * 1000; // 9 minutes
    public const int IntermissionDuration = 5 * 60 * 1000; // 5 minutes

    public Dictionary<string, Player> Players { get; set; } = new Dictionary<string, Player>();
    public Dictionary<int, HQ> HQs { get; set; } = new Dictionary<int, HQ>();
    public List<Tank> Tanks { get; set; } = new List<Tank>();
    public List<CapturePoint> CapturePoints { get; set; } = new List<CapturePoint>();
    public bool IsIntermission { get; set; } = true;
    public bool MatchInProgress { get; set; } = false;
    public DateTime? MatchStartTime { get; set; }
    public DateTime? IntermissionStartTime { get; set; }

    private static readonly Random random = new Random();

    public GameState()
    {
        ResetGameState();
    }

    public void ResetGameState()
    {
        Players.Clear();
        HQs = new Dictionary<int, HQ>
        {
            { 1, new HQ { Position = new Position { X = 10, Y = 90 }, Tanks = 0 } },
            { 2, new HQ { Position = new Position { X = 90, Y = 10 }, Tanks = 0 } }
        };
        Tanks.Clear();
        CapturePoints = GenerateCapturePoints();
    }

    private List<CapturePoint> GenerateCapturePoints()
    {
        var points = new List<CapturePoint>();
        double centerX = MapSize / 2.0;
        double centerY = MapSize / 2.0;
        double radius = 30;

        points.Add(new CapturePoint
        {
            Id = 1,
            Position = new Position { X = centerX, Y = centerY },
            ControlledBy = null,
            Tanks = new Dictionary<int, int> { { 1, 0 }, { 2, 0 } },
            CaptureProgress = 0,
            DefenseBoost = 1,
            CaptureTime = null
        });

        for (int i = 0; i < 8; i++)
        {
            double angle = (i / 8.0) * 2 * Math.PI;
            points.Add(new CapturePoint
            {
                Id = i + 2,
                Position = new Position
                {
                    X = centerX + radius * Math.Cos(angle),
                    Y = centerY + radius * Math.Sin(angle)
                },
                ControlledBy = null,
                Tanks = new Dictionary<int, int> { { 1, 0 }, { 2, 0 } },
                CaptureProgress = 0,
                DefenseBoost = 1,
                CaptureTime = null
            });
        }

        return points;
    }

    public Position CalculateSpawnArea(int hq)
    {
        var hqPosition = HQs[hq].Position;
        double angle = hq == 1 ? Math.PI / 4 : 5 * Math.PI / 4; // Angle facing away from the corner
        double radius = 10; // Adjust this value to change the spawn area size

        double randomAngle = angle + (random.NextDouble() - 0.5) * Math.PI / 2; // Random angle within a quarter circle
        double randomRadius = radius * Math.Sqrt(random.NextDouble()); // Random radius for uniform distribution

        double x = hqPosition.X + randomRadius * Math.Cos(randomAngle);
        double y = hqPosition.Y + randomRadius * Math.Sin(randomAngle);

        return new Position
        {
            X = Math.Clamp(x, 0, MapSize),
            Y = Math.Clamp(y, 0, MapSize)
        };
    }

    public void UpdateCapturePointStatus(CapturePoint capturePoint)
    {
        var totalTanks = capturePoint.Tanks[1] + capturePoint.Tanks[2];
        var hq1Percentage = totalTanks > 0 ? (double)capturePoint.Tanks[1] / totalTanks : 0;
        var hq2Percentage = totalTanks > 0 ? (double)capturePoint.Tanks[2] / totalTanks : 0;

        var currentTime = DateTime.UtcNow;

        // Update capture progress
        if (hq1Percentage > hq2Percentage)
        {
            capturePoint.CaptureProgress += (hq1Percentage - hq2Percentage) * 10;
        }
        else if (hq2Percentage > hq1Percentage)
        {
            capturePoint.CaptureProgress -= (hq2Percentage - hq1Percentage) * 10;
        }

        // Clamp capture progress between -100 and 100
        capturePoint.CaptureProgress = Math.Clamp(capturePoint.CaptureProgress, -100, 100);

        // Update control status
        if (capturePoint.CaptureProgress == 100 && capturePoint.ControlledBy != 1)
        {
            capturePoint.ControlledBy = 1;
            capturePoint.CaptureTime = currentTime;
            capturePoint.DefenseBoost = 1;
            Console.WriteLine($"Capture Point {capturePoint.Id} captured by HQ 1");
        }
        else if (capturePoint.CaptureProgress == -100 && capturePoint.ControlledBy != 2)
        {
            capturePoint.ControlledBy = 2;
            capturePoint.CaptureTime = currentTime;
            capturePoint.DefenseBoost = 1;
            Console.WriteLine($"Capture Point {capturePoint.Id} captured by HQ 2");
        }
        else if (capturePoint.CaptureProgress == 0 && capturePoint.ControlledBy != null)
        {
            capturePoint.ControlledBy = null;
            capturePoint.CaptureTime = null;
            capturePoint.DefenseBoost = 1;
            Console.WriteLine($"Capture Point {capturePoint.Id} neutralized");
        }

        // Increase defense boost if point is controlled
        if (capturePoint.ControlledBy != null && capturePoint.CaptureTime.HasValue)
        {
            var timeSinceCapture = (currentTime - capturePoint.CaptureTime.Value).TotalMilliseconds;
            if (timeSinceCapture >= 9000)
            {
                var oldDefenseBoost = capturePoint.DefenseBoost;
                capturePoint.DefenseBoost = Math.Min(1.5, capturePoint.DefenseBoost + 0.01);
                Console.WriteLine($"Defense boost applied to Capture Point {capturePoint.Id}. Old boost: {oldDefenseBoost}, New boost: {capturePoint.DefenseBoost}, Controlled by HQ: {capturePoint.ControlledBy}, Time since capture: {timeSinceCapture}ms");
            }
        }

        // Tank battle logic
        if (capturePoint.Tanks[1] > 0 && capturePoint.Tanks[2] > 0)
        {
            var tanks1 = Tanks.Where(t => t.CapturePointId == capturePoint.Id && t.HQ == 1).ToList();
            var tanks2 = Tanks.Where(t => t.CapturePointId == capturePoint.Id && t.HQ == 2).ToList();

            var damage1 = 10.0 * capturePoint.Tanks[2] / (capturePoint.Tanks[1] * (capturePoint.ControlledBy == 1 ? capturePoint.DefenseBoost : 1));
            var damage2 = 10.0 * capturePoint.Tanks[1] / (capturePoint.Tanks[2] * (capturePoint.ControlledBy == 2 ? capturePoint.DefenseBoost : 1));

            ApplyDamageToTanks(tanks1, damage1, tanks2, capturePoint);
            ApplyDamageToTanks(tanks2, damage2, tanks1, capturePoint);

            // Update tank counts
            capturePoint.Tanks[1] = tanks1.Count;
            capturePoint.Tanks[2] = tanks2.Count;
        }

        // Ensure tank counts are non-negative
        capturePoint.Tanks[1] = Math.Max(0, capturePoint.Tanks[1]);
        capturePoint.Tanks[2] = Math.Max(0, capturePoint.Tanks[2]);
    }

    private void ApplyDamageToTanks(List<Tank> tanks, double damage, List<Tank> enemyTanks, CapturePoint capturePoint)
    {
        foreach (var tank in tanks.ToList())
        {
            tank.Health -= damage;
            Console.WriteLine($"HQ{tank.HQ} tank {tank.Id} health: {tank.Health}");
            if (tank.Health <= 0)
            {
                var killerTank = enemyTanks.Count > 0 ? enemyTanks[random.Next(enemyTanks.Count)] : null;
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

        Console.WriteLine($"Tank {tank.Id} destroyed at position ({tank.Position.X}, {tank.Position.Y})");
    }

    public void SaveState()
    {
        var json = JsonSerializer.Serialize(this);
        File.WriteAllText("gamestate.json", json);
        Console.WriteLine("Game state saved to gamestate.json");
    }

    public void LoadState()
    {
        if (File.Exists("gamestate.json"))
        {
            var json = File.ReadAllText("gamestate.json");
            var loadedState = JsonSerializer.Deserialize<GameState>(json);
            if (loadedState != null)
            {
                Players = loadedState.Players;
                HQs = loadedState.HQs;
                Tanks = loadedState.Tanks;
                CapturePoints = loadedState.CapturePoints;
                IsIntermission = loadedState.IsIntermission;
                MatchInProgress = loadedState.MatchInProgress;
                MatchStartTime = loadedState.MatchStartTime;
                IntermissionStartTime = loadedState.IntermissionStartTime;
                Console.WriteLine("Game state loaded from gamestate.json");
            }
        }
        else
        {
            Console.WriteLine("No saved game state found. Starting with a fresh state.");
            ResetGameState();
        }
    }
}

public class Player
{
    public required string WalletAddress { get; set; }
    public int HQ { get; set; }
    public int TankId { get; set; }
    public int Scans { get; set; }
    public int ArtilleryStrikesAvailable { get; set; }
}

public class HQ
{
    public required Position Position { get; set; }
    public int Tanks { get; set; }
}

public class Tank
{
    public int Id { get; set; }
    public required string Owner { get; set; }
    public int HQ { get; set; }
    public required Position Position { get; set; }
    public int? CapturePointId { get; set; }
    public object? MovingTo { get; set; } // Can be int? for CapturePointId or string "base"
    public double Rotation { get; set; }
    public bool Visible { get; set; }
    public double Health { get; set; }
    public DateTime? RespawnTime { get; set; }
    public DateTime? HealingStartTime { get; set; }
    public int FriendlyKills { get; set; }
    public int EnemyKills { get; set; }
}

public class CapturePoint
{
    public int Id { get; set; }
    public required Position Position { get; set; }
    public int? ControlledBy { get; set; }
    public required Dictionary<int, int> Tanks { get; set; }
    public double CaptureProgress { get; set; }
    public double DefenseBoost { get; set; }
    public DateTime? CaptureTime { get; set; }
}

public class Position
{
    public double X { get; set; }
    public double Y { get; set; }
}