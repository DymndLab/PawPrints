using System.Numerics;
namespace Blueprint.Rendering;

// Recorded while composing annotations, never by re-rendering on pointer move.
// Lines and non-space glyph cells remain sparse. Raster silhouettes use 32px
// bit tiles (128 bytes each), rather than a full-page owner array per object.
sealed class CanvasHitShape
{
    readonly List<(Vector2 A, Vector2 B)> lines = [];
    readonly List<Box> glyphs = [];
    readonly Dictionary<(int X, int Y), ulong[]> pixels = [];

    public void Line(int x0, int y0, int x1, int y1) => lines.Add((new(x0, y0), new(x1, y1)));
    public void Text(string text, int x, int y, int size)
    {
        float width = NativeText.MonoWidth(size);
        for (int i = 0; i < text.Length; i++)
            if (!char.IsWhiteSpace(text[i]))
                glyphs.Add(new(x + i * width, y, width, size));
    }
    public void Pixel(int x, int y)
    {
        var key = (x >> 5, y >> 5);
        if (!pixels.TryGetValue(key, out var tile))
            pixels[key] = tile = new ulong[16];
        int bit = (y & 31) * 32 + (x & 31);
        tile[bit >> 6] |= 1UL << (bit & 63);
    }
    public bool Hit(Vector2 point, float tolerance)
    {
        if (glyphs.Any(b => b.Contains(point)))
            return true;
        foreach (var (a, b) in lines)
        {
            var ab = b - a;
            float t = Math.Clamp(Vector2.Dot(point - a, ab) / Math.Max(1, ab.LengthSquared()), 0, 1);
            if (Vector2.DistanceSquared(point, a + t * ab) <= tolerance * tolerance)
                return true;
        }
        int radius = Math.Clamp((int)MathF.Ceiling(tolerance), 1, 12);
        int x = (int)MathF.Floor(point.X), y = (int)MathF.Floor(point.Y);
        for (int yy = y - radius; yy <= y + radius; yy++)
            for (int xx = x - radius; xx <= x + radius; xx++)
                if (pixels.TryGetValue((xx >> 5, yy >> 5), out var tile))
                {
                    int bit = (yy & 31) * 32 + (xx & 31);
                    if ((tile[bit >> 6] & (1UL << (bit & 63))) != 0)
                        return true;
                }
        return false;
    }
}
