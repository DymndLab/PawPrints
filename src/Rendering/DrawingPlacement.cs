using System.Numerics;
namespace Blueprint.Rendering;

// Centres are normalized to paper dimensions; scale is a physical 1:N ratio.
// IDs identify drawings, not cameras: repeated directions are intentional.
sealed record ViewAxes(Vector3 Right, Vector3 Up, Vector3 Toward);
sealed record DrawingView(int Id, int Camera, float X, float Y, int? Parent = null, int Alignment = 0, ViewAxes? Axes = null, int RollDirection = -1, int ProjectionAngle = 3);
sealed record DrawingPlacement(float Ratio, DrawingView[] Views);
static class DrawingPlacements
{
    public const int Limit = 12;
    public static readonly string[] Names = ["Three-quarter", "Front", "Side", "Top", "Isometric", "Rear", "Left side", "Bottom"];


    public static bool Oblique(int camera) => camera is 0 or 4;


    public static ViewAxes Axes(DrawingView view)
    {
        var basis = SheetLayout.Basis(view.Camera);
        return view.Axes ?? new(basis.Right, basis.Up, basis.Toward);
    }


    public static string Name(DrawingView view) => view.RollDirection >= 0 && view.Camera == 0 ? "Auxiliary" : Names[view.Camera];


    // Directions 0..3: right, up, left, down; 4..7: upper-right, upper-left, lower-left, lower-right.
    public static DrawingView Roll(DrawingView draft, DrawingView source, int direction, int angle)
    {
        if (direction is < 0 or > 7 || angle is not (1 or 3))
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (direction >= 4)
        {
            var basis = Axes(source);
            float flip = angle == 3 ? 1 : -1;
            float x = direction is 4 or 7 ? flip : -flip, y = direction is 4 or 5 ? flip : -flip;
            var toward = Vector3.Normalize(basis.Toward + x * basis.Right + y * basis.Up);
            var right = Vector3.Normalize(Vector3.Cross(basis.Up, toward));
            var up = Vector3.Cross(toward, right);
            bool iso = Math.Abs(Math.Abs(toward.X) - Math.Abs(toward.Y)) < .001f && Math.Abs(Math.Abs(toward.Y) - Math.Abs(toward.Z)) < .001f;
            return draft with
            {
                Camera = iso ? 4 : 0,
                Axes = new(right, up, toward),
                Parent = source.Id,
                Alignment = 0,
                RollDirection = direction,
                ProjectionAngle = angle
            };
        }
        var b = Axes(source);
        float sign = (direction < 2 ? 1 : -1) * (angle == 3 ? 1 : -1);
        var axes = direction % 2 == 0 ? new ViewAxes(-sign * b.Toward, b.Up, sign * b.Right) : new ViewAxes(b.Right, -sign * b.Toward, sign * b.Up);
        int camera = new[] { 1, 2, 3, 5, 6, 7 }.FirstOrDefault(c => Vector3.Dot(SheetLayout.Basis(c).Toward, axes.Toward) > .9999f, 0);
        return draft with
        {
            Camera = camera,
            Axes = axes,
            Parent = source.Id,
            Alignment = direction % 2 == 0 ? 2 : 1,
            RollDirection = direction,
            ProjectionAngle = angle
        };
    }


    public static int Direction(Vector2 delta) => Math.Abs(delta.X) >= Math.Abs(delta.Y) ? delta.X >= 0 ? 0 : 2 : delta.Y < 0 ? 1 : 3;


    public static int ProjectionDirection(Vector2 delta, int previous)
    {
        float major = Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)), minor = Math.Min(Math.Abs(delta.X), Math.Abs(delta.Y));
        // A wider exit band prevents repeated renders when hovering around a diagonal boundary.
        if (major > 0 && minor / major >= (previous >= 4 ? .4f : .55f))
            return delta.Y < 0 ? (delta.X >= 0 ? 4 : 5) : (delta.X < 0 ? 6 : 7);
        return Direction(delta);
    }


    // Manual footprints include caption clearance already; allow contact with the title rule.
    public static Box Area(PrintOptions options) => PageLayout.Placement(options);


    public static DrawingPlacement FromScene(RenderScene scene) => FromViews(scene.Views, scene.Options);


    public static DrawingPlacement FromViews(IEnumerable<ViewRegion> regions, PrintOptions options)
    {
        var views = regions.Where(v => v.Id < 1000).ToArray();
        float ratio = views.Length == 0 ? 100 : PrintScale.Density(options) / views[0].Frame!.Scale;
        return new(ratio, views.Select(v => new DrawingView(v.Id, v.CameraView, v.Bounds.Center.X / options.Page.Width, v.Bounds.Center.Y / options.Page.Height,
            Axes: v.Frame is { } f ? new(f.Right, f.Up, f.Toward) : null)).ToArray());
    }


    public static ViewRegion[] Plan(Capture capture, PrintOptions options, DrawingPlacement drawing) => PreparePlan(capture, options, drawing)(drawing.Ratio);


    // Capture projection extents once; scale previews and fit searches need no
    // rasterization or repeated walks over every mesh vertex.
    public static Func<float, ViewRegion[]> PreparePlan(Capture capture, PrintOptions options, DrawingPlacement drawing)
    {
        if (drawing.Views.Length > Limit || drawing.Views.Select(v => v.Id).Distinct().Count() != drawing.Views.Length)
            throw new InvalidOperationException("Invalid view collection.");
        var points = capture.Meshes.SelectMany(m => m.Vertices).Select(ExplodedGeometry.World).ToArray();
        var lo = points.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min);
        var hi = points.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max);
        var center = (lo + hi) / 2;
        var measured = drawing.Views.Select(v =>
        {
            if (v.Id < 0 || v.Id >= 1000 || v.Camera < 0 || v.Camera >= Names.Length || !float.IsFinite(v.X) || !float.IsFinite(v.Y))
                throw new InvalidOperationException("Invalid drawing view.");
            var visited = new HashSet<int> { v.Id };
            var cursor = v;
            while (cursor.Parent is { } parent)
            {
                if (!visited.Add(parent))
                    throw new InvalidOperationException("A projection cannot depend on itself.");
                cursor = drawing.Views.SingleOrDefault(p => p.Id == parent) ?? throw new InvalidOperationException("Projection source is missing.");
            }
            var b = Axes(v);
            if (!float.IsFinite(b.Right.LengthSquared() + b.Up.LengthSquared() + b.Toward.LengthSquared())
                || Math.Abs(b.Right.LengthSquared() - 1) > .001f || Math.Abs(b.Up.LengthSquared() - 1) > .001f || Math.Abs(b.Toward.LengthSquared() - 1) > .001f
                || Vector3.Distance(Vector3.Cross(b.Right, b.Up), b.Toward) > .001f)
                throw new InvalidOperationException("Invalid drawing orientation.");
            float dx = points.Max(p => Math.Abs(Vector3.Dot(p - center, b.Right))) * 2, dy = points.Max(p => Math.Abs(Vector3.Dot(p - center, b.Up))) * 2;
            return (View: v, Axes: b, Width: dx, Height: dy);
        }).ToArray();
        return ratio =>
        {
            if (!float.IsFinite(ratio) || ratio < .01f || ratio > 1000000)
                throw new InvalidOperationException("Choose a positive drawing scale between 1:0.01 and 1:1000000.");
            float scale = PrintScale.Density(options) / ratio;
            return measured.Select(m =>
            {
                var v = m.View;
                var b = m.Axes;
                float dx = m.Width, dy = m.Height;
                int w = Math.Max(100, (int)MathF.Ceiling(dx * scale) + 32), h = Math.Max(100, (int)MathF.Ceiling(dy * scale) + 32);
                // Bound renderer allocations before any large buffers are created.
                if (w > options.Page.Width || h + ViewCaption.Band > options.Page.Height)
                    throw new InvalidOperationException("This preview is too big for the canvas.");
                var box = new Box(MathF.Round(v.X * options.Page.Width - w / 2f), MathF.Round(v.Y * options.Page.Height - h / 2f), w, h);
                return new ViewRegion(v.Id, Name(v).ToUpperInvariant(), box, new(w, h, scale, center, b.Right, b.Up, b.Toward), v.Camera);
            }).ToArray();
        };
    }


    public static Box Footprint(ViewRegion v, bool captions = true) => v.Bounds with { Height = v.Bounds.Height + (captions ? ViewCaption.Band : 0) };


    public static void Validate(ViewRegion[] views, PrintOptions options, IEnumerable<Box>? obstacles = null)
    {
        var area = Area(options);
        var boxes = views.Select(v => Footprint(v, options.Captions)).ToArray();
        foreach (var b in boxes)
        {
            if (!PageLayout.Fits(b, options))
                throw new InvalidOperationException("Keep the view and its caption inside the drawing area; move it or reduce its scale.");
            if ((obstacles ?? []).Any(o => o.Overlaps(b)))
                throw new InvalidOperationException("Place the view clear of tables, details and performance blocks.");
        }
        for (int i = 0; i < boxes.Length; i++)
            for (int j = i + 1; j < boxes.Length; j++)
                if (boxes[i].Overlaps(boxes[j]))
                    throw new InvalidOperationException("Separate the views before committing this placement or scale.");
    }


    public static DrawingPlacement Move(DrawingPlacement layout, int id, Vector2 centre, PrintOptions options, bool unlock = false)
    {
        var old = layout.Views.Single(v => v.Id == id);
        var p = old.Parent is { } pid ? layout.Views.Single(v => v.Id == pid) : null;
        float x = centre.X / options.Page.Width, y = centre.Y / options.Page.Height;
        if (!unlock && p != null)
        {
            if (old.Alignment == 1)
                x = p.X;
            if (old.Alignment == 2)
                y = p.Y;
        }
        float dx = x - old.X, dy = y - old.Y;
        var affected = Dependents(layout, id);
        return layout with
        {
            Views = layout.Views.Select(v => affected.Contains(v.Id) ? v with { X = v.X + dx, Y = v.Y + dy, Alignment = unlock && v.Id == id ? 0 : v.Alignment } : v).ToArray()
        };
    }


    public static HashSet<int> Dependents(DrawingPlacement layout, int id)
    {
        var affected = new HashSet<int> { id };
        for (int i = 0; i < layout.Views.Length; i++)
            foreach (var v in layout.Views)
                if (v.Parent is { } parent && affected.Contains(parent))
                    affected.Add(v.Id);
        return affected;
    }


    public static DrawingPlacement Remove(DrawingPlacement layout, int id) => layout with { Views = layout.Views.Where(v => v.Id != id).Select(v => v.Parent == id ? v with { Parent = null, Alignment = 0 } : v).ToArray() };


    public static DrawingPlacement Repage(RenderScene previous, PrintOptions options) => Repage(previous.Options, previous.Views, previous.Drawing ?? FromScene(previous), options);


    public static DrawingPlacement Repage(PrintOptions before, IEnumerable<ViewRegion> regions, DrawingPlacement layout, PrintOptions options)
    {
        var views = regions.Where(v => v.Id < 1000).ToArray();
        if (views.Length == 0 || before.Page == options.Page)
            return layout;
        var area = PageLayout.UpperArea(options);
        float oldScale = views[0].Frame!.Scale, density = PrintScale.Density(options);
        var origin = views.Select(v => v.Bounds.Center).Aggregate(Vector2.Zero, (a, b) => a + b) / views.Length;
        var centres = views.Select(v => layout.Views.Single(d => d.Id == v.Id)).Select(v =>
            new Vector2(v.X * before.Page.Width, v.Y * before.Page.Height) - origin).ToArray();
        float caption = options.Captions ? ViewCaption.Band : 0;
        // Geometry scales, but the 32px capture padding, minimum tile size and
        // caption band do not. Solve centre spacing separately so a compressed
        // portrait template cannot overlap after moving to landscape.
        bool Fit(float factor, out float spacing, out Box bounds)
        {
            var sizes = views.Select(v => new Vector2(Math.Max(100, MathF.Ceiling((v.Bounds.Width - 32) * factor) + 32),
                Math.Max(100, MathF.Ceiling((v.Bounds.Height - 32) * factor) + 32))).ToArray();
            spacing = 0;
            for (int i = 0; i < views.Length; i++)
                for (int j = i + 1; j < views.Length; j++)
                {
                    var delta = Vector2.Abs(centres[j] - centres[i]);
                    float x = delta.X > .001f ? ((sizes[i].X + sizes[j].X) / 2 + 4) / delta.X : float.PositiveInfinity;
                    float y = delta.Y > .001f ? ((sizes[i].Y + sizes[j].Y) / 2 + caption + 4) / delta.Y : float.PositiveInfinity;
                    spacing = Math.Max(spacing, Math.Min(x, y));
                }
            var lo = new Vector2(float.PositiveInfinity);
            var hi = new Vector2(float.NegativeInfinity);
            for (int i = 0; i < views.Length; i++)
            {
                lo = Vector2.Min(lo, centres[i] * spacing - sizes[i] / 2);
                hi = Vector2.Max(hi, centres[i] * spacing + sizes[i] / 2 + new Vector2(0, caption));
            }
            bounds = new(lo.X, lo.Y, hi.X - lo.X, hi.Y - lo.Y);
            return float.IsFinite(spacing) && bounds.Width <= area.Width - 8 && bounds.Height <= area.Height - 8;
        }
        if (!Fit(0, out _, out _))
            throw new InvalidOperationException("These view positions cannot fit this paper even at minimum size. Move views farther apart or choose a larger sheet.");
        float low = 0, high = density / PrintScale.Density(before);
        if (Fit(high, out _, out _))
            low = high;
        else
            for (int i = 0; i < 40; i++)
            {
                float mid = (low + high) / 2;
                if (Fit(mid, out _, out _))
                    low = mid;
                else
                    high = mid;
            }
        float scale = PrintScale.FitScale(oldScale * low, options);
        if (!Fit(scale / oldScale, out var spacing, out var fitted) || scale <= 0)
            throw new InvalidOperationException("Cannot fit the views on this paper. Your previous print is unchanged.");
        var target = area.Center - fitted.Center;
        return new(density / scale, layout.Views.Select(v =>
        {
            var p = target + (new Vector2(v.X * before.Page.Width, v.Y * before.Page.Height) - origin) * spacing;
            return v with
            {
                X = p.X / options.Page.Width,
                Y = p.Y / options.Page.Height
            };
        }).ToArray());
    }



    // Reflow changes paper, not the user's relative view arrangement. Keep the
    // shared physical scale unless the new page requires smaller geometry.
    public static DrawingPlacement FitToPage(Capture capture, PrintOptions options, DrawingPlacement drawing, Box[]? obstacles = null)
    {
        var plan = PreparePlan(capture, options, drawing);
        var area = Area(options);
        var blocked = obstacles ?? [];
        bool Fits(float ratio, out Vector2 shift)
        {
            shift = Vector2.Zero;
            ViewRegion[] views;
            try
            {
                views = plan(ratio);
            }
            catch (InvalidOperationException) { return false; }
            bool Valid(Vector2 delta)
            {
                try
                {
                    Validate(views.Select(v => v with { Bounds = v.Bounds with { X = v.Bounds.X + delta.X, Y = v.Bounds.Y + delta.Y } }).ToArray(), options, blocked);
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }
            if (Valid(Vector2.Zero))
                return true;
            var boxes = views.Select(v => Footprint(v, options.Captions)).ToArray();
            if (boxes.Length == 0)
                return true;
            var boundaries = blocked.Append(PageLayout.TitleBounds(options)).ToArray();
            var xs = new[] { 0f, area.X - boxes.Min(b => b.X), area.Right - boxes.Max(b => b.Right) }
                .Concat(boxes.SelectMany(b => boundaries.SelectMany(o => new[] { o.X - b.Right - 4, o.Right - b.X + 4 }))).Distinct().ToArray();
            var ys = new[] { 0f, area.Y - boxes.Min(b => b.Y), area.Bottom - boxes.Max(b => b.Bottom) }
                .Concat(boxes.SelectMany(b => boundaries.SelectMany(o => new[] { o.Y - b.Bottom - 4, o.Bottom - b.Y + 4 }))).Distinct().ToArray();
            foreach (var delta in xs.SelectMany(x => ys.Select(y => new Vector2(x, y))).OrderBy(d => d.LengthSquared()))
                if (Valid(delta))
                {
                    shift = delta;
                    return true;
                }
            return false;
        }
        float low = drawing.Ratio, high = low;
        if (!Fits(high, out _))
        {
            while (high < 1000000 && !Fits(high, out _))
                high = Math.Min(1000000, high * 1.5f);
            if (!Fits(high, out _))
                throw new InvalidOperationException("There is not enough space for these views and supporting blocks on this page.");
            for (int i = 0; i < 28; i++)
            {
                float mid = (low + high) / 2;
                if (Fits(mid, out _))
                    high = mid;
                else
                    low = mid;
            }
            high *= 1.00001f;
        }
        Fits(high, out var shift);
        return drawing with
        {
            Ratio = high,
            Views = drawing.Views.Select(v => v with
            {
                X = v.X + shift.X / options.Page.Width,
                Y = v.Y + shift.Y / options.Page.Height
            }).ToArray()
        };
    }

    public static float SuggestRatio(Capture capture, int camera, PrintOptions options)
    {
        var pts = capture.Meshes.SelectMany(m => m.Vertices).Select(ExplodedGeometry.World).ToArray();
        var b = SheetLayout.Basis(camera);
        var area = PageLayout.UpperArea(options);
        float w = pts.Max(p => Vector3.Dot(p, b.Right)) - pts.Min(p => Vector3.Dot(p, b.Right)), h = pts.Max(p => Vector3.Dot(p, b.Up)) - pts.Min(p => Vector3.Dot(p, b.Up));
        return PrintScale.Density(options) / PrintScale.FitScale(Math.Min((area.Width - 64) * .65f / Math.Max(.001f, w), (area.Height - ViewCaption.Band - 32) * .65f / Math.Max(.001f, h)), options);
    }
}
