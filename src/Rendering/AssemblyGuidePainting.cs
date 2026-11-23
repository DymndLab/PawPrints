using System.Numerics;
namespace Blueprint.Rendering;

// Assembly guides share the print's line pattern, but remain subdued and have
// no leader endpoint. Mask the entire stroke against geometry, including dots.
static class AssemblyGuidePainting
{
    public static ViewPixels Paint(ViewPixels pixels, IEnumerable<(Vector2 From, Vector2 To)> paths, LeaderLine line, CancellationToken token)
    {
        var canvas = new Canvas(new byte[pixels.Width * pixels.Height * 3], pixels.Width, pixels.Height, new(255));
        foreach (var (from, to) in paths)
        {
            token.ThrowIfCancellationRequested();
            LeaderPainting.Paint(canvas, [from, to], line, LeaderTip.None);
        }
        var ink = (byte[])pixels.Ink.Clone();
        for (int y = 0; y < pixels.Height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < pixels.Width; x++)
            {
                int q = y * pixels.Width + x;
                if (pixels.MeshOwners[q] < 0)
                    ink[q] = Math.Max(ink[q], (byte)(canvas.Pixels[q * 3] * 90 / 255));
            }
        }
        return pixels with { Ink = ink };
    }
}
