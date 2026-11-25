namespace Blueprint.Rendering;

using System.Runtime.InteropServices;

// Offscreen Windows x64 font rasterization. Uses native text APIs without
// System.Drawing, game objects or UI state.
static class NativeText
{

    [StructLayout(LayoutKind.Sequential)]
    struct Header
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPels, YPels;
        public uint Colors, Important;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BitmapInfo
    {
        public Header Header;
        public uint Color;
    }

    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")] static extern int SetBkMode(IntPtr dc, int mode);

    [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr dc, uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(int height, int width, int angle, int orientation, int weight, uint italic, uint underline, uint strike, uint charset, uint output, uint clip, uint quality, uint pitch, string face);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(IntPtr dc, int x, int y, string text, int length);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int GetTextFaceW(IntPtr dc, int count, System.Text.StringBuilder face);

    [DllImport("gdi32.dll")] static extern bool GdiFlush();

    [StructLayout(LayoutKind.Sequential)]
    struct FontMetrics
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AveWidth, MaxWidth, Weight, Overhang, AspectX, AspectY;
        public ushort FirstChar, LastChar, DefaultChar, BreakChar;
        public byte Italic, Underlined, StruckOut, Pitch, CharSet;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextMetricsW(IntPtr dc, out FontMetrics metrics);

    [StructLayout(LayoutKind.Sequential)]
    struct TextSize
    {
        public int Width, Height;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr dc, string text, int length, out TextSize size);

    [DllImport("gdi32.dll")] static extern bool BeginPath(IntPtr dc);

    [DllImport("gdi32.dll")] static extern bool EndPath(IntPtr dc);

    [DllImport("gdi32.dll")] static extern bool StrokePath(IntPtr dc);

    [DllImport("gdi32.dll")] static extern IntPtr CreatePen(int style, int width, uint color);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<int, float> MonoWidths = new();

    public static float MonoWidth(int height)
    {
#if PAWPRINTS_PORTABLE_RENDERING
        if (PortableRendering.Active)
            return PortableRendering.MonoWidth(height);
#endif
        return MonoWidths.GetOrAdd(height, MeasureWindowsMono);
    }

    static float MeasureWindowsMono(int size)
    {
        var dc = CreateCompatibleDC(IntPtr.Zero);
        var font = CreateFontW(-size, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4, 0, "Consolas");
        var old = SelectObject(dc, font);
        try
        {
            if (!GetTextExtentPoint32W(dc, "MMMMMMMMMM", 10, out var measured))
                throw new InvalidOperationException("Cannot measure label font");
            return measured.Width / 10f;
        }
        finally { SelectObject(dc, old); DeleteObject(font); DeleteDC(dc); }
    }

    public static void Apply(byte[] rgb, int width, int height, List<TextRun> runs)
    {
#if PAWPRINTS_PORTABLE_RENDERING
        if (PortableRendering.Active)
        {
            PortableRendering.Apply(rgb, width, height, runs);
            return;
        }
#endif
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Blueprint typography currently uses Windows fonts.");
        foreach (var face in runs.Select(r => r.Face).Distinct())
            InitialsFont.EnsureLoaded(face);
        var bgra = new byte[width * height * 4];
        for (int q = 0; q < width * height; q++)
        {
            bgra[q * 4] = rgb[q * 3 + 2];
            bgra[q * 4 + 1] = rgb[q * 3 + 1];
            bgra[q * 4 + 2] = rgb[q * 3];
            bgra[q * 4 + 3] = 255;
        }
        var dc = CreateCompatibleDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
            throw new InvalidOperationException("Cannot create typography surface");
        var info = new BitmapInfo { Header = new Header { Size = (uint)Marshal.SizeOf<Header>(), Width = width, Height = -height, Planes = 1, BitCount = 32 } };
        var bitmap = CreateDIBSection(dc, ref info, 0, out var bits, IntPtr.Zero, 0);
        var old = SelectObject(dc, bitmap);
        try
        {
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                throw new InvalidOperationException("Cannot allocate typography bitmap");
            Marshal.Copy(bgra, 0, bits, bgra.Length);
            SetBkMode(dc, 1);
            foreach (var run in runs)
            {
                var font = CreateFontW(-run.Height, 0, 0, 0, run.Bold ? 700 : 400, run.Italic ? 1u : 0u, run.Underline ? 1u : 0u, 0, 1, 0, 0, 4, 0, run.Face ?? (run.Height > 60 ? "Segoe UI" : "Consolas"));
                var previous = SelectObject(dc, font);
                try
                {
                    if (InitialsFont.IsRegistered(run.Face))
                    {
                        var actual = new System.Text.StringBuilder(64);
                        GetTextFaceW(dc, actual.Capacity, actual);
                        if (actual.ToString() != run.Face)
                            throw new InvalidOperationException("Requested initials font was substituted: " + actual);
                    }
                    uint color = (uint)run.Color.X | ((uint)run.Color.Y << 8) | ((uint)run.Color.Z << 16);
                    SetTextColor(dc, color);
                    int x = run.X;
                    if (!GetTextMetricsW(dc, out var baseMetrics))
                        throw new InvalidOperationException("Cannot measure font baseline");
                    int baseline = run.Y + baseMetrics.Ascent;
                    foreach (var span in run.Spans ?? [new TextSpan(run.Text, run.Outline)])
                    {
                        int fontHeight = Math.Max(1, (int)Math.Round(run.Height * span.Scale));
                        var spanFont = span.Scale == 1 ? IntPtr.Zero : CreateFontW(-fontHeight, 0, 0, 0, run.Bold ? 700 : 400, run.Italic ? 1u : 0u, run.Underline ? 1u : 0u, 0, 1, 0, 0, 4, 0, run.Face ?? "Consolas");
                        if (spanFont != IntPtr.Zero)
                            SelectObject(dc, spanFont);
                        try
                        {
                            if (!GetTextMetricsW(dc, out var metrics))
                                throw new InvalidOperationException("Cannot align inline type");
                            if (span.Outline && !BeginPath(dc))
                                throw new InvalidOperationException("Cannot begin outlined type");
                            if (!TextOutW(dc, x, baseline - metrics.Ascent, span.Text, span.Text.Length))
                                throw new InvalidOperationException("Text rendering failed");
                            if (span.Outline)
                            {
                                if (!EndPath(dc))
                                    throw new InvalidOperationException("Cannot finish outlined type");
                                var pen = CreatePen(0, span.Stroke > 0 ? span.Stroke : Math.Max(1, fontHeight / 23), color);
                                var oldPen = SelectObject(dc, pen);
                                try
                                {
                                    if (!StrokePath(dc))
                                        throw new InvalidOperationException("Cannot stroke outlined type");
                                }
                                finally { SelectObject(dc, oldPen); DeleteObject(pen); }
                            }
                            if (!GetTextExtentPoint32W(dc, span.Text, span.Text.Length, out var extent))
                                throw new InvalidOperationException("Cannot measure footer type");
                            x += extent.Width;
                        }
                        finally { if (spanFont != IntPtr.Zero) { SelectObject(dc, font); DeleteObject(spanFont); } }
                    }
                }
                finally { SelectObject(dc, previous); DeleteObject(font); }
            }
            GdiFlush();
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            for (int q = 0; q < width * height; q++)
            {
                rgb[q * 3] = bgra[q * 4 + 2];
                rgb[q * 3 + 1] = bgra[q * 4 + 1];
                rgb[q * 3 + 2] = bgra[q * 4];
            }
        }
        finally { SelectObject(dc, old); DeleteObject(bitmap); DeleteDC(dc); }
    }
}
