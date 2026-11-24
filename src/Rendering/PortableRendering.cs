#if PAWPRINTS_PORTABLE_RENDERING
using SkiaSharp;

namespace Blueprint.Rendering;

static class PortableRendering
{
    static SKTypeface? mono;
    static IReadOnlyDictionary<string, SKTypeface>? initials;

    public static bool Active => mono != null;

    public static void Initialize(string monoPath)
    {
        if (Active)
            return;

        var faces = new Dictionary<string, SKTypeface>(StringComparer.Ordinal);
        var defaultFace = SKTypeface.FromFile(monoPath)
            ?? throw new InvalidDataException("Cannot load the drawing font.");
        try
        {
            foreach (var name in InitialsFont.Names)
            {
                using var stream = InitialsFont.OpenBundled(name);
                faces.Add(name, SKTypeface.FromStream(stream)
                    ?? throw new InvalidDataException("Cannot load the initials font " + name));
            }
        }
        catch
        {
            foreach (var face in faces.Values)
                face.Dispose();
            defaultFace.Dispose();
            throw;
        }

        // Keep the immutable typefaces for the lifetime of the mod assembly.
        initials = faces;
        mono = defaultFace;
    }

    static SKFont Font(SKTypeface face, float height, bool bold = false, bool italic = false) =>
        new(face, Math.Max(1, height), skewX: italic ? -.2f : 0)
        {
            Embolden = bold,
            Edging = SKFontEdging.Antialias
        };

    public static float MonoWidth(int height)
    {
        using var font = Font(mono!, height);
        return font.MeasureText("MMMMMMMMMM") / 10f;
    }

    public static unsafe void Apply(byte[] rgb, int width, int height, List<TextRun> runs)
    {
        if (runs.Count == 0)
            return;

        var rgba = new byte[checked(width * height * 4)];
        for (int src = 0, dst = 0; src < rgb.Length; src += 3, dst += 4)
        {
            rgba[dst] = rgb[src];
            rgba[dst + 1] = rgb[src + 1];
            rgba[dst + 2] = rgb[src + 2];
            rgba[dst + 3] = 255;
        }

        fixed (byte* pixels = rgba)
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque),
                (nint)pixels, checked(width * 4)) ?? throw new InvalidOperationException("Cannot allocate the text surface.");
            using var paint = new SKPaint { IsAntialias = true };
            foreach (var run in runs)
                Draw(surface.Canvas, run, paint);

            surface.Canvas.Flush();
        }

        for (int src = 0, dst = 0; dst < rgb.Length; src += 4, dst += 3)
        {
            rgb[dst] = rgba[src];
            rgb[dst + 1] = rgba[src + 1];
            rgb[dst + 2] = rgba[src + 2];
        }
    }

    static void Draw(SKCanvas canvas, TextRun run, SKPaint paint)
    {
        var face = run.Face != null && initials!.TryGetValue(run.Face, out var requested) ? requested : mono!;
        using var normal = Font(face, run.Height, run.Bold, run.Italic);
        float baseline = run.Y - normal.Metrics.Ascent;
        float x = run.X;
        paint.Color = new((byte)run.Color.X, (byte)run.Color.Y, (byte)run.Color.Z);
        foreach (var span in run.Spans ?? [new(run.Text, run.Outline)])
        {
            float height = Math.Max(1, MathF.Round(run.Height * span.Scale));
            using var scaled = span.Scale == 1 ? null : Font(face, height, run.Bold, run.Italic);
            var font = scaled ?? normal;
            paint.Style = span.Outline ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
            paint.StrokeWidth = span.Stroke > 0 ? span.Stroke : Math.Max(1, height / 23);
            canvas.DrawText(span.Text, x, baseline, SKTextAlign.Left, font, paint);
            float advance = font.MeasureText(span.Text);
            if (run.Underline)
            {
                var metrics = font.Metrics;
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = metrics.UnderlineThickness ?? Math.Max(1, height / 16);
                float line = baseline + (metrics.UnderlinePosition ?? height / 12);
                canvas.DrawLine(x, line, x + advance, line, paint);
            }
            x += advance;
        }
    }

    public static unsafe LogoImage DecodePng(byte[] bytes)
    {
        var (width, height) = LogoPng.Validate(bytes);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec == null || codec.EncodedFormat != SKEncodedImageFormat.Png ||
            codec.Info.Width != width || codec.Info.Height != height)
            throw new InvalidDataException("Cannot decode this PNG logo.");

        var rgba = new byte[checked(width * height * 4)];
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        fixed (byte* pixels = rgba)
            if (codec.GetPixels(info, (nint)pixels) != SKCodecResult.Success)
                throw new InvalidDataException("Cannot decode this PNG logo.");
        return new(width, height, rgba);
    }
}
#endif
