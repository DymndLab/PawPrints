using System.Numerics;

namespace Blueprint.Rendering;

enum WearKind
{
    FineSplit, ShortNick, CurvedTear, SteppedTear, SmallScuff, TexturedRub, SoftRub, BroadFade, CoffeeRing
}

// Coordinates and width are fractions of the sheet. Edge is left, right, top or bottom, or -1 for free placement.
sealed record WearMark(WearKind Kind, float X, float Y, float Width, float Angle, float Strength = 1,
    float Depth = 1, bool FlipX = false, bool FlipY = false, int Edge = -1);

static class WearPlacement
{
    public const int Limit = 128;
    public static readonly string[] Names = ["Fine split", "Short nick", "Curved tear", "Stepped tear", "Small scuff", "Textured rub", "Soft rub", "Broad fade", "Coffee ring"];
    static readonly float[] widths = [248, 112, 220, 193, 62, 173, 173, 346, 285];
    static readonly float[] opacity = [1, 1, 1, 1, .92f, .97f, .75f, .87f, .52f];
    readonly record struct Level(int Low, int High, float Strength, int Tear, int Two, int Ring, int Small, int Soft, int Textured);
    static readonly Level[] levels = [
        new(0,0,0,0,0,0,0,0,0), new(1,1,.38f,5,0,0,100,100,100), new(1,2,.50f,15,5,5,60,100,100),
        new(2,2,.62f,40,10,8,30,75,100), new(2,3,.74f,65,25,15,18,45,85), new(3,4,1,100,40,20,10,32,72)];
    public static bool Tear(WearKind kind) => kind <= WearKind.SteppedTear;
    public static uint NewSeed() => (uint)Random.Shared.NextInt64(1, 1L << 32);
    static uint Mix(uint value)
    {
        value = unchecked((value ^ (value >> 16)) * 0x85ebca6bu);
        value = unchecked((value ^ (value >> 13)) * 0xc2b2ae35u);
        return value ^ (value >> 16);
    }

    struct Stream(uint seed, int slot)
    {
        uint state = Mix(unchecked(seed + (uint)slot * 0x9e3779b9u));
        public float Next()
        {
            state = unchecked(1664525u * state + 1013904223u);
            return (state >> 8) / 16777216f;
        }
        public int Index(int length) => Math.Min(length - 1, (int)(Next() * length));
    }

    public static WearMark Stamp(WearKind kind) => new(kind, .5f, .5f, widths[(int)kind] / 1920f, 0, opacity[(int)kind]);
    static WearMark VaryTear(WearMark mark, ref Stream draw, int edge, float tilt)
    {
        float band = draw.Next(), within = draw.Next();
        float depth = band < .15f ? .10f + .12f * within : band < .80f ? .22f + .72f * within : 1;
        bool flipX = draw.Next() < .5f, flipY = draw.Next() < .5f;
        if (edge is 0 or 1)
            flipX = edge == 1;
        if (edge is 2 or 3)
            flipY = edge == 3;
        return mark with
        {
            Edge = edge,
            Depth = depth,
            FlipX = flipX,
            FlipY = flipY,
            Angle = tilt + (edge >= 2 ? 90 : 0)
        };
    }
    static WearMark Snap(WearMark mark, int edge) => edge switch
    {
        0 => mark with { X = 0 },
        1 => mark with { X = 1 },
        2 => mark with { Y = 0 },
        _ => mark with { Y = 1 }
    };

    public static WearMark Manual(WearKind kind, Vector2 point, PageSize page, float size, float angle, bool snap, uint variation)
    {
        var m = Stamp(kind) with
        {
            X = Math.Clamp(point.X / page.Width, 0, 1),
            Y = Math.Clamp(point.Y / page.Height, 0, 1),
            Angle = angle
        };
        m = m with
        {
            Width = m.Width * size
        };
        if (!Tear(kind))
            return m;
        int edge = -1;
        if (snap)
        {
            float horizontal = Math.Min(point.X, page.Width - point.X), vertical = Math.Min(point.Y, page.Height - point.Y);
            edge = horizontal < vertical ? (point.X < page.Width * .5f ? 0 : 1) : (point.Y < page.Height * .5f ? 2 : 3);
            m = Snap(m, edge);
        }
        var random = new Stream(variation, 1 + (int)kind);
        return VaryTear(m, ref random, edge, angle);
    }

    public static bool Valid(WearMark? m) => m != null && Enum.IsDefined(m.Kind) && m.X is >= 0 and <= 1 && m.Y is >= 0 and <= 1 &&
        m.Width is >= .001f and <= .5f && m.Angle is >= -360 and <= 360 && m.Strength is >= 0 and <= 1 &&
        m.Depth is >= .09f and <= 1 && m.Edge is >= -1 and <= 3;

    // Choose a non-space character near the middle of the text actually drawn.
    public static Vector2[] Lettering(IEnumerable<TextRun> runs, PageSize page)
    {
        var words = new List<Vector2>();
        var zones = new List<Vector2>();
        foreach (var run in runs)
        {
            if (string.IsNullOrWhiteSpace(run.Text))
                continue;
            int i = run.Text.Length / 2;
            while (i > 0 && char.IsWhiteSpace(run.Text[i]))
                i--;
            var point = new Vector2((run.X + (i + .5f) * NativeText.MonoWidth(run.Height)) / page.Width,
                (run.Y + run.Height * .53f) / page.Height);
            if (point.X is < 0 or > 1 || point.Y is < 0 or > 1)
                continue;
            if (run.Text.Length > 1)
                words.Add(point);
            else
                zones.Add(point);
        }
        return (words.Count >= 4 ? words : words.Concat(zones)).Distinct().ToArray();
    }
    public static WearMark[] Resolve(PrintOptions options, string title, IReadOnlyList<Vector2> fields, WearMark[] manual)
    {
        int level = options.Archive ? Math.Clamp(options.WearLevel, 0, 5) : 0;
        if (level == 0)
            return manual;
        var p = levels[level];
        var outMarks = new List<WearMark>();
        uint seed = options.WearSeed;
        if (seed == 0)
        {
            seed = 2166136261;
            foreach (char ch in title)
                seed = unchecked((seed ^ ch) * 16777619);
        }
        var rolls = new Stream(seed, 1);
        float tear = rolls.Next() * 100;
        int count = rolls.Next() < .5f ? p.Low : p.High;
        float ring = rolls.Next() * 100;
        var spots = fields.ToList();
        for (int i = 0; i < count; i++)
        {
            var finish = new Stream(seed, 2 + 16 * i);
            float n = finish.Next() * 100;
            var kind = n < p.Small ? WearKind.SmallScuff : n < p.Soft ? WearKind.SoftRub : n < p.Textured ? WearKind.TexturedRub : WearKind.BroadFade;
            var draw = new Stream(seed, 3 + 16 * i);
            Vector2 pos;
            if (spots.Count > 0)
            {
                int ix = draw.Index(spots.Count);
                pos = spots[ix];
                spots.RemoveAt(ix);
            }
            else
                pos = new(.08f + .84f * draw.Next(), .08f + .84f * draw.Next());
            var m = Stamp(kind);
            float tilt = 26 * draw.Next() - 13;
            outMarks.Add(m with
            {
                X = pos.X,
                Y = pos.Y,
                Angle = tilt,
                Width = m.Width * (.76f + .42f * draw.Next()) * (.64f + .072f * level),
                Strength = Math.Min(m.Strength, p.Strength)
            });
        }
        if (ring < p.Ring)
        {
            var draw = new Stream(seed, 4);
            var m = Stamp(WearKind.CoffeeRing);
            float width = m.Width * (.76f + .40f * draw.Next());
            float marginY = Math.Min(.45f, width * .5f * options.Page.Width / options.Page.Height);
            outMarks.Add(m with
            {
                Width = width * (.68f + .064f * level),
                X = width / 2 + (1 - width) * draw.Next(),
                Y = marginY + (1 - 2 * marginY) * draw.Next(),
                Angle = 360 * draw.Next(),
                Strength = m.Strength * (.52f + .096f * level)
            });
        }
        var styles = new List<WearKind> { WearKind.FineSplit, WearKind.ShortNick, WearKind.CurvedTear, WearKind.SteppedTear };
        var edges = new List<int> { 0, 1, 2, 3 };
        var chooser = new Stream(seed, 5);
        int tears = tear < p.Two ? 2 : tear < p.Tear ? 1 : 0;
        for (int i = 0; i < tears; i++)
        {
            int k = chooser.Index(styles.Count), e = chooser.Index(edges.Count), edge = edges[e];
            var m = Stamp(styles[k]);
            styles.RemoveAt(k);
            edges.RemoveAt(e);
            float along = .075f + .85f * chooser.Next();
            m = Snap(m with
            {
                X = along,
                Y = along,
                Width = m.Width * (.78f + .43f * chooser.Next()) * (.52f + .096f * level)
            }, edge);
            var variation = new Stream(seed, 6 + i);
            outMarks.Add(VaryTear(m, ref variation, edge, 25 * chooser.Next() - 12.5f));
        }
        outMarks.AddRange(manual);
        return outMarks.ToArray();
    }
}
