namespace Blueprint.Rendering;

static class SheetZones
{

    public static (int Columns, int Rows) Count(PrintOptions options)
    {
        var page = options.Page;
        float shortSide = Math.Min(page.Width, page.Height);
        return (Math.Clamp((int)MathF.Ceiling(5 * page.Width / shortSide), 2, 24),
            Math.Clamp((int)MathF.Ceiling(5 * page.Height / shortSide), 2, 24));
    }

    public static int[] GridAxis(int length, int zones)
    {
        var lines = new int[length];
        float pitch = Math.Max(1, (length - 2f * PageLayout.Margin) / (zones * 10));
        for (int i = (int)Math.Floor(-PageLayout.Margin / pitch); ; i++)
        {
            int pixel = (int)MathF.Round(PageLayout.Margin + i * pitch);
            if (pixel >= length)
                break;
            if (pixel < 0)
                continue;
            lines[pixel] = i % 10 == 0 ? 10 : i % 5 == 0 ? 5 : 1;
        }
        return lines;
    }

    public static string Column(int index, int count) => (count - index).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static string Row(int index, int count) => ((char)('A' + count - 1 - index)).ToString();

    public static int RowBoundary(PrintOptions options, int index) => (int)MathF.Round(PageLayout.Margin +
        (PageLayout.Bottom(options) - PageLayout.Margin) * index / (float)Count(options).Rows);

    public static void Paint(Canvas c, PrintOptions options)
    {
        if (!options.Zones)
            return;
        var (columns, rows) = Count(options);
        int right = PageLayout.Right(options), bottom = PageLayout.Bottom(options);
        const int edge = PageLayout.Margin, outer = edge - 48, font = 30;
        for (int i = 0; i <= columns; i++)
        {
            int x = (int)MathF.Round(edge + (right - edge) * i / (float)columns);
            c.Line(x, outer, x, edge, .6f);
            c.Line(x, bottom, x, bottom + 48, .6f);
            if (i == columns)
                continue;
            string text = Column(i, columns);
            int tx = (int)(edge + (right - edge) * (i + .5f) / columns - text.Length * NativeText.MonoWidth(font) / 2);
            c.Text(text, tx, outer + 9, font, .9f);
            c.Text(text, tx, bottom + 9, font, .9f);
        }
        for (int i = 0; i <= rows; i++)
        {
            int y = RowBoundary(options, i);
            c.Line(outer, y, edge, y, .6f);
            c.Line(right, y, right + 48, y, .6f);
            if (i == rows)
                continue;
            int ty = (int)(edge + (bottom - edge) * (i + .5f) / rows - font / 2f);
            string text = Row(i, rows);
            c.Text(text, (int)(outer + 24 - NativeText.MonoWidth(font) / 2), ty, font, .9f);
            c.Text(text, (int)(right + 24 - NativeText.MonoWidth(font) / 2), ty, font, .9f);
        }
        // A solid registration square closes the lower-right zone corner.
        // Ink follows the active theme; the outer zone perimeter stays open.
        for (int y = bottom; y < bottom + 48; y++)
            for (int x = right; x < right + 48; x++)
                c.Ink(x, y, 1);
    }
}
