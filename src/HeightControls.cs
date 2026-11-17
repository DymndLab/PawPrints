using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static int heightViewIndex, heightGap = 90, heightControlsView = -1;
    static HeightDimension? heightControlsValue;
    static HeightDimension? dragHeight;
    static float heightGrabOffset;


    static void QueueHeight(HeightDimension? dimension, int viewId)
    {
        if (ControlsLocked || sheet == null || document == null)
            return;
        var current = sheet;
        var labels = document.Labels.Select(l => l with { ManualPlacement = true }).ToArray();
        var dimensions = document.Scene.Dimensions.Where(d => d.ViewId != viewId).ToList();
        if (dimension != null)
            dimensions.Add(dimension);
        var scene = document.Scene.WithDimensions(dimensions.ToArray());
        cancellation?.Dispose();
        cancellation = new();
        var token = cancellation.Token;
        renderKeepsDocument = true;
        composingLabels = true;
        progress = "Updating craft height";
        renderTask = Task.Run(() => AnnotationPainting.Compose(current with { Scene = scene }, labels, token) with { Notice = "Height dimension updated. Ctrl+Z undoes this change." }, token);
    }


    static void DrawHeightControls(bool collapsible = true)
    {
        if (collapsible && !ImGui.CollapsingHeader("Dimensions - craft height"u8))
            return;
        if (sheet?.Scene == null || document == null)
        {
            PrintUiText.Wrapped("Generate a PawPrint first, then add a height beside a view.");
            return;
        }
        var views = sheet.Scene.Views.Where(HeightDimensions.Eligible).ToArray();
        if (views.Length == 0)
        {
            PrintUiText.Wrapped("Add a front, side or isometric view. A top view looks down the height axis.");
            return;
        }
        ImGui.BeginDisabled(ControlsLocked || !PrintIsCurrent);
        try
        {
            heightViewIndex = Math.Clamp(heightViewIndex, 0, views.Length - 1);
            string[] names = views.Select(v => v.Name + (v.Id >= 100 ? $" (box {v.Id - 99})" : "")).ToArray();
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##Height view"u8, ref heightViewIndex, names, names.Length);
            int viewId = views[heightViewIndex].Id;
            var dimension = document.Scene.Dimensions.FirstOrDefault(d => d.ViewId == viewId);
            if (heightControlsView != viewId || heightControlsValue != dimension)
            {
                heightControlsView = viewId;
                heightControlsValue = dimension;
                heightGap = dimension?.Gap ?? 90;
            }
            ImGui.SetNextItemWidth(-1);
            ImGui.SliderInt("##Height gap"u8, ref heightGap, Math.Min(-300, heightGap), Math.Max(600, heightGap), "Gap: %d px"u8);
            bool exists = dimension != null;
            if (ImGui.Button(exists ? "Update height" : "Add height"))
            {
                var proposed = (dimension ?? new(viewId)) with
                {
                    Gap = heightGap,
                    X = null
                };
                var mark = HeightDimensions.Place(document.Scene, proposed, false);
                var located = HeightDimensions.Move(document.Scene, proposed, mark.X);
                if (exists)
                    QueueHeight(located, viewId);
                else
                    BeginHeight(located);
            }
            if (exists)
            {
                ImGui.SameLine();
                if (ImGui.Button("Remove height"u8))
                    QueueHeight(null, viewId);
            }
            PrintUiText.Wrapped("Drag the number or bracket left/right, including between views. Ticks stay aligned to this view's actual craft height. Gap fine-tunes spacing on its current side. Ctrl+Z / Ctrl+Y undo or redo.");
        }
        finally { ImGui.EndDisabled(); }
    }


    static bool DrawHeightCanvas(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (!editable)
        {
            dragHeight = null;
            return false;
        }
        var scene = document!.Scene;
        float2 Screen(float x, float y) => origin + new float2(x * factor, y * factor);
        void DrawMark(HeightMark mark, uint color)
        {
            var draw = ImGui.GetWindowDrawList();
            draw.AddLine(Screen(mark.X, mark.Top), Screen(mark.X, mark.TextBounds.Y - 10), color, 1.5f);
            draw.AddLine(Screen(mark.X, mark.TextBounds.Bottom + 10), Screen(mark.X, mark.Bottom), color, 1.5f);
            draw.AddLine(Screen(mark.X - 22, mark.Top), Screen(mark.X + 22, mark.Top), color, 1.5f);
            draw.AddLine(Screen(mark.X - 22, mark.Bottom), Screen(mark.X + 22, mark.Bottom), color, 1.5f);
            draw.AddText(ImGui.GetFont(), HeightDimensions.Font * factor, Screen(mark.TextBounds.X, mark.TextBounds.Y), color, mark.Text);
        }
        if (dragHeight is { } dragging)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                dragHeight = null;
                return true;
            }
            var proposed = HeightDimensions.Move(scene, dragging, point.X + heightGrabOffset);
            var mark = HeightDimensions.Place(scene, proposed);
            bool clear = !scene.Specs.Any(b => b.Bounds.Overlaps(mark.Bounds)) && !scene.Details.Any(d => d.Definition.Bounds.Overlaps(mark.Bounds)) && !PrintTables.Panels(scene, document.Labels).Any(t => t.Bounds.Overlaps(mark.Bounds));
            DrawMark(mark, clear ? 0xFF8EDD8E : 0xFF7777FF);
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                dragHeight = null;
                if (clear)
                {
                    heightGap = proposed.Gap;
                    if (Math.Abs(mark.X - HeightDimensions.Place(scene, dragging).X) > .01f)
                        QueueHeight(proposed, proposed.ViewId);
                }
                else
                    ReportFailure("Height unchanged: place it clear of tables and exploded details.");
            }
            return true;
        }
        if (!hovered || ControlsLocked || partMenuOpen)
            return false;
        var hit = scene.Dimensions.LastOrDefault(d => HeightDimensions.Hit(HeightDimensions.Place(scene, d), point, 7 / factor));
        if (hit == null)
            return false;
        var current = HeightDimensions.Place(scene, hit);
        DrawMark(current, 0xFFFFDD99);
        ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);
        PrintUiText.Tooltip("Drag height left/right - " + scene.Views.Single(v => v.Id == hit.ViewId).Name);
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            dragHeight = hit;
            heightGrabOffset = current.X - point.X;
            SelectLabel(-1);
            heightViewIndex = Array.FindIndex(scene.Views.Where(HeightDimensions.Eligible).ToArray(), v => v.Id == hit.ViewId);
            heightControlsView = hit.ViewId;
            heightGap = hit.Gap;
        }
        return true;
    }
}
