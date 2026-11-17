using System.Text.Json;
using Blueprint.Rendering;

// These optional checks need privately captured, version-matched game assets.
static class FixtureChecks
{
    public static void Run(Action<bool, string> Check, string root, string output)
    {
        var cancelled = new CancellationToken(true);
        var c = JsonSerializer.Deserialize<Capture>(File.ReadAllText(Path.Combine(root, "materials/lander-1.materials.json")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var maps = new Dictionary<string, Texture>();
        foreach (var mesh in c.Meshes)
            foreach (var info in new[] { mesh.NormalTexture, mesh.DiffuseTexture })
                if (info != null && !maps.ContainsKey(info.Path))
                    maps.Add(info.Path, new(info, File.ReadAllBytes(Path.Combine(root, "materials", info.Path))));
        ExplodedChecks.Lander(Check, c, maps, output);
        var renderer = new BlueprintRenderer(c, maps, "Lander 1", 0, default);
        var sheet = renderer.Render(new(Grid: false), [], Console.WriteLine);
        bool previewExact = sheet.PreviewRgba.Length == Sheet.Width * Sheet.Height * 4;
        for (int i = 0; previewExact && i < Sheet.Width * Sheet.Height; i++)
            previewExact = sheet.PreviewRgba[i * 4] == sheet.Rgb[i * 3] && sheet.PreviewRgba[i * 4 + 1] == sheet.Rgb[i * 3 + 1] && sheet.PreviewRgba[i * 4 + 2] == sheet.Rgb[i * 3 + 2] && sheet.PreviewRgba[i * 4 + 3] == 255;
        Check(previewExact, "Preview preserves every printed ink/text pixel and opaque alpha at native resolution");
        var baseline = Png.ReadOwn(Path.Combine(root, "lander-1-native-annotation-base.png"));
        long error = 0;
        int count = 0, maxError = 0;
        // Compare craft ink within the reference region, excluding border and title typography.
        for (int y = 350; y < 2400; y++)
            for (int x = 150; x < 2250; x++)
                for (int k = 0; k < 3; k++)
                {
                    int q = (y * 2400 + x) * 3 + k;
                    int d = Math.Abs(sheet.Rgb[q] - baseline[q]);
                    error += d;
                    count++;
                    maxError = Math.Max(maxError, d);
                }
        double mae = (double)error / count;
        Check(mae < 1.0 && maxError <= 2, $"Captured craft matches the reference image within paper-grain tolerance (MAE {mae:F4}, max {maxError})");
        string one = PrintFiles.Save(sheet, output), two = PrintFiles.Save(sheet, output);
        Check(one != two && File.Exists(one) && File.Exists(two), "Repeated saves use distinct files without overwrites");
        Check(Png.ReadOwn(one).SequenceEqual(sheet.Rgb), "Exported PNG round-trips exactly");
        Check(!Directory.GetFiles(output, "*.partial").Any(), "Atomic save leaves no partial file");
        try
        {
            new BlueprintRenderer(c, maps, "cancelled", 0, cancelled).Render(new(), [], _ => { });
            throw new Exception("Render cancellation ignored");
        }
        catch (OperationCanceledException) { Check(true, "Renderer respects cancellation"); }
        var missingMaps = new Capture(c.Meshes.Select(m => m with { NormalTexture = null, DiffuseTexture = null }).ToArray());
        var fallback = new BlueprintRenderer(missingMaps, new Dictionary<string, Texture>(), "Missing maps", 0, default).Render(new(), ["Material unavailable"], _ => { });
        Check(fallback.Rgb.Length == 2400 * 3000 * 3 && fallback.Warnings.Length == 1, "Missing normal/color maps retain geometry without crashing");
        AnnotationChecks.Run(Check, c, maps, output, root);
        PageChecks.Run(Check, c, maps, output);
        TitleLayoutChecks.Run(Check, c, maps, output);
    }
}
