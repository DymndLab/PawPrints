namespace Blueprint.Rendering;

sealed record TableLayout(Box? Parts = null, Box? Flagnotes = null)
{


    public TableLayout Set(int id, Box box) => id == 0 ? this with { Parts = box } : this with { Flagnotes = box };
}
static class FrameLayout
{
    public const float Gap = 24;


    public static Box Area(PrintOptions options) => PageLayout.Placement(options);


    public static Box Clamp(Box box, PrintOptions options, float minWidth = 200, float minHeight = 100)
    {
        var area = Area(options);
        if (options.FreeLayout)
        {
            CanvasBounds.Require(box);
            return new(MathF.Round(box.X), MathF.Round(box.Y), Math.Clamp(box.Width, minWidth, 8192), Math.Clamp(box.Height, minHeight, 8192));
        }
        float w = Math.Clamp(box.Width, Math.Min(minWidth, area.Width), area.Width), h = Math.Clamp(box.Height, Math.Min(minHeight, area.Height), area.Height);
        return new(MathF.Round(Math.Clamp(box.X, area.X, area.Right - w)), MathF.Round(Math.Clamp(box.Y, area.Y, area.Bottom - h)), MathF.Round(w), MathF.Round(h));
    }


    public static Box Snap(Box box, IEnumerable<Box> neighbors, Box area, bool resize, float tolerance, Box? boundary = null)
    {
        // Compare independent edge deltas, choosing only the nearest on each axis.
        float dx = 0, dy = 0, bx = tolerance, by = tolerance;
        void X(float d)
        {
            if (Math.Abs(d) < bx)
            {
                bx = Math.Abs(d);
                dx = d;
            }
        }
        void Y(float d)
        {
            if (Math.Abs(d) < by)
            {
                by = Math.Abs(d);
                dy = d;
            }
        }
        if (resize)
        {
            X(area.Right - box.Right);
            Y(area.Bottom - box.Bottom);
        }
        else
        {
            X(area.X - box.X);
            X(area.Right - box.Right);
            Y(area.Y - box.Y);
            Y(area.Bottom - box.Bottom);
        }
        foreach (var other in neighbors)
        {
            float gap = other == boundary ? 0 : Gap;
            if (resize)
            {
                X(other.Right - box.Right);
                X(other.X - gap - box.Right);
                X(box.X + other.Width - box.Right);
                Y(other.Bottom - box.Bottom);
                Y(other.Y - gap - box.Bottom);
                Y(box.Y + other.Height - box.Bottom);
            }
            else
            {
                X(other.X - box.X);
                X(other.Right - box.Right);
                X(other.Right + gap - box.X);
                X(other.X - gap - box.Right);
                Y(other.Y - box.Y);
                Y(other.Bottom - box.Bottom);
                Y(other.Bottom + gap - box.Y);
                Y(other.Y - gap - box.Bottom);
            }
        }
        return resize ? box with
        {
            Width = box.Width + dx,
            Height = box.Height + dy
        } : box with
        {
            X = box.X + dx,
            Y = box.Y + dy
        };
    }


    public static Box Resize(Box box, PrintOptions options, float minWidth, float minHeight)
    {
        var area = Area(options);
        if (options.FreeLayout)
            return Clamp(box, options, minWidth, minHeight);
        return box with
        {
            Width = MathF.Round(Math.Clamp(box.Width, Math.Min(minWidth, area.Right - box.X), area.Right - box.X)),
            Height = MathF.Round(Math.Clamp(box.Height, Math.Min(minHeight, area.Bottom - box.Y), area.Bottom - box.Y))
        };
    }


    public static Box? Clear(Box desired, IEnumerable<Box> obstacles, Box area, bool resize, float minWidth, float minHeight, float gap = 0, Func<Box, bool>? fits = null, Box? boundary = null)
    {
        var others = (boundary is { } border ? obstacles.Append(border) : obstacles).Distinct().ToArray();
        float Clearance(Box other) => other == boundary ? 0 : gap;
        bool Valid(Box b) => b.Width >= minWidth && b.Height >= minHeight && b.X >= area.X && b.Y >= area.Y && b.Right <= area.Right && b.Bottom <= area.Bottom
            && others.All(o => !b.Overlaps(o, Clearance(o))) && (fits?.Invoke(b) ?? true);
        if (Valid(desired))
            return desired;
        // Search nearby legal edge alignments, rather than accepting an overlap
        // or discarding the entire drag. Resize keeps its upper-left anchor.
        float[] xs = resize ? [desired.Width, area.Right - desired.X, .. others.Select(o => MathF.Floor(o.X - Clearance(o) - desired.X))]
            : [desired.X, area.X, area.Right - desired.Width, .. others.SelectMany(o => new[] { MathF.Floor(o.X - Clearance(o) - desired.Width), MathF.Ceiling(o.Right + Clearance(o)) })];
        float[] ys = resize ? [desired.Height, area.Bottom - desired.Y, .. others.Select(o => MathF.Floor(o.Y - Clearance(o) - desired.Y))]
            : [desired.Y, area.Y, area.Bottom - desired.Height, .. others.SelectMany(o => new[] { MathF.Floor(o.Y - Clearance(o) - desired.Height), MathF.Ceiling(o.Bottom + Clearance(o)) })];
        return xs.Distinct().SelectMany(x => ys.Distinct().Select(y => resize ? desired with { Width = x, Height = y } : desired with { X = x, Y = y }))
            .Where(Valid).OrderBy(b => resize ? MathF.Pow(b.Width - desired.Width, 2) + MathF.Pow(b.Height - desired.Height, 2) : MathF.Pow(b.X - desired.X, 2) + MathF.Pow(b.Y - desired.Y, 2))
            .Select(b => (Box?)b).FirstOrDefault();
    }
}
