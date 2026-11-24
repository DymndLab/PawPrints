using System.Collections.Immutable;

namespace Blueprint.Rendering;

static class StarterDrawings
{
    public static readonly (string Name, LayoutTemplate Template)[] All =
    [
        ("Orthographic", Layout([(3, 0, 0), (1, 0, 1), (2, 1, 1)], 2, 2)),
        ("Orthographic + ISO", Layout([(3, 0, 0), (4, 1, 0), (1, 0, 1), (2, 1, 1)], 2, 2)),
        ("ISO + Exploded", Layout([(4, 0, 0), (8, 1, 0)], 2, 1)),
        ("Front + Side", Layout([(1, 0, 0), (2, 1, 0)], 2, 1)),
        ("Isometric", Layout([(4, 0, 0)], 1, 1)),
        ("Exploded", Layout([(8, 0, 0)], 1, 1))
    ];

    public static LayoutTemplate FromSelection(IEnumerable<int> selected)
    {
        var cameras = selected.ToArray();
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(cameras.Length)));
        int rows = Math.Max(1, (cameras.Length + columns - 1) / columns);
        return Layout(cameras.Select((c, i) => (c, i % columns, i / columns)).ToArray(), columns, rows);
    }

    static LayoutTemplate Layout((int Camera, int X, int Y)[] slots, int columns, int rows)
    {
        static bool Ortho(int camera) => camera is 1 or 2 or 3 or 5 or 6 or 7;
        int primary = Array.FindIndex(slots, s => s.Camera == 1);
        if (primary < 0) primary = Array.FindIndex(slots, s => Ortho(s.Camera));
        bool linked = slots.Count(s => Ortho(s.Camera)) > 1;
        var views = slots.Select((s, i) =>
        {
            int id = s.Camera == 8 ? 1001 : 100 + i;
            bool group = linked && Ortho(s.Camera);
            return new ViewDefinition(id, s.Camera == 8 ? 4 : s.Camera,
                new((s.X + .04f) / columns, (s.Y + .04f) / rows, .92f / columns, .92f / rows),
                100, group && i != primary ? 100 + primary : null, group ? 100 + primary : null,
                Exploded: s.Camera == 8, Alignment: group && i != primary
                    ? s.X == slots[primary].X ? 1 : s.Y == slots[primary].Y ? 2 : 0
                    : 0);
        }).ToImmutableArray();
        ImmutableArray<ProjectionGroup> groups = linked
            ? [new(100 + primary, 100 + primary, views.Where(v => v.Group != null).Select(v => v.Id).ToImmutableArray(), 100)]
            : [];
        return new(1, "pawprints.layout", new(views, groups,
            views.Select(v => new DrawingObject(DrawingObjectKind.View, v.Id)).ToImmutableArray()));
    }

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
        var result = PrintGeneration.Render(source, geometry, craft, options with { Drawing = drawing }, warnings, null, false, token);
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
