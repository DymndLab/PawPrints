using System.Numerics;
namespace Blueprint.Rendering;

// Paths run from the block to the part: their first segment is the horizontal
// landing at the block. At most two elbows, and only the final contact touches ink.
static class SpecLeaderRouting
{

    public static Vector2[] Route(RenderScene scene, SpecBlock block, IReadOnlyList<PartLabel> labels)
    {
        var part = scene.Parts.FirstOrDefault(p => p.NodeId == block.EnginePart && scene.Visible.ContainsKey((block.ViewId, p.Id)));
        if (part == null || !scene.Visible.TryGetValue((block.ViewId, part.Id), out var visual))
            return [];
        var boxes = scene.Specs.Where(b => b.Id != block.Id).Select(b => b.Bounds)
            .Concat(PrintTables.Panels(scene, labels).Select(p => p.Bounds))
            .Concat(scene.Details.Select(d => d.Definition.Bounds))
            .Concat(scene.Captions.Select(c => c.Bounds))
            .Concat(scene.FreeText.Select(b => b.Bounds))
            .Concat(labels.Where(l => l.Visible).Select(AnnotationText.Bounds)).Append(PageLayout.TitleBounds(scene.Options)).ToArray();
        var shapes = scene.Visible.Values.Select(p => p.Bounds).Concat(boxes)
            .OrderBy(b => Vector2.DistanceSquared(b.Center, visual.Bounds.Center)).Take(64).ToArray();
        var sourceParts = scene.Visible.Where(p => p.Key.View == block.ViewId).Select(p => p.Value.Bounds).ToArray();
        float outerLeft = sourceParts.Min(b => b.X) - 12, outerRight = sourceParts.Max(b => b.Right) + 12;
        var edges = visual.Edges.Length > 0 ? visual.Edges : visual.ContactEdges;
        // Keep nearby contacts and uniform samples: a blocked near edge must not
        // hide a clear approach around the opposite end of an intervening part.
        var ends = edges.OrderBy(p => Vector2.DistanceSquared(p, block.Bounds.Center)).Take(16)
            .Concat(edges.Where((_, i) => i % Math.Max(1, (int)Math.Ceiling(edges.Length / 32d)) == 0)).Distinct().ToArray();
        var area = PageLayout.Placement(scene.Options);
        var strokes = new Dictionary<(Vector2, Vector2), bool>();
        bool Clear(Vector2[] path)
        {
            for (int i = 1; i < path.Length; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                if (!area.Contains(a) || !area.Contains(b)
                    || boxes.Any(box => LeaderRouting.SegmentBox(a, b, box, 6))
                    || i > 1 && LeaderRouting.SegmentBox(a, b, block.Bounds, 2))
                    return false;
                if (i == path.Length - 1)
                {
                    if (!LeaderRouting.FirstHit(scene, a, b, out var hit, out int owner) || owner != part.Id
                        || Vector2.DistanceSquared(hit, b) > 4 || !LeaderRouting.ClearStroke(scene, a, hit, part.Id, block.Tip, 10))
                        return false;
                    path[i] = hit;
                }
                else
                {
                    if (!strokes.TryGetValue((a, b), out bool free))
                        strokes[(a, b)] = free = LeaderRouting.ClearStroke(scene, a, b, -1, LeaderTip.None);
                    if (!free)
                        return false;
                }
            }
            return true;
        }
        // Common straight/elbow routes resolve before allocating corner detours.
        var proposals = new List<(float Cost, Vector2[] Path)>();
        void Add(params Vector2[] path)
        {
            var p = path.Where((v, i) => i == 0 || Vector2.DistanceSquared(v, path[i - 1]) > .25f).ToArray();
            if (p.Length < 2 || p.Length > 4 || Math.Abs(p[0].Y - p[1].Y) > .1f)
                return;
            if (block.Bounds.Contains(Vector2.Lerp(p[0], p[1], .01f)))
                return;
            float length = 0;
            for (int i = 1; i < p.Length; i++)
                length += Vector2.Distance(p[i - 1], p[i]);
            proposals.Add((length + (p.Length - 2) * 24, p));
        }
        foreach (var end in ends)
            foreach (float sx in new[] { block.Bounds.X, block.Bounds.Right })
                foreach (float sy in new[] { Math.Clamp(end.Y, block.Bounds.Y + 12, block.Bounds.Bottom - 12), block.Bounds.Y + 12, block.Bounds.Center.Y, block.Bounds.Bottom - 12 }.Distinct())
                {
                    var start = new Vector2(sx, sy);
                    float sign = sx == block.Bounds.X ? -1 : 1;
                    Add(start, end);
                    Add(start, new(end.X, sy), end);
                    float[] xs = [sx + sign * 24, sx + sign * 60, .. shapes.SelectMany(b => new[] { b.X - 12, b.Right + 12 })];
                    foreach (float x in xs.Distinct().Where(x => (x - sx) * sign > 3 && x >= area.X && x <= area.Right)
                        .OrderBy(x => Math.Abs(x - sx) + Math.Abs(x - end.X)).Take(12)
                        .Concat(new[] { outerLeft, outerRight }.Where(x => (x - sx) * sign > 3 && x >= area.X && x <= area.Right)).Distinct())
                    {
                        var elbow = new Vector2(x, sy);
                        Add(start, elbow, end);
                        Add(start, elbow, new(x, end.Y), end);
                    }
                }
        foreach (var proposal in proposals.OrderBy(p => p.Cost))
        {
            if (Clear(proposal.Path))
                return proposal.Path;
        }
        // Slanted legs need contour waypoints: whole-part rectangles can hide
        // the clear triangular space between a leg and an engine bell.
        var corners = new HashSet<Vector2>();
        foreach (var b in sourceParts)
            foreach (int margin in new[] { 24, 48, 96 })
                foreach (float x in new[] { b.X - margin, b.Right + margin })
                    foreach (float y in new[] { b.Y - margin, b.Bottom + margin })
                        corners.Add(new(x, y));
        var outerCorners = corners.ToArray();
        foreach (var p in scene.Visible.Where(p => p.Key.View == block.ViewId).SelectMany(p => p.Value.Edges.Where((_, i) => i % Math.Max(1, p.Value.Edges.Length / 24) == 0)))
            foreach (int margin in new[] { 12, 24, 48 })
                foreach (var offset in new[] { new Vector2(margin, 0), new(-margin, 0), new(0, margin), new(0, -margin) })
                {
                    var corner = p + offset;
                    if (scene.Owner((int)corner.X, (int)corner.Y) < 0 && !block.Bounds.Contains(corner))
                        corners.Add(corner);
                }
        Vector2[] best = [];
        float cost = float.PositiveInfinity;
        foreach (var corner in outerCorners.Concat(corners.OrderBy(p => Vector2.DistanceSquared(p, visual.Bounds.Center)).Take(512)).Distinct())
            foreach (var end in ends)
            {
                // Reject impossible final approaches before considering block sockets.
                if (!LeaderRouting.FirstHit(scene, corner, end, out var hit, out int owner) || owner != part.Id || Vector2.DistanceSquared(hit, end) > 4
                    || !LeaderRouting.ClearStroke(scene, corner, hit, part.Id, block.Tip, 10))
                    continue;
                foreach (float sx in new[] { block.Bounds.X, block.Bounds.Right })
                    foreach (float sy in new[] { Math.Clamp(corner.Y, block.Bounds.Y + 12, block.Bounds.Bottom - 12), block.Bounds.Center.Y, block.Bounds.Y + 12 }.Distinct())
                        foreach (float x in new[] { sx + (sx == block.Bounds.X ? -24 : 24), corner.X }.Distinct())
                        {
                            if ((x - sx) * (sx == block.Bounds.X ? -1 : 1) <= 0)
                                continue;
                            Vector2[] path = [new(sx, sy), new(x, sy), corner, hit];
                            float length = Vector2.Distance(path[0], path[1]) + Vector2.Distance(path[1], path[2]) + Vector2.Distance(path[2], path[3]);
                            if (length >= cost || !Clear(path))
                                continue;
                            best = path;
                            cost = length;
                        }
            }
        if (best.Length > 0)
            return best;
        // Never invent a clear connection through another part. The spec remains
        // usable; moving it or disabling its leader can resolve an enclosed target.
        return [];
    }
}
