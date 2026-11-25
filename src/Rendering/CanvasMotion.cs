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
        var positioned = drawing.Views.Select(v => ids.Contains(v.Id)
            ? v with
            {
                X = v.X + delta.X / options.Page.Width,
                Y = v.Y + delta.Y / options.Page.Height
            } : v).ToArray();
        var updated = positioned.ToDictionary(v => v.Id);
        var original = drawing.Views.ToDictionary(v => v.Id);
        void Reorient(int viewId)
        {
            var previous = original[viewId];
            var view = updated[viewId];
            if (view.Parent is { } parentId)
            {
                var beforeParent = original[parentId];
                var parent = updated[parentId];
                var before = Relative(previous, beforeParent, options.Page);
                var after = Relative(view, parent, options.Page);
                int oldDirection = previous.RollDirection >= 0 ? previous.RollDirection : Direction(previous, before, -1);
                int newDirection = Direction(previous, after, oldDirection);

                if (newDirection >= 0 && (oldDirection != newDirection ||
                    DrawingPlacements.Axes(beforeParent) != DrawingPlacements.Axes(parent)))
                {
                    var rolled = DrawingPlacements.Roll(view, parent, newDirection, previous.ProjectionAngle);
                    updated[viewId] = rolled with { Alignment = previous.Alignment };
                }
            }

            foreach (var child in drawing.Views.Where(v => v.Parent == viewId))
                Reorient(child.Id);
        }

        Reorient(id);
        return drawing with { Views = positioned.Select(v => updated[v.Id]).ToArray() };
    }

    static Vector2 Relative(DrawingView view, DrawingView parent, PageSize page) =>
        new((view.X - parent.X) * page.Width, (view.Y - parent.Y) * page.Height);

    static int Direction(DrawingView view, Vector2 delta, int previous)
    {
        const float centerBand = .5f;
        if (view.RollDirection >= 4)
        {
            bool right = Math.Abs(delta.X) <= centerBand ? previous is 4 or 7 : delta.X > 0;
            bool up = Math.Abs(delta.Y) <= centerBand ? previous is 4 or 5 : delta.Y < 0;
            return up ? right ? 4 : 5 : right ? 7 : 6;
        }

        if (view.Alignment == 2 || view.RollDirection is 0 or 2)
            return Math.Abs(delta.X) <= centerBand && previous is 0 or 2 ? previous : delta.X >= 0 ? 0 : 2;
        if (view.Alignment == 1 || view.RollDirection is 1 or 3)
            return Math.Abs(delta.Y) <= centerBand && previous is 1 or 3 ? previous : delta.Y <= 0 ? 1 : 3;
        return -1;
    }
}
