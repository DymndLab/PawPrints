using System.Numerics;
namespace Blueprint.Rendering;

enum GroupResizeMode
{
    Arrangement, Sizes, Spacing
}

// A gesture always evaluates its opening snapshot. Modifiers are latched at
// pointer-down (Shift wins over Alt); changing them cannot introduce a jump.
static class ProjectionGroups
{
    public static int Root(DrawingPlacement drawing, int id)
    {
        var seen = new HashSet<int>();
        while (true)
        {
            if (!seen.Add(id))
                throw new InvalidDataException("Cyclic projection group.");
            var view = drawing.Views.Single(v => v.Id == id);
            if (view.Parent is not { } parent)
                return id;
            id = parent;
        }
    }

    public static HashSet<int> Members(DrawingPlacement drawing, int id)
    {
        int root = Root(drawing, id);
        return drawing.Views.Where(v => Root(drawing, v.Id) == root).Select(v => v.Id).ToHashSet();
    }

    public static float Ratio(DrawingPlacement drawing, int id) =>
        drawing.Views.Single(v => v.Id == Root(drawing, id)).Ratio ?? drawing.Ratio;

    public static DrawingPlacement SetRatio(DrawingPlacement drawing, int id, float ratio)
    {
        if (!float.IsFinite(ratio) || ratio < .01f || ratio > 1000000)
            throw new InvalidOperationException("Choose a scale between 1:0.01 and 1:1000000.");
        int root = Root(drawing, id);
        return drawing with
        {
            Views = drawing.Views.Select(v => v.Id == root ? v with { Ratio = ratio } : v).ToArray()
        };
    }

    public static Box Bounds(IEnumerable<Box> boxes)
    {
        var all = boxes.ToArray();
        return new(all.Min(b => b.X), all.Min(b => b.Y), all.Max(b => b.Right) - all.Min(b => b.X), all.Max(b => b.Bottom) - all.Min(b => b.Y));
    }

    public static DrawingPlacement Resize(DrawingPlacement drawing, int id, IReadOnlyDictionary<int, Box> geometry,
        PrintOptions options, float factor, GroupResizeMode mode, Vector2 anchor, IReadOnlyDictionary<int, Vector2>? extents = null)
    {
        if (!float.IsFinite(factor) || factor <= 0)
            throw new InvalidOperationException("Invalid resize factor.");
        var members = Members(drawing, id);
        var oldBounds = Bounds(members.Select(v => geometry[v]));
        if (mode == GroupResizeMode.Spacing && members.Count == 1)
            return drawing;
        var changed = mode == GroupResizeMode.Spacing ? drawing : SetRatio(drawing, id, Ratio(drawing, id) / factor);
        var page = options.Page;
        var moved = changed.Views.Select(v =>
        {
            if (!members.Contains(v.Id) || mode == GroupResizeMode.Sizes)
                return v;
            var center = new Vector2(v.X * page.Width, v.Y * page.Height);
            center = anchor + (center - anchor) * factor;
            return v with
            {
                X = center.X / page.Width,
                Y = center.Y / page.Height
            };
        }).ToArray();
        if (mode != GroupResizeMode.Sizes)
        {
            // Correct the outer bounds, including fixed capture padding and
            // minimum tile dimensions. Spacing-only keeps view sizes fixed.
            var bounds = Bounds(moved.Where(v => members.Contains(v.Id)).Select(v =>
            {
                var b = geometry[v.Id];
                var extent = extents?.GetValueOrDefault(v.Id) ?? new Vector2(b.Width - 32, b.Height - 32);
                float w = mode == GroupResizeMode.Spacing ? b.Width : Math.Max(100, MathF.Ceiling(extent.X * factor) + 32);
                float h = mode == GroupResizeMode.Spacing ? b.Height : Math.Max(100, MathF.Ceiling(extent.Y * factor) + 32);
                return new Box(v.X * page.Width - w / 2, v.Y * page.Height - h / 2, w, h);
            }));
            var fraction = new Vector2(oldBounds.Width == 0 ? .5f : (anchor.X - oldBounds.X) / oldBounds.Width,
                oldBounds.Height == 0 ? .5f : (anchor.Y - oldBounds.Y) / oldBounds.Height);
            var correction = anchor - new Vector2(bounds.X + bounds.Width * fraction.X, bounds.Y + bounds.Height * fraction.Y);
            moved = moved.Select(v => members.Contains(v.Id) ? v with { X = v.X + correction.X / page.Width, Y = v.Y + correction.Y / page.Height } : v).ToArray();
        }
        return changed with
        {
            Views = moved
        };
    }

    public static DrawingPlacement Delete(DrawingPlacement drawing, int id, bool deleteProjected)
    {
        var affected = DrawingPlacements.Dependents(drawing, id);
        return drawing with
        {
            Views = drawing.Views.Where(v => v.Id != id && (!deleteProjected || !affected.Contains(v.Id)))
            .Select(v => affected.Contains(v.Id) ? v with { Parent = null, Alignment = 0, Ratio = Ratio(drawing, v.Id) } : v).ToArray()
        };
    }
}
