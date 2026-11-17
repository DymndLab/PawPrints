namespace Blueprint.Rendering;

using System.Runtime.InteropServices;

// Private to this process; no Windows font installation or game UI font changes.
static class InitialsFont
{
    public const string Face = "Reenie Beanie", ApproverFace = "Zeyada";
    public static readonly string[] Names = ["Mr Dafoe", "Kristi", Face, "Nothing You Could Do", "Zeyada", "La Belle Aurore"];
    static readonly string[] Slugs = ["mrdafoe", "kristi", "reeniebeanie", "nothingyoucoulddo", "zeyada", "labelleaurore"];
    static readonly object Gate = new();

    public static string Normalize(string? face, string fallback = Face) => Names.Contains(face) ? face! : fallback;

    public static void EnsureLoaded(string? face)
    {
        int index = Array.IndexOf(Names, face);
        if (index < 0)
            return;
        lock (Gate)
        {
            if (PreviewFonts.ContainsKey(face!))
                return;
            using var stream = typeof(InitialsFont).Assembly.GetManifestResourceStream("PawPrints.Fonts." + Slugs[index] + ".ttf")
                ?? throw new InvalidOperationException("Bundled initials font is missing: " + face);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            RegisterPreview(face!, buffer.ToArray());
        }
    }
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IntPtr> PreviewFonts = new();

    public static bool IsRegistered(string? face) => face != null && (Names.Contains(face) || PreviewFonts.ContainsKey(face));

    public static void RegisterPreview(string face, byte[] bytes)
    {
        lock (Gate)
        {
            if (PreviewFonts.ContainsKey(face))
                return;
            var handle = AddFontMemResourceEx(bytes, (uint)bytes.Length, IntPtr.Zero, out uint count);
            if (handle == IntPtr.Zero || count == 0)
                throw new InvalidOperationException("Cannot load initials font: " + face);
            // Windows copies the data and releases the private registration at exit.
            PreviewFonts[face] = handle;
        }
    }

    [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(byte[] bytes, uint size, IntPtr reserved, out uint count);

    static Box Measure(string text, string face, int size)
    {
        const int w = 2048, h = 400;
        var rgb = new byte[w * h * 3];
        Array.Fill(rgb, (byte)255);
        var c = new Canvas(rgb, w, h, System.Numerics.Vector3.Zero);
        c.Text(text, 100, 20, size, 1, false, false, false, face);
        NativeText.Apply(rgb, w, h, c.Runs);
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (rgb[(y * w + x) * 3] < 245)
                {
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
        if (x1 < 0)
            return new(0, 0, 0, 0);
        if (x0 == 0 || x1 == w - 1 || y1 == h - 1)
            throw new InvalidOperationException("Initials exceed measuring surface.");
        return new(x0 - 100, y0 - 20, x1 - x0 + 1, y1 - y0 + 1);
    }

    public static void Draw(Canvas c, string text, Box box, string? style, int percent = 100, bool leftAligned = false)
    {
        text = AnnotationText.Clean(text, 16);
        if (string.IsNullOrWhiteSpace(text) || box.Width < 1 || box.Height < 1)
            return;
        string face = Normalize(style);
        var reference = Measure(text, face, 100);
        if (reference.Width == 0 || reference.Height == 0)
            return;
        float fill = Math.Clamp(percent, 60, 120) / 100f;
        int size = Math.Clamp((int)Math.Floor(100 * Math.Min(box.Width / reference.Width, box.Height / reference.Height) * fill), 1, 160);
        Box ink = Measure(text, face, size);
        while (size > 1 && ink.Width > box.Width)
        {
            size--;
            ink = Measure(text, face, size);
        }
        c.Text(text, (int)Math.Round(box.X + (leftAligned ? 0 : Math.Max(0, box.Width - ink.Width) * .55f) - ink.X), (int)Math.Round(box.Y + (box.Height - ink.Height) / 2 - ink.Y), size, 1, false, false, false, face);
    }
}
