using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static int captionViewId, captionSize = ViewCaption.DefaultSize;


    static void ResizeCaption(int id, int size)
    {
        var current = sheet!;
        var scene = current.Scene!;
        var captions = scene.Captions.Select(c => c.ViewId == id ? ViewCaption.Resize(c, scene.Views.Single(v => v.Id == c.ViewId), size) : c).ToArray();
        var labels = document!.Labels.Select(l => l with { ManualPlacement = true }).ToArray();
        var tables = document.Tables;
        renderKeepsDocument = true;
        renderAdoptsHistory = false;
        composingLabels = true;
        progress = "Updating view caption";
        StartRender(token => AnnotationPainting.Compose(current with { Scene = scene.WithCaptions(captions) }, labels, token, tables) with { Notice = "View caption updated. Ctrl+Z undoes the size change." });
    }


    static bool DrawCaptionMenu(Vec point, bool hovered, bool editable)
    {
        if (editable && hovered && !ControlsLocked && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            var mark = sheet!.Scene!.Captions.FirstOrDefault(c => c.Bounds.Contains(point) && canvasTarget?.Kind == DrawingObjectKind.View && canvasTarget?.Id == c.ViewId);
            if (mark != null)
            {
                captionViewId = mark.ViewId;
                captionSize = mark.FontSize;
                ImGui.OpenPopup("View caption"u8);
            }
        }
        if (!ImGui.BeginPopup("View caption"u8))
            return false;
        try
        {
            partMenuOpen = true;
            var mark = sheet?.Scene?.Captions.FirstOrDefault(c => c.ViewId == captionViewId);
            if (!editable || mark == null)
            {
                ImGui.CloseCurrentPopup();
                return true;
            }
            if (ImGui.MenuItem("Adjust Scale"u8))
            {
                BeginViewSize(captionViewId);
                ImGui.CloseCurrentPopup();
            }
            ImGui.Text(mark.Text);
            ImGui.Text("Font size");
            ImGui.SetNextItemWidth(240);
            ImGui.SliderInt("##Caption size"u8, ref captionSize, 20, ViewCaption.MaxSize, "%d px"u8);
            if (ImGui.Button("Apply size"u8))
            {
                ResizeCaption(captionViewId, captionSize);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"u8))
                ImGui.CloseCurrentPopup();
        }
        finally { ImGui.EndPopup(); }
        return true;
    }
}
