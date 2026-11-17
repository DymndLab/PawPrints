namespace Blueprint.Rendering;

using System.Numerics;

// Approved shallow-stack 02; no review-gallery or external asset dependencies.
static class CompactPrintTitle
{


    record Mark(Box Upper, Box Lower, Box Paws, float Scale, int Height);
    static readonly Lazy<Mark> Three = new(() => Measure(3)), Four = new(() => Measure(4));
    const int W = 640, H = 240;


    static Canvas Blank()
    {
        var rgb = new byte[W * H * 3];
        Array.Fill(rgb, (byte)255);
        return new(rgb, W, H, Vector3.Zero);
    }


    static Box Ink(Canvas c)
    {
        int x0 = W, y0 = H, x1 = -1, y1 = -1;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
                if (c.Pixels[(y * W + x) * 3] < 245)
                {
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
        if (x1 < 0)
            throw new InvalidOperationException("Empty maker mark");
        return new(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }


    static void Word(Canvas c, string text, int x, int y, int size, bool hollow)
    {
        c.Text(text, x, y, size, 1, hollow, false, hollow, "Consolas");
        if (hollow)
            c.Runs[^1] = c.Runs[^1] with
            {
                Spans = [new(text, true, 1, 2)]
            };
    }


    static Box MeasureWord(string text, int size, bool hollow)
    {
        var c = Blank();
        Word(c, text, 20, 20, size, hollow);
        NativeText.Apply(c.Pixels, W, H, c.Runs);
        return Ink(c);
    }


    static void Paws(Canvas c, int count, float scale, Vector2 offset)
    {
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)(count - 1), x = 175 * t;
            float y = count == 4 ? (i is 0 or 3 ? 14 : -18.90897f) : (i == 1 ? -20 : 12);
            float angle = count == 4 ? 10 + 80 * t : new[] { 55f, 85f, 65f }[i];
            PawMark.Draw(c, new Vector2(100 + x * scale, 100 + y * .15f * scale) + offset, .82f * scale, angle, true);
        }
    }


    static Box MeasurePaws(int count, float scale)
    {
        var c = Blank();
        Paws(c, count, scale, Vector2.Zero);
        return Ink(c);
    }


    static Mark Measure(int count)
    {
        var upper = MeasureWord("PawPrints", 36, false);
        var lower = MeasureWord("DYMND", 64, true);
        float scale = lower.Width / MeasurePaws(count, 1).Width;
        var paws = MeasurePaws(count, scale);
        return new(upper, lower, paws, scale, (int)(upper.Height + lower.Height + paws.Height) + 8);
    }


    static void Maker(Canvas c, Box area, int count)
    {
        var m = (count == 3 ? Three : Four).Value;
        int center = (int)(area.X + area.Width / 2), top = (int)(area.Y + (area.Height - m.Height) / 2);
        Word(c, "PawPrints", center - (int)m.Upper.Width / 2 - (int)m.Upper.X + 20, top - (int)m.Upper.Y + 20, 36, false);
        int pawTop = top + (int)m.Upper.Height + 4;
        Paws(c, count, m.Scale, new(center - (int)m.Paws.Width / 2 - m.Paws.X, pawTop - m.Paws.Y));
        Word(c, "DYMND", center - (int)m.Lower.Width / 2 - (int)m.Lower.X + 20, pawTop + (int)m.Paws.Height + 4 - (int)m.Lower.Y + 20, 64, true);
    }


    public static void Draw(Canvas c, string title, PrintOptions options, string? exported = null, string? calculatedScale = null)
    {
        int first = c.Runs.Count;
        CompanyTitleBlock.DrawCompact(c, title, options, exported, (_, _) => { }, 199, CompanyTitleBlock.BrandingHeight(options), calculatedScale: calculatedScale);
        var runs = c.Runs.Skip(first).ToArray();
        c.Runs.RemoveRange(first, c.Runs.Count - first);
        foreach (var run in runs)
        {
            if (run.Spans != null && run.Text.StartsWith("Print generated", StringComparison.Ordinal))
            {
                int length = run.Spans.Sum(s => s.Text.Length), rightEdge = CompanyTitleBlock.Layout(options).Split, size = Math.Clamp((int)((rightEdge - run.X - 14) / (length * .60f)), 8, 22);
                float x = run.X;
                foreach (var span in run.Spans)
                {
                    foreach (char ch in span.Text.ToUpperInvariant())
                    {
                        c.Text(ch.ToString(), (int)Math.Round(x), run.Y, size, 1, span.Outline, true, span.Outline, "Consolas");
                        if (span.Outline)
                            c.Runs[^1] = c.Runs[^1] with
                            {
                                Spans = [new(ch.ToString(), true, 1, 1)]
                            };
                        x += size * .60f;
                    }
                }
            }
            else
                c.Runs.Add(InitialsFont.IsRegistered(run.Face) ? run : run with
                {
                    Text = run.Text.ToUpperInvariant(),
                    Bold = false,
                    Italic = false,
                    Spans = null
                });
        }
        int right = PageLayout.Right(options);
        Maker(c, new(right - 199, CompanyTitleBlock.Top(options), 199, CompanyTitleBlock.BrandingHeight(options)), 4);
    }
}
