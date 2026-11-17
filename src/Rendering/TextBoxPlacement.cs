using System.Numerics;
namespace Blueprint.Rendering;

// Build once per gesture. Mouse movement only checks cached rectangles and line
// segments; it never rerenders craft geometry or reroutes specification leaders.
sealed record TextPlacementSpace(PrintOptions Options, Box[] Obstacles, (Vector2 Start, Vector2 End)[] Leaders)
{
    public bool Clear(Box box) => PageLayout.Fits(box, Options)
        && Obstacles.All(b => !box.Overlaps(b))
        && Leaders.All(line => !Crosses(line.Start, line.End, box));

    // Slab clipping avoids sampling every pixel along a long diagonal leader.
    static bool Crosses(Vector2 a, Vector2 b, Box box)
    {
        float near = 0, far = 1;
        bool Axis(float start, float delta, float low, float high)
        {
            if (Math.Abs(delta) < .00001f)
                return start >= low && start <= high;
            float t0 = (low - start) / delta, t1 = (high - start) / delta;
            near = Math.Max(near, Math.Min(t0, t1));
            far = Math.Min(far, Math.Max(t0, t1));
            return near <= far;
        }
        return Axis(a.X, b.X - a.X, box.X - 2, box.Right + 2)
            && Axis(a.Y, b.Y - a.Y, box.Y - 2, box.Bottom + 2);
    }

    public Vector2 SnapCorner(Vector2 point, float tolerance)
    {
        var area = PageLayout.Placement(Options);
        var snapped = FrameLayout.Snap(new(point.X, point.Y, 0, 0), Obstacles, area, false, tolerance,
            PageLayout.TitleBounds(Options));
        return new(snapped.X, snapped.Y);
    }

    public FreeTextBox Snap(FreeTextBox text, bool resize, float tolerance)
    {
        var raw = FreeTextPainting.Fit(text, Options);
        var b = raw.Bounds;
        var snap = FrameLayout.Snap(b, Obstacles, PageLayout.Placement(Options), resize, tolerance,
            PageLayout.TitleBounds(Options));
        Box xOnly = resize ? b with
        {
            Width = snap.Width
        } : b with
        {
            X = snap.X
        };
        Box yOnly = resize ? b with
        {
            Height = snap.Height
        } : b with
        {
            Y = snap.Y
        };
        foreach (var candidate in new[] { snap, xOnly, yOnly, b }.Distinct())
        {
            var fitted = FreeTextPainting.Fit(raw with
            {
                Bounds = candidate
            }, Options);
            if (Clear(fitted.Bounds))
                return fitted;
        }
        // Keep the invalid preview under the pointer; the UI paints it red and
        // leaves the previous accepted box intact if released here.
        return raw;
    }
}

static class TextBoxPlacement
{
    public const string OverlapMessage = "Text box overlaps another item. Move or resize it onto clear paper.";

    public static TextPlacementSpace Space(RenderScene scene, IReadOnlyList<PartLabel> labels,
        IReadOnlyList<LabelGeometry> routes, int except = -1,
        IReadOnlyList<(Vector2 Start, Vector2 End)>? specLeaders = null)
    {
        var obstacles = scene.Views.Where(v => v.Id < 1000).Select(v => v.Bounds)
            .Concat(scene.Captions.Select(c => c.Bounds))
            .Concat(scene.Details.Select(d => d.Definition.Bounds))
            .Concat(PrintTables.Panels(scene, labels).Select(p => p.Bounds))
            .Concat(scene.Specs.Select(b => b.Bounds))
            .Concat(scene.Dimensions.Select(d => HeightDimensions.Place(scene, d, false).Bounds))
            .Concat(scene.Kittens.Select(k => KittenReferences.Bounds(scene, k)))
            .Concat(scene.FreeText.Where(b => b.Id != except).Select(b => b.Bounds))
            .Concat(labels.Where(l => l.Visible).Select(AnnotationText.Bounds))
            .Append(PageLayout.TitleBounds(scene.Options)).Distinct().ToArray();
        var leaders = routes.Select(r => (r.Start, r.End)).ToList();
        if (specLeaders != null)
            leaders.AddRange(specLeaders);
        else
            foreach (var block in scene.Specs.Where(b => b.Leader && !b.Vehicle))
            {
                var path = SpecLeaderRouting.Route(scene, block, labels);
                for (int i = 1; i < path.Length; i++)
                    leaders.Add((path[i - 1], path[i]));
            }
        return new(scene.Options, obstacles, leaders.ToArray());
    }

    public static void RequireClear(TextPlacementSpace space, Box box)
    {
        if (!PageLayout.Fits(box, space.Options))
            throw new InvalidOperationException("Keep the whole text box inside the drawing border and clear of the title block.");
        if (!space.Clear(box))
            throw new InvalidOperationException(OverlapMessage);
    }

    public static bool LabelsClear(RenderScene scene, IReadOnlyList<LabelGeometry> routes)
    {
        if (scene.FreeText.Length == 0)
            return true;
        var space = new TextPlacementSpace(scene.Options, routes.Select(r => r.TextBox).ToArray(),
            routes.Select(r => (r.Start, r.End)).ToArray());
        return scene.FreeText.All(text => space.Clear(text.Bounds));
    }

    public static void Validate(RenderScene scene, IReadOnlyList<PartLabel> labels,
        IReadOnlyList<LabelGeometry> routes, IReadOnlyList<(Vector2 Start, Vector2 End)> specLeaders)
    {
        foreach (var text in scene.FreeText)
            RequireClear(Space(scene, labels, routes, text.Id, specLeaders), text.Bounds);
    }
}
