using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static DrawingObject? canvasTarget, listedObject;
    static int pendingPrimaryDelete = -1;
    static bool groupCenterAnchor;
    static void ReportCanvasClipping(Sheet? previous, Sheet result)
    {
        if (result.Scene is not { } scene || !scene.Options.FreeLayout)
            return;
        try
        {
            var before = previous?.Scene is { } old ? CanvasObjects.All(old, previous.Labels).ToDictionary(o => o, o => CanvasObjects.ContentBounds(old, previous.Labels, o)) : new Dictionary<DrawingObject, Box>();
            if (CanvasObjects.All(scene, result.Labels).Any(o =>
            {
                var bounds = CanvasObjects.ContentBounds(scene, result.Labels, o);
                return (!before.TryGetValue(o, out var oldBounds) || oldBounds != bounds) && CanvasBounds.Clipped(bounds, scene.Options);
            }))
            {
                PrintDiagnostics.Record("layout.clipped");
                AddActivity("layout.clipped", "Part of the drawing is outside the image. Use Objects to reveal it.", notify: true);
            }
        }
        catch (Exception)
        {
            // An advisory notice must never invalidate an accepted render.
            PrintDiagnostics.Record("layout.clipping_notice_failed");
        }
    }
    sealed record GroupDrag(DrawingPlacement Drawing, int View, Dictionary<int, Box> Geometry,
        Vec Anchor, Vec Start, GroupResizeMode Mode, Dictionary<int, Vec> Extents, ImGuiMouseCursor Cursor);
    static GroupDrag? groupDrag;

    static Box? ClearPanelPlacement(Box desired, IEnumerable<Box> obstacles, Box area, bool resize,
        float minWidth, float minHeight, float gap = 0, Func<Box, bool>? fits = null, Box? boundary = null)
        => Options.FreeLayout ? CanvasBounds.Safe(desired) && (fits?.Invoke(desired) ?? true) ? desired : null
            : FrameLayout.Clear(desired, obstacles, area, resize, minWidth, minHeight, gap, fits, boundary);

    static void SelectCanvasObject(DrawingObject? item)
    {
        listedObject = item;
        SelectLabel(-1);
        SelectDetail(-1);
        selectedCanvasView = selectedText = selectedKitten = selectedSpec = selectedTable = selectedHeight = -1;
        if (item is not { } target)
            return;
        switch (target.Kind)
        {
            case DrawingObjectKind.View:
                if (target.Id < 1000)
                    SelectCanvasView(target.Id);
                else
                    SelectDetail(target.Id - 1000);
                break;
            case DrawingObjectKind.Text:
                selectedText = target.Id;
                break;
            case DrawingObjectKind.Label:
                SelectLabel(target.Id);
                break;
            case DrawingObjectKind.Dimension:
                selectedHeight = target.Id;
                break;
            case DrawingObjectKind.Performance:
                selectedSpec = target.Id;
                break;
            case DrawingObjectKind.ScaleReference:
                selectedKitten = target.Id;
                break;
            case DrawingObjectKind.PartsTable:
                selectedTable = 0;
                break;
            case DrawingObjectKind.FlagTable:
                selectedTable = 1;
                break;
        }
    }

    static void OrderCanvasObject(DrawingObject item, int direction)
    {
        var layers = CanvasObjects.All(document!.Scene, document.Labels);
        int from = Array.IndexOf(layers, item), to = Math.Clamp(from + direction, 0, layers.Length - 1);
        (layers[from], layers[to]) = (layers[to], layers[from]);
        var drawing = CurrentDrawing();
        QueueDrawing(drawing with
        {
            Layers = layers,
            Views = drawing.Views.OrderBy(v => Array.IndexOf(layers, new DrawingObject(DrawingObjectKind.View, v.Id))).ToArray()
        });
    }

    static void RevealCanvasObject(DrawingObject item)
    {
        var scene = document!.Scene;
        var box = CanvasObjects.Bounds(scene, document.Labels, item);
        var delta = new Vec(Options.Page.Width / 2, Options.Page.Height / 2) - box.Center;
        Box Move(Box b) => b with
        {
            X = b.X + delta.X,
            Y = b.Y + delta.Y
        };
        switch (item.Kind)
        {
            case DrawingObjectKind.View:
                if (item.Id < 1000)
                    QueueDrawing(DrawingPlacements.Move(CurrentDrawing(), item.Id, box.Center + delta, Options, true));
                else
                    QueueDetail(document.Details.Single(d => d.ViewId == item.Id) with
                    {
                        Bounds = Move(box)
                    });
                break;
            case DrawingObjectKind.Label:
                var label = document.Labels.Single(l => l.Id == item.Id);
                ChangeLabel(label with
                {
                    X = label.X + delta.X,
                    Y = label.Y + delta.Y
                });
                break;
            case DrawingObjectKind.Text:
                QueueTextBoxes(scene.FreeText.Select(t => t.Id == item.Id ? t with { Bounds = Move(t.Bounds) } : t).ToArray());
                break;
            case DrawingObjectKind.Dimension:
                QueueHeight(scene.Dimensions.Single(d => d.ViewId == item.Id) with
                {
                    X = Options.Page.Width / 2
                }, item.Id);
                break;
            case DrawingObjectKind.Performance:
                QueueSpecs(scene.Specs.Select(b => b.Id == item.Id ? b with { Bounds = Move(b.Bounds) } : b).ToArray());
                break;
            case DrawingObjectKind.ScaleReference:
                QueueKittens(scene.Kittens.Select(k => k.Id == item.Id ? k with { X = k.X + delta.X, Y = k.Y + delta.Y } : k).ToArray());
                break;
            default:
                QueueTables(scene.Tables.Set(item.Kind == DrawingObjectKind.PartsTable ? 0 : 1, Move(box)));
                break;
        }
    }

    static void CommitViewDeletion(int id, bool projected)
    {
        var before = CurrentDrawing();
        var after = ProjectionGroups.Delete(before, id, projected);
        selectedCanvasView = -1;
        QueueDrawing(after, removedViews: before.Views.Select(v => v.Id).Except(after.Views.Select(v => v.Id)).ToHashSet());
    }

    static void DrawPrimaryDeletion()
    {
        if (pendingPrimaryDelete < 0)
            return;
        if (!ImGui.IsPopupOpen("Delete primary view"u8))
            ImGui.OpenPopup("Delete primary view"u8);
        CenterDrawingDialog();
        if (!ImGui.BeginPopupModal("Delete primary view"u8, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            int count = DrawingPlacements.Dependents(CurrentDrawing(), pendingPrimaryDelete).Count - 1;
            PrintUiText.Wrapped($"This primary view has {count} projections. What should I do with them?");
            void Finish(bool? delete)
            {
                int id = pendingPrimaryDelete;
                pendingPrimaryDelete = -1;
                ImGui.CloseCurrentPopup();
                if (delete.HasValue)
                    CommitViewDeletion(id, delete.Value);
            }
            if (ImGui.Button("Keep as primary views"))
                Finish(false);
            if (ImGui.Button("Delete projected views"))
                Finish(true);
            if (ImGui.Button("Cancel"))
                Finish(null);
        }
        finally { ImGui.EndPopup(); }
    }

    static void DrawGroupControls()
    {
        if (document == null || !ImGui.CollapsingHeader("Objects and projection groups"))
            return;
        var drawing = CurrentDrawing();
        ImGui.BeginDisabled(ControlsLocked);
        foreach (var item in CanvasObjects.All(document.Scene, document.Labels).Reverse())
        {
            string label = item.Kind == DrawingObjectKind.View && item.Id < 1000
                ? $"{DrawingPlacements.Name(drawing.Views.Single(v => v.Id == item.Id))} #{item.Id} / group {ProjectionGroups.Root(drawing, item.Id)}"
                : $"{item.Kind} #{item.Id}";
            if (ImGui.Selectable(label, listedObject == item))
                SelectCanvasObject(item);
        }
        if (listedObject is { } selected && CanvasObjects.All(document.Scene, document.Labels).Contains(selected))
        {
            if (ImGui.Button("Reveal selected object"))
                RevealCanvasObject(selected);
            if (ImGui.Button("Bring forward"))
                OrderCanvasObject(selected, 1);
            ImGui.SameLine();
            if (ImGui.Button("Send backward"))
                OrderCanvasObject(selected, -1);
        }
        if (selectedCanvasView >= 0 && drawing.Views.Any(v => v.Id == selectedCanvasView))
        {
            int id = selectedCanvasView;
            var members = ProjectionGroups.Members(drawing, id);
            ImGui.Text($"{members.Count} linked view(s), 1:{ProjectionGroups.Ratio(drawing, id):0.##}");
            ImGui.Checkbox("Resize about group center", ref groupCenterAnchor);
            PrintUiText.Wrapped("Drag a corner: sizes and spacing. Shift: sizes only. Alt: spacing only. Modifiers lock at drag start; Shift wins if both are held. Drag ink or a caption to move a view.");
            if (ImGui.Button("Delete selected view"))
                DeleteDrawingView(id);
        }
        ImGui.EndDisabled();
    }

    static bool DrawGroupResize(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (!editable || document == null || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
        {
            groupDrag = null;
            return false;
        }
        var drawing = CurrentDrawing();
        if (groupDrag is { } drag)
        {
            ImGui.SetMouseCursor(drag.Cursor);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                groupDrag = null;
                return true;
            }
            var delta = drag.Start - drag.Anchor;
            float scale = Math.Clamp(Vec.Dot(point - drag.Anchor, delta) / Math.Max(1, delta.LengthSquared()), .05f, 20);
            DrawingPlacement? changed = null;
            try
            {
                changed = ProjectionGroups.Resize(drag.Drawing, drag.View, drag.Geometry, Options, scale, drag.Mode, drag.Anchor, drag.Extents);
                var members = ProjectionGroups.Members(changed, drag.View);
                foreach (var view in changed.Views.Where(v => members.Contains(v.Id)))
                {
                    var box = drag.Geometry[view.Id];
                    float size = drag.Mode == GroupResizeMode.Spacing ? 1 : scale;
                    var extent = drag.Extents[view.Id];
                    float width = drag.Mode == GroupResizeMode.Spacing ? box.Width : Math.Max(100, MathF.Ceiling(extent.X * size) + 32);
                    float height = drag.Mode == GroupResizeMode.Spacing ? box.Height : Math.Max(100, MathF.Ceiling(extent.Y * size) + 32);
                    Outline(new(view.X * Options.Page.Width - width / 2, view.Y * Options.Page.Height - height / 2, width, height));
                }
            }
            catch (InvalidOperationException) { }
            var anchor = Screen(drag.Anchor);
            ImGui.GetWindowDrawList().AddCircleFilled(anchor, 4, 0xFF44DDFF);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                groupDrag = null;
                if (changed != null && Vec.Distance(point, drag.Start) * factor >= 4)
                    QueueDrawing(changed);
            }
            return true;
        }
        if (selectedCanvasView < 0 || !drawing.Views.Any(v => v.Id == selectedCanvasView) || ControlsLocked)
            return false;
        var ids = ProjectionGroups.Members(drawing, selectedCanvasView);
        var geometry = document.Scene.Views.Where(v => ids.Contains(v.Id)).ToDictionary(v => v.Id, v => v.Bounds);
        if (geometry.Count != ids.Count)
            return false;
        var bounds = ProjectionGroups.Bounds(geometry.Values);
        Outline(bounds);
        var primary = drawing.Views.Single(v => v.Id == ProjectionGroups.Root(drawing, selectedCanvasView));
        foreach (var view in drawing.Views.Where(v => ids.Contains(v.Id)))
            ImGui.GetWindowDrawList().AddLine(Screen(new(primary.X * Options.Page.Width, primary.Y * Options.Page.Height)), Screen(new(view.X * Options.Page.Width, view.Y * Options.Page.Height)), 0x6644DDFF, 1);
        Vec[] corners = [new(bounds.X, bounds.Y), new(bounds.Right, bounds.Y), new(bounds.Right, bounds.Bottom), new(bounds.X, bounds.Bottom)];
        for (int i = 0; i < corners.Length; i++)
        {
            var p = Screen(corners[i]);
            ImGui.GetWindowDrawList().AddRectFilled(p - new float2(5), p + new float2(5), 0xFF44DDFF);
            if (hovered && Vec.Distance(point, corners[i]) * factor < 12)
            {
                var cursor = i % 2 == 0 ? ImGuiMouseCursor.ResizeNWSE : ImGuiMouseCursor.ResizeNESW;
                ImGui.SetMouseCursor(cursor);
                if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left)) return false;
                var io = ImGui.GetIO();
                groupDrag = new(drawing, selectedCanvasView, geometry, groupCenterAnchor ? bounds.Center : corners[(i + 2) % 4], point,
                    io.KeyShift ? GroupResizeMode.Sizes : io.KeyAlt ? GroupResizeMode.Spacing : GroupResizeMode.Arrangement,
                    document.Scene.Views.Where(v => ids.Contains(v.Id)).ToDictionary(v => v.Id,
                        v => v.Frame?.ContentExtent ?? new Vec(v.Bounds.Width - 32, v.Bounds.Height - 32)), cursor);
                return true;
            }
        }
        return false;
        float2 Screen(Vec p) => origin + new float2(p.X * factor, p.Y * factor);
        void Outline(Box b) => ImGui.GetWindowDrawList().AddRect(Screen(new(b.X, b.Y)), Screen(new(b.Right, b.Bottom)), 0xAA44DDFF, 0, ImDrawFlags.None, 1);
    }
}
