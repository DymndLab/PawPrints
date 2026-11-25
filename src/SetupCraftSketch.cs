using System.Numerics;
using Blueprint.Rendering;
using Brutal.Numerics;
using Brutal.Render.Mesh;
using KSA;
using RenderCore;

namespace Blueprint;

// Setup needs recognizable shapes at cell size. Sample only exterior positions
// once per editor revision, without textures, interiors or a full print capture.
sealed class SetupCraftSketch(List<Vector3[]> primitives)
{
    public sealed record Outline(Vector2[][] Faces, Vector2 Center, Vector2 Size);
    readonly Dictionary<(ViewAxes, bool), Outline> cache = new();

    public static SetupCraftSketch? Read(PartTree tree)
    {
        if (tree.Parts.Length > 512) return null;
        var points = new List<Vector3[]>();
        const int limit = 512;
        CraftCensus.VisitModels(tree, false, (part, mesh, _, _) =>
        {
            if (mesh?.HostPrimitives == null || points.Count > limit) return;
            var pose = part.ComputeMatrixAsmb2VehicleAsmb();
            foreach (var primitive in mesh.HostPrimitives)
            {
                var vertices = primitive.GetVertexSpan<float3>(MeshAttribute.Position);
                if (vertices.Length < 3) continue;
                int count = Math.Min(vertices.Length, 48);
                var sample = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    var v = vertices[(int)((long)i * (vertices.Length - 1) / (count - 1))];
                    var world = new double3(v.X, v.Y, v.Z).Transform(pose);
                    sample[i] = new((float)world.Y, (float)world.Z, (float)world.X);
                    if (!float.IsFinite(sample[i].LengthSquared())) return;
                }
                points.Add(sample);
                if (points.Count > limit) break;
            }
        });
        return points.Count is > 0 and <= limit ? new(points) : null;
    }

    public Outline Project(ViewAxes axes, bool exploded)
    {
        if (cache.TryGetValue((axes, exploded), out var known)) return known;
        var faces = new List<(float Depth, Vector2[] Hull)>();
        var low = new Vector2(float.PositiveInfinity);
        var high = new Vector2(float.NegativeInfinity);
        foreach (var samples in primitives)
        {
            var center = samples.Aggregate(Vector3.Zero, (sum, p) => sum + p) / samples.Length;
            var offset = exploded ? center * .3f : Vector3.Zero;
            var points = samples.Select(v => new Vector2(Vector3.Dot(v + offset, axes.Right), -Vector3.Dot(v + offset, axes.Up)))
                .Distinct().OrderBy(v => v.X).ThenBy(v => v.Y).ToArray();
            if (points.Length < 3) continue;
            var hull = Hull(points);
            if (hull.Length < 3) continue;
            foreach (var p in hull) { low = Vector2.Min(low, p); high = Vector2.Max(high, p); }
            faces.Add((Vector3.Dot(center, axes.Toward), hull));
        }
        var result = faces.Count == 0 ? new Outline([], Vector2.Zero, Vector2.One)
            : new Outline(faces.OrderBy(f => f.Depth).Select(f => f.Hull).ToArray(), (low + high) / 2, high - low);
        cache.Add((axes, exploded), result);
        return result;
    }

    static Vector2[] Hull(Vector2[] sorted)
    {
        var hull = new Vector2[2 * sorted.Length];
        int count = 0;
        void Add(Vector2 p, int start)
        {
            while (count >= start + 2)
            {
                var a = hull[count - 1] - hull[count - 2];
                var b = p - hull[count - 1];
                if (a.X * b.Y > a.Y * b.X) break;
                count--;
            }
            hull[count++] = p;
        }
        foreach (var p in sorted) Add(p, 0);
        int lower = count - 1;
        for (int i = sorted.Length - 2; i >= 0; i--) Add(sorted[i], lower);
        return hull[..(count - 1)];
    }
}
