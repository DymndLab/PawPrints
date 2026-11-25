using System.Numerics;
namespace Blueprint.Rendering;

// Geometry-only option 04: conservative curvature plus strict cylinder recognition. Weld positional duplicates, merge flat
// triangle patches, then look for repeated bends around a common local axis.
sealed class CurvatureLines
{
    readonly HashSet<(int Mesh, int A, int B)> suppress = [];
    readonly int[][] patches;
    long patchCount;
    CurvatureLines(int meshCount) => patches = new int[meshCount][];
    internal int SuppressedEdges => suppress.Count;
    // Count capacity, including spare slots, with room for buckets and entries.
    internal long EstimatedRetainedBytes => 64L * ((long)suppress.EnsureCapacity(0) +
        smoothCorners.EnsureCapacity(0)) + 4 * patchCount + 32L * patches.Length;
    readonly Dictionary<(int Mesh, int A, int B), float> smoothCorners = [];
    public bool Smooth(int mesh, int a, int b, float creaseCosine = 1)
    {
        int[] group = patches[mesh];
        a = group[a];
        b = group[b];
        var key = (mesh, Math.Min(a, b), Math.Max(a, b));
        return suppress.Contains(key) || smoothCorners.TryGetValue(key, out var cosine) && cosine >= creaseCosine;
    }
    public static CurvatureLines Build(Capture capture, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var result = new CurvatureLines(capture.Meshes.Length);
        long total = capture.Meshes.Sum(m => (long)(m.Indices.Length / 3)), completed = 0;
        bool large = total >= NormalLinework.LargeScopeTriangles;
        int reported = -1;
        for (int mi = 0; mi < capture.Meshes.Length; mi++)
        {
            token.ThrowIfCancellationRequested();
            int percent = total == 0 ? 0 : (int)(100 * completed / total);
            if (reported != percent)
            {
                RenderProgress.Report("Analyzing geometry", percent / 100f);
                reported = percent;
            }
            var mesh = capture.Meshes[mi];
            if (!mesh.FeatureInk)
            {
                if (large)
                    RenderMemory.RequireGeometry(checked(result.EstimatedRetainedBytes +
                        RenderMemory.CurvatureScratch(mesh.VertexCount, mesh.Indices.Length / 3)));
                result.ClassifyMesh(mesh, mi, token, large);
            }
            completed += mesh.Indices.Length / 3;
        }
        return result;
    }

    void ClassifyMesh(Mesh m, int mi, CancellationToken token, bool large)
    {
        int n = m.Indices.Length / 3;
        long scratch = large ? RenderMemory.CurvatureScratch(m.VertexCount, n) : 0;
        void CheckGrowth(int count)
        {
            if (large && (count & 4095) == 0)
                RenderMemory.RequireGeometry(checked(EstimatedRetainedBytes + scratch));
        }
        var points = new Vector3[m.VertexCount];
        var weld = new Dictionary<(long, long, long), int>();
        var ids = new int[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
            var p = points[i] = m.PointAt(i).Native;
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
        var edges = new Dictionary<(int, int), (int First, int Second, int Count)>();
        void TrackEdge(int a, int b, int triangle)
        {
            var key = (Math.Min(a, b), Math.Max(a, b));
            if (!edges.TryGetValue(key, out var edge))
                edges.Add(key, (triangle, 0, 1));
            else if (edge.Count < 3)
                edges[key] = (edge.First, triangle, edge.Count + 1);
        }
        for (int i = 0; i < n; i++)
        {
            if ((i & 1023) == 0)
                token.ThrowIfCancellationRequested();
            int a = m.Indices[i * 3], b = m.Indices[i * 3 + 1], c = m.Indices[i * 3 + 2];
            var cross = Vector3.Cross(points[b] - points[a], points[c] - points[a]);
            area[i] = cross.Length() * .5f;
            normals[i] = area[i] > 1e-12 ? Vector3.Normalize(cross) : Vector3.Zero;
            TrackEdge(ids[a], ids[b], i);
            TrackEdge(ids[b], ids[c], i);
            TrackEdge(ids[c], ids[a], i);
        }
        foreach (var e in edges.Values.Where(e => e.Count == 2))
            if (Vector3.Dot(normals[e.First], normals[e.Second]) > .9999f)
                parent[Find(e.First)] = Find(e.Second);
        // Triangle indices are dense within a captured primitive.
        for (int i = 0; i < n; i++)
            parent[i] = Find(i);
        patches[mi] = parent;
        patchCount += n;
        var areas = new Dictionary<int, float>();
        var ns = new Dictionary<int, Vector3>();
        var graph = new Dictionary<int, HashSet<int>>();
        for (int i = 0; i < n; i++)
        {
            int p = Find(i);
            areas[p] = areas.GetValueOrDefault(p) + area[i];
            ns[p] = normals[i];
            if (!graph.ContainsKey(p))
                graph.Add(p, []);
        }
        foreach (var e in edges.Values.Where(e => e.Count == 2))
        {
            int a = Find(e.First), b = Find(e.Second);
            if (a != b)
            {
                graph[a].Add(b);
                graph[b].Add(a);
            }
        }
        float Angle(int a, int b) => MathF.Acos(Math.Clamp(Vector3.Dot(ns[a], ns[b]), -1, 1)) * 180 / MathF.PI;
        var seen = new HashSet<int>();
        var queue = new Queue<(int Patch, int Depth)>();
        Classify(6, 42, 2, .85f, 0);
        Classify(4, 55, 4, .97f, 90);

        void Classify(int minimum, float cap, float flatRatio, float axisMatch, float minimumSpan)
        {
            foreach (var e in edges.Values.Where(e => e.Count == 2))
            {
                int a = Find(e.First), b = Find(e.Second);
                if (a == b)
                    continue;
                float angle = Angle(a, b);
                if (angle < 21.9f || angle > cap)
                    continue;
                var axis = Vector3.Normalize(Vector3.Cross(ns[a], ns[b]));
                seen.Clear();
                queue.Clear();
                seen.Add(a);
                seen.Add(b);
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
                    foreach (int next in graph[item.Patch])
                    {
                        float bend = Angle(item.Patch, next);
                        var turn = Vector3.Cross(ns[item.Patch], ns[next]);
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
                        if (item.Depth < 3 && seen.Add(next))
                            queue.Enqueue((next, item.Depth + 1));
                        if (seen.Count > 128)
                        {
                            broad = true;
                            break;
                        }
                    }
                }
                if (broad || seen.Count < minimum)
                    continue;
                if (minimumSpan > 0 && (seen.Max(p => areas[p]) / Math.Max(1e-12f, seen.Min(p => areas[p])) > 2
                    || !seen.Any(u => seen.Any(v => Angle(u, v) >= minimumSpan))))
                    continue;
                if (suppress.Add((mi, Math.Min(a, b), Math.Max(a, b))))
                    CheckGrowth(suppress.Count);
            }
        }
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
                if (!suppress.Contains(key))
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
                    if (smoothCorners.TryGetValue(diagonal, out var prior))
                        smoothCorners[diagonal] = Math.Max(prior, cosine);
                    else
                    {
                        smoothCorners.Add(diagonal, cosine);
                        CheckGrowth(smoothCorners.Count);
                    }
                }
            }
        }
    }
}
