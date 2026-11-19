using RenderCore.Input;
using HarmonyLib;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    // Native settings, popups, console and ImGui receive keys in OnKey before
    // this dispatcher. Guard the game action here rather than swallowing text
    // input, or depending on the previous frame's ImGui keyboard focus.
    [HarmonyPatch(typeof(KSA.Program), "DispatchKeyEvent")]
    static class VehicleSwitchPatch
    {
        static bool Prefix(in GlfwKeyEvent keyEvent, ref bool __result)
        {
            if (!enabled || !open || KSA.Program.Editor == null)
                return true;
            // Match the configured actions, including rebound keys/modifiers.
            if (!Input.MatchPressed(in keyEvent, InputAction.VesselNext)
                && !Input.MatchPressed(in keyEvent, InputAction.VesselPrevious))
                return true;
            PrintDiagnostics.Record("input.vehicle-switch.blocked");
            __result = true;
            return false;
        }
    }
}
