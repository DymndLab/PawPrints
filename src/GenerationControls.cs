using Blueprint.Rendering;
using Brutal.ImGuiApi;
using KSA;
namespace Blueprint;

public sealed partial class BlueprintMod
{


    static string? GenerationBlock(VehicleEditor editor, bool startFresh = false) => GenerationAvailability.Reason(
        editor.EditingSpace.Parts is { } tree && tree.Parts.Length > 0, busy, ControlsLocked && !busy,
        (!startFresh && Options.Drawing != null) || FixedViewLayouts.Cameras(Options).Length > 0);


    static void TryGenerate(VehicleEditor editor, bool startFresh = false)
    {
        var reason = GenerationBlock(editor, startFresh);
        if (reason != null)
        {
            ReportFailure(reason);
            return;
        }
        Generate(editor, startFresh);
    }


    static void DrawGenerateButton(VehicleEditor editor, string id)
    {
        var reason = GenerationBlock(editor);
        ImGui.BeginDisabled(reason != null);
        if (PrintUiStyle.YellowButton("Generate PawPrint##" + id))
            TryGenerate(editor);
        ImGui.EndDisabled();
        PrintUiStyle.Help(reason ?? "Capture the craft and apply print settings. Existing notes and detail frames are kept.");
    }
}
