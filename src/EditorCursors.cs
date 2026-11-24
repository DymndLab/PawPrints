using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using HarmonyLib;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool precisionCursorRequested, precisionCursorAttempted;
    static GlfwCursor precisionCursor;

    static void RequestPrecisionCursor()
    {
        precisionCursorRequested = true;
        ImGui.SetMouseCursor(ImGuiMouseCursor.Arrow);
    }

    static void ReleasePrecisionCursor()
    {
        precisionCursorRequested = precisionCursorAttempted = false;
        if (!precisionCursor.IsNull())
            precisionCursor.Dispose();
        precisionCursor = default;
    }

    // The hash-guarded host owns cursor mode, viewport routing and arrow fallback.
    // ImGui has no crosshair slot, so lend this update the system's precision cursor.
    [HarmonyPatch(typeof(KSA.ImGuiBackendGlfwImpl), "UpdateMouseCursor")]
    static class PrecisionCursorPatch
    {
        static void Prefix(GlfwCursor[] ____mouseCursors, out GlfwCursor? __state)
        {
            __state = null;
            bool requested = precisionCursorRequested;
            precisionCursorRequested = false;
            if (!requested || !open || !placingText || ImGui.GetMouseCursor() != ImGuiMouseCursor.Arrow)
                return;
            if (!precisionCursorAttempted)
            {
                precisionCursorAttempted = true;
                try { precisionCursor = Glfw.CreateCursor(GlfwStandardCursor.Crosshair); }
                catch (Exception e) { PrintDiagnostics.Error("cursor.precision", e); }
            }
            if (precisionCursor.IsNull()) return;
            int arrow = (int)ImGuiMouseCursor.Arrow;
            __state = ____mouseCursors[arrow];
            ____mouseCursors[arrow] = precisionCursor;
        }

        static void Finalizer(GlfwCursor[] ____mouseCursors, GlfwCursor? __state)
        {
            if (__state is { } original)
                ____mouseCursors[(int)ImGuiMouseCursor.Arrow] = original;
        }
    }
}
