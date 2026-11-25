using System.Numerics;
using Blueprint.Rendering;
static class CurvatureChecks
{
    public static void Run(Action<bool, string> check)
    {
        Capture Surface(Vector2[] profile, bool closed)
        {
            var vertices = profile.SelectMany(p => new[] { new double[] { p.X, p.Y, 0 }, new double[] { p.X, p.Y, 2 } }).ToArray();
            var indices = new List<int>();
            for (int i = 0; i < (closed ? profile.Length : profile.Length - 1); i++)
            {
                int a = i * 2, b = ((i + 1) % profile.Length) * 2;
                indices.AddRange(new[] { a, b, b + 1, a, b + 1, a + 1 });
            }
            return new([new Mesh("anonymous", vertices, indices.ToArray(), vertices, vertices.Select(_ => new double[] { 0, 0, 1 }).ToArray(), vertices.Select(_ => new double[] { 0, 0 }).ToArray(), "structure", null, null)]);
        }
        var cylinder = Surface(Enumerable.Range(0, 16).Select(i => new Vector2(MathF.Cos(i * MathF.PI / 8), MathF.Sin(i * MathF.PI / 8))).ToArray(), true);
        var curved = CurvatureLines.Build(cylinder, default);
        check(curved.SuppressedEdges == 16, "Repeated cylindrical facets are simplified without part rules");
        // Two bands on the same piston meet at a slight taper. Across the join
        // these diagonal faces touch at one vertex, without sharing an edge.
        const int sides = 16;
        var stepped = Enumerable.Range(0, 3).SelectMany(row => Enumerable.Range(0, sides).Select(i =>
        {
            double angle = i * 2 * Math.PI / sides, radius = row == 2 ? .88 : 1;
            return new[] { Math.Cos(angle) * radius, Math.Sin(angle) * radius, (double)row };
        })).ToArray();
        var bands = new List<int>();
        for (int row = 0; row < 2; row++)
            for (int i = 0; i < sides; i++)
            {
                int a = row * sides + i, b = row * sides + (i + 1) % sides;
                bands.AddRange([a, b, b + sides, a, b + sides, a + sides]);
            }
        var piston = cylinder.Meshes[0] with { Vertices = stepped, Indices = bands.ToArray() };
        var joint = CurvatureLines.Build(new([piston]), default);
        check(joint.Smooth(0, 0, (sides + 1) * 2, .93f), "Smooth cylinder-taper corner has no isolated pixel crease");
        check(!joint.Smooth(0, 0, (sides + 1) * 2, .999f), "A real bend above the selected crease threshold is retained");
        var fillet = new List<Vector2> { new(1, -8) };
        fillet.AddRange(Enumerable.Range(0, 5).Select(i => new Vector2(MathF.Cos(i * MathF.PI / 8), MathF.Sin(i * MathF.PI / 8))));
        fillet.Add(new(-8, 1));
        var corner = CurvatureLines.Build(Surface(fillet.ToArray(), false), default);
        check(corner.SuppressedEdges == 0, "Rounded transition between broad faces retains its feature edges");
        check(curved.SuppressedEdges == 16, "A second classifier does not mutate an existing renderer");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            CurvatureLines.Build(cylinder, canceled.Token);
            check(false, "Canceled classification must stop");
        }
        catch (OperationCanceledException) { check(true, "Curvature classification observes cancellation"); }


    }
}
