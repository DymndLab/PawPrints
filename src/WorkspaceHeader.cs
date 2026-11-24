using Brutal.ImGuiApi;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static void CloseExport()
    {
        cancellation?.Cancel();
        FlushTitlePreferences();
        open = false;
        viewSizeDialog = false;
        exportSetupOpen = false;
        CancelPlacement();
    }

    static void DrawWorkspaceHeader(VehicleEditor editor)
    {
        ImGui.Text("PawPrints");
        string[] actions = ["Generate PawPrint", "Save PNG", "View exported drawings", "Back to editor"];
        float width = actions.Concat(new[] { "New drawing..." }).Sum(label => ImGui.CalcTextSize(label).X + 28);
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
        ImGui.BeginDisabled(sheet == null || !PrintIsCurrent);
        if (PrintUiStyle.PrimaryButton("Save PNG"))
            Save();
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        Next("New drawing...");
        ImGui.BeginDisabled(ControlsLocked);
        if (ImGui.Button("New drawing..."))
            setupStage = SetupStage.Choose;
        ImGui.EndDisabled();
        Next(actions[2]);
        ImGui.BeginDisabled(ControlsLocked || exportFolder == null);
        if (PrintUiStyle.YellowButton("View exported drawings"))
            OpenExportFolder();
        if (ImGui.IsItemHovered())
            PrintUiText.Tooltip("Open the folder containing your exported PNG drawings.");
        ImGui.EndDisabled();
        Next(actions[3]);
        if (PrintUiStyle.BackButton(textDraft != null || pendingLabel != null || flagnotePlacement != null || GhostActive || DetailGesture))
            CloseExport();
        PrintUiText.Wrapped("Save PNG before loading another craft or leaving the Vehicle Editor. Editable sheets are not saved between sessions.");
    }
}
