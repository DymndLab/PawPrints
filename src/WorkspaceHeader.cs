using Brutal.ImGuiApi;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static void CloseExport()
    {
        ClearSetupGesture();
        ReleaseSetupSketch();
        ResetFinalExport();
        cancellation?.Cancel();
        if (setupStage != SetupStage.Editing)
        {
            DiscardDrawingDraft();
            setupStage = sheet == null ? SetupStage.Choose : SetupStage.Editing;
        }
        open = false;
        viewSizeDialog = false;
        exportSetupOpen = false;
        projectsToOpen = null;
        CancelPlacement();
    }

    static void DrawWorkspaceHeader(VehicleEditor editor)
    {
        ImGui.Text("PawPrints");
        string[] actions = ["Generate PawPrint", "Save PNG", "View exported drawings", PrintUiStyle.ReturnToVehicleLabel];
        float width = actions.Concat(new[] { "New drawing...", "Save Drawing", "Open Drawing..." }).Sum(label => ImGui.CalcTextSize(label).X + 28);
        if (ImGui.GetContentRegionAvail().X > width + 120)
        {
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, ImGui.GetContentRegionAvail().X - width));
        }
        // Wrap actions on small windows instead of pushing buttons off-screen.
        void Next(string label)
        {
            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize(label).X + 24)
                ImGui.NewLine();
        }
        ImGui.BeginDisabled(ControlsLocked);
        DrawGenerateButton(editor, "Header");
        Next(actions[1]);
        string? exportBlock = ExportRefreshBlock;
        ImGui.BeginDisabled(sheet == null || !PrintIsCurrent || exportBlock != null);
        if (PrintUiStyle.PrimaryButton("Save PNG"))
            Save();
        PrintUiStyle.ShowUnavailableCursor(ControlsLocked || sheet == null || !PrintIsCurrent || exportBlock != null);
        if (exportBlock != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            PrintUiText.Tooltip(exportBlock);
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        Next("Save Drawing");
        ImGui.BeginDisabled(ControlsLocked || sheet == null || !PrintIsCurrent || exportBlock != null);
        if (ImGui.Button("Save Drawing")) SaveDrawingProject();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            PrintUiText.Tooltip(exportBlock ?? "Save the editable drawing in this profile.");
        Next("Open Drawing...");
        ImGui.BeginDisabled(ControlsLocked);
        if (ImGui.Button("Open Drawing...")) RequestOpenDrawing();
        ImGui.EndDisabled();
        Next("New drawing...");
        ImGui.BeginDisabled(ControlsLocked);
        if (ImGui.Button("New drawing..."))
            RequestNewDrawing();
        PrintUiStyle.ShowUnavailableCursor(ControlsLocked);
        ImGui.EndDisabled();
        Next(actions[2]);
        ImGui.BeginDisabled(ControlsLocked || exportFolder == null);
        if (PrintUiStyle.YellowButton("View exported drawings"))
            OpenExportFolder();
        PrintUiStyle.ShowUnavailableCursor(ControlsLocked || exportFolder == null);
        if (ImGui.IsItemHovered())
            PrintUiText.Tooltip("Open the folder containing your exported PNG drawings.");
        ImGui.EndDisabled();
        Next(actions[3]);
        if (PrintUiStyle.ReturnToVehicleButton(textDraft != null || pendingLabel != null || flagnotePlacement != null || GhostActive || DetailGesture))
            CloseExport();
        if (renderTask != null && !composingLabels)
        {
            Next("Cancel render");
            if (ImGui.Button("Cancel render")) cancellation?.Cancel();
        }
        if (saveTask != null)
        {
            Next("Cancel export");
            if (ImGui.Button("Cancel export")) exportCancel?.Cancel();
            ImGui.SameLine();
            ImGui.Text(exportProgress?.Latest.Label ?? "Preparing PNG");
        }
        if (DrawingHasUnsavedChanges) PrintUiText.Wrapped("Unsaved drawing");
    }
}
