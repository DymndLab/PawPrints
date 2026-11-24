using System.Numerics;
namespace Blueprint.Rendering;

// Only pointer movement uses this policy. Scale, resize, loaded positions and
// renderer validation remain independent of the page edge.
static class CanvasMotion
{
    public static Vector2 Limit(Box content, Vector2 delta, PageSize page)
    {
        float Position(float start, float size, float step, float extent) =>
            Math.Clamp(start + step, Math.Min(0, extent - size), Math.Max(0, extent - size)) - start;
        return new(Position(content.X, content.Width, delta.X, page.Width),
            Position(content.Y, content.Height, delta.Y, page.Height));
    }
    public static Box KeepOnPage(Box box, PrintOptions options)
    {
        var delta = Limit(box, Vector2.Zero, options.Page);
        return box with
        {
            X = box.X + delta.X,
            Y = box.Y + delta.Y
        };
    }
    public static DrawingPlacement MoveViews(RenderScene scene, PartLabel[] labels, DrawingPlacement drawing,
        int id, Vector2 center, PrintOptions options)
    {
        var old = drawing.Views.Single(v => v.Id == id);
        var moved = DrawingPlacements.Move(drawing, id, center, options);
        var actual = moved.Views.Single(v => v.Id == id);
        var delta = new Vector2((actual.X - old.X) * options.Page.Width, (actual.Y - old.Y) * options.Page.Height);
        var ids = DrawingPlacements.Dependents(drawing, id);
        var boxes = ids.Select(i => CanvasObjects.ContentBounds(scene, labels, new(DrawingObjectKind.View, i))).ToArray();
        var group = ProjectionGroups.Bounds(boxes);
        // Oversized arrangements can only be partially on paper. Keep the grabbed
        // view reachable, preserving the relative positions of its dependents.
        if (group.Width > options.Page.Width || group.Height > options.Page.Height)
            group = CanvasObjects.ContentBounds(scene, labels, new(DrawingObjectKind.View, id));
        var constrained = Limit(group, delta, options.Page);
        // An aligned secondary may already cross the edge after scaling. Its
        // locked axis still belongs to its primary; dragging must not break it.
        if (old.Parent != null && old.Alignment == 1)
            constrained.X = delta.X;
        if (old.Parent != null && old.Alignment == 2)
            constrained.Y = delta.Y;
        delta = constrained;
        return drawing with
        {
            Views = drawing.Views.Select(v => ids.Contains(v.Id)
            ? v with
            {
                X = v.X + delta.X / options.Page.Width,
                Y = v.Y + delta.Y / options.Page.Height
            } : v).ToArray()
        };
    }
}
