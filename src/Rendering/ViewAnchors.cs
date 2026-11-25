using System.Numerics;
namespace Blueprint.Rendering;

static class ViewAnchors
{
    public static Dictionary<int, PartVisual> Measure(ViewPixels tile, int[] meshParts, Box bounds, CancellationToken token = default)
    {
        var stats = new Dictionary<int, (int Count, int Left, int Top, int Right, int Bottom, List<Vector2> Edges)>();
        int Part(int x, int y)
        {
            if (x < 0 || y < 0 || x >= tile.Width || y >= tile.Height)
                return -1;
            int mesh = tile.MeshOwners[y * tile.Width + x];
            return mesh < 0 ? -1 : meshParts[mesh];
        }
        for (int y = 0; y < tile.Height; y++)
            for (int x = 0; x < tile.Width; x++)
            {
                if (x == 0)
                    token.ThrowIfCancellationRequested();
                int id = Part(x, y);
                if (id < 0)
                    continue;
                if (!stats.TryGetValue(id, out var s))
                    s = (0, x, y, x, y, []);
                s.Count++;
                s.Left = Math.Min(s.Left, x);
                s.Top = Math.Min(s.Top, y);
                s.Right = Math.Max(s.Right, x);
                s.Bottom = Math.Max(s.Bottom, y);
                if (Part(x - 1, y) != id || Part(x + 1, y) != id || Part(x, y - 1) != id || Part(x, y + 1) != id)
                    s.Edges.Add(new(bounds.X + x, bounds.Y + y));
                stats[id] = s;
            }
        return stats.ToDictionary(p => p.Key, p =>
        {
            var s = p.Value;
            int step = Math.Max(1, (int)Math.Ceiling(s.Edges.Count / 256d));
            return new PartVisual(p.Key, s.Count, new(bounds.X + s.Left, bounds.Y + s.Top, s.Right - s.Left + 1, s.Bottom - s.Top + 1), s.Edges.Where((_, i) => i % step == 0).ToArray()) { ContactEdges = s.Edges.ToArray() };
        });
    }
}
