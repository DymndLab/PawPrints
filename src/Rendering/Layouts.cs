using System.Numerics;
namespace Blueprint.Rendering;

sealed record LabelPart(int Id, string Name, string TemplateId, int[] Meshes, bool DetailOnly = false, uint? NodeId = null);
sealed record ViewPixels(int Width, int Height, byte[] Ink, int[] MeshOwners);
readonly record struct Box(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Center => new(X + Width / 2, Y + Height / 2);

    public bool Contains(Vector2 p) => p.X >= X && p.Y >= Y && p.X < Right && p.Y < Bottom;

    public bool Overlaps(Box b, float gap = 0) => X - gap < b.Right && Right + gap > b.X && Y - gap < b.Bottom && Bottom + gap > b.Y;
}
sealed record ProjectionFrame(int Width, int Height, float Scale, Vector3 Center, Vector3 Right, Vector3 Up, Vector3 Toward);
sealed record ViewRegion(int Id, string Name, Box Bounds, ProjectionFrame? Frame, int? Projection = null)
{
    public int CameraView => Projection ?? Id;
}
sealed record PartVisual(int PartId, int Area, Box Bounds, Vector2[] Edges)
{
    public Vector2[] ContactEdges { get; init; } = [];
}
sealed class RenderScene
{
    public byte[] BaseRgb
    {
        get; private set;
    }

    public RenderScene WithBase(byte[] rgb, PrintOptions options)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.BaseRgb = rgb;
        copy.Options = options;
        return copy;
    }
    public readonly int[] Owners;
    public readonly LabelPart[] Parts;
    public readonly ViewRegion[] Views;
    public Box[] Reserved
    {
        get; private set;
    }
    public ViewCaptionMark[] Captions { get; private set; } = [];
    public CraftHeight? CraftHeight
    {
        get; init;
    }
    public IReadOnlyDictionary<int, CraftHeight> ViewHeights { get; init; } = new Dictionary<int, CraftHeight>();
    public HeightDimension[] Dimensions { get; private set; } = [];
    public KittenModel? KittenModel
    {
        get; private set;
    }
    public KittenReference[] Kittens { get; private set; } = [];
    public PerformanceData? Performance
    {
        get; private set;
    }
    public SpecBlock[] Specs { get; private set; } = [];
    public FreeTextBox[] FreeText { get; private set; } = [];

    public RenderScene WithFreeText(FreeTextBox[] boxes)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.FreeText = boxes;
        return copy;
    }

    public RenderScene WithPerformance(PerformanceData? data)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Performance = data;
        return copy;
    }

    public RenderScene WithSpecs(SpecBlock[] specs)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Specs = specs;
        return copy;
    }
    public PartPose[] Poses { get; private set; } = [];

    public RenderScene WithPoses(PartPose[] poses)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Poses = poses;
        return copy;
    }

    public RenderScene WithKittens(KittenReference[] kittens, KittenModel? model = null)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Kittens = kittens;
        copy.KittenModel = model ?? KittenModel;
        return copy;
    }

    public RenderScene WithDimensions(HeightDimension[] dimensions)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Dimensions = dimensions;
        return copy;
    }

    public RenderScene WithCaptions(ViewCaptionMark[] captions, bool align = true)
    {
        if (align)
            captions = ViewCaption.AlignRows(captions, Views);
        var copy = (RenderScene)MemberwiseClone();
        copy.Reserved = [.. Reserved.Where(b => !Captions.Any(c => c.Bounds == b)), .. captions.Select(c => c.Bounds)];
        copy.Captions = captions;
        return copy;
    }
    public PrintOptions Options
    {
        get; private set;
    }
    public DrawingPlacement? Drawing
    {
        get; private set;
    }

    public RenderScene WithDrawing(DrawingPlacement? drawing)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Drawing = drawing;
        return copy;
    }

    public RenderScene WithOptions(PrintOptions options)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Options = options;
        return copy;
    }

    // Isolated item preview must not validate its temporary position against other panels.
    public RenderScene WithoutPanels()
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Details = [];
        copy.Specs = [];
        copy.Dimensions = [];
        copy.Options = Options with
        {
            PartsTable = false,
            FlagTable = false
        };
        return copy;
    }
    public TableLayout Tables { get; private set; } = new();

    public RenderScene WithTables(TableLayout tables)
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.Tables = tables;
        return copy;
    }
    public int[]? MeshOwners
    {
        get; init;
    }
    public uint[]? MeshNodes
    {
        get; set;
    }
    public RenderedDetail[] Details { get; set; } = [];

    public RenderScene WithoutDetailCache()
    {
        var copy = (RenderScene)MemberwiseClone();
        copy.detailsCacheInvalid = true;
        return copy;
    }
    internal bool detailsCacheInvalid;
    public int Width => Options.Page.Width;
    public int Height => Options.Page.Height;
    public Box DrawingBounds => PageLayout.Drawing(Options);
    readonly Dictionary<(int View, int Part), Box[]> highlights = [];
    public readonly Dictionary<(int View, int Part), PartVisual> Visible = [];

    public RenderScene(byte[] rgb, int[] owners, LabelPart[] parts, ViewRegion[] views, Box[]? reserved = null, PrintOptions? options = null, int[]? viewOwners = null)
    {
        BaseRgb = rgb;
        Owners = owners;
        Parts = parts;
        Views = views;
        Reserved = reserved ?? [];
        Options = options ?? new();
        Drawing = Options.Drawing;
        foreach (var view in views)
        {
            var pixels = new Dictionary<int, List<int>>();
            for (int y = (int)view.Bounds.Y; y < view.Bounds.Bottom; y++)
                for (int x = (int)view.Bounds.X; x < view.Bounds.Right; x++)
                {
                    int q = y * Width + x, id = owners[q];
                    if (id < 0 || viewOwners != null && viewOwners[q] != view.Id)
                        continue;
                    if (!pixels.TryGetValue(id, out var list))
                        pixels[id] = list = [];
                    list.Add(q);
                }
            foreach (var pair in pixels)
            {
                int x0 = Width, y0 = Height, x1 = 0, y1 = 0;
                var edges = new List<Vector2>();
                var contacts = new List<Vector2>();
                foreach (int q in pair.Value)
                {
                    int x = q % Width, y = q / Width;
                    x0 = Math.Min(x0, x);
                    x1 = Math.Max(x1, x);
                    y0 = Math.Min(y0, y);
                    y1 = Math.Max(y1, y);
                    if (x > 0 && y > 0 && x < Width - 1 && y < Height - 1 &&
                        (owners[q - 1] < 0 || owners[q + 1] < 0 || owners[q - Width] < 0 || owners[q + Width] < 0))
                        edges.Add(new(x, y));
                    if (Owner(x - 1, y) != pair.Key || Owner(x + 1, y) != pair.Key || Owner(x, y - 1) != pair.Key || Owner(x, y + 1) != pair.Key)
                        contacts.Add(new(x, y));
                }
                // Sample the complete perimeter uniformly, including small exposed regions.
                int step = Math.Max(1, (int)Math.Ceiling(edges.Count / 256d));
                Visible[(view.Id, pair.Key)] = new(pair.Key, pair.Value.Count, new(x0, y0, x1 - x0 + 1, y1 - y0 + 1), edges.Where((_, i) => i % step == 0).ToArray())
                {
                    ContactEdges = contacts.ToArray()
                };
            }
        }
    }

    public int Owner(int x, int y) => x < 0 || y < 0 || x >= Width || y >= Height ? -1 : Owners[y * Width + x];

    public (int View, int Part)? Pick(Vector2 point)
    {
        var view = Views.LastOrDefault(v => v.Bounds.Contains(point));
        if (view == null)
            return null;
        int part = Owner((int)point.X, (int)point.Y);
        return part < 0 ? null : (view.Id, part);
    }

    public uint? PickNode(Vector2 point)
    {
        int x = (int)point.X, y = (int)point.Y;
        if (MeshOwners == null || MeshNodes == null || x < 0 || y < 0 || x >= Width || y >= Height)
            return null;
        int mesh = MeshOwners[y * Width + x];
        return mesh >= 0 && mesh < MeshNodes.Length ? MeshNodes[mesh] : null;
    }

    public Box[] Highlight(int view, int part)
    {
        if (highlights.TryGetValue((view, part), out var cached))
            return cached;
        if (!Visible.TryGetValue((view, part), out var visual))
            return [];
        var runs = new List<Box>();
        var b = visual.Bounds;
        for (int y = (int)b.Y; y < b.Bottom; y++)
            for (int x = (int)b.X; x < b.Right; x++)
            {
                if (Owner(x, y) != part)
                    continue;
                int start = x;
                while (x + 1 < b.Right && Owner(x + 1, y) == part)
                    x++;
                runs.Add(new(start, y, x - start + 1, 1));
            }
        return highlights[(view, part)] = runs.ToArray();
    }
}

static class SheetLayout
{

    public static (Vector3 Paper, Vector3 Ink) Colors(int theme) => theme switch
    {
        1 => (new(240, 241, 239), new(48, 56, 64)),
        2 => (new(243, 235, 216), new(68, 60, 50)),
        3 => (new(43, 47, 51), new(233, 234, 226)),
        _ => (new(19, 51, 87), new(214, 232, 242))
    };

    public static string ViewName(int id) => id >= 1000 ? "DETAIL " + (char)('A' + id - 1001) : id >= 100 ? "BOX " + (id - 99) : DrawingPlacements.Names[id].ToUpperInvariant();

    public static string ViewSummary(PrintOptions options) => options.Drawing != null ? string.Join(" / ", options.Drawing.Views.Select(v => DrawingPlacements.Name(v).ToUpperInvariant()).Distinct()) : options.Boxes != null ? string.Join(" / ", FixedViewLayouts.Cameras(options).Distinct().Select(ViewName)) : options.Layout == 0 ? ViewName(options.View) :
        string.Join(" / ", ViewArrangements.Views(options).Order().Select(ViewName));

    public static (Vector3 Right, Vector3 Up, Vector3 Toward) Basis(int view)
    {
        var toward = Vector3.Normalize(view switch
        {
            0 => new Vector3(9, -17, 7),
            1 => Vector3.UnitX,
            2 => -Vector3.UnitY,
            3 => Vector3.UnitZ,
            5 => -Vector3.UnitX,
            6 => Vector3.UnitY,
            7 => -Vector3.UnitZ,
            _ => new Vector3(1, -1, 1)
        });
        var right = view is 3 or 7 ? Vector3.UnitY : Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, toward));
        return (right, Vector3.Cross(toward, right), toward);
    }

    public static ViewRegion[] Plan(Capture capture, PrintOptions options, bool resolved = false)
    {
        if (options.Drawing is { } placedDrawing)
            return DrawingPlacements.Plan(capture, options, placedDrawing);
        if (!resolved)
            options = FixedViewLayouts.Resolve(capture, options);
        if (options.Boxes == null && options.TitleBlock == null && options.Layout == 0 && options.DrawingSize == 0 && !options.PartsTable && !options.FlagTable && options.Page == new PageSize(2400, 3000))
            return [new(options.View, ViewName(options.View), new(130, 280, 2140, 2200), null)];
        var points = capture.Meshes.SelectMany(m => m.Vertices).Select(v => new Vector3((float)v[1], (float)v[2], (float)v[0])).ToArray();
        var min = points.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min);
        var max = points.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max);
        var center = (min + max) * .5f;
        float scale = float.PositiveInfinity;
        bool tables = options.PartsTable || options.FlagTable;
        var drawing = PageLayout.Drawing(options);
        float width = drawing.Width;
        float fraction = options.DrawingSize == 0 ? .7f : Math.Clamp(options.DrawingSize, 50, 250) / 100f;
        if (options.Boxes != null)
            return FixedViewLayouts.Plan(points, center, options, fraction);
        if (options.Layout == 0)
        {
            var b = Basis(options.View);
            int w = (int)width, h = (int)drawing.Height;
            float dx = points.Max(p => Math.Abs(Vector3.Dot(p - center, b.Right))) * 2, dy = points.Max(p => Math.Abs(Vector3.Dot(p - center, b.Up))) * 2;
            float fit = Math.Min((w - 64) / Math.Max(dx, .001f), (h - 100) / Math.Max(dy, .001f)) * fraction;
            return [new(options.View, ViewName(options.View), new(drawing.X, drawing.Y, w, h), new(w, h, fit, center, b.Right, b.Up, b.Toward))];
        }
        if (options.DrawingSize > 0 || options.TitleBlock != null)
            return FitViews(points, center, options, drawing, fraction);
        int tileWidth = (int)((width - (tables ? 170 : 220)) / 2), tileHeight = (int)((drawing.Height - 280) / 2);
        // All orthographic slots use the same scale even when one is hidden. Top
        // shares front's horizontal Y axis; front and side share vertical Z.
        foreach (int id in new[] { 1, 2, 3 })
        {
            var b = Basis(id);
            float dx = points.Max(p => Math.Abs(Vector3.Dot(p - center, b.Right))) * 2, dy = points.Max(p => Math.Abs(Vector3.Dot(p - center, b.Up))) * 2;
            float fitW = options.DrawingSize == 0 && !tables ? 620 : (tileWidth - 64) * fraction;
            float fitH = options.DrawingSize == 0 && !tables ? 660 : (tileHeight - 100) * fraction;
            scale = Math.Min(scale, Math.Min(fitW / Math.Max(dx, .001f), fitH / Math.Max(dy, .001f)));
        }
        int right = 180 + tileWidth + (tables ? 70 : 120), bottom = 330 + tileHeight + 120;
        var slots = new[] { (Id: 3, X: 180, Y: 330), (Id: 4, X: right, Y: 330), (Id: 1, X: 180, Y: bottom), (Id: 2, X: right, Y: bottom) };
        var result = new List<ViewRegion>();
        foreach (var slot in slots)
        {
            int bit = slot.Id == 4 ? 8 : 1 << (slot.Id - 1);
            if ((options.VisibleViews & bit) == 0 || (slot.Id == 4 && options.Layout == 1))
                continue;
            var b = Basis(slot.Id);
            float fit = scale;
            if (slot.Id == 4)
            {
                float dx = points.Max(p => Math.Abs(Vector3.Dot(p - center, b.Right))) * 2, dy = points.Max(p => Math.Abs(Vector3.Dot(p - center, b.Up))) * 2;
                fit = Math.Min(scale, Math.Min((options.DrawingSize == 0 && !tables ? 620 : (tileWidth - 64) * fraction) / Math.Max(dx, .001f), (options.DrawingSize == 0 && !tables ? 660 : (tileHeight - 100) * fraction) / Math.Max(dy, .001f)));
            }
            result.Add(new(slot.Id, ViewName(slot.Id), new(slot.X, slot.Y, tileWidth, tileHeight), new(tileWidth, tileHeight, fit, center, b.Right, b.Up, b.Toward)));
        }
        if (result.Count == 0)
            throw new InvalidOperationException("Choose at least one visible view.");
        return result.ToArray();
    }

    static ViewRegion[] FitViews(Vector3[] points, Vector3 center, PrintOptions options, Box drawing, float fraction)
    {
        var ids = ViewArrangements.Views(options);
        if (ids.Length == 0)
            throw new InvalidOperationException("Choose at least one visible view.");
        var extents = ids.ToDictionary(id => id, id =>
        {
            var basis = Basis(id);
            return new Vector2(points.Max(p => Math.Abs(Vector3.Dot(p - center, basis.Right))) * 2, points.Max(p => Math.Abs(Vector3.Dot(p - center, basis.Up))) * 2);
        });
        int arrangement = ViewArrangements.Choices(options).Any(c => c.Id == options.Arrangement) ? options.Arrangement : 0;
        ViewRegion[] best = [];
        float bestScale = -1;
        foreach (int columns in arrangement == 0 ? Enumerable.Range(1, ids.Length) : new[] { 0 })
        {
            var cells = ViewArrangements.Cells(options, arrangement, ids, columns);
            if (cells.Length != ids.Length)
                continue;
            var fits = ids.Select((id, i) => (id, fit: Math.Min((cells[i].Width - ViewArrangements.Padding) / Math.Max(extents[id].X, .001f), (cells[i].Height - ViewArrangements.Padding) / Math.Max(extents[id].Y, .001f)) * fraction)).ToDictionary(p => p.id, p => p.fit);
            float common = ids.Where(id => id != 4).Select(id => fits[id]).DefaultIfEmpty(float.PositiveInfinity).Min();
            float score = fits.Values.Min();
            if (score <= bestScale)
                continue;
            bestScale = score;
            best = ids.Select((id, index) =>
            {
                var cell = cells[index];
                var b = Basis(id);
                float scale = id == 4 ? (arrangement == 4 ? fits[id] : Math.Min(common, fits[id])) : common;
                return new ViewRegion(id, ViewName(id), cell, new((int)cell.Width, (int)cell.Height, scale, center, b.Right, b.Up, b.Toward));
            }).ToArray();
        }
        if (best.Length == 0)
            throw new InvalidOperationException("The selected views do not fit the drawing area.");
        return best;
    }

    public static Sheet Render(Capture capture, IReadOnlyDictionary<string, Texture> maps, string title, LabelPart[] parts, PrintOptions options, string[] warnings, CancellationToken token, Action<string> progress, IReadOnlyDictionary<int, Capture>? viewCaptures = null, Capture? fitCapture = null, bool composeAnnotations = true)
    {
        if (options.Drawing == null)
            options = FixedViewLayouts.Resolve(fitCapture ?? capture, options);
        title = PrintScale.Title(title, options.TitleBlock);
        if (options.TitleBlock is { } data && string.IsNullOrWhiteSpace(data.Title))
            options = options with
            {
                TitleBlock = data with
                {
                    Title = title
                }
            };
        var views = Plan(fitCapture ?? capture, options, true);
        var meshParts = Enumerable.Repeat(-1, capture.Meshes.Length).ToArray();
        foreach (var part in parts)
            foreach (int mi in part.Meshes)
                meshParts[mi] = part.Id;
        if (meshParts.Any(p => p < 0))
            throw new InvalidDataException("Every captured mesh needs a part identity for safe label routing.");
        if (options.Drawing == null && options.Boxes == null && options.Layout == 0 && views[0].Frame == null)
        {
            var renderer = new BlueprintRenderer(viewCaptures?.GetValueOrDefault(views[0].Id) ?? capture, maps, title, options.View, token);
            var result = renderer.Render(options, warnings, progress);
            var owners = renderer.MeshOwners();
            for (int i = 0; i < owners.Length; i++)
                if (owners[i] >= 0)
                    owners[i] = meshParts[owners[i]];
            return result with
            {
                Scene = new(result.Rgb, owners, parts, views, options: options)
            };
        }
        if (options.Drawing != null)
            DrawingPlacements.Validate(views, options);
        int width = options.Page.Width, height = options.Page.Height;
        var colors = Colors(options.Theme);
        var canvas = new Canvas(width, height, colors.Paper, colors.Ink, options);
        var sheetOwners = Enumerable.Repeat(-1, width * height).ToArray();
        var meshOwners = Enumerable.Repeat(-1, width * height).ToArray();
        var captions = new List<ViewCaptionMark>();
        var inkTiles = new List<ViewInk>();
        foreach (var view in views)
        {
            token.ThrowIfCancellationRequested();
            progress("Drawing " + view.Name.ToLowerInvariant());
            var tile = RenderTile(viewCaptures?.GetValueOrDefault(view.Id) ?? capture, maps, title, view, token);
            inkTiles.Add(new(view.Bounds, tile.Width, tile.Height, tile.Ink));
            int left = tile.Width, top = tile.Height, right = -1, bottom = -1;
            for (int y = 0; y < tile.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < tile.Width; x++)
                {
                    int q = y * tile.Width + x, xx = x + (int)view.Bounds.X, yy = y + (int)view.Bounds.Y;
                    canvas.Ink(xx, yy, tile.Ink[q] / 255f);
                    if (tile.MeshOwners[q] >= 0)
                    {
                        sheetOwners[yy * width + xx] = meshParts[tile.MeshOwners[q]];
                        meshOwners[yy * width + xx] = tile.MeshOwners[q];
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }
            if (options.Captions && (options.Layout != 0 || options.Boxes != null || options.Drawing != null) && right >= left)
            {
                var occupied = new Box(view.Bounds.X + left, view.Bounds.Y + top, right - left + 1, bottom - top + 1);
                var caption = ViewCaption.Place(view, occupied);
                captions.Add(new(view.Id, view.Name, occupied, caption.Bounds, caption.FontSize));
            }
        }
        PageLayout.Decorate(canvas, title, options, PrintScale.Summary(views, options));
        NativeText.Apply(canvas.Pixels, width, height, canvas.Runs);
        token.ThrowIfCancellationRequested();
        var resultSheet = new Sheet(title, options, canvas.Pixels, Sheet.MakePreview(canvas.Pixels), warnings) { MainInk = inkTiles.ToArray(), Scene = new RenderScene(canvas.Pixels, sheetOwners, parts, views, options: options) { MeshOwners = meshOwners, CraftHeight = HeightDimensions.Measure(capture), ViewHeights = viewCaptures?.ToDictionary(p => p.Key, p => HeightDimensions.Measure(p.Value)) ?? new Dictionary<int, CraftHeight>() }.WithCaptions(captions.ToArray()) };
        // Rebuilding an existing composition restores its real table bounds and
        // notes first. Temporary default tables may not fit and must not veto it.
        return composeAnnotations ? AnnotationPainting.Compose(resultSheet, [], token) : resultSheet;
    }

    // Separate method lets each large renderer become unreachable between views.
    static ViewPixels RenderTile(Capture capture, IReadOnlyDictionary<string, Texture> maps, string title, ViewRegion view, CancellationToken token)
        => new BlueprintRenderer(capture, maps, title, view.CameraView, token, view.Frame).RenderView(_ => { });
}
