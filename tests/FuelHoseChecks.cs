using Blueprint.Rendering;
using System.Numerics;

static class FuelHoseChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var samples = Enumerable.Range(0, 33).Select(i =>
        {
            float angle = MathF.PI * .5f * i / 32;
            return new HoseSample(new(MathF.Sin(angle), 0, MathF.Cos(angle)),
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle + MathF.PI * .5f));
        }).ToArray();
        var hose = FuelHoseGeometry.Build(samples, 1, 2, 1000000)!;
        Vector3 Point(double[] p) => new((float)p[0], (float)p[1], (float)p[2]);
        check(hose.Vertices.Length == samples.Length * 16 && hose.Indices.Length == 32 * 16 * 6,
            "Fuel hose forms the native 16-sided tube across every captured route sample");
        check(hose.Indices.All(i => i >= 0 && i < hose.Vertices.Length) && hose.ConnectedParts.SequenceEqual(new uint[] { 1, 2 }),
            "Hose indices and both endpoint identities survive capture");
        check(hose.Vertices.Select((p, i) => Math.Abs(Vector3.Distance(Point(p), samples[i / 16].Position) - .05f)).Max() < 1e-6f,
            "Fuel hose follows the routed curve with the native 0.05 metre radius");
        check(hose.SourceNormals.All(n => Math.Abs(Point(n).Length() - 1) < 1e-6f), "Tube normals are finite unit vectors");
        bool outward = true;
        for (int i = 0; i < hose.Indices.Length; i += 3)
        {
            int a = hose.Indices[i], b = hose.Indices[i + 1], c = hose.Indices[i + 2];
            outward &= Vector3.Dot(Vector3.Cross(Point(hose.Vertices[b]) - Point(hose.Vertices[a]),
                Point(hose.Vertices[c]) - Point(hose.Vertices[a])), Point(hose.SourceNormals[a])) > 0;
        }
        check(outward, "Curved hose triangles face outward, including the closed radial seam");
        check(FuelHoseGeometry.Build([], 1, 2, 0) == null
            && FuelHoseGeometry.Build([new(Vector3.Zero, Quaternion.Identity), new(Vector3.Zero, Quaternion.Identity)], 1, 2, 32) == null,
            "Empty and zero-length links produce no degenerate meshes");
        void Reject(HoseSample[] input, int budget, string reason)
        {
            bool rejected = false;
            try { FuelHoseGeometry.Build(input, 1, 2, budget); }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException) { rejected = true; }
            check(rejected, reason);
        }
        Reject(samples, hose.Vertices.Length - 1, "Hoses share the bounded craft vertex budget");
        Reject(Enumerable.Repeat(samples[0], 257).ToArray(), 1000000, "Excess route samples fail before mesh allocation");
        Reject([samples[0], new(new(float.NaN, 0, 0), Quaternion.Identity)], 1000000, "Nonfinite hose positions are rejected");
        Reject([samples[0], new(Vector3.One, default)], 1000000, "Invalid hose rotations are rejected");
        var scene = new AssemblyCapture(new([hose]), [new(0, null, "Craft", "craft", false),
            new(1, 0, "Port A", "port", false), new(2, 0, "Port B", "port", false)], [1], [false]);
        var deltas = Enumerable.Repeat(Matrix4x4.CreateTranslation(1, 2, 3), 101).ToArray();
        var control = new PoseControl(0, "move", "Craft", false, 0, [new(0, 1, [0, 1, 2], deltas)]);
        var source = new PoseSource(scene, [control], 1, [new(0, "Port A", "port", [0], NodeId: 1)], new Dictionary<string, Texture>());
        var moved = PartPoses.Apply(source, [new(0, "move", -1, 100)], 100);
        check(Vector3.Distance(ExplodedGeometry.World(moved.Geometry.Meshes[0].Vertices[0]),
            ExplodedGeometry.World(hose.Vertices[0]) + new Vector3(1, 2, 3)) < 1e-5f,
            "A shared rigid print pose carries the hose with both connected parts");
        bool blocked = false;
        try
        {
            PartPoses.Apply(source with { Controls = [control with { Tracks = [new(1, 2, [1], deltas)] }] },
                [new(0, "move", -1, 100)], 100);
        }
        catch (InvalidOperationException e) { blocked = e.Message.Contains("fuel hose"); }
        check(blocked && ReferenceEquals(scene.Geometry.Meshes[0], hose),
            "Independent endpoint posing fails explicitly rather than drawing a disconnected hose or mutating the capture");
        var detail = ExplodedGeometry.Build(scene, new(1, 0, new(0, 0, 600, 600), "Hose fixture", Separation: 1));
        check(detail.Capture.Meshes.Length == 1 && detail.OriginalMeshes.Single() == 0 && detail.GroupNodes.Single() == 1,
            "Exploded geometry retains the complete hose with its first fuel port");
        var frame = new ProjectionFrame(400, 400, 200, new(0, .5f, .5f), Vector3.UnitZ, Vector3.UnitY, Vector3.UnitX);
        var exposed = DetailSampling.Render(new([hose]), source.Maps, "Hose", 0, frame, default);
        double[][] coverPoints = [[-.5, 1, -.5], [1.5, 1, -.5], [1.5, 1, 1.5], [-.5, 1, 1.5]];
        var cover = new Mesh("Tank wall", coverPoints, [0, 2, 1, 0, 3, 2], coverPoints,
            coverPoints.Select(_ => new double[] { 0, 1, 0 }).ToArray(), coverPoints.Select(_ => new double[] { 0, 0 }).ToArray(), "structure", null, null);
        var hidden = DetailSampling.Render(new([hose, cover]), source.Maps, "Covered hose", 0, frame, default);
        check(exposed.MeshOwners.Any(id => id == 0) && hidden.MeshOwners.All(id => id != 0) && hidden.MeshOwners.Any(id => id == 1),
            "Fuel hose participates in depth occlusion instead of showing through covering craft geometry");
        var options = new PrintOptions(Boxes: new(1, 4), DrawingSize: 100, TitleBlock: new(Title: "Fuel hose regression"));
        var print = PosePainting.RenderMain(source, "Fuel hose regression", options, [], [], default);
        check(print.MainInk!.Any(tile => tile.Pixels.Any(p => p != 0)) && print.Scene!.Owners.Any(id => id == 0),
            "Captured hose reaches the normal drawing and picking pipelines as visible geometry");
        string path = PrintFiles.Save(print, output);
        check(Png.ReadOwn(path).SequenceEqual(print.Rgb), "Fuel-hose PNG export retains exact rendered pixels");
        Console.WriteLine("Fuel hose visual fixture: " + path);
    }
}
