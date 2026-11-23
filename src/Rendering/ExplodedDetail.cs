using System.Numerics;
namespace Blueprint.Rendering;

sealed record AssemblyPose(Vector3 Origin, Vector3 X, Vector3 Y, Vector3 Z);
sealed record AssemblyNode(uint Id, uint? Parent, string Name, string TemplateId, bool Subpart, AssemblyPose? Pose = null);
sealed record AssemblyCapture(Capture Geometry, AssemblyNode[] Nodes, uint[] MeshNodes, bool[] Internal);
sealed record ExplodedSource(AssemblyCapture Assembly, IReadOnlyDictionary<string, Texture> Maps);
sealed record ExplodedGroup(uint Node, Vector3 Offset, bool Hidden = false, int Axis = 0);
sealed record DetailSizing(float Ratio, float? Spread = null, Vector3? Center = null, int Width = 0, int Height = 0);
sealed record ExplodedDetail(int Id, uint Scope, Box Bounds, string Caption, int View = 4, float Separation = 1, int Axis = 0, bool IncludeInternal = false,
    bool Guides = true, uint[]? Expanded = null, ExplodedGroup[]? Groups = null, ProjectionFrame? Camera = null, uint? Seed = null, DetailSizing? Sizing = null, LeaderLine GuideLine = LeaderLine.Dotted)
{
    public int ViewId => 1000 + Id;
}
sealed record DetailGeometry(Capture Capture, int[] OriginalMeshes, uint[] GroupNodes, Vector3[] OriginalCenters, Vector3[] MovedCenters)
{
    public uint Anchor
    {
        get; init;
    }
}
sealed record RenderedDetail(ExplodedDetail Definition, ProjectionFrame Frame, int[] Meshes, uint[] Groups, ViewPixels Pixels)
{
    public int CaptionFont { get; init; }
    public uint? Anchor
    {
        get; init;
    }
    public Box CaptionBounds
    {
        get; init;
    }
    public DetailSizing? Sizing
    {
        get; init;
    }
}

static class AssemblyGraph
{


    public static uint[] Ancestors(AssemblyCapture source, uint seed)
    {
        var map = source.Nodes.ToDictionary(n => n.Id);
        var result = new List<uint>();
        var seen = new HashSet<uint>();
        for (uint? id = seed; id.HasValue; id = map[id.Value].Parent)
        {
            if (!map.ContainsKey(id.Value) || !seen.Add(id.Value))
                throw new InvalidDataException("Broken assembly hierarchy.");
            result.Add(id.Value);
        }
        return result.ToArray();
    }


    public static HashSet<uint> Subtree(AssemblyCapture source, uint root)
    {
        if (!source.Nodes.Any(n => n.Id == root))
            throw new InvalidDataException("Assembly no longer exists.");
        var children = source.Nodes.Where(n => n.Parent != null).ToLookup(n => n.Parent!.Value);
        var found = new HashSet<uint>();
        var todo = new Stack<uint>();
        todo.Push(root);
        while (todo.TryPop(out var id))
        {
            if (!found.Add(id))
                throw new InvalidDataException("Cyclic assembly hierarchy.");
            foreach (var child in children[id])
                todo.Push(child.Id);
        }
        return found;
    }


    public static uint GroupFor(AssemblyCapture source, ExplodedDetail detail, uint node)
    {
        var path = Ancestors(source, node);
        int root = Array.IndexOf(path, detail.Scope);
        if (root < 0)
            throw new InvalidDataException("Part is outside detail scope.");
        // Craft attachments are real part boundaries; authored SubParts are
        // meshes/components of their owning part. Keep those skins, windows,
        // fittings and internal meshes together unless explicitly expanded.
        uint group = detail.Scope;
        for (int i = root - 1; i >= 0; i--)
            if (!source.Nodes.First(n => n.Id == path[i]).Subpart || (detail.Expanded ?? []).Contains(group))
                group = path[i];
        return group;
    }


    public static uint[] PartAncestors(AssemblyCapture source, uint seed, uint? retain = null) =>
        Ancestors(source, seed).Where(id => !source.Nodes.First(n => n.Id == id).Subpart || id == retain).ToArray();


    public static int Depth(AssemblyCapture source, ExplodedDetail detail, uint group) =>
        Ancestors(source, group).TakeWhile(id => id != detail.Scope).Count(id => GroupFor(source, detail, id) == id);


    public static uint[] Groups(AssemblyCapture source, ExplodedDetail detail)
    {
        var subtree = Subtree(source, detail.Scope);
        return source.MeshNodes.Where((id, i) => subtree.Contains(id) && (detail.IncludeInternal || !source.Internal[i]))
            .Select(id => GroupFor(source, detail, id)).Distinct().ToArray();
    }
}

static class ExplodedGeometry
{


    public static Vector3 World(double[] p) => new((float)p[1], (float)p[2], (float)p[0]);


    static Vector3 Center(IEnumerable<Vector3> points)
    {
        var all = points.ToArray();
        return all.Length == 0 ? Vector3.Zero : (all.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min) + all.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max)) * .5f;
    }


    public static AssemblyPose RootPose(AssemblyCapture source, ExplodedDetail detail)
    {
        var root = source.Nodes.First(n => n.Id == detail.Scope);
        // Captures without pose metadata use only the root's own geometry,
        // never the changing bounds of its descendants, as the fallback origin.
        return root.Pose ?? new(Center(source.Geometry.Meshes.Where((_, i) => source.MeshNodes[i] == detail.Scope).SelectMany(m => m.Vertices).Select(World)), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
    }


    public static Vector3 GroupDirection(AssemblyCapture source, ExplodedDetail detail, uint group)
    {
        if (group == AnchorGroup(source, detail))
            return Vector3.Zero;
        var scope = AssemblyGraph.Subtree(source, detail.Scope);
        var center = Center(source.Geometry.Meshes.Where((_, i) => scope.Contains(source.MeshNodes[i]) && AssemblyGraph.GroupFor(source, detail, source.MeshNodes[i]) == group && (detail.IncludeInternal || !source.Internal[i])).SelectMany(m => m.Vertices).Select(World));
        return Direction(RootPose(source, detail), detail, group, center);
    }


    public static uint AnchorGroup(AssemblyCapture source, ExplodedDetail detail)
    {
        // Prefab roots can be empty containers. Give such a root a visible base:
        // the largest substantial group centered on its authored origin. Use a
        // size-relative tolerance, so microscopic mesh asymmetry cannot eject
        // the central shell sideways. Hiding a group must not change the anchor.
        var subtree = AssemblyGraph.Subtree(source, detail.Scope);
        var groups = Enumerable.Range(0, source.MeshNodes.Length)
            .Where(i => subtree.Contains(source.MeshNodes[i]) && (detail.IncludeInternal || !source.Internal[i]))
            .GroupBy(i => AssemblyGraph.GroupFor(source, detail, source.MeshNodes[i])).ToArray();
        if (groups.Any(g => g.Key == detail.Scope))
            return detail.Scope;
        var origin = RootPose(source, detail).Origin;
        uint anchor = detail.Scope;
        float largest = 0;
        foreach (var group in groups.OrderBy(g => g.Key))
        {
            var points = group.SelectMany(i => source.Geometry.Meshes[i].Vertices).Select(World).ToArray();
            if (points.Length == 0)
                continue;
            var lo = points.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min);
            var hi = points.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max);
            var size = hi - lo;
            float volume = size.X * size.Y * size.Z;
            if (volume > largest && Vector3.Distance((lo + hi) * .5f, origin) <= Math.Max(.00001f, size.Length() * .02f))
            {
                anchor = group.Key;
                largest = volume;
            }
        }
        return anchor;
    }


    static Vector3 Direction(AssemblyPose root, ExplodedDetail detail, uint group, Vector3 center)
    {
        if (group == detail.Scope)
            return Vector3.Zero;
        var setting = (detail.Groups ?? []).FirstOrDefault(g => g.Node == group);
        int axis = setting?.Axis > 0 ? setting.Axis : detail.Axis;
        Vector3 relative = center - root.Origin;
        float x = Vector3.Dot(relative, root.X), y = Vector3.Dot(relative, root.Y), z = Vector3.Dot(relative, root.Z);
        // Choose one root-local axis; ties and concentric shells use local Y.
        // No camera basis, circular fallback, or node-ID-dependent spacing.
        if (axis == 0)
            axis = Math.Abs(y) >= Math.Abs(x) && Math.Abs(y) >= Math.Abs(z) ? 2 : Math.Abs(x) >= Math.Abs(z) ? 1 : 3;
        var direction = axis == 1 ? root.X : axis == 2 ? root.Y : root.Z;
        return direction * (Vector3.Dot(relative, direction) < -1e-5f ? -1 : 1);
    }


    public static Vector3 DragOffset(ProjectionFrame frame, Vector3 direction, Vector2 delta)
    {
        var projected = new Vector2(Vector3.Dot(direction, frame.Right), -Vector3.Dot(direction, frame.Up));
        // An axis viewed end-on cannot be dragged meaningfully in this view.
        return projected.LengthSquared() < .01f ? Vector3.Zero : direction * (Vector2.Dot(delta, projected) / (frame.Scale * projected.LengthSquared()));
    }


    public static DetailGeometry Build(AssemblyCapture source, ExplodedDetail detail)
    {
        var scope = AssemblyGraph.Subtree(source, detail.Scope);
        var meshes = new List<Mesh>();
        var indices = new List<int>();
        var nodes = new List<uint>();
        var selected = Enumerable.Range(0, source.Geometry.Meshes.Length).Where(i => scope.Contains(source.MeshNodes[i]) && (detail.IncludeInternal || !source.Internal[i])).ToArray();
        var grouped = selected.GroupBy(i => AssemblyGraph.GroupFor(source, detail, source.MeshNodes[i])).OrderBy(g => g.Key).ToArray();
        var all = selected.SelectMany(i => source.Geometry.Meshes[i].Vertices).Select(World).ToArray();
        if (all.Length == 0)
            throw new InvalidOperationException("This scope has no drawable geometry.");
        Vector3 lo = all.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min), hi = all.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max);
        float size = Math.Max(.01f, Vector3.Distance(lo, hi));
        var root = RootPose(source, detail);
        uint anchor = AnchorGroup(source, detail);
        var originalCenters = new List<Vector3>();
        var movedCenters = new List<Vector3>();
        for (int g = 0; g < grouped.Length; g++)
        {
            var group = grouped[g];
            var setting = (detail.Groups ?? []).FirstOrDefault(s => s.Node == group.Key);
            if (setting?.Hidden == true)
                continue;
            var pts = group.SelectMany(i => source.Geometry.Meshes[i].Vertices).Select(World).ToArray();
            var groupCenter = (pts.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min) + pts.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max)) * .5f;
            Vector3 delta = Vector3.Zero;
            if (group.Key != detail.Scope && group.Key != anchor)
            {
                var direction = Direction(root, detail, group.Key, groupCenter);
                // Successive attached parts need distinct distances. Equal
                // shifts leave parent and child apparently joined. Depth is
                // measured in actual groups, never authored mesh nesting.
                delta = direction * (size * .28f * detail.Separation * Math.Max(1, AssemblyGraph.Depth(source, detail, group.Key)) + Vector3.Dot(setting?.Offset ?? Vector3.Zero, direction));
            }
            foreach (int i in group)
            {
                var m = source.Geometry.Meshes[i];
                // Translate detached vertices only. Source geometry/normals stay intact.
                var shifted = m.Vertices.Select(p => new[] { p[0] + delta.Z, p[1] + delta.X, p[2] + delta.Y }).ToArray();
                meshes.Add(m with
                {
                    Vertices = shifted
                });
                indices.Add(i);
                nodes.Add(group.Key);
                originalCenters.Add(groupCenter);
                movedCenters.Add(groupCenter + delta);
            }
        }
        if (meshes.Count == 0)
            throw new InvalidOperationException("Keep at least one detail group visible.");
        return new(new(meshes.ToArray()), indices.ToArray(), nodes.ToArray(), originalCenters.ToArray(), movedCenters.ToArray())
        {
            Anchor = anchor
        };
    }


    public static RenderedDetail Render(ExplodedSource source, ExplodedDetail detail, CancellationToken token, PrintOptions? options = null)
    {
        var geometry = Build(source.Assembly, detail);
        int w = (int)detail.Bounds.Width - 24, h = (int)detail.Bounds.Height - ExplodedPainting.CaptionSpace;
        var basis = SheetLayout.Basis(detail.View);
        var points = geometry.Capture.Meshes.SelectMany(m => m.Vertices).Select(World).ToArray();
        var center = (points.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min) + points.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max)) * .5f;
        float dx = points.Max(p => Math.Abs(Vector3.Dot(p - center, basis.Right))) * 2, dy = points.Max(p => Math.Abs(Vector3.Dot(p - center, basis.Up))) * 2;
        ProjectionFrame frame;
        DetailSizing effectiveSizing;
        if (detail.Sizing is { } requested)
        {
            (geometry, frame, effectiveSizing) = DetailPartSizing.Place(geometry, requested, w, h, detail.View, options ?? new());
            detail = detail with
            {
                Sizing = effectiveSizing
            };
        }
        else
        {
            frame = detail.Camera is { } pinned ? pinned with
            {
                Width = w,
                Height = h
            } : new(w, h, Math.Min((w - 32) / Math.Max(dx, .001f), (h - 32) / Math.Max(dy, .001f)), center, basis.Right, basis.Up, basis.Toward);
            // Preserve a stable drag camera while it fits; expand the framing when
            // necessary so a manual move never silently clips exported geometry.
            if (points.Any(p => Math.Abs(Vector3.Dot(p - frame.Center, frame.Right)) * frame.Scale > (w - 24) * .5f || Math.Abs(Vector3.Dot(p - frame.Center, frame.Up)) * frame.Scale > (h - 24) * .5f))
                frame = new(w, h, Math.Min((w - 32) / Math.Max(dx, .001f), (h - 32) / Math.Max(dy, .001f)), center, basis.Right, basis.Up, basis.Toward);
            if (options != null)
                frame = frame with
                {
                    Scale = PrintScale.FitScale(frame.Scale, options)
                };

            effectiveSizing = new(PrintScale.Density(options ?? new()) / frame.Scale, frame.Scale, frame.Center, w, h);
        }
        var pixels = DetailSampling.Render(geometry.Capture, source.Maps, detail.Caption, detail.View, frame, token);
        if (detail.Guides)
        {
            Vector2 Project(Vector3 p) => new(w * .5f + Vector3.Dot(p - frame.Center, frame.Right) * frame.Scale, h * .5f - Vector3.Dot(p - frame.Center, frame.Up) * frame.Scale);
            var paths = Enumerable.Range(0, geometry.GroupNodes.Length).DistinctBy(i => geometry.GroupNodes[i])
                .Select(i => (Project(geometry.OriginalCenters[i]), Project(geometry.MovedCenters[i])));
            pixels = AssemblyGuidePainting.Paint(pixels, paths, detail.GuideLine, token);
        }

        return new(detail, frame, geometry.OriginalMeshes, geometry.GroupNodes, pixels)
        {
            Anchor = geometry.Anchor,
            Sizing = effectiveSizing
        };
    }
}
