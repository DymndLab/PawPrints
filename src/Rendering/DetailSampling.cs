using System.Numerics;
namespace Blueprint.Rendering;

// Temporary supersampling is confined to the craft's projected bounds. Cached
// pixels and picking buffers remain at sheet resolution, including tiny parts.
static class DetailSampling
{
    internal const int MaxSamples = 4_000_000;


    internal static int Rate(int width, int height) => (long)width * height * 9 <= MaxSamples ? 3 : (long)width * height * 4 <= MaxSamples ? 2 : 1;


    public static ViewPixels Render(Capture capture, IReadOnlyDictionary<string, Texture> maps, string title, int view, ProjectionFrame frame, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var projected = capture.Meshes.SelectMany(m => m.WorldVertices()).Select(v =>
        {
            var p = v - frame.Center;
            return new Vector2(frame.Width * .5f + Vector3.Dot(p, frame.Right) * frame.Scale, frame.Height * .5f - Vector3.Dot(p, frame.Up) * frame.Scale);
        }).ToArray();
        int left = Math.Clamp((int)MathF.Floor(projected.Min(p => p.X)) - 16, 0, frame.Width - 1), top = Math.Clamp((int)MathF.Floor(projected.Min(p => p.Y)) - 16, 0, frame.Height - 1);
        int right = Math.Clamp((int)MathF.Ceiling(projected.Max(p => p.X)) + 16, left + 1, frame.Width), bottom = Math.Clamp((int)MathF.Ceiling(projected.Max(p => p.Y)) + 16, top + 1, frame.Height);
        int w = right - left, h = bottom - top, rate = Rate(w, h);
        if (rate == 1)
            return new BlueprintRenderer(capture, maps, title, view, token, frame).RenderView(_ => { });
        var center = frame.Center + frame.Right * ((left + w * .5f - frame.Width * .5f) / frame.Scale) - frame.Up * ((top + h * .5f - frame.Height * .5f) / frame.Scale);
        var highFrame = frame with
        {
            Width = w * rate,
            Height = h * rate,
            Scale = frame.Scale * rate,
            Center = center
        };
        var high = new BlueprintRenderer(capture, maps, title, view, token, highFrame, rate, new(left, top), new(frame.Width, frame.Height)).RenderView(_ => { });
        token.ThrowIfCancellationRequested();
        var ink = new byte[frame.Width * frame.Height];
        var owners = new int[ink.Length];
        Array.Fill(owners, -1);
        int count = rate * rate;
        Span<int> samples = stackalloc int[9];
        for (int y = 0; y < h; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < w; x++)
            {
                int q = (y + top) * frame.Width + x + left, total = 0, n = 0;
                for (int yy = 0; yy < rate; yy++)
                    for (int xx = 0; xx < rate; xx++)
                    {
                        int at = (y * rate + yy) * high.Width + x * rate + xx;
                        total += high.Ink[at];
                        samples[n++] = high.MeshOwners[at];
                    }
                ink[q] = (byte)((total + count / 2) / count);
                owners[q] = Owner(samples[..count]);
            }
        }
        return new(frame.Width, frame.Height, ink, owners);
    }


    // Majority visible mesh; coverage in one subsample still makes a tiny part pickable.
    internal static int Owner(int a, int b, int c, int d) => Owner([a, b, c, d]);


    internal static int Owner(ReadOnlySpan<int> samples)
    {
        int best = -1, count = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            int v = samples[i];
            if (v < 0 || samples[..i].Contains(v))
                continue;
            int n = 0;
            foreach (int sample in samples)
                if (sample == v)
                    n++;
            if (n > count)
            {
                best = v;
                count = n;
            }
        }
        return best;
    }
}
