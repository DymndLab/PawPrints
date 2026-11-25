using System.Collections.Immutable;

namespace Blueprint.Rendering;

static class StarterDrawings
{
    static readonly int[] primaryOrder = [1, 2, 6, 3, 7, 5];

    public static readonly (string Name, LayoutTemplate Template)[] All =
    [
        ("Orthographic", Layout([(3, 0, 0), (1, 0, 1), (6, 1, 1)], 2, 2)),
        ("Orthographic + ISO", Layout([(3, 0, 0), (4, 1, 0), (1, 0, 1), (6, 1, 1)], 2, 2)),
        ("ISO + Exploded", Layout([(4, 0, 0), (8, 1, 0)], 2, 1)),
        ("Front + Side", Layout([(1, 0, 0), (6, 1, 0)], 2, 1)),
        ("Isometric", Layout([(4, 0, 0)], 1, 1)),
        ("Exploded", Layout([(8, 0, 0)], 1, 1))
    ];

    public static bool Ortho(int camera) => camera is 1 or 2 or 3 or 5 or 6 or 7;

    static IEnumerable<int> PrimaryCandidates(int[] cameras, int? preferred)
    {
        if (preferred is int pick && Ortho(pick) && cameras.Contains(pick)) return [pick];
        // Each orthographic camera can project its four adjacent faces directly.
        // If both opposing faces are selected, prefer a view adjacent to both.
        return primaryOrder.Where(cameras.Contains).OrderBy(camera =>
            cameras.Contains(camera switch { 1 => 5, 5 => 1, 2 => 6, 6 => 2, 3 => 7, _ => 3 }));
    }

    public static int? ChoosePrimary(int[] cameras, int? preferred = null) =>
        PrimaryCandidates(cameras, preferred).Cast<int?>().FirstOrDefault();

    public static HashSet<int> PrimaryViewIds(DrawingLayout layout)
    {
        var roots = layout.Groups.Select(g => g.Primary).ToHashSet();
        var parents = layout.Views.Where(v => v.Parent.HasValue).Select(v => v.Parent!.Value).ToHashSet();
        var orthographic = layout.Views.Where(v => !v.Exploded && Ortho(v.Camera)).ToArray();
        foreach (var view in orthographic)
            if (view.Parent == null && parents.Contains(view.Id)) roots.Add(view.Id);
        if (roots.Count == 0 && orthographic.Length == 1) roots.Add(orthographic[0].Id);
        return roots;
    }

    public static LayoutTemplate FromSelection(IEnumerable<int> selected, int angle = 3, int? primaryCamera = null)
    {
        var cameras = selected.ToArray();
        int root = ChoosePrimary(cameras, primaryCamera) ?? 0;
        if (root != 0 && cameras.Count(Ortho) > 1)
        {
            var slots = ProjectedSlots(cameras, root);
            int left = slots.Values.Min(p => p.X), top = slots.Values.Min(p => p.Y);
            int right = slots.Values.Max(p => p.X), rows = slots.Values.Max(p => p.Y) - top + 1;
            int extra = 0;
            foreach (int camera in cameras.Where(c => !slots.ContainsKey(c)))
            {
                slots.Add(camera, (right + 1 + extra / rows, top + extra % rows));
                extra++;
            }
            var arranged = cameras.Select(c => (c, slots[c].X - left, slots[c].Y - top)).ToArray();
            return ForAngle(Layout(arranged, slots.Values.Max(p => p.X) - left + 1, rows, root), angle);
        }
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(cameras.Length)));
        return Layout(cameras.Select((c, i) => (c, i % columns, i / columns)).ToArray(), columns,
            Math.Max(1, (cameras.Length + columns - 1) / columns));
    }

    static Dictionary<int, (int X, int Y)> ProjectedSlots(int[] cameras, int root)
    {
        var primary = new DrawingView(0, root, 0, 0);
        var slots = new Dictionary<int, (int X, int Y)> { [root] = (0, 0) };
        (int X, int Y)[] offsets = [(1, 0), (0, -1), (-1, 0), (0, 1)];
        for (int d = 0; d < offsets.Length; d++)
        {
            int camera = DrawingPlacements.Roll(primary, primary, d, 3).Camera;
            if (cameras.Contains(camera)) slots[camera] = offsets[d];
        }
        return slots;
    }

    public const string GridFullMessage = "You do not have space for additional views.";

    // Evaluate the same proposed selection as the click handler, including automatic
    // primary inference. A different primary can make a rectangular grid usable.
    internal static string? ViewAdditionIssue(int[] cameras, int candidate, int angle, int? preferred,
        int? resolvedPrimary, int columns, int rows, SetupViewGrid? current = null)
    {
        if (columns == 0 || cameras.Contains(candidate)) return null;
        if (cameras.Length >= columns * rows) return GridFullMessage;
        var grid = current ?? SetupViewGrid.Empty(angle);
        if (grid.Arrange([.. cameras, candidate], columns, rows, angle, preferred) != null) return null;
        int[] linked = current == null ? cameras : current.Slots.Where(s => !s.Detached).Select(s => s.Camera).ToArray();
        return ProjectionAxisHint(linked, candidate, angle, resolvedPrimary, columns, rows) ?? "These linked views need a different grid shape.";
    }

    internal static string? PrimaryChoiceIssue(int[] cameras, int candidate, int angle, int columns, int rows, SetupViewGrid? current = null)
    {
        if (!Ortho(candidate)) return "Only orthographic views can be the primary projection.";
        if (columns == 0) return null;
        int[] selection = cameras.Contains(candidate) ? cameras : [.. cameras, candidate];
        if (selection.Length > columns * rows) return GridFullMessage;
        if ((current ?? SetupViewGrid.Empty(angle)).Arrange(selection, columns, rows, angle, candidate) != null) return null;
        return $"Choose a different grid to make {DrawingPlacements.Names[candidate]} primary.";
    }

    internal static string? ProjectionAxisHint(int[] cameras, int candidate, int angle, int? resolvedPrimary, int columns, int rows)
    {
        if (resolvedPrimary is not int root || !Ortho(candidate)) return null;
        var parent = new DrawingView(0, root, 0, 0);
        for (int direction = 0; direction < 4; direction++)
        {
            if (DrawingPlacements.Roll(parent, parent, direction, angle).Camera != candidate) continue;
            bool vertical = direction % 2 == 1;
            int opposite = DrawingPlacements.Roll(parent, parent, (direction + 2) % 4, angle).Camera;
            int required = cameras.Contains(opposite) ? 3 : 2;
            if ((vertical ? rows : columns) >= required) return null;
            string side = direction switch { 0 => "to the right of", 1 => "above", 2 => "to the left of", _ => "below" };
            return $"{DrawingPlacements.Names[candidate]} projects {side} {DrawingPlacements.Names[root]}. Choose a grid with at least {required} {(vertical ? "rows" : "columns")}.";
        }
        return null;
    }

    public static bool TryGrid(int[] cameras, int angle, int? primaryCamera, int columns, int rows,
        out LayoutTemplate? template, out string? message)
    {
        var grid = SetupViewGrid.Empty(angle).Arrange(cameras, columns, rows, angle, primaryCamera);
        template = grid?.ToTemplate();
        message = grid != null ? null : cameras.Length > columns * rows ? GridFullMessage : "These linked views need a different grid shape.";
        return grid != null;
    }

    public static LayoutTemplate ForAngle(LayoutTemplate template, int angle)
    {
        var first = template.Layout.Views.FirstOrDefault(v => v.RollDirection >= 0);
        if (first == null || first.ProjectionAngle == angle) return template;
        var views = template.Layout.Views.Select(v => v with
        {
            Bounds = v.Bounds with { X = 1 - v.Bounds.Right, Y = 1 - v.Bounds.Bottom },
            RollDirection = v.RollDirection < 0 ? -1 : DrawingPlacements.OppositeDirection(v.RollDirection),
            ProjectionAngle = angle
        }).ToImmutableArray();
        return template with { Layout = template.Layout with { Views = views } };
    }

    static LayoutTemplate Layout((int Camera, int X, int Y)[] slots, int columns, int rows, int? primaryCamera = null)
    {
        int? camera = ChoosePrimary(slots.Select(s => s.Camera).ToArray(), primaryCamera);
        int primary = Array.FindIndex(slots, s => s.Camera == camera);
        bool linked = slots.Count(s => Ortho(s.Camera)) > 1;
        var views = slots.Select((s, i) =>
        {
            int id = s.Camera == 8 ? 1001 : 100 + i;
            bool group = linked && Ortho(s.Camera);
            DrawingView? projection = null;
            if (group && i != primary)
            {
                var parent = slots[primary];
                int direction = DrawingPlacements.Direction(new(s.X - parent.X, s.Y - parent.Y));
                var source = new DrawingView(100 + primary, parent.Camera, 0, 0);
                var rolled = DrawingPlacements.Roll(new(id, s.Camera, 0, 0), source, direction, 3);
                if (rolled.Camera == s.Camera) projection = rolled;
            }
            return new ViewDefinition(id, s.Camera == 8 ? 4 : s.Camera,
                new((s.X + .04f) / columns, (s.Y + .04f) / rows, .92f / columns, .92f / rows),
                100, group && i != primary ? 100 + primary : null, group ? 100 + primary : null,
                Exploded: s.Camera == 8, Alignment: projection?.Alignment ?? 0,
                Axes: projection?.Axes, RollDirection: projection?.RollDirection ?? -1);
        }).ToImmutableArray();
        ImmutableArray<ProjectionGroup> groups = linked
            ? [new(100 + primary, 100 + primary, views.Where(v => v.Group != null).Select(v => v.Id).ToImmutableArray(), 100)]
            : [];
        return new(1, "pawprints.layout", new(views, groups,
            views.Select(v => new DrawingObject(DrawingObjectKind.View, v.Id)).ToImmutableArray()));
    }

    internal static int? ProjectionConvention(IEnumerable<ViewDefinition> views) =>
        views.FirstOrDefault(v => !v.Exploded && v.Parent != null && (v.RollDirection >= 0 || v.Alignment != 0))?.ProjectionAngle;

    public static Sheet Render(PoseSource source, Capture geometry, string craft, PrintOptions options,
        string[] warnings, LayoutTemplate template, bool fitStarter, CancellationToken token)
    {
        // The layout never carries a source binding or session appearance.
        var page = options.Page;
        var area = fitStarter ? PageLayout.Drawing(options) : new Box(0, 0, page.Width, page.Height);
        Box Cell(ViewDefinition v) => new(area.X + v.Bounds.X * area.Width, area.Y + v.Bounds.Y * area.Height,
            v.Bounds.Width * area.Width, v.Bounds.Height * area.Height);
        var main = template.Layout.Views.Where(v => !v.Exploded).ToArray();
        var cells = main.ToDictionary(v => v.Id, Cell);
        var views = main.Select(v =>
        {
            var box = cells[v.Id];
            float caption = fitStarter && options.Captions ? ViewCaption.Band : 0;
            return new DrawingView(v.Id, v.Camera, box.Center.X / page.Width, (box.Center.Y - caption / 2) / page.Height,
                v.Parent, v.Alignment, v.Axes, v.RollDirection, v.ProjectionAngle, v.Parent == null ? v.Ratio : null);
        }).ToArray();
        var drawing = new DrawingPlacement(100, views, template.Layout.Layers.ToArray());
        if (fitStarter && views.Length != 0)
        {
            var plan = DrawingPlacements.PreparePlan(geometry, options, drawing);
            var regions = plan(100000).ToDictionary(v => v.Id);
            // Measure at a safe small scale once. A group then takes the most
            // restrictive slot scale among its members.
            float Fit(DrawingView view)
            {
                var extent = regions[view.Id].Frame!.ContentExtent!.Value * 1000;
                var box = cells[view.Id];
                float cap = options.Captions ? ViewCaption.Band : 0;
                return Math.Max(extent.X / Math.Max(1, box.Width - 32),
                    extent.Y / Math.Max(1, box.Height - cap - 32));
            }
            var ratios = views.GroupBy(v => ProjectionGroups.Root(drawing, v.Id))
                .ToDictionary(g => g.Key, g => Math.Clamp(100 * g.Max(Fit), .01f, 1000000));
            drawing = drawing with { Views = views.Select(v => v.Parent == null ? v with { Ratio = ratios[v.Id] } : v).ToArray() };
        }
        var convention = ProjectionConvention(main) ?? options.DrawingAngle;
        var result = PrintGeneration.Render(source, geometry, craft, options with { Drawing = drawing, ProjectionAngle = convention }, warnings, null, false, token);
        var detailSlots = template.Layout.Views.Where(v => v.Exploded).ToArray();
        if (detailSlots.Length == 0) return result;
        if (ExplodedPainting.WholeCraftRoot(source.Assembly) is not { } root)
            throw new InvalidOperationException("Whole-vehicle exploded views need a single connected assembly.");
        var details = detailSlots.Select(v => new ExplodedDetail(v.Id - 1000, root,
            ExplodedPainting.ClampFrame(Cell(v), options), craft, View: v.Camera,
            Sizing: fitStarter ? null : new DetailSizing(v.Ratio))).ToArray();
        return ExplodedPainting.Compose(result, result, details, [], token) with { PoseMain = result };
    }
}
