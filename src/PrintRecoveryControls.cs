using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool newDrawingDialog;

    static void CenterDrawingDialog()
    {
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + ImGui.GetWindowSize() * .5f,
            ImGuiCond.Always, new float2(.5f, .5f));
    }

    static void RequestNewDrawing()
    {
        if (sheet == null)
            setupStage = SetupStage.Choose;
        else
            newDrawingDialog = true;
    }

    static void ConfirmNewDrawing()
    {
        CancelPlacement();
        DiscardDrawingDraft();
        setupStage = SetupStage.Choose;
    }

    static void KeepAcceptedPrintEditable()
    {
        if (busy || sheet == null || document == null)
            return;
        RestoreAcceptedDocument();
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

    }

    static void DrawNewDrawingDialog(VehicleEditor editor)
    {
        if (!newDrawingDialog)
            return;
        if (!ImGui.IsPopupOpen("New drawing?"u8))
            ImGui.OpenPopup("New drawing?"u8);
        CenterDrawingDialog();
        ImGui.SetNextWindowSize(new float2(520, 0), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal("New drawing?"u8, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            partMenuOpen = true;
            PrintUiText.Wrapped("Start a new drawing and clear the current one?");
            PrintUiText.Wrapped("You can return to the current drawing until creation succeeds.");
            bool saved = false;
            if (DrawingHasUnsavedChanges)
            {
                ImGui.BeginDisabled(!PrintIsCurrent || ExportRefreshBlock != null);
                if (PrintUiStyle.PrimaryButton("Save and continue")) saved = SaveDrawingProject();
                ImGui.EndDisabled();
                ImGui.SameLine();
            }
            if (ImGui.Button(DrawingHasUnsavedChanges ? "Continue without saving" : "Create new drawing") || saved)
            {
                newDrawingDialog = false;
                ImGui.CloseCurrentPopup();
                ConfirmNewDrawing();
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
