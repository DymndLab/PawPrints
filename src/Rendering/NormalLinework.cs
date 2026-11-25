using System.Numerics;

namespace Blueprint.Rendering;

static class NormalLinework
{
    // Ordinary fixtures topped out at 436k triangles. The first station scope
    // with a measured benefit had 1.30M. The fixed fit reproduces the approved
    // isometric study without looking at output resolution or print scale.
    internal const long LargeScopeTriangles = 1_000_000;
    const float SmallSpan = 22, DetailedSmallSpan = 8, ReferenceFill = .91f;

    public static Capture Prepare(Capture capture, CancellationToken token, Capture? unspread = null, bool detailed = false)
    {
        token.ThrowIfCancellationRequested();
        if (capture.NormalLineworkPrepared)
            return capture;
        long triangles = capture.Meshes.Sum(m => m.Indices.LongLength / 3);
        if (triangles < LargeScopeTriangles)
            return capture with { NormalLineworkPrepared = true, Detailed = detailed };

        var scope = unspread ?? capture;
        var axes = SheetLayout.Basis(4);
        var spans = new Vector2[scope.Meshes.Length];
        Vector2 lo = new(float.PositiveInfinity), hi = new(float.NegativeInfinity);
        for (int i = 0; i < scope.Meshes.Length; i++)
        {
            var mesh = scope.Meshes[i];
            Vector2 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                if ((v & 4095) == 0) token.ThrowIfCancellationRequested();
                var world = mesh.PointAt(v).World;
                var projected = new Vector2(Vector3.Dot(world, axes.Right), Vector3.Dot(world, axes.Up));
                min = Vector2.Min(min, projected);
                max = Vector2.Max(max, projected);
            }
            spans[i] = max - min;
            lo = Vector2.Min(lo, min);
            hi = Vector2.Max(hi, max);
        }
        var extent = hi - lo;
        float scale = ReferenceFill * Math.Min(2560 / extent.X, 1440 / extent.Y);
        if (!float.IsFinite(scale) || scale <= 0)
            return capture with { NormalLineworkPrepared = true, Detailed = detailed };

        var meshes = (Mesh[])capture.Meshes.Clone();
        int featured = 0;
        for (int i = 0; i < meshes.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var mesh = meshes[i];
            // Procedural hoses keep full linework. No mesh is dropped or remeshed.
            if (mesh.ConnectedParts.Length == 0 && Math.Max(spans[i].X, spans[i].Y) * scale <= (detailed ? DetailedSmallSpan : SmallSpan))
            {
                meshes[i] = mesh with
                {
                    FeatureInk = true,
                    Surface = "structure",
                    NormalTexture = null,
                    DiffuseTexture = null,
                    SolarDirection = null
                };
                featured++;
            }
        }
        PrintDiagnostics.Record($"{(detailed ? "detailed" : "normal")}.linework triangles={triangles} meshes={meshes.Length} feature_meshes={featured}");
        return new(meshes) { NormalLineworkPrepared = true, Detailed = detailed };
    }
}
