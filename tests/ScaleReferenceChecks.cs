using Blueprint.Rendering;
using System.Numerics;

static class ScaleReferenceChecks
{
    public static void CheckFlips(Action<bool, string> check, KittenModel model, string name)
    {
        var options = new PrintOptions(Grid: false);
        var colors = SheetLayout.Colors(options.Theme);
        const int width = 2400, height = 3000;
        var scene = new RenderScene(new byte[width * height * 3], Enumerable.Repeat(-1, width * height).ToArray(), [],
            [new(7, "SOURCE", new(400, 300, 200, 400), new(200, 400, 170, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitY), 2)], options: options).WithKittens([], model);
        foreach (bool outline in new[] { false, true })
        {
            var original = KittenReferences.FromView(scene, 1, 7, outline) with
            {
                X = 200,
                Y = 200
            };
            var bounds = KittenReferences.Bounds(scene, original);
            int w = (int)bounds.Width, h = (int)bounds.Height;
            byte[] Render(KittenReference reference)
            {
                // Compare reference ink on uniform paper; sheet grain must not be mirrored.
                var canvas = new Canvas(new byte[width * height * 3], width, height, colors.Ink);
                KittenReferences.Paint(canvas, scene.WithKittens([reference]), default);
                var crop = new byte[w * h * 3];
                for (int y = 0; y < h; y++)
                    Array.Copy(canvas.Pixels, ((200 + y) * width + 200) * 3, crop, y * w * 3, w * 3);
                return crop;
            }
            var baseline = Render(original);
            foreach (var (horizontal, vertical) in new[] { (true, false), (false, true), (true, true) })
            {
                var flipped = original with
                {
                    FlipHorizontal = horizontal,
                    FlipVertical = vertical
                };
                var actual = Render(flipped);
                bool matches = true;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        for (int c = 0; c < 3; c++)
                            matches &= actual[(y * w + x) * 3 + c] == baseline[((vertical ? h - 1 - y : y) * w + (horizontal ? w - 1 - x : x)) * 3 + c];
                check(matches && KittenReferences.Bounds(scene, flipped) == bounds && flipped.SourceView == original.SourceView,
                    $"{name} {(outline ? "outline" : "silhouette")} flip H={horizontal} V={vertical} mirrors exact ink without changing bounds or source link");
            }
            var doc = new AnnotationDocument(scene.WithKittens([original]));
            var changed = original with
            {
                FlipHorizontal = true,
                FlipVertical = true
            };
            doc.Rebind(doc.Scene.WithKittens([changed]), [], []);
            doc.Undo();
            check(doc.Scene.Kittens.Single() == original, $"{name} flip undo restores orientation and view link");
            doc.Redo();
            check(doc.Scene.Kittens.Single() == changed, $"{name} flip redo restores both directions");
        }
    }

    public static void Run(Action<bool, string> check, string output)
    {
        var model = BananaReference.Model;
        CheckFlips(check, model, "Banana");
        check(Math.Abs(model.Vertices.Max(v => v.Z) - model.Vertices.Min(v => v.Z) - .40f) < .00001f,
            "Banana reference spans a nominal 40 cm tip to tip");
        check(model.Indices.All(i => i >= 0 && i < model.Vertices.Length) && model.Indices.Length % 3 == 0,
            "Procedural reference has valid bounded triangle geometry");
        var side = KittenReferences.Project(model, 2, 1000);
        var iso = KittenReferences.Project(model, 4, 1000);
        check(Math.Abs(side.Height - 402) <= 1 && iso.Width != side.Width,
            "Upright banana is 40 cm high in side view and projects differently in isometric");
        var doubled = KittenReferences.Project(model, 2, 2000);
        check(Math.Abs((doubled.Width - 2) - 2 * (side.Width - 2)) <= 1,
            "Banana size follows the main drawing scale");
        var options = new PrintOptions(Grid: false);
        var colors = SheetLayout.Colors(options.Theme);
        var canvas = new Canvas(2400, 3000, colors.Paper, colors.Ink, options);
        var scene = new RenderScene(canvas.Pixels, Enumerable.Repeat(-1, 2400 * 3000).ToArray(), [],
            [new(1, "FRONT", new(900, 500, 200, 400), new(200, 400, 1000, Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitY))], options: options);
        KittenReference[] references = [new(1, 2, 250, 350, true, ScaleReferenceKind.Banana, FlipHorizontal: true),
            new(2, 4, 700, 350, true, ScaleReferenceKind.Banana),
            new(3, 2, 250, 850, false, ScaleReferenceKind.Banana),
            new(4, 4, 700, 850, false, ScaleReferenceKind.Banana)];
        var withReferences = scene.WithKittens(references);
        var source = new Sheet("Banana scale references", options, canvas.Pixels, [], []) { Scene = scene };
        var composed = AnnotationPainting.Compose(source with
        {
            Scene = withReferences
        }, [], default);
        check(withReferences.KittenModel == null && !composed.Rgb.SequenceEqual(source.Rgb),
            "Both reference styles render without loading or requiring kitten game assets");
        check(model.Outlines.Values.All(ink => ink.Count(v => v > 0) > 40), "Outline references contain visible linework");
        var doc = new AnnotationDocument(scene);
        doc.Rebind(withReferences, [], []);
        doc.Undo();
        check(doc.Scene.Kittens.Length == 0, "Undo removes banana references");
        doc.Redo();
        check(doc.Scene.Kittens.SequenceEqual(references), "Redo restores reference type, styles and positions");
        doc.Rebind(withReferences.WithKittens(references.Where(k => k.Id != 1).ToArray()), [], []);
        check(doc.Scene.Kittens.Length == 3 && doc.Scene.Kittens.All(k => k.Id != 1), "Removing a reference preserves its neighbours");
        doc.Undo();
        check(doc.Scene.Kittens.SequenceEqual(references), "Undo restores a deleted reference");
        doc.Redo();
        check(doc.Scene.Kittens.Length == 3, "Redo removes the same reference again");
        doc.Rebind(doc.Scene.WithKittens([]), [], []);
        check(doc.Scene.Kittens.Length == 0, "Removing the last references leaves none");
        var moved = KittenReferences.Clamp(withReferences, references[0] with
        {
            X = -500,
            Y = 99999
        });
        check(PageLayout.Fits(KittenReferences.Bounds(withReferences, moved), options), "Reference dragging respects the printed frame and title block");
        var regenerated = CompositionRegeneration.Rebuild(source, composed, default);
        check(regenerated.Scene!.Kittens.SequenceEqual(references), "Regeneration retains banana references");
        Directory.CreateDirectory(output);
        // Export a close-up contact sheet for visual inspection at a known scale.
        var crop = new byte[900 * 1100 * 3];
        for (int y = 0; y < 1100; y++)
            Array.Copy(composed.Rgb, ((y + 250) * 2400 + 150) * 3, crop, y * 900 * 3, 900 * 3);
        string target = Path.Combine(output, "banana-side-iso.png");
        if (File.Exists(target))
            File.Delete(target);
        Png.Write(target, 900, 1100, crop);
    }
}
