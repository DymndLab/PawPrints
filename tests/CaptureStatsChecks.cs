using Blueprint;
using Blueprint.Rendering;
using System.Numerics;

static class CaptureStatsChecks
{
    public static void Run(Action<bool, string> check)
    {
        var stats = new CaptureStats();
        for (uint p = 0; p < 2_000; p++)
            stats.Add("panel", p, false, 1_000, 200, p == 0);
        stats.Complete = true;
        stats.RequireBudget(includeInterior: false);
        check(stats.SourceBytes(false) < 2L * 1024 * 1024 && stats.WorkingGeometryBytes(false) > 100L * 1024 * 1024,
            "Two million repeated vertices fit only when source sharing and expanded complexity are accounted separately");

        stats.Add("dense", 3, false, 1, 10_000_000, true);
        bool rejected = false;
        try { stats.RequireBudget(includeInterior: false); }
        catch (InvalidOperationException e)
        {
            rejected = e.Message.Contains("limit:") && stats.Failure.StartsWith("geometry") && stats.Complete;
        }
        check(rejected, "A completed census keeps measured counts when triangle-heavy geometry is rejected");

        var interior = new CaptureStats();
        interior.Add("body", 1, false, 100, 50, true);
        interior.Add("cabin", 2, true, 100, 50, true);
        interior.Material(true, 8192, 8192, 64 * 1024 * 1024, true);
        interior.RequireBudget(includeInterior: false);
        try { interior.RequireBudget(includeInterior: true); }
        catch (InvalidOperationException) { }
        check(interior.Failure == "material-working-bytes", "Compressed interior materials account for decode scratch and can be omitted independently");

        var source = new MeshSource([new(.25f, 0, 0)], [Vector3.Normalize(new(1, 1, 0))], [new(.25f, .75f)], []);
        var transform = new MeshTransform(new(-2, 0, 0), new(0, 3, 0), new(0, 0, 4), new(32_000_000, 16, -8));
        var mesh = new Mesh("test", [], source.Indices, [], [], [], "solar", null, null)
        {
            Instance = new(source, transform)
        };
        var translated = mesh.Translate(new(1, 2, 3));
        check(mesh.PointAt(0).X == 31_999_999.5 && translated.PointAt(0).X == 32_000_002.5 &&
            ReferenceEquals(mesh.Instance!.Source, translated.Instance!.Source) && translated.Vertices.Length == 0,
            "Large origins retain double-precision points and exploded translations share the source");

        var normal = Vector3.Normalize(Vector3.TransformNormal(source.Normals[0], mesh.Instance!.NormalMatrix()));
        var expected = Vector3.Normalize(new Vector3(1f / 3, 0, -.5f));
        check(Vector3.Distance(normal, expected) < .000001f, "Mirrored nonuniform transforms use the inverse transpose for authored normals");
        var pose = Matrix4x4.CreateRotationY(.25f);
        var posed = mesh.ApplyPose(pose);
        check(Vector3.Distance(Vector3.Normalize(Vector3.TransformNormal(source.Normals[0], posed.Instance!.NormalMatrix())),
                Vector3.TransformNormal(expected, pose)) < .000001f && ReferenceEquals(source, posed.Instance.Source),
            "Detached poses preserve the shared buffers and rotate the authored normals");

        check(RenderMemory.PeakOverBudget(1L << 30, 2L << 30) is { Limit: 2L << 30 },
            "Peak admission includes the previous accepted drawing");
        try { RenderMemory.RasterPixels(int.MaxValue, int.MaxValue); rejected = false; }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected, "Oversized raster multiplication is rejected before buffer allocation");

        var distributed = new CaptureStats();
        for (uint i = 0; i < 12_000; i++)
            distributed.Add("station", i, false, 370, 320, i < 226);
        distributed.CandidatePixelBytes = RenderMemory.CandidatePixels(new(2560, 1440));
        distributed.RetainedBytes = 256L * 1024 * 1024;
        distributed.RequireBudget(false);
        check(distributed.WorkingGeometryBytes(false) < RenderMemory.GeometryLimit &&
            distributed.EstimatedPeakBytes >= distributed.CandidatePixelBytes + distributed.RetainedBytes + RenderMemory.GeometryLimit,
            "Distributed station geometry reserves a classification phase and the retained drawing without summing mutually exclusive per-face work");
        distributed.RetainedBytes = 2L * 1024 * 1024 * 1024;
        try { distributed.RequireBudget(false); rejected = false; }
        catch (InvalidOperationException) { rejected = distributed.Failure == "combined-memory"; }
        check(rejected, "A large station cannot replace an accepted drawing when the combined peak is over budget");

        var largeHull = new Mesh("hull", [[0, 0, 0], [100, 0, 0], [0, 100, 0]], new int[3_000_000], [], [], [], "structure", null, null);
        var smallPanel = new Mesh("panel", [[0, 0, 0], [.1, 0, 0], [0, .1, 0]], [0, 1, 2], [], [], [], "solar", null, null)
        {
            LineworkNode = 7
        };
        var protectedHose = smallPanel with { ConnectedParts = [7, 8] };
        var physical = new Capture([largeHull, smallPanel, protectedHose]);
        var plan = NormalLinework.Prepare(physical, default);
        var exploded = new Capture([largeHull, smallPanel.Translate(new(50, 30, 0)), protectedHose]);
        var explodedInk = NormalLinework.Prepare(exploded, default, physical);
        check(!plan.Meshes[0].FeatureInk && plan.Meshes[1].FeatureInk && !plan.Meshes[2].FeatureInk &&
            plan.Meshes[1].LineworkNode == 7 && ReferenceEquals(plan.Meshes[1].Indices, smallPanel.Indices) &&
            ReferenceEquals(plan.Meshes[1].Vertices, smallPanel.Vertices),
            "Large Normal scopes simplify small-panel ink without changing geometry, identity or protected hoses");
        check(explodedInk.Meshes[1].FeatureInk && ReferenceEquals(explodedInk.Meshes[1].Vertices, exploded.Meshes[1].Vertices)
            && !NormalLinework.Prepare(new([smallPanel]), default).Meshes[0].FeatureInk,
            "Explosion spread cannot change the physical detail tier and a focused small scope keeps full ink");
    }
}
