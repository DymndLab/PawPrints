using System.Numerics;
namespace Blueprint.Rendering;

// Geometry-only option 04: conservative curvature plus strict cylinder recognition. Weld positional duplicates, merge flat
// triangle patches, then look for repeated bends around a common local axis.
sealed class CurvatureLines
{
    HashSet<(int Mesh, int A, int B)> suppress = [];
    Dictionary<(int Mesh, int Triangle), int> patches = [];
    internal int SuppressedEdges => suppress.Count;
    readonly Dictionary<(int Mesh, int A, int B), float> smoothCorners = [];
    public bool Smooth(int mesh, int a, int b, float creaseCosine = 1)
    {
        a = patches.GetValueOrDefault((mesh, a), a);
        b = patches.GetValueOrDefault((mesh, b), b);
        var key = (mesh, Math.Min(a, b), Math.Max(a, b));
        return suppress.Contains(key) || smoothCorners.TryGetValue(key, out var cosine) && cosine >= creaseCosine;
    }
    public static CurvatureLines Build(Capture capture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new CurvatureLines();
        result.Prepare(capture, 6, 42, 2, token);
        var conservative = result.suppress.ToHashSet();
        result.Prepare(capture, 4, 55, 4, token, 90, .97f, 2, conservative: conservative);
        result.suppress.UnionWith(conservative);
        return result;
    }
    void Prepare(Capture capture, int minimum, float cap, float flatRatio, CancellationToken token, float minimumSpan = 0, float axisMatch = .85f, float areaSpread = float.PositiveInfinity, bool protectTransitions = false, HashSet<(int Mesh, int A, int B)>? conservative = null)
    {
        suppress = [];
        patches = [];
        if (minimum == 0)
            return;
        for (int mi = 0; mi < capture.Meshes.Length; mi++)
        {
            token.ThrowIfCancellationRequested();
            var m = capture.Meshes[mi];
            int n = m.Indices.Length / 3;
            var points = m.Vertices.Select(v => new Vector3((float)v[0], (float)v[1], (float)v[2])).ToArray();
            var weld = new Dictionary<(long, long, long), int>();
            var ids = new int[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                var key = ((long)Math.Round(p.X * 100000), (long)Math.Round(p.Y * 100000), (long)Math.Round(p.Z * 100000));
                if (!weld.TryGetValue(key, out int id))
                    weld[key] = id = weld.Count;
                ids[i] = id;
            }
            var normals = new Vector3[n];
            var area = new float[n];
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }
            var edges = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < n; i++)
            {
                if ((i & 1023) == 0)
                    token.ThrowIfCancellationRequested();
                int a = m.Indices[i * 3], b = m.Indices[i * 3 + 1], c = m.Indices[i * 3 + 2];
                var cross = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
                area[i] = cross.Length() * .5f;
                normals[i] = area[i] > 1e-12 ? Vector3.Normalize(cross) : Vector3.Zero;
                foreach (var e in new[] { (ids[a], ids[b]), (ids[b], ids[c]), (ids[c], ids[a]) })
                {
                    var key = (Math.Min(e.Item1, e.Item2), Math.Max(e.Item1, e.Item2));
                    if (!edges.TryGetValue(key, out var list))
                        edges[key] = list = [];
                    list.Add(i);
                }
            }
            foreach (var e in edges.Values.Where(e => e.Count == 2))
                if (Vector3.Dot(normals[e[0]], normals[e[1]]) > .9999f)
                    parent[Find(e[0])] = Find(e[1]);
            for (int i = 0; i < n; i++)
                patches[(mi, i)] = Find(i);
            var areas = new Dictionary<int, float>();
            var ns = new Dictionary<int, Vector3>();
            var graph = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < n; i++)
            {
                int p = Find(i);
                areas[p] = areas.GetValueOrDefault(p) + area[i];
                ns[p] = normals[i];
                graph.TryAdd(p, []);
            }
            foreach (var e in edges.Values.Where(e => e.Count == 2))
            {
                int a = Find(e[0]), b = Find(e[1]);
                if (a != b)
                {
                    graph[a].Add(b);
                    graph[b].Add(a);
                }
            }
            float Angle(int a, int b) => MathF.Acos(Math.Clamp(Vector3.Dot(ns[a], ns[b]), -1, 1)) * 180 / MathF.PI;
            foreach (var e in edges.Values.Where(e => e.Count == 2))
            {
                int a = Find(e[0]), b = Find(e[1]);
                if (a == b)
                    continue;
                float angle = Angle(a, b);
                if (angle < 21.9f || angle > cap)
                    continue;
                var axis = Vector3.Normalize(Vector3.Cross(ns[a], ns[b]));
                var seen = new HashSet<int> { a, b };
                var queue = new Queue<(int, int)>();
                queue.Enqueue((a, 0));
                queue.Enqueue((b, 0));
                bool broad = false;
                float localArea = Math.Max(areas[a], areas[b]);
                while (queue.TryDequeue(out var item))
                {
                    token.ThrowIfCancellationRequested();
                    // Ambiguous/high-valence topology falls back to ordinary edges.
                    if (seen.Count > 128)
                    {
                        broad = true;
                        break;
                    }
                    foreach (int next in graph[item.Item1])
                    {
                        float bend = Angle(item.Item1, next);
                        var turn = Vector3.Cross(ns[item.Item1], ns[next]);
                        if (bend < 2 || bend > cap || turn.LengthSquared() < 1e-8)
                            continue;
                        if (Math.Abs(Vector3.Dot(Vector3.Normalize(turn), axis)) < axisMatch)
                            continue;
                        if (areas[next] > localArea * flatRatio)
                        {
                            broad = true;
                            continue;
                        }
                        if (bend < angle * .45f || bend > angle * 2.2f)
                            continue;
                        if (item.Item2 < 3 && seen.Add(next))
                            queue.Enqueue((next, item.Item2 + 1));
                        if (seen.Count > 128)
                        {
                            broad = true;
                            break;
                        }
                    }
                }
                float span = 0;
                foreach (int u in seen)
                    foreach (int v in seen)
                        span = Math.Max(span, Angle(u, v));
                bool uniformArea = seen.Max(p => areas[p]) / Math.Max(1e-12f, seen.Min(p => areas[p])) <= areaSpread;
                bool transition = protectTransitions && seen.Any(p => graph[p].Any(next => !seen.Contains(next) && areas[next] > localArea * 2 && Angle(p, next) > 3));
                if (!broad && !transition && uniformArea && span >= minimumSpan && seen.Count >= minimum)
                    suppress.Add((mi, Math.Min(a, b), Math.Max(a, b)));
            }
            if (conservative != null)
            {
                // Neighboring pixels can fall on diagonally opposite patches at a
                // welded vertex. Comparing their normals directly draws isolated
                // dots even when both intervening edges are smooth.
                var rings = new Dictionary<int, HashSet<int>>();
                for (int i = 0; i < m.Indices.Length; i++)
                {
                    int vertex = ids[m.Indices[i]];
                    if (!rings.TryGetValue(vertex, out var ring))
                        rings[vertex] = ring = [];
                    ring.Add(Find(i / 3));
                }
                foreach (var ring in rings.Values)
                {
                    if (ring.Count < 3 || ring.Count > 12)
                        continue;
                    foreach (int middle in ring)
                    foreach (int a in ring)
                    {
                        if (!graph[middle].Contains(a))
                            continue;
                        var key = (mi, Math.Min(a, middle), Math.Max(a, middle));
                        if (!suppress.Contains(key) && !conservative.Contains(key))
                            continue;
                        foreach (int b in ring)
                        {
                            if (b == a || !graph[middle].Contains(b) || graph[a].Contains(b))
                                continue;
                            float cosine = Vector3.Dot(ns[middle], ns[b]);
                            // Only bridge a classified curved facet and a shallow longitudinal bend.
                            if (cosine < .94f)
                                continue;
                            var diagonal = (mi, Math.Min(a, b), Math.Max(a, b));
                            smoothCorners[diagonal] = Math.Max(smoothCorners.GetValueOrDefault(diagonal), cosine);
                        }
                    }
                }
            }
        }

    }
}
