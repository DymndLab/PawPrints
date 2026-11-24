using System.Numerics;
namespace Blueprint.Rendering;

sealed record TextSpan(string Text, bool Outline = false, float Scale = 1, int Stroke = 0);
sealed record TextRun(string Text, int X, int Y, int Height, Vector3 Color, bool Bold = false, bool Italic = false, bool Outline = false, string? Face = null, TextSpan[]? Spans = null, bool Underline = false);

sealed record PrintOptions(int View = 0, int Theme = 0, bool Grid = true, bool Archive = false, int Emphasis = 10, int Layout = 0, int VisibleViews = 15, bool Captions = true,
    bool PartsTable = false, bool FlagTable = false, int DrawingSize = 0, int Orientation = 0, int Aspect = 0, int Brand = 0, TitleBlockData? TitleBlock = null, int Arrangement = 0, FixedViewLayout? Boxes = null, DrawingPlacement? Drawing = null, bool Zones = true, int CustomWidth = 2560, int CustomHeight = 1440, int WearLevel = 3, DrawingOutput OutputTarget = DrawingOutput.Image)
{
    public PageSize Page => Orientation == 3 ? PageSize.Workspace(CustomWidth, CustomHeight) : PageSize.From(Orientation, Aspect);
    public PageSize OutputPage => Orientation == 3 ? PageSize.Custom(CustomWidth, CustomHeight) : Page;
}
sealed record ViewInk(Box Bounds, int Width, int Height, byte[] Pixels);
sealed record Sheet(string Title, PrintOptions Options, byte[] Rgb, byte[] PreviewRgba, string[] Warnings)
{
    public const int Width = 2400, Height = 3000, PreviewWidth = Width, PreviewHeight = Height;
    public int PixelWidth => Options.Page.Width;
    public int PixelHeight => Options.Page.Height;
    public RenderScene? Scene
    {
        get; init;
    }
    public ViewInk[]? MainInk
    {
        get; init;
    }
    public PartLabel[] Labels { get; init; } = [];
    public ExplodedDetail[] Details { get; init; } = [];
    public ExplodedSource? DetailSource
    {
        get; init;
    }
    public string? Notice
    {
        get; init;
    }
    public Sheet? RegeneratedBase
    {
        get; init;
    }
    public PoseSource? PoseSource
    {
        get; init;
    }
    public Sheet? PoseMain
    {
        get; init;
    }

    public Sheet WithPixels(byte[] pixels) => this with { Rgb = pixels, PreviewRgba = MakePreview(pixels) };

    public static byte[] MakePreview(byte[] rgb)
    {
        // Preserve every print pixel for zoomed inspection. The GPU's filtered
        // mip chain handles minification instead of baking in a half-size copy.
        var preview = new byte[rgb.Length / 3 * 4];
        for (int source = 0, dest = 0; source < rgb.Length; source += 3, dest += 4)
        {
            preview[dest] = rgb[source];
            preview[dest + 1] = rgb[source + 1];
            preview[dest + 2] = rgb[source + 2];
            preview[dest + 3] = 255;
        }
        return preview;
    }
}

sealed partial class BlueprintRenderer
{

    void DrawCraftInk(Action<int, int, float> put)
    {
        for (int y = 1; y < H - 1; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 1; x < W - 1; x++)
            {
                int q = y * W + x, id = face[q];
                float line = 0;
                if (id >= 0)
                {
                    var t = Triangles[id];
                    var m = capture.Meshes[t.Mesh];
                    line = structuralLines[q];
                    float nd = 0, ld = 0;
                    foreach (int j in new[] { q - 1, q + 1, q - W, q + W })
                    {
                        if (face[j] < 0)
                        {
                            line = 1;
                            continue;
                        }
                        var other = Triangles[face[j]];
                        if (t.Mesh != other.Mesh)
                        {
                            line = Math.Max(line, .72f);
                            continue;
                        }
                        if (m.Surface != "structure")
                        {
                            nd = Math.Max(nd, Vector3.Distance(surfaceNormal[q], surfaceNormal[j]));
                            ld = Math.Max(ld, Math.Abs(luma[q] - luma[j]));
                        }
                    }
                    if (m.Surface == "solar")
                        line = Math.Max(line, solarInk?[q] ?? 0);
                    line = Math.Max(line, HatchInk(q, x, y, 5));
                }
                float neighbor = Math.Max(Math.Max(structuralLines[q - 1], structuralLines[q + 1]), Math.Max(structuralLines[q - W], structuralLines[q + W]));
                put(x, y, Math.Max(line, neighbor * .3f));
            }
        }
    }


    internal ViewPixels RenderView(Action<string> progress)
    {
        progress("Drawing view");
        Rasterize();
        PrepareStructure(22);
        PreservePanelCorners();
        PrepareHatching();
        var ink = new byte[W * H];
        var owners = new int[W * H];
        DrawCraftInk((x, y, a) => ink[y * W + x] = (byte)Math.Clamp((int)(a * 255), 0, 255));
        for (int q = 0; q < owners.Length; q++)
            owners[q] = face[q] < 0 ? -1 : Triangles[face[q]].Mesh;
        return new(W, H, ink, owners);
    }

    internal int[] MeshOwners() => face.Select(id => id < 0 ? -1 : Triangles[id].Mesh).ToArray();

    public Sheet Render(PrintOptions options, string[] warnings, Action<string> progress)
    {
        progress("Drawing the craft");
        Rasterize();
        cancellation.ThrowIfCancellationRequested();
        progress("Tracing panel edges");
        PrepareStructure(22);
        PreservePanelCorners();
        cancellation.ThrowIfCancellationRequested();
        progress("Drawing crosshatching");
        PrepareHatching();
        cancellation.ThrowIfCancellationRequested();
        progress("Preparing the print");
        var (paper, ink) = options.Theme switch
        {
            1 => (new Vector3(240, 241, 239), new Vector3(48, 56, 64)),
            2 => (new Vector3(243, 235, 216), new Vector3(68, 60, 50)),
            3 => (new Vector3(43, 47, 51), new Vector3(233, 234, 226)),
            _ => (new Vector3(19, 51, 87), new Vector3(214, 232, 242))
        };
        var canvas = new Canvas(W, H, paper, ink, options);
        DrawCraftInk(canvas.Ink);
        PageLayout.Decorate(canvas, title, options);
        NativeText.Apply(canvas.Pixels, W, H, canvas.Runs);
        var preview = Sheet.MakePreview(canvas.Pixels);
        cancellation.ThrowIfCancellationRequested();
        return new(title, options, canvas.Pixels, preview, warnings);
    }
}

sealed class Canvas
{
    readonly int width, height;
    readonly Vector3 ink;
    public readonly byte[] Pixels;
    public readonly List<TextRun> Runs = [];

    public Canvas(int width, int height, Vector3 paper, Vector3 ink, PrintOptions options)
    {
        this.width = width;
        this.height = height;
        this.ink = ink;
        Pixels = new byte[width * height * 3];
        var zones = SheetZones.Count(options);
        var gx = SheetZones.GridAxis(width, zones.Columns);
        var gy = SheetZones.GridAxis(height, zones.Rows);
        // Level 3 retains the original archival appearance for existing callers.
        float wear = options.Archive ? Math.Clamp(options.WearLevel, 0, 5) / 3f : 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                uint noise = unchecked((uint)(x * 374761393 + y * 668265263));
                noise = (noise ^ (noise >> 13)) * 1274126177;
                float grain = ((noise & 255) / 255f - .5f) * (.6f + 1.4f * wear);
                float margin = Math.Min(Math.Min(x, width - 1 - x), Math.Min(y, height - 1 - y));
                float age = MathF.Exp(-margin / 90f) * .06f * wear;
                var color = paper * (1 - age) + new Vector3(grain);
                float rule = 0;
                int gridLine = Math.Max(gx[x], gy[y]);
                if (options.Grid && gridLine > 0)
                {
                    rule = .07f;
                    if (options.Emphasis >= 5 && gridLine >= 5)
                        rule = .11f;
                    if (options.Emphasis == 10 && gridLine == 10)
                        rule = .16f;
                }
                color = Vector3.Lerp(color, ink, rule);
                int q = (y * width + x) * 3;
                Pixels[q] = (byte)Math.Clamp(color.X, 0, 255);
                Pixels[q + 1] = (byte)Math.Clamp(color.Y, 0, 255);
                Pixels[q + 2] = (byte)Math.Clamp(color.Z, 0, 255);
            }
    }

    public Canvas(byte[] pixels, int width, int height, Vector3 ink)
    {
        this.width = width;
        this.height = height;
        this.ink = ink;
        Pixels = pixels;
    }

    public void Ink(int x, int y, float alpha)
        => Color(x, y, ink, alpha);

    public void Color(int x, int y, Vector3 color, float alpha)
    {
        if (x < 0 || y < 0 || x >= width || y >= height || alpha <= 0)
            return;
        int q = (y * width + x) * 3;
        var mixed = Vector3.Lerp(new(Pixels[q], Pixels[q + 1], Pixels[q + 2]), color, Math.Clamp(alpha, 0, 1));
        Pixels[q] = (byte)mixed.X;
        Pixels[q + 1] = (byte)mixed.Y;
        Pixels[q + 2] = (byte)mixed.Z;
    }

    public void Line(int x0, int y0, int x1, int y1, float alpha)
    {
        int n = Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
        for (int i = 0; i <= n; i++)
            Ink(x0 + (x1 - x0) * i / Math.Max(n, 1), y0 + (y1 - y0) * i / Math.Max(n, 1), alpha);
    }

    public void InlineText(TextSpan[] spans, int x, int y, int size) => Runs.Add(new(string.Concat(spans.Select(s => s.Text)), x, y, size, ink, true, true, false, "Consolas", spans));

    public void Text(string text, int x, int y, int size, float alpha, bool bold = false, bool italic = false, bool outline = false, string? face = null, bool underline = false) => Runs.Add(new(text, x, y, size, ink, bold, italic, outline, face, Underline: underline));
}

static class PrintFiles
{

    public static string SafeStem(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var stem = new string(title.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (stem.Length > 70)
            stem = stem[..70];
        return string.IsNullOrWhiteSpace(stem) ? "Craft" : stem;
    }

    public static string Save(Sheet sheet, string folder, bool requireExistingFolder = false)
    {
        if (requireExistingFolder)
            Blueprint.Storage.ExportPreferences.RequireExistingFolder(folder);
        else
            Directory.CreateDirectory(folder);
        string stem = $"PawPrints-{SafeStem(sheet.Title)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string target = Path.Combine(folder, stem + ".png"), temp = Path.Combine(folder, stem + ".partial");
        try
        {
            var output = sheet.Options.OutputPage;
            var rgb = DesktopExport.Resize(CompanyTitleBlock.StampExportDate(sheet, CompanyTitleBlock.LocalDate()), sheet.Options.Page, output);
            int density = (int)Math.Round(PrintScale.Density(sheet.Options) * (double)output.Width / sheet.PixelWidth);
            Png.Write(temp, output.Width, output.Height, rgb, density);
            File.Move(temp, target, false);
            return target;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
