using System.Numerics;
namespace Blueprint.Rendering;

enum LeaderTip
{
    Dot, FilledTriangle, OutlineTriangle, None
}
enum LeaderLine
{
    Dotted, Dashed, Solid
}
enum LabelSymbol
{
    Square, Circle, Flag
}
sealed record LeaderPoint(float X, float Y)
{
    public Vector2 Vector => new(X, Y);
}
sealed record PartLabel(int Id, int ViewId, int PartId, string Text, float X, float Y, bool Visible = true, LeaderTip Tip = LeaderTip.Dot, LeaderLine Line = LeaderLine.Dotted,
    LeaderPoint? Attachment = null, LeaderPoint? Socket = null, bool ManualPlacement = false, bool FlagNote = false, string? Key = null, bool AttachmentPinned = false, bool ShowQuantity = false, int Quantity = 1, int FlagnoteNumber = 0,
    LabelSymbol Symbol = LabelSymbol.Square, bool FullText = false)
{


    public PartLabel MoveTo(float x, float y) => this with { X = x, Y = y, ManualPlacement = true, Socket = null, Attachment = AttachmentPinned ? Attachment : null };
}
sealed record LabelGeometry(PartLabel Label, Box TextBox, string[] Lines, Vector2 Start, Vector2 End);
static class AnnotationText
{
    public const int Font = 30, Leading = 38;


    public static string Clean(string text, int limit = 80)
    {
        string clean = new(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        clean = clean.Trim();
        return clean.Length > limit ? clean[..limit] : clean;
    }


    public static string[] Wrap(string text, int columns = 25, int limit = 80)
    {
        var result = new List<string>();
        string line = "";
        foreach (string word in Clean(text, limit).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string rest = word;
            if (line.Length > 0 && line.Length + rest.Length + 1 > columns)
            {
                result.Add(line);
                line = "";
            }
            while (rest.Length > columns)
            {
                if (line.Length > 0)
                {
                    result.Add(line);
                    line = "";
                }
                result.Add(rest[..columns]);
                rest = rest[columns..];
            }
            line = line.Length == 0 ? rest : line + " " + rest;
        }
        if (line.Length > 0)
            result.Add(line);
        return result.Count == 0 ? ["Part"] : result.ToArray();
    }


    public static Box Bounds(PartLabel label)
    {
        if (label.Key is { } key)
        {
            float width = Math.Max(54, key.Length * Font * .62f + 20);
            if (label.Symbol == LabelSymbol.Circle)
            {
                float diameter = MathF.Ceiling(new Vector2(key.Length * NativeText.MonoWidth(Font), Font).Length() + 14);
                return new(label.X, label.Y, Math.Max(54, diameter), Math.Max(54, diameter));
            }
            return new(label.X, label.Y, width + (label.Symbol == LabelSymbol.Flag ? FlagPointDepth : 0), 50);
        }
        var lines = Lines(label);
        return new(label.X, label.Y, lines.Max(l => l.Length) * NativeText.MonoWidth(Font) + 6, (lines.Length - 1) * Leading + Font);
    }


    public static string[] Lines(PartLabel label) => label.Key != null ? [label.Key] : Wrap(label.Text + (!label.FlagNote && label.ShowQuantity && label.Quantity > 1 ? $" (QTY {label.Quantity})" : ""), limit: 100);

    const float FlagPointDepth = 18;

    public static Vector2 FlagPoint(Box box, Vector2 part) => new(part.X < box.Center.X ? box.X : box.Right, box.Center.Y);

    public static Vector2 TextOrigin(PartLabel label, Box box, Vector2 part)
    {
        if (label.Key is not { } key)
            return new(box.X, box.Y);
        float center = box.Center.X;
        if (label.Symbol == LabelSymbol.Flag)
            center += FlagPoint(box, part).X == box.X ? FlagPointDepth / 2 : -FlagPointDepth / 2;
        return new(center - key.Length * Font * .31f, box.Center.Y - 19);
    }

    public static Vector2[] SymbolOutline(LabelSymbol symbol, Box box, Vector2 part)
    {
        if (symbol == LabelSymbol.Circle)
            return Enumerable.Range(0, 64).Select(i =>
            {
                float angle = MathF.Tau * i / 64;
                return box.Center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (box.Width / 2);
            }).ToArray();
        if (symbol == LabelSymbol.Flag)
        {
            var tip = FlagPoint(box, part);
            bool pointsLeft = tip.X == box.X;
            float shoulder = tip.X + (pointsLeft ? FlagPointDepth : -FlagPointDepth);
            float back = pointsLeft ? box.Right : box.X;
            return [tip, new(shoulder, box.Y), new(back, box.Y), new(back, box.Bottom), new(shoulder, box.Bottom)];
        }
        return [new(box.X, box.Y), new(box.Right, box.Y), new(box.Right, box.Bottom), new(box.X, box.Bottom)];
    }
}

static class LeaderRouting
{


    public static bool TryLayout(RenderScene scene, IReadOnlyList<PartLabel> labels, out LabelGeometry[] result, out string reason)
    {
        var routes = new List<LabelGeometry>();
        var visible = labels.Where(l => l.Visible).ToArray();
        if (!PrintTables.Fits(scene, visible, out reason))
        {
            result = [];
            return false;
        }
        foreach (var label in visible)
        {
            var box = AnnotationText.Bounds(label);
            var view = scene.Views.FirstOrDefault(v => v.Id == label.ViewId);
            var allowed = label.ManualPlacement ? new Box(0, 0, scene.Width, scene.Height) : PageLayout.Placement(scene.Options);
            if (view == null || !CanvasBounds.Safe(box) || !scene.Options.FreeLayout && (box.X < allowed.X || box.Y < allowed.Y || box.Right > allowed.Right || box.Bottom > allowed.Bottom))
            {
                result = [];
                reason = "Keep the whole label inside the drawing area.";
                return false;
            }
            if (label.ManualPlacement || scene.Options.FreeLayout)
            {
                // A projection can turn a labeled surface out of sight. Keep
                // the label in the document so it returns with the surface.
                if (scene.Options.FreeLayout && !scene.Visible.ContainsKey((label.ViewId, label.PartId)) &&
                    scene.Parts.Any(p => p.Id == label.PartId))
                    continue;
                if (!ManualRoute(scene, label, box, out var manual))
                {
                    result = [];
                    reason = "Part is no longer visible in this snapshot.";
                    return false;
                }
                routes.Add(manual!);
                continue;
            }
            if (PrintTables.Reserved(scene, labels).Any(b => box.Overlaps(b, 8)))
            {
                result = [];
                reason = "Keep view captions and tables clear.";
                return false;
            }
            if (visible.Any(other => other.Id != label.Id && !other.ManualPlacement && box.Overlaps(AnnotationText.Bounds(other), 8)))
            {
                result = [];
                reason = "Labels need a little space between them.";
                return false;
            }
            if (!Empty(scene, box, 4))
            {
                result = [];
                reason = "Move the label off the craft.";
                return false;
            }
            var otherBoxes = visible.Where(other => other.Id != label.Id && !other.ManualPlacement).Select(AnnotationText.Bounds).Concat(PrintTables.Reserved(scene, labels)).ToArray();
            if (!TryRoute(scene, label, box, otherBoxes, routes.Where(r => !r.Label.ManualPlacement).ToList(), out var route))
            {
                result = [];
                reason = "No clear leader to this part's near edge. Try another position.";
                return false;
            }
            routes.Add(route!);
        }
        result = routes.ToArray();
        if (!TextBoxPlacement.LabelsClear(scene, result))
        {
            result = [];
            reason = "Keep labels and leader lines clear of text boxes.";
            return false;
        }
        reason = "";
        return true;
    }


    public static bool AutomaticRoute(RenderScene scene, PartLabel label, IReadOnlyList<LabelGeometry> existing, out LabelGeometry? route)
        => TryRoute(scene, label, AnnotationText.Bounds(label), existing.Select(r => r.TextBox).Concat(PrintTables.Reserved(scene, existing.Select(r => r.Label).ToArray())).ToArray(), existing.ToList(), out route);


    static bool ManualRoute(RenderScene scene, PartLabel label, Box box, out LabelGeometry? route)
    {
        route = null;
        if (!scene.Visible.TryGetValue((label.ViewId, label.PartId), out var part))
            return false;
        // Manual routes use the shortest visible boundary-to-box connection, not
        // the automatic clearance filter or a sampled perimeter/centre ray.
        var end = label.AttachmentPinned ? label.Attachment?.Vector ?? part.Bounds.Center : part.Bounds.Center;
        if (!label.AttachmentPinned || label.Attachment == null)
        {
            float best = float.PositiveInfinity, bestCenter = float.PositiveInfinity;
            foreach (var candidate in part.ContactEdges)
            {
                float distance = Vector2.DistanceSquared(candidate, TextSocket(label, box, candidate));
                float center = Vector2.DistanceSquared(candidate, box.Center);
                if (distance < best || (distance == best && center < bestCenter))
                {
                    best = distance;
                    bestCenter = center;
                    end = candidate;
                }
            }
        }
        var start = TextSocket(label, box, end);
        route = new(label, box, AnnotationText.Lines(label), start, end);
        return true;
    }


    public static Vector2 NearestBoxEdge(Box box, Vector2 point)
    {
        var nearest = new Vector2(Math.Clamp(point.X, box.X, box.Right), Math.Clamp(point.Y, box.Y, box.Bottom));
        if (point.X <= box.X || point.X >= box.Right || point.Y <= box.Y || point.Y >= box.Bottom)
            return nearest;
        float left = point.X - box.X, right = box.Right - point.X, top = point.Y - box.Y, bottom = box.Bottom - point.Y;
        float distance = Math.Min(Math.Min(left, right), Math.Min(top, bottom));
        if (distance == left)
            return new(box.X, point.Y);
        if (distance == right)
            return new(box.Right, point.Y);
        return distance == top ? new(point.X, box.Y) : new(point.X, box.Bottom);
    }


    public static Vector2 TextSocket(PartLabel label, Box box, Vector2 point)
    {
        if (label.Key != null)
        {
            if (label.Symbol == LabelSymbol.Flag)
                return AnnotationText.FlagPoint(box, point);
            if (label.Symbol == LabelSymbol.Circle)
            {
                var direction = point - box.Center;
                return box.Center + (direction.LengthSquared() > 0 ? Vector2.Normalize(direction) : Vector2.UnitX) * (box.Width / 2);
            }
        }
        Vector2[] sockets = label.Key == null
            ? [new(box.X, box.Center.Y), new(box.Right, box.Center.Y)]
            : [new(box.X,box.Y),new(box.Right,box.Y),new(box.Right,box.Bottom),new(box.X,box.Bottom),
              new(box.X,box.Center.Y),new(box.Right,box.Center.Y),new(box.Center.X,box.Y),new(box.Center.X,box.Bottom)];
        return sockets.MinBy(p => Vector2.DistanceSquared(p, point));
    }


    public static bool Empty(RenderScene scene, Box box, int margin)
    {
        for (int y = (int)box.Y - margin; y <= box.Bottom + margin; y++)
            for (int x = (int)box.X - margin; x <= box.Right + margin; x++)
                if (scene.Owner(x, y) >= 0)
                    return false;
        return true;
    }


    static bool TryRoute(RenderScene scene, PartLabel label, Box box, Box[] otherBoxes, List<LabelGeometry> existing, out LabelGeometry? result)
    {
        result = null;
        if (!scene.Visible.TryGetValue((label.ViewId, label.PartId), out var part))
            return false;
        IEnumerable<Vector2> candidates = label.AttachmentPinned && label.Attachment is { } pinned ? [pinned.Vector] : part.Edges.OrderBy(p => Vector2.DistanceSquared(p, TextSocket(label, box, p))).ThenBy(p => Vector2.DistanceSquared(p, box.Center)).Take(64);
        foreach (var candidate in candidates)
        {
            Vector2 start = TextSocket(label, box, candidate);
            if (Vector2.DistanceSquared(start, candidate) < 1 ||
                (label.Key != null && label.Symbol == LabelSymbol.Circle ? box.Contains(candidate) : box.Contains(start + Vector2.Normalize(candidate - start) * 3)))
                continue;
            if (!FirstHit(scene, start, candidate, out var hit, out int owner) || owner != label.PartId)
                continue;
            if (label.AttachmentPinned && label.Attachment != null && Vector2.DistanceSquared(hit, candidate) > 2)
                continue;
            if (Vector2.Distance(start, hit) < 22)
                continue;
            // Validate the complete stroke plus its endpoint footprint, not only
            // the center line. Only this target can occupy the tiny contact zone.
            if (!ClearStroke(scene, start, hit, label.PartId, label.Tip))
                continue;
            if (otherBoxes.Any(b => SegmentBox(start, hit, b, 8)))
                continue;
            if (existing.Any(r => SegmentsNear(start, hit, r.Start, r.End, 12) || SegmentBox(r.Start, r.End, box, 8)))
                continue;
            result = new(label, box, AnnotationText.Lines(label), start, hit);
            return true;
        }
        return false;
    }


    public static LeaderPoint? SnapAttachment(RenderScene scene, PartLabel label, Vector2 point, float radius)
    {
        var view = scene.Views.FirstOrDefault(v => v.Id == label.ViewId);
        if (view == null)
            return null;
        float best = radius * radius;
        LeaderPoint? result = null;
        if (scene.Options.FreeLayout && scene.Visible.TryGetValue((label.ViewId, label.PartId), out var visual))
        {
            // Clicking anywhere on the visible part selects its nearest raster
            // silhouette edge, even when the part interior is wider than the snap radius.
            if (scene.PickSurface(label.ViewId, point)?.Part == label.PartId)
                best = float.PositiveInfinity;
            foreach (var edge in visual.ContactEdges.Length == 0 ? visual.Edges : visual.ContactEdges)
            {
                float distance = Vector2.DistanceSquared(point, edge);
                if (distance <= best) { best = distance; result = new(edge.X, edge.Y); }
            }
            return result;
        }
        for (int y = (int)Math.Max(view.Bounds.Y, point.Y - radius); y <= Math.Min(view.Bounds.Bottom - 1, point.Y + radius); y++)
            for (int x = (int)Math.Max(view.Bounds.X, point.X - radius); x <= Math.Min(view.Bounds.Right - 1, point.X + radius); x++)
            {
                if (scene.Owner(x, y) != label.PartId)
                    continue;
                if (scene.Owner(x - 1, y) == label.PartId && scene.Owner(x + 1, y) == label.PartId && scene.Owner(x, y - 1) == label.PartId && scene.Owner(x, y + 1) == label.PartId)
                    continue;
                float d = Vector2.DistanceSquared(point, new(x, y));
                if (d <= best)
                {
                    best = d;
                    result = new(x, y);
                }
            }
        return result;
    }


    public static bool FirstHit(RenderScene scene, Vector2 start, Vector2 end, out Vector2 hit, out int owner)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(start, end) * 2));
        for (int i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(start, end, i / (float)steps);
            int x = (int)MathF.Round(p.X), y = (int)MathF.Round(p.Y);
            int id = scene.Owner(x, y);
            if (id >= 0)
            {
                hit = new(x, y);
                owner = id;
                return true;
            }
        }
        hit = default;
        owner = -1;
        return false;
    }


    internal static bool ClearStroke(RenderScene scene, Vector2 start, Vector2 end, int target, LeaderTip tip, float contactDistance = 3)
    {
        float length = Vector2.Distance(start, end);
        var away = Vector2.Normalize(start - end);
        var side = new Vector2(-away.Y, away.X);
        for (float t = 0; t <= length; t += 1)
        {
            var p = start - away * t;
            for (int y = -2; y <= 2; y++)
                for (int x = -2; x <= 2; x++)
                {
                    int id = scene.Owner((int)MathF.Round(p.X) + x, (int)MathF.Round(p.Y) + y);
                    if (id >= 0 && !(id == target && length - t <= contactDistance))
                        return false;
                }
        }
        if (tip == LeaderTip.Dot)
        {
            for (int y = -5; y <= 5; y++)
                for (int x = -5; x <= 5; x++)
                    if (x * x + y * y <= 25)
                    {
                        int id = scene.Owner((int)end.X + x, (int)end.Y + y);
                        if (id >= 0 && id != target)
                            return false;
                    }
        }
        else if (tip != LeaderTip.None)
            for (int t = 0; t <= 16; t++)
                for (int s = -6; s <= 6; s++)
                    if (Math.Abs(s) <= t * 6 / 16f + 1)
                    {
                        var p = end + away * t + side * s;
                        int id = scene.Owner((int)MathF.Round(p.X), (int)MathF.Round(p.Y));
                        if (id >= 0 && !(id == target && t <= 2))
                            return false;
                    }
        return true;
    }


    internal static bool SegmentBox(Vector2 a, Vector2 b, Box box, float margin)
    {
        var expanded = new Box(box.X - margin, box.Y - margin, box.Width + margin * 2, box.Height + margin * 2);
        int steps = Math.Max(1, (int)Math.Ceiling(Vector2.Distance(a, b) / 2));
        for (int i = 0; i <= steps; i++)
            if (expanded.Contains(Vector2.Lerp(a, b, i / (float)steps)))
                return true;
        return false;
    }


    static float PointSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var d = b - a;
        return Vector2.Distance(p, a + d * Math.Clamp(Vector2.Dot(p - a, d) / Math.Max(d.LengthSquared(), .001f), 0, 1));
    }


    static bool SegmentsNear(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float margin)
    {
        static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
        if (Cross(b - a, c - a) * Cross(b - a, d - a) < 0 && Cross(d - c, a - c) * Cross(d - c, b - c) < 0)
            return true;
        return Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)), Math.Min(PointSegment(c, a, b), PointSegment(d, a, b))) < margin;
    }
}

sealed class AnnotationDocument
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public long Revision { get; private set; }
    public RenderScene Scene
    {
        get; private set;
    }
    public ExplodedDetail[] Details { get; private set; } = [];
    public TableLayout Tables { get; private set; } = new();
    public PartLabel[] Labels
    {
        get; private set;
    }
    public LabelGeometry[] Routes { get; private set; } = [];
    public const int Limit = 120;


    readonly List<DrawingContent> undo = [], redo = [];


    public DrawingContent Snapshot() => new DrawingContent(Labels, Details, Tables, Scene.Captions, Scene.Dimensions, Scene.Kittens, Scene.Poses, Scene.Specs, Scene.FreeText, Scene.Drawing, Scene.Options) { WearMarks = Scene.WearMarks }.Copy();

    public AnnotationDocument Fork() => new(this);

    AnnotationDocument(AnnotationDocument source)
    {
        Id = source.Id;
        Revision = source.Revision;
        Scene = source.Scene;
        Labels = source.Labels;
        Details = source.Details;
        Tables = source.Tables;
        Routes = source.Routes;
        nextId = source.nextId;
        undo = [.. source.undo];
        redo = [.. source.redo];
    }

    public void Restore(AnnotationDocument accepted)
    {
        if (Id != accepted.Id) throw new InvalidOperationException("Cannot restore a different drawing.");
        Scene = accepted.Scene;
        Labels = accepted.Labels;
        Details = accepted.Details;
        Tables = accepted.Tables;
        Routes = accepted.Routes;
        nextId = Math.Max(nextId, accepted.nextId);
        undo.Clear(); undo.AddRange(accepted.undo);
        redo.Clear(); redo.AddRange(accepted.redo);
        Revision = checked(Revision + 1);
    }


    void Remember()
    {
        undo.Add(Snapshot());
        if (undo.Count > 64)
            undo.RemoveAt(0);
        redo.Clear();
    }


    public void Rebind(RenderScene scene, PartLabel[] labels, ExplodedDetail[] details, bool remember = true)
    {
        labels = PrintTables.ResolveLabelKeys(scene, labels);
        if (remember && (!Details.SequenceEqual(details) || !Labels.SequenceEqual(labels) || Tables != scene.Tables || !Scene.Captions.SequenceEqual(scene.Captions) || !Scene.Dimensions.SequenceEqual(scene.Dimensions) || !Scene.Kittens.SequenceEqual(scene.Kittens) || !Scene.Poses.SequenceEqual(scene.Poses) || !Scene.Specs.SequenceEqual(scene.Specs) || !Scene.FreeText.SequenceEqual(scene.FreeText) || !Scene.WearMarks.SequenceEqual(scene.WearMarks) || Scene.Drawing != scene.Drawing || Scene.Options != scene.Options))
            Remember();
        Scene = scene;
        Tables = scene.Tables;
        Labels = labels;
        Details = details;
        Revision = checked(Revision + 1);
        if (labels.Length > 0)
            nextId = Math.Max(nextId, labels.Max(l => l.Id) + 1);
        RefreshRoutes();
    }


    public void Regenerate(RenderScene scene, PartLabel[] labels, ExplodedDetail[] details, bool sourceChanged = false)
    {
        bool paperChange = Scene.Options.OutputPage != scene.Options.OutputPage;
        Rebind(scene, labels, details);
        if (sourceChanged || !paperChange)
        {
            undo.Clear();
            redo.Clear();
        }
    }
    int nextId = 1;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;


    public AnnotationDocument(RenderScene scene, PartLabel[]? initial = null)
    {
        Scene = scene;
        Tables = scene.Tables;
        Labels = PrintTables.ResolveLabelKeys(scene, initial ?? []);
        nextId = Labels.Length == 0 ? 1 : Labels.Max(l => l.Id) + 1;
        RefreshRoutes();
    }


    void RefreshRoutes()
    {
        LeaderRouting.TryLayout(Scene, Labels, out var routes, out _);
        Routes = routes;
    }


    public bool Apply(PartLabel[] labels, out string reason)
    {
        labels = PrintTables.ResolveLabelKeys(Scene, labels);
        if (labels.Length > Limit)
        {
            reason = $"This sheet supports up to {Limit} annotations.";
            return false;
        }
        if (!LeaderRouting.TryLayout(Scene, labels, out var routes, out reason))
            return false;
        if (Labels.SequenceEqual(labels))
            return true;
        Remember();
        Labels = labels;
        Routes = routes;
        Revision = checked(Revision + 1);
        if (labels.Length > 0)
            nextId = Math.Max(nextId, labels.Max(l => l.Id) + 1);
        return true;
    }


    public void Undo()
    {
        if (!CanUndo)
            return;
        redo.Add(Snapshot());
        var state = undo[^1];
        bool changedPaper = Scene.Options.OutputPage != state.Options.OutputPage;
        Labels = state.Labels;
        Details = state.Details;
        Tables = state.Tables;
        Scene = state.ApplyTo(Scene);
        Revision = checked(Revision + 1);
        undo.RemoveAt(undo.Count - 1);
        if (changedPaper)
            Routes = [];
        else
            RefreshRoutes();
    }


    public void Redo()
    {
        if (!CanRedo)
            return;
        undo.Add(Snapshot());
        var state = redo[^1];
        bool changedPaper = Scene.Options.OutputPage != state.Options.OutputPage;
        Labels = state.Labels;
        Details = state.Details;
        Tables = state.Tables;
        Scene = state.ApplyTo(Scene);
        Revision = checked(Revision + 1);
        redo.RemoveAt(redo.Count - 1);
        if (changedPaper)
            Routes = [];
        else
            RefreshRoutes();
    }


    public bool Change(PartLabel label, out string reason) => Apply(Labels.Select(l => l.Id == label.Id ? label : l).ToArray(), out reason);


    public bool Delete(int id, out string reason) => Apply(Labels.Where(l => l.Id != id).ToArray(), out reason);


    public PartLabel? Draft(int viewId, int partId, out string reason, LeaderLine line = LeaderLine.Dotted, LeaderTip tip = LeaderTip.Dot, string? note = null, LabelSymbol symbol = LabelSymbol.Square)
    {
        if (Labels.Length >= Limit)
        {
            reason = $"This sheet supports up to {Limit} annotations.";
            return null;
        }
        if (!Scene.Visible.ContainsKey((viewId, partId)))
        {
            reason = "Part is hidden in this view.";
            return null;
        }
        reason = "Drag or click to place the label anywhere on the sheet. Right-click to cancel.";
        int flagNumber = note == null ? 0 : FlagnoteNumbers.Next(Labels);
        return new(nextId, viewId, partId, AnnotationText.Clean(note ?? Scene.Parts.Single(p => p.Id == partId).Name), 0, 0, Tip: tip, Line: line, ManualPlacement: true, FlagNote: note != null,
            Key: note != null ? (Scene.Options.FlagTable ? $"F{flagNumber}" : null) : (Scene.Options.PartsTable ? PrintTables.PartKey(Scene, partId) : null),
            Quantity: note == null ? LabelQuantities.Count(Scene, viewId, partId) : 1, FlagnoteNumber: flagNumber, Symbol: symbol);
    }


    public int Add(int viewId, int partId, out string reason, LeaderLine line = LeaderLine.Dotted, LeaderTip tip = LeaderTip.Dot, CancellationToken token = default)
    {
        var existing = Labels.FirstOrDefault(l => !l.FlagNote && l.ViewId == viewId && l.PartId == partId);
        if (existing != null)
        {
            reason = "";
            return existing.Id;
        }
        if (!Scene.Visible.TryGetValue((viewId, partId), out var visual))
        {
            reason = "Part is hidden in this view.";
            return -1;
        }
        // View bounds follow the craft ink; labels need the clear paper around it.
        // Global collision and routing checks still protect other content.
        var area = PageLayout.Placement(Scene.Options);
        var label = Draft(viewId, partId, out reason, line, tip);
        if (label == null)
            return -1;
        label = label with
        {
            ManualPlacement = false
        };
        var size = AnnotationText.Bounds(label);
        var positions = new List<Vector2>();
        foreach (int gap in new[] { 24, 40, 80, 140 })
            foreach (int offset in new[] { 0, -80, 80, -160, 160, -240, 240 })
            {
                positions.Add(new(visual.Bounds.X - size.Width - gap, visual.Bounds.Center.Y - size.Height / 2 + offset));
                positions.Add(new(visual.Bounds.Right + gap, visual.Bounds.Center.Y - size.Height / 2 + offset));
                positions.Add(new(visual.Bounds.Center.X - size.Width / 2 + offset, visual.Bounds.Y - size.Height - gap));
                positions.Add(new(visual.Bounds.Center.X - size.Width / 2 + offset, visual.Bounds.Bottom + gap));
            }
        var candidates = positions.Select(p => label with { X = Math.Clamp(p.X, area.X + 8, Math.Max(area.X + 8, area.Right - size.Width - 8)), Y = Math.Clamp(p.Y, area.Y + 8, Math.Max(area.Y + 8, area.Bottom - size.Height - 8)) })
            .DistinctBy(l => (l.X, l.Y)).OrderBy(l =>
            {
                var b = AnnotationText.Bounds(l);
                return visual.Edges.Select(e => Vector2.DistanceSquared(e, new(Math.Clamp(e.X, b.X, b.Right), Math.Clamp(e.Y, b.Y, b.Bottom)))).DefaultIfEmpty(float.PositiveInfinity).Min();
            });
        foreach (var candidate in candidates)
        {
            token.ThrowIfCancellationRequested();
            var item = candidate;
            var box = AnnotationText.Bounds(item);
            if (!LeaderRouting.Empty(Scene, box, 4) || Labels.Any(l => l.Visible && box.Overlaps(AnnotationText.Bounds(l), 8)))
                continue;
            if (!LeaderRouting.AutomaticRoute(Scene, item, Routes, out var route))
                continue;
            item = item with
            {
                Attachment = new(route!.End.X, route.End.Y),
                Socket = new((route.Start.X - box.X) / box.Width, (route.Start.Y - box.Y) / box.Height)
            };
            if (Apply([.. Labels, item], out reason))
                return item.Id;
        }
        reason = "No clear space for this label in this view. Try a different view or hide another label.";
        return -1;
    }


    public string AddAll(LeaderLine line, LeaderTip tip, CancellationToken token, Action<string>? progress = null)
    {
        int added = 0, existing = 0, blocked = 0, limited = 0;
        var candidates = Scene.Visible.OrderBy(p => p.Key.View).ThenByDescending(p => p.Value.Area).ToArray();
        foreach (var pair in candidates)
        {
            token.ThrowIfCancellationRequested();
            if (Labels.Any(l => !l.FlagNote && l.ViewId == pair.Key.View && l.PartId == pair.Key.Part))
            {
                existing++;
                continue;
            }
            if (Labels.Length >= Limit)
            {
                limited++;
                continue;
            }
            progress?.Invoke($"Auto label: {added} added; {blocked} blocked");
            if (Add(pair.Key.View, pair.Key.Part, out _, line, tip, token) >= 0)
                added++;
            else
                blocked++;
        }
        return $"Auto label: {added} added, {existing} already present, {blocked} without a clear route" + (limited > 0 ? $", {limited} beyond the {Limit}-annotation limit" : "") + ". Hidden parts cannot receive leaders.";
    }


    public void AddMajorParts()
    {
        foreach (var view in Scene.Views)
        {
            var parts = Scene.Visible.Where(p => p.Key.View == view.Id).Select(p => p.Value).OrderByDescending(p => p.Area).ToArray();
            int max = parts.FirstOrDefault()?.Area ?? 0, added = 0;
            foreach (var part in parts.Where(p => p.Area >= Math.Max(100, max * .08)))
            {
                if (Add(view.Id, part.PartId, out _) >= 0 && ++added >= (Scene.Views.Length == 1 ? 5 : 2))
                    break;
            }
        }
        undo.Clear();
        redo.Clear();
    }
}

static class AnnotationPainting
{


    public static Sheet Compose(Sheet source, PartLabel[] labels, CancellationToken token, TableLayout? tables = null)
    {
        var scene = source.Scene ?? throw new InvalidOperationException("No annotation geometry.");
        labels = PrintTables.ResolveLabelKeys(scene, labels).Select(l => LabelQuantities.Refresh(scene, l)).ToArray();
        if (tables != null)
        {
            scene = scene.WithTables(tables);
            source = source with
            {
                Scene = scene
            };
        }
        if (!LeaderRouting.TryLayout(scene, labels, out var routes, out var reason))
            throw new InvalidOperationException(reason);
        var canvas = new Canvas((byte[])scene.BaseRgb.Clone(), scene.Width, scene.Height, SheetLayout.Colors(source.Options.Theme).Ink)
        { HitShapes = scene.Options.FreeLayout ? [] : null };
        foreach (var route in routes)
        {
            token.ThrowIfCancellationRequested();
            canvas.HitTarget = new(DrawingObjectKind.Label, route.Label.Id);
            LeaderPainting.Paint(canvas, [route.Start, route.End], route.Label.Line, route.Label.Tip, route.Label.Key == null ? 10 : 0);
            if (route.Label.Key is { } key)
            {
                var outline = AnnotationText.SymbolOutline(route.Label.Symbol, route.TextBox, route.End);
                for (int i = 0; i < outline.Length; i++)
                {
                    var a = outline[i];
                    var b = outline[(i + 1) % outline.Length];
                    canvas.Line((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, 1);
                }
                var origin = AnnotationText.TextOrigin(route.Label, route.TextBox, route.End);
                canvas.Text(key, (int)origin.X, (int)origin.Y, AnnotationText.Font, 1);
            }
            else
                for (int i = 0; i < route.Lines.Length; i++)
                    canvas.Text(route.Lines[i], (int)route.TextBox.X, (int)route.TextBox.Y + i * AnnotationText.Leading, AnnotationText.Font, 1);
        }
        canvas.HitTarget = null;
        PrintTables.Paint(canvas, scene, labels);
        List<(Vector2 Start, Vector2 End)>? specLeaders = scene.FreeText.Length > 0 ? [] : null;
        PerformancePainting.Paint(canvas, scene, labels, specLeaders);
        canvas.HitTarget = null;
        HeightDimensions.Paint(canvas, scene, labels);
        KittenReferences.Paint(canvas, scene, token);
        canvas.HitTarget = null;
        string? titleScale = PrintScale.PrintedTitleScale(scene.Views, scene.Options);
        foreach (var caption in scene.Captions.Where(c => scene.Options.FreeLayout || !scene.Details.Any(d => d.Definition.Bounds.Overlaps(c.Bounds))))
        {
            canvas.Text(caption.Text, (int)caption.Bounds.X, (int)caption.Bounds.Y, caption.FontSize, .85f);
            if (scene.Options.FreeLayout && scene.Views.FirstOrDefault(v => v.Id == caption.ViewId) is { } view)
            {
                string ratio = PrintScale.Ratio(view.Frame, scene.Options);
                if (ratio != titleScale)
                {
                    string scale = "SCALE " + ratio;
                    canvas.Text(scale, (int)(caption.Bounds.Center.X - scale.Length * NativeText.MonoWidth(15) / 2), (int)caption.Bounds.Bottom + 4, 15, 1);
                }
            }
        }
        TextBoxPlacement.Validate(scene, labels, routes, specLeaders ?? []);
        FreeTextPainting.Paint(canvas, scene.FreeText);
        NativeText.Apply(canvas.Pixels, scene.Width, scene.Height, canvas.Runs);
        token.ThrowIfCancellationRequested();
        Vector2[] fields = scene.BaseLettering;
        if (source.Options.Archive && source.Options.WearLevel > 0 && (fields.Length < 4 || !source.Options.TitleBlockVisible))
            fields = WearPlacement.Lettering(canvas.Runs, source.Options.Page).Concat(fields).Distinct().ToArray();
        var wear = WearPlacement.Resolve(source.Options, source.Title, fields, scene.WearMarks);
        WearPainting.Apply(canvas.Pixels, scene.Width, scene.Height, source.Options.Theme, wear, token);
        return source.WithPixels(canvas.Pixels) with
        {
            Labels = labels,
            AppliedWear = wear, ObjectHits = canvas.HitShapes
        };
    }

}
