using Blueprint.Rendering;
using Brutal.ImGuiApi;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool titleBlockVisible = true;
    static int CurrentProjectionAngle => projectionStyle == 0 ? 3 : 1;

    static void ApplyProjectionStyle(int style)
    {
        projectionStyle = style;
        if (document?.Scene.Drawing is { } drawing)
        {
            var updated = DrawingPlacements.Reproject(drawing, document.Scene.Options.Page, CurrentProjectionAngle);
            if (updated != drawing)
            {
                QueueDrawing(updated);
                return;
            }
        }
        RefreshTitleBlock();
    }

    static void DrawPreferencesControls()
    {
        if (ImGui.Checkbox("Live preview updates", ref livePreviewUpdates))
        {
            livePreviewGate.Reset();
            SaveProfileSettings(profileDefaults with { LivePreviewUpdates = livePreviewUpdates });
        }
        PrintUiStyle.Help("When on, the PawPrint preview updates as settings change. When off, click Generate PawPrint to apply layout and preference changes.");
        ImGui.BeginDisabled(ControlsLocked);
        if (ImGui.Checkbox("Show title block", ref titleBlockVisible))
            RefreshTitleBlock();
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.BeginDisabled(ControlsLocked || !FinalPng.Is4K(Options.OutputPage));
        if (ImGui.Checkbox("Detailed export", ref detailedExport))
            SaveProfileSettings(profileDefaults with { DetailedExport = detailedExport });
        ImGui.EndDisabled();
        PrintUiStyle.Help("At 4K, retain fine surface lines and shallower creases in the final PNG. Editing uses Normal. This preference is remembered across output sizes.");
        DrawExportTarget();
        ImGui.BeginDisabled(ControlsLocked || !PrintIsCurrent || ExportRefreshBlock != null);
        if (ImGui.Button("Preview final PNG")) BeginFinalExport(review: true);
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.Text("Projection");
        ImGui.BeginDisabled(ControlsLocked);
        ImGui.SetNextItemWidth(-1);
        int style = projectionStyle;
        if (ImGui.Combo("##Projection angle"u8, ref style, new[] { "Third-angle", "First-angle" }, 2))
            ApplyProjectionStyle(style);
        ImGui.EndDisabled();
        PrintUiStyle.Help("Changes the faces shown in linked projections at their current positions. New layouts arrange the selected faces using this convention.");
        ImGui.Separator();
        if (ImGui.Checkbox("Snapping", ref snapPanels))
            SaveProfileSettings(profileDefaults with { SnapPanels = snapPanels });
        PrintUiStyle.Help("Align nearby captions, frames, tables, views and text-box edges during placement.");
        ImGui.Separator();
        ImGui.BeginDisabled(ControlsLocked);
        ImGui.Text("Leader and assembly guide lines");
        int printLine = defaultLine;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Print leader"u8, ref printLine, LineNames, LineNames.Length))
        {
            ApplyPrintStyle(printLine, defaultTip);
            SaveProfileSettings(profileDefaults with { Leader = (LeaderLine)printLine });
        }
        PrintUiStyle.Help("Applies to note leaders, performance leaders and exploded assembly guides. Assembly guides stay subdued and have no endpoint symbol.");
        ImGui.Text("Endpoints");
        int printTip = defaultTip;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Print tip"u8, ref printTip, TipNames, TipNames.Length))
        {
            ApplyPrintStyle(defaultLine, printTip);
            SaveProfileSettings(profileDefaults with { Endpoint = (LeaderTip)printTip });
        }
        ImGui.EndDisabled();
        if (profileMessage != null) PrintUiText.Wrapped(profileMessage);

        ImGui.Separator();
        PrintUiStyle.SectionHeader("Hotkeys");
        Shortcut("Open/Close PawPrints", "Ctrl+Alt+P");
        Shortcut("Pan drawing", "Space + drag");
        Shortcut("Zoom (On cursor)", "Mouse wheel");
        Shortcut("Undo", "Ctrl+Z");
        Shortcut("Redo", "Ctrl+Y");
        Shortcut("Flip scale references", "F");
        Shortcut("Delete object", "Delete");
        Shortcut("Object options / cancel placement", "Right-click");
        Shortcut("Deselect", "Click blank paper");
        Shortcut("Edit text box", "Double-click text");
        Shortcut("Bold (text editor)", "Ctrl+B");
        Shortcut("Italic (text editor)", "Ctrl+I");
        Shortcut("Underline (text editor)", "Ctrl+U");
        Shortcut("Redo (text editor)", "Ctrl+Shift+Z");
        Shortcut("Select all (text editor)", "Ctrl+A");
        Shortcut("Copy (text editor)", "Ctrl+C");
        Shortcut("Cut (text editor)", "Ctrl+X");
        Shortcut("Paste (text editor)", "Ctrl+V");
        ImGui.Separator();
        PrintUiStyle.SectionHeader("Export folder");
        PrintUiText.Wrapped(exportFolder ?? "No folder selected.");
        DrawExportFolderActions();
        DrawActivity();
    }
    static void Shortcut(string action, string keys)
    {
        float right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        PrintUiText.Wrapped(action + " -");
        // Keep the key beside the action when it fits; otherwise wrap it below.
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(keys).X <= right)
            ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, KSA.ConsoleStyle.Pending);
        try { PrintUiText.Wrapped(keys); }
        finally { ImGui.PopStyleColor(); }
    }

}
