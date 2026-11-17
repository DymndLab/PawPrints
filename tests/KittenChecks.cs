using Blueprint.Rendering;
using System.Numerics;
static class KittenChecks
{

    public static void Run(Action<bool, string> check, string output, string path)
    {
        var model = KittenModel.Load(path);
        ScaleReferenceChecks.CheckFlips(check, model, "Kitten");
        model.Outlines.Clear();
        float height = model.Vertices.Max(v => v.Z) - model.Vertices.Min(v => v.Z);
        check(Math.Abs(height - 1.06036067f) < .0001f, "Installed helmet-free kitten body matches the 1.06036m reference rest-pose height");
        var side = KittenReferences.Project(model, 2, 600);
        var iso = KittenReferences.Project(model, 4, 600);
        check(Math.Abs(side.Height - (height * 600 + 2)) < 1.1f && iso.Height != side.Height, "Side and isometric silhouettes project the same metre geometry without independent resizing");
        check(ReferenceEquals(side, KittenReferences.Project(model, 2, 600)), "Repeated editor hit tests reuse bounded projected geometry");
        var opts = new PrintOptions(Theme: 0, Grid: true, Layout: 1, TitleBlock: new(Title: "Kitten scale references"));
        var colors = SheetLayout.Colors(opts.Theme);
        var canvas = new Canvas(2400, 3000, colors.Paper, colors.Ink, opts);
        var scene = new RenderScene(canvas.Pixels, Enumerable.Repeat(-1, 2400 * 3000).ToArray(), [], [new(1, "FRONT", new(130, 280, 2140, 2100), new(2140, 2100, 600, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitY))], options: opts);
        KittenReference[] kittens = [new(1, 2, 300, 400), new(2, 4, 1300, 400), new(3, 2, 300, 1450, true), new(4, 4, 1300, 1450, true)];
        var composedScene = scene.WithKittens(kittens, model);
        for (int camera = 0; camera < DrawingPlacements.Names.Length; camera++)
        {
            var axes = DrawingPlacements.Axes(new(10, camera, 0, 0));
            // Include an unfolded/rolled orientation, not only named camera presets.
            if (camera == 2)
                axes = new(axes.Up, -axes.Right, axes.Toward);
            var frame = new ProjectionFrame(600, 800, 250, Vector3.Zero, axes.Right, axes.Up, axes.Toward);
            var matching = new RenderScene(canvas.Pixels, Enumerable.Repeat(-1, 2400 * 3000).ToArray(), [], [new(10, "SOURCE", new(300, 300, 600, 800), frame, camera)], options: opts).WithKittens([], model);
            var reference = KittenReferences.FromView(matching, 1, 10) with
            {
                X = 300,
                Y = 400
            };
            var bounds = KittenReferences.Bounds(matching, reference);
            var projected = model.Vertices.Select(v => new Vector2(Vector3.Dot(v, axes.Right), -Vector3.Dot(v, axes.Up))).ToArray();
            var extent = projected.Aggregate(Vector2.Max) - projected.Aggregate(Vector2.Min);
            check(reference.View == camera && reference.SourceView == 10 &&
                Math.Abs(bounds.Width - (MathF.Ceiling(extent.X * 250) + 2)) < .01f &&
                Math.Abs(bounds.Height - (MathF.Ceiling(extent.Y * 250) + 2)) < .01f,
                $"View-matched kitten uses actual {DrawingPlacements.Names[camera]} axes and scale");
            var matchedCanvas = new Canvas(2400, 3000, colors.Paper, colors.Ink, opts);
            KittenReferences.Paint(matchedCanvas, matching.WithKittens([reference]), default);
            check(model.Outlines.Values.Any(ink => ink.Count(v => v > 0) > 100), $"View-matched {DrawingPlacements.Names[camera]} outline renders");
        }
        model.Outlines.Clear();
        var original = new Sheet("Kitten references", opts, canvas.Pixels, Sheet.MakePreview(canvas.Pixels), []) { Scene = scene };
        var print = AnnotationPainting.Compose(original with
        {
            Scene = composedScene
        }, [], default);
        check(!print.Rgb.SequenceEqual(original.Rgb) && print.Scene!.Kittens.Length == 4, "Actual game body renders both styles in both views, with no helmet asset");
        check(model.Outlines.Count == 2 && model.Outlines.Values.All(ink => ink.Count(v => v > 0) > 100 && ink.Count(v => v > 0) < ink.Length / 3), "Blueprint kitten uses sparse visible linework from the craft renderer, not a filled silhouette");
        var doc = new AnnotationDocument(scene);
        doc.Rebind(composedScene, [], []);
        doc.Undo();
        check(doc.Scene.Kittens.Length == 0, "Undo removes reference silhouettes");
        doc.Redo();
        check(doc.Scene.Kittens.SequenceEqual(kittens) && ReferenceEquals(doc.Scene.KittenModel, model), "Redo restores positions and reuses detached geometry");
        var moved = KittenReferences.Clamp(composedScene, kittens[0] with
        {
            X = -10000,
            Y = 99999
        });
        var box = KittenReferences.Bounds(composedScene, moved);
        var area = FrameLayout.Area(opts);
        check(box.X >= area.X && box.Bottom <= area.Bottom, "Reference dragging stays within printable paper at true scale");
        var initial = new KittenReference(5, 2, area.X + 80, area.Bottom - 160, true);
        check(!PageLayout.Fits(KittenReferences.Bounds(composedScene, initial), opts), "Regression fixture reproduces a kitten preview initially overlapping the title block");
        var placement = KittenReferences.PlacementScene(scene, initial, model);
        var preview = PlacementPreviews.Panel(placement, KittenReferences.Bounds(placement, placement.Kittens[0]), c => KittenReferences.Paint(c, placement, default));
        check(PageLayout.Fits(KittenReferences.Bounds(placement, placement.Kittens[0]), opts), "New kitten preview starts clear of the title block without changing its scale");
        var regenerated = CompositionRegeneration.Rebuild(original, print, default);
        check(regenerated.Scene!.Kittens.SequenceEqual(kittens) && ReferenceEquals(regenerated.Scene.KittenModel, model), "Generate retains both kitten reference views and positions");
        var text = new Canvas(print.Rgb, 2400, 3000, colors.Ink);
        text.Text("SIDE", 300, 1120, 36, 1);
        text.Text("ISOMETRIC", 1300, 1120, 36, 1);
        text.Text("SIDE / BLUEPRINT OUTLINE", 300, 2170, 36, 1);
        text.Text("ISO / BLUEPRINT OUTLINE", 1300, 2170, 36, 1);
        text.Text("KSA BODY / HELMET OFF / NEUTRAL POSE / SAME SCALE", 200, 200, 36, 1);
        NativeText.Apply(text.Pixels, 2400, 3000, text.Runs);
        Directory.CreateDirectory(output);
        string sample = Path.Combine(output, "kitten-views.png");
        if (File.Exists(sample))
            File.Delete(sample);
        Png.Write(sample, 2400, 3000, text.Pixels);
        File.WriteAllText(Path.Combine(output, "kitten-source.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            path,
            height,
            vertices = model.Vertices.Length,
            triangles = model.Indices.Length / 3,
            sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)))
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
