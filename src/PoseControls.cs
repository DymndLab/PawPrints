using Blueprint.Rendering;
using Brutal.ImGuiApi;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool poseDialog;
    static bool poseKeepSymmetry = true;
    static bool poseUpdating;
    internal static bool PosePopupDrawn
    {
        get; private set;
    }
    static PoseControl[] poseChoices = [];
    static int poseIndex, poseScope, poseView, posePercent;


    static PoseControl[] PoseChoices(uint? node, int part)
    {
        var source = sheet?.PoseSource;
        if (source == null || document == null)
            return [];
        var owner = document.Scene.Parts.FirstOrDefault(p => p.Id == part)?.NodeId;
        uint? seed = node ?? owner;
        if (seed == null)
            return [];
        var ancestors = AssemblyGraph.Ancestors(source.Assembly, seed.Value).ToHashSet();
        return source.Controls.Where(c => ancestors.Contains(c.Part) || c.Part == owner).ToArray();
    }


    static void BeginPose(uint? node, int part, int view)
    {
        poseChoices = PoseChoices(node, part);
        if (poseChoices.Length == 0)
            return;
        poseIndex = 0;
        poseScope = 0;
        poseView = view;
        poseKeepSymmetry = true;
        poseDialog = true;
        poseUpdating = false;
        PosePopupDrawn = false;
        ReadPoseValue();
    }


    static void ReadPoseValue()
    {
        var c = poseChoices[poseIndex];
        posePercent = PartPoses.Value(document!.Scene.Poses, c, poseScope == 0 ? -1 : poseView) ?? c.CapturedPercent;
        if (posePercent < 0)
            posePercent = c.CapturedPercent;
    }


    static void QueuePose(PartPose[] poses)
    {
        if (busy || sheet == null || document == null)
            return;
        var current = sheet with
        {
            Scene = document.Scene,
            Labels = document.Labels,
            Details = document.Details
        };
        renderKeepsDocument = true;
        renderAdoptsHistory = false;
        composingLabels = true;
        progress = "Drawing part pose";
        StartRender(token => PosePainting.Rebuild(current, poses, token));
    }


    static void DrawPoseDialog()
    {
        if (poseDialog && !ImGui.IsPopupOpen("Part pose"u8))
            ImGui.OpenPopup("Part pose"u8);
        if (!ImGui.BeginPopupModal("Part pose"u8, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            PosePopupDrawn = true;
            partMenuOpen = true;
            if (!poseDialog || document == null || sheet?.Options != Options)
            {
                poseDialog = false;
                ImGui.CloseCurrentPopup();
                return;
            }
            if (poseUpdating && !busy)
            {
                poseUpdating = false;
                ReadPoseValue();
            }
            PartPose[]? requested = null;
            ImGui.BeginDisabled(busy);
            try
            {
                if (poseChoices.Length > 1)
                {
                    ImGui.SetNextItemWidth(480);
                    if (ImGui.Combo("Animation"u8, ref poseIndex, poseChoices.Select((c, i) => $"{c.Name} / {i + 1}").ToArray(), poseChoices.Length))
                        ReadPoseValue();
                }
                var control = poseChoices[poseIndex];
                ImGui.Text(control.Name);
                int linked = PartPoses.Linked(sheet!.PoseSource!, control, true).Length;
                ImGui.BeginDisabled(linked < 2);
                ImGui.Checkbox("Keep symmetry", ref poseKeepSymmetry);
                ImGui.EndDisabled();
                ImGui.Text(poseKeepSymmetry && linked > 1 ? $"Affects {linked} linked parts." : "Affects only this part.");
                if (ImGui.RadioButton("All views"u8, poseScope == 0))
                {
                    poseScope = 0;
                    ReadPoseValue();
                }
                ImGui.SameLine();
                if (ImGui.RadioButton("This view only"u8, poseScope == 1))
                {
                    poseScope = 1;
                    ReadPoseValue();
                }
                ImGui.Text(poseScope == 0 ? "Sets all main views and details; clears this part's individual overrides." : "Only " + document.Scene.Views.Single(v => v.Id == poseView).Name + " changes.");
                bool update = false;
                if (ImGui.Button(control.DeployRetract ? "Retracted"u8 : "Closed / start"u8))
                {
                    posePercent = 0;
                    update = true;
                }
                ImGui.SameLine();
                if (ImGui.Button(control.DeployRetract ? "Deployed"u8 : "Open / end"u8))
                {
                    posePercent = 100;
                    update = true;
                }
                ImGui.SetNextItemWidth(480);
                ImGui.SliderInt("Position"u8, ref posePercent, 0, 100, "%d%%"u8);
                update |= ImGui.IsItemDeactivatedAfterEdit();
                ImGui.Text("Print pose only. Your craft in the Vehicle Editor stays unchanged.");
                int scope = poseScope == 0 ? -1 : poseView;
                PartPose[] Change(int? percent) => PartPoses.SetLinked(sheet!.PoseSource!, document!.Scene.Poses, control, scope, percent, poseKeepSymmetry);
                if (update)
                    requested = Change(posePercent);
                if (ImGui.Button("Reset to captured"u8))
                    requested = Change(poseScope == 0 ? null : -1);
                if (poseScope == 1)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Use shared pose"u8))
                        requested = Change(null);
                }
            }
            finally { ImGui.EndDisabled(); }
            if (requested != null && !requested.SequenceEqual(document.Scene.Poses))
            {
                QueuePose(requested);
                poseUpdating = true;
            }
            ImGui.Text(busy ? "Updating pose preview..." : "Release the slider to update. Ctrl+Z can undo changes after closing.");
            if (!busy && status.StartsWith("Preview failed:", StringComparison.Ordinal))
                PrintUiStyle.Warning(status);
            if (ImGui.Button("Done"u8))
            {
                poseDialog = false;
                ImGui.CloseCurrentPopup();
            }
        }
        finally { DrawWarningDialog(true); ImGui.EndPopup(); }
    }
}
