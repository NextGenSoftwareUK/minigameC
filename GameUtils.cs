using System;

public static class GameUtils
{
    public static double Distance(Position point1, Position point2)
    {
        return Math.Sqrt(Math.Pow(point1.X - point2.X, 2) + Math.Pow(point1.Y - point2.Y, 2));
    }

    public static bool IsTankAtHQ(Tank tank, HQ hq)
    {
        return Distance(tank.Position, hq.Position) < 1; // Assuming a tank is "at" the HQ if it's within 1 unit
    }

    public static void ReconcileTankCounts(GameState gameState)
    {
        foreach (var capturePoint in gameState.CapturePoints)
        {
            var tanksAtPoint = gameState.Tanks.FindAll(tank => tank.CapturePointId == capturePoint.Id);
            var hq1Tanks = tanksAtPoint.Count(tank => tank.HQ == 1);
            var hq2Tanks = tanksAtPoint.Count(tank => tank.HQ == 2);

            if (capturePoint.Tanks[1] != hq1Tanks || capturePoint.Tanks[2] != hq2Tanks)
            {
                Console.WriteLine($"Reconciling tank counts for capture point {capturePoint.Id}");
                Console.WriteLine($"Before: HQ1: {capturePoint.Tanks[1]}, HQ2: {capturePoint.Tanks[2]}");
                Console.WriteLine($"After: HQ1: {hq1Tanks}, HQ2: {hq2Tanks}");
                capturePoint.Tanks[1] = hq1Tanks;
                capturePoint.Tanks[2] = hq2Tanks;
            }
        }
    }
}