using HarmonyLib;
using StarMap.API;

// Separate development mod. Never distributed or enabled in a player's profile.
[StarMapMod]
public sealed class Acceptance
{
    static string? instance;
    Harmony? harmony;

    internal static bool LoadPending(KSA.VehicleEditor editor) =>
        typeof(KSA.VehicleEditor).GetField("_historyCommitPending",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor) is true;

    [StarMapBeforeMain]
    public void Load()
    {
        string? profile = Environment.GetEnvironmentVariable("STARMAP_INSTANCE_PATH");
        if (profile == null || Environment.GetEnvironmentVariable("BLUEPRINT_ACCEPTANCE") != "1")
            return;
        string root = Path.GetFullPath(profile);
        if (!(root.EndsWith("blueprint-runtime\\instance", StringComparison.OrdinalIgnoreCase)
            || root.EndsWith("pawprints-ui-check\\instance", StringComparison.OrdinalIgnoreCase)))
            return;
        instance = root;
        harmony = new("blueprint.acceptance");
        harmony.PatchAll(typeof(Acceptance).Assembly);
    }

    [StarMapUnload]
    public void Unload() => harmony?.UnpatchAll("blueprint.acceptance");

    [HarmonyPatch(typeof(KSA.Program), "OnFrameEditor")]
    static class Frame
    {
        static void Postfix()
        {
            if (instance == null)
                return;
            if (Environment.GetEnvironmentVariable("BLUEPRINT_PERFORMANCE_QA") == "1")
                PerformanceAcceptance.Tick(instance);
            else if (Environment.GetEnvironmentVariable("BLUEPRINT_POSE_QA") == "1")
                PoseAcceptance.Tick(instance);
            else
                DrawingAcceptance.Tick(instance);
        }
    }
}
