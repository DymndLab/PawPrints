using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool newDrawingDialog;

    static void KeepAcceptedPrintEditable()
    {
        if (busy || sheet == null || document == null)
            return;
        AdoptPrintSettings(sheet.Options);
        printStyleDirty = false;
        var label = sheet.Labels.FirstOrDefault();
        var spec = sheet.Scene?.Specs.FirstOrDefault();
        if (label != null || spec != null)
        {
            defaultLine = (int)(label?.Line ?? spec!.Line);
            defaultTip = (int)(label?.Tip ?? spec!.Tip);
        }
        else if (sheet.Details.FirstOrDefault() is { } detail)
            defaultLine = (int)detail.GuideLine;
        draftRatio = drawingPlacement?.Ratio ?? 100;
        templatesOpen = false;
        beforeTemplates = null;
        beforeDrawing = null;
        livePreviewGate.Reset();
        SaveTitlePreferences();

    }

    static void DrawNewDrawingDialog(VehicleEditor editor)
    {
        if (!newDrawingDialog)
            return;
        if (!ImGui.IsPopupOpen("New drawing?"u8))
            ImGui.OpenPopup("New drawing?"u8);
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + ImGui.GetWindowSize() * .5f,
            ImGuiCond.Appearing, new float2(.5f, .5f));
        ImGui.SetNextWindowSize(new float2(520, 0), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal("New drawing?"u8, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            partMenuOpen = true;
            PrintUiText.Wrapped("Fit the current craft to the selected template using the current paper and title settings.");
            PrintUiText.Wrapped("This replaces the editable drawing, including notes, details, poses and undo history. Save its PNG first if you need it. The previous print is kept if generation fails.");
            if (PrintUiStyle.PrimaryButton("Create new drawing"))
            {
                newDrawingDialog = false;
                ImGui.CloseCurrentPopup();
                TryGenerate(editor, startFresh: true);
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                newDrawingDialog = false;
                ImGui.CloseCurrentPopup();
            }
        }
        finally { ImGui.EndPopup(); }
    }
}
