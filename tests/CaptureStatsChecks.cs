using Blueprint;

static class CaptureStatsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var stats = new CaptureStats();
        stats.Add("panel", 1, false, 100, 50);
        stats.Add("panel", 2, true, 100, 50);
        stats.Log(TimeSpan.Zero);
        bool rejected = false;
        try { stats.Add("dense", 3, false, 1, 10_000_000); }
        catch (InvalidOperationException e) { rejected = e.Message.Contains("limit:") && stats.Failure == "geometry-estimated-bytes"; }
        check(rejected, "capture rejects triangle-heavy geometry with a measured budget warning");
    }
}
