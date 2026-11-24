using Brutal.ImGuiApi;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool titleBlockVisible = true;

    static void DrawPreferencesControls()
    {
        if (ImGui.Checkbox("Live preview updates", ref livePreviewUpdates))
        {
            livePreviewGate.Reset();
            SaveTitlePreferences();
        }
        PrintUiStyle.Help("When on, the PawPrint preview updates as settings change. When off, click Generate PawPrint to apply layout and preference changes.");
        ImGui.BeginDisabled(ControlsLocked);
        if (ImGui.Checkbox("Show title block", ref titleBlockVisible))
        {
            SaveTitlePreferences();
            RefreshTitleVisibility();
        }
        ImGui.EndDisabled();
        ImGui.Separator();
        ImGui.Text("Projection");
        ImGui.BeginDisabled(busy);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Projection angle"u8, ref projectionStyle, new[] { "Third-angle", "First-angle" }, 2) && viewDraft?.RollDirection >= 0)
            PrepareView();
        ImGui.EndDisabled();
        PrintUiStyle.Help("Default convention for newly projected views. Existing views stay in place.");
        ImGui.Separator();
        ImGui.Checkbox("Snapping", ref snapPanels);
        PrintUiStyle.Help("Align nearby captions, frames, tables, views and text-box edges during placement.");
        ImGui.Separator();
        ImGui.BeginDisabled(ControlsLocked);
        ImGui.Text("Leader and assembly guide lines");
        int printLine = defaultLine;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Print leader"u8, ref printLine, LineNames, LineNames.Length))
            ApplyPrintStyle(printLine, defaultTip);
        PrintUiStyle.Help("Applies to note leaders, performance leaders and exploded assembly guides. Assembly guides stay subdued and have no endpoint symbol.");
        ImGui.Text("Endpoints");
        int printTip = defaultTip;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Print tip"u8, ref printTip, TipNames, TipNames.Length))
            ApplyPrintStyle(defaultLine, printTip);
        ImGui.EndDisabled();

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
