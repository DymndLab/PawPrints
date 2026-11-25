using System.IO.Compression;
using System.Numerics;

namespace Blueprint.Rendering;

static class WearPainting
{
    sealed record Sprite(int Width, int Height, byte[] Rgba);
    static readonly string[] Files = ["split", "nick", "hook", "jog", "rub", "rub", "soft", "wide", "coffee"];
    static readonly Dictionary<string, Lazy<Sprite>> Sprites = Files.Distinct().ToDictionary(n => n, n => new Lazy<Sprite>(() => Read(n)));
    static readonly float[][] Centres = [
        [69,67,75,72,65,69,70,67,67,63,55,54,61,67,71,72,67,64,63,67,75,77,73,70,72,74,72,71,72,74,75,74,76],
        [330,329,316,326,326,326,325,318,301,303,312,323,327,331,320,315,320,322,328,330,324,315,319,321,320,322,327,320,319,317,319,312,329],
        [149,164,157,164,157,155,148,144,148,144,134,155,142,150,158,162,168,182,182,188,193,211,227,246,263,283,310,334,359,382,408,447,496],
        [125,120,127,126,115,114,121,124,116,107,107,110,129,135,135,140,130,134,135,133,170,230,233,227,231,231,224,233,232,233,233,228,227]];
    static readonly float[] OriginalHeights = [138, 635, 500, 275];
    static Sprite Read(string name)
    {
        using var stream = typeof(WearPainting).Assembly.GetManifestResourceStream("PawPrints.Wear." + name + ".rgba.z")
            ?? throw new InvalidDataException("Missing wear texture " + name);
        using var header = new BinaryReader(stream);
        int w = header.ReadInt32(), h = header.ReadInt32();
        if (w != 768 || h is < 1 or > 1024)
            throw new InvalidDataException("Invalid wear texture dimensions.");
        var bytes = new byte[checked(w * h * 4)];
        using var decoder = new ZLibStream(stream, CompressionMode.Decompress);
        decoder.ReadExactly(bytes);
        if (decoder.ReadByte() != -1)
            throw new InvalidDataException("Invalid wear texture length.");
        return new(w, h, bytes);
    }

    sealed class Brush
    {
        readonly Sprite image;
        readonly WearMark mark;
        readonly Vector2 anchor, offset, axisX, axisY;
        readonly float width, height, cut;
        readonly Vector3 paper;
        readonly int theme;
        public Box Bounds
        {
            get;
        }
        public Brush(WearMark m, int theme, int pageWidth, int pageHeight)
        {
            mark = m;
            this.theme = theme;
            paper = SheetLayout.Colors(theme).Paper;
            image = Sprites[Files[(int)m.Kind]].Value;
            float padScale = m.Kind switch
            {
                WearKind.SoftRub => (1515 + 6 * 78) / 1515f,
                WearKind.BroadFade => (1515 + 6 * 115) / 1515f,
                _ => 1
            };
            width = m.Width * pageWidth * padScale;
            height = width * image.Height / image.Width * (m.Kind == WearKind.BroadFade ? .86f : m.Kind == WearKind.SmallScuff ? .94f : 1);
            anchor = new(m.X * pageWidth, m.Y * pageHeight);
            float a = m.Angle * MathF.PI / 180, fx = m.FlipX ? -1 : 1, fy = m.FlipY ? -1 : 1;
            axisX = new(MathF.Cos(a) * fx, MathF.Sin(a) * fy);
            axisY = new(-MathF.Sin(a) * fx, MathF.Cos(a) * fy);
            if (WearPlacement.Tear(m.Kind))
            {
                var line = Centres[(int)m.Kind];
                float hidden = 1 - m.Depth, t = hidden * (line.Length - 1);
                int i = (int)t;
                float center = float.Lerp(line[i], line[Math.Min(line.Length - 1, i + 1)], t - i) / OriginalHeights[(int)m.Kind];
                offset = new(-width * hidden - (hidden == 0 && m.Edge >= 0 ? pageWidth / 640f : 0), -height * center);
                cut = m.Edge < 0 ? hidden : 0;
            }
            else
                offset = new(-width / 2, -height / 2);
            var origin = anchor + axisX * offset.X + axisY * offset.Y;
            var dx = axisX * width;
            var dy = axisY * height;
            var min = origin + Vector2.Min(dx, Vector2.Zero) + Vector2.Min(dy, Vector2.Zero);
            var max = origin + Vector2.Max(dx, Vector2.Zero) + Vector2.Max(dy, Vector2.Zero);
            if (m.Edge == 0)
                min.X = Math.Max(min.X, anchor.X);
            if (m.Edge == 1)
                max.X = Math.Min(max.X, anchor.X);
            if (m.Edge == 2)
                min.Y = Math.Max(min.Y, anchor.Y);
            if (m.Edge == 3)
                max.Y = Math.Min(max.Y, anchor.Y);
            Bounds = new(MathF.Floor(min.X), MathF.Floor(min.Y), Math.Max(1, MathF.Ceiling(max.X) - MathF.Floor(min.X)), Math.Max(1, MathF.Ceiling(max.Y) - MathF.Floor(min.Y)));
        }
        Vector4 Texel(int x, int y)
        {
            int i = 4 * (y * image.Width + x);
            var data = image.Rgba;
            float a = data[i + 3] / 255f;
            return new(data[i] * a, data[i + 1] * a, data[i + 2] * a, a);
        }
        public (Vector3 Color, float Alpha) Sample(float x, float y, Vector3 background)
        {
            var delta = new Vector2(x, y) - anchor;
            float u = (Vector2.Dot(delta, axisX) - offset.X) / width, v = (Vector2.Dot(delta, axisY) - offset.Y) / height;
            if (u < cut || u < 0 || u >= 1 || v < 0 || v >= 1)
                return default;
            float sx = Math.Clamp(u * image.Width - .5f, 0, image.Width - 1), sy = Math.Clamp(v * image.Height - .5f, 0, image.Height - 1);
            int i = (int)sx, j = (int)sy, ii = Math.Min(i + 1, image.Width - 1), jj = Math.Min(j + 1, image.Height - 1);
            var tex = Vector4.Lerp(Vector4.Lerp(Texel(i, j), Texel(ii, j), sx - i), Vector4.Lerp(Texel(i, jj), Texel(ii, jj), sx - i), sy - j);
            if (tex.W <= 0 || mark.Strength <= 0)
                return default;
            var rgb = new Vector3(tex.X, tex.Y, tex.Z) / tex.W;
            bool dark = theme is 0 or 3;
            if (WearPlacement.Tear(mark.Kind))
                return (Vector3.Lerp(rgb, paper, dark ? .31f : .07f), tex.W);
            if (mark.Kind == WearKind.CoffeeRing)
            {
                var stain = background * rgb / 255;
                float a = tex.W * mark.Strength;
                if (!dark)
                    return (stain, a);
                float residue = tex.W * .15f * mark.Strength / .52f, combined = a + (1 - a) * residue;
                return ((stain * a * (1 - residue) + rgb * residue) / combined, combined);
            }
            if (mark.Kind is WearKind.SoftRub or WearKind.BroadFade || dark)
                rgb = paper;
            else if (theme == 1)
                rgb = Vector3.Lerp(rgb, paper, .88f);
            return (rgb, tex.W * mark.Strength);
        }
    }

    public static void Apply(byte[] rgb, int w, int h, int theme, IEnumerable<WearMark> marks, CancellationToken token, Box? clip = null)
    {
        foreach (var mark in marks)
        {
            var brush = new Brush(mark, theme, w, h);
            var b = brush.Bounds;
            var area = clip ?? new Box(0, 0, w, h);
            int left = Math.Max(0, (int)Math.Max(area.X, b.X)), top = Math.Max(0, (int)Math.Max(area.Y, b.Y));
            int right = Math.Min(w, (int)Math.Min(area.Right, b.Right)), bottom = Math.Min(h, (int)Math.Min(area.Bottom, b.Bottom));
            for (int y = top; y < bottom; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = left; x < right; x++)
                {
                    int i = 3 * (y * w + x);
                    var old = new Vector3(rgb[i], rgb[i + 1], rgb[i + 2]);
                    var (color, alpha) = brush.Sample(x + .5f, y + .5f, old);
                    if (alpha <= 0)
                        continue;
                    var v = Vector3.Clamp(Vector3.Lerp(old, color, alpha), Vector3.Zero, new(255));
                    rgb[i] = (byte)MathF.Round(v.X);
                    rgb[i + 1] = (byte)MathF.Round(v.Y);
                    rgb[i + 2] = (byte)MathF.Round(v.Z);
                }
            }
        }
    }

    public static PlacementPreview Palette(int theme, uint seed)
    {
        const int cellW = 150, cellH = 75, w = cellW * 3, h = cellH * 3;
        var colors = SheetLayout.Colors(theme);
        var rgb = new byte[w * h * 3];
        var c = new Canvas(rgb, w, h, colors.Ink);
        for (int i = 0; i < rgb.Length; i += 3)
        {
            rgb[i] = (byte)colors.Paper.X;
            rgb[i + 1] = (byte)colors.Paper.Y;
            rgb[i + 2] = (byte)colors.Paper.Z;
        }
        for (int i = 0; i < 9; i++)
        {
            int x = i % 3 * cellW, y = i / 3 * cellH;
            c.Line(x, y, x + cellW - 1, y, .5f);
            c.Line(x, y, x, y + cellH - 1, .5f);
            c.Text(i < 4 ? "D" : "GEMINI 7", x + (i < 4 ? 12 : 22), y + 26, 23, 1);
        }
        NativeText.Apply(rgb, w, h, c.Runs);
        for (int i = 0; i < 9; i++)
        {
            int x = i % 3 * cellW, y = i / 3 * cellH;
            var kind = (WearKind)i;
            var m = WearPlacement.Manual(kind, new(960, 540), new(1920, 1080), 1, 0, false, seed);
            bool tear = WearPlacement.Tear(kind);
            float px = tear ? (m.FlipX ? x + cellW : x) : x + cellW * .5f;
            m = m with
            {
                X = px / w,
                Y = (y + cellH * .5f) / h,
                Edge = tear ? (m.FlipX ? 1 : 0) : -1,
                Width = m.Width * 1920 * (kind == WearKind.CoffeeRing ? .22f : tear ? .53f : .68f) / w
            };
            Apply(rgb, w, h, theme, [m], default, new(x, y, cellW, cellH));
        }
        return new(w, h, Sheet.MakePreview(rgb), new(0, 0, w, h));
    }

    // The texture's bounds are relative to the pointer on a 1920-wide reference sheet.
    public static PlacementPreview Preview(WearMark m, int theme)
    {
        var brush = new Brush(m with
        {
            X = 0,
            Y = 0
        }, theme, 1920, 1080);
        var b = brush.Bounds;
        int w = (int)b.Width, h = (int)b.Height;
        var data = new byte[checked(w * h * 4)];
        var paper = SheetLayout.Colors(theme).Paper;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var (color, alpha) = brush.Sample(b.X + x + .5f, b.Y + y + .5f, paper);
                int i = 4 * (y * w + x);
                data[i] = (byte)Math.Clamp(color.X, 0, 255);
                data[i + 1] = (byte)Math.Clamp(color.Y, 0, 255);
                data[i + 2] = (byte)Math.Clamp(color.Z, 0, 255);
                data[i + 3] = (byte)Math.Clamp(MathF.Round(alpha * 255), 0, 255);
            }
        return new(w, h, data, b);
    }
}
