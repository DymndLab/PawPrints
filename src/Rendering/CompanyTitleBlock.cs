using System.Globalization;

namespace Blueprint.Rendering;

static class CompanyTitleBlock
{
    public const int SignoffRowHeight = 44;


    public static bool ContentSized(PrintOptions options) => options.TitleBlock != null && options.Page.Width > options.Page.Height;


    // Paper-space geometry is shared by painting, placement, export restamping and
    // thumbnails. Landscape grows leftwards from A1; other sheets keep their layout.
    public static (Box Bounds, int Split) Layout(PrintOptions options)
    {
        int width = PageLayout.InnerWidth(options), right = PageLayout.Right(options);
        if (!ContentSized(options))
            return (new(PageLayout.Margin, Top(options), width, PageLayout.Bottom(options) - Top(options)), PageLayout.Margin + (int)(width * .6f));
        var data = options.TitleBlock!;
        int Measure(string text, int size) => (int)Math.Ceiling(AnnotationText.Clean(text, 100).Length * Math.Max(size * .63f, NativeText.MonoWidth(size)));
        // Preserve the current fonts rather than stretching empty columns with paper.
        int left = Math.Max(164 + 28 + Measure(PrintIdentity.Credit(2), 22),
            60 + Math.Max(Measure(data.Title, 68), Math.Max(Measure(data.Subtitle, 27), Measure("SCALE   " + PrintScale.Text(data, "1:100") + "   /   " + PrintScale.Paper(options), 24))));
        int signing = Math.Max(840, Math.Max(Measure(data.Company, 26) + 239, Math.Max(Measure(data.Creator, 26), Measure(data.Approver, 26)) + 230));
        // Long text can consume the full page; retain the existing fit-to-cell
        // typography once there is physically no further room to expand.
        int total = Math.Min(width, left + signing);
        int actualLeft = left + signing <= width ? left : (int)(width * (double)left / (left + signing));
        actualLeft = Math.Clamp(actualLeft, Math.Min(300, total / 2), total - Math.Min(signing, Math.Max(500, (int)(total * .35f))));
        return (new(right - total, Top(options), total, PageLayout.Bottom(options) - Top(options)), right - total + actualLeft);
    }


    public static int Top(PrintOptions options) => SheetZones.RowBoundary(options, SheetZones.Count(options).Rows - 1);


    public static int BrandingHeight(PrintOptions options) => PageLayout.Bottom(options) - Top(options) - 4 * SignoffRowHeight;


    public static int FooterTop(PrintOptions options) => PageLayout.Bottom(options) - SignoffRowHeight;


    public static int CreditTop(PrintOptions options) => FooterTop(options) + 10;


    static int RevisionFont(TitleBlockData? data)
    {
        string revision = AnnotationText.Clean(data?.Revision ?? "0", 12);
        int font = 20;
        while (font > 8 && (3 * NativeText.MonoWidth(font) > 64 - 12 || revision.Length * NativeText.MonoWidth(font) > 100 - 12))
            font--;
        return font;
    }


    // REV remains centred in its 64px cell. All left-column text takes its
    // left edge from that centred label, including when a long revision shrinks it.
    static int TitleLeft(PrintOptions options) => (int)(Layout(options).Bounds.X + (64 - 3 * NativeText.MonoWidth(RevisionFont(options.TitleBlock))) / 2);


    public static string LocalDate() => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);


    static void Text(Canvas c, string text, int x, int y, int width, int max, bool bold = false, string face = "Consolas")
    {
        text = AnnotationText.Clean(text, Math.Max(1, Math.Min(100, width / 7)));
        int size = Math.Clamp((int)(width / Math.Max(1, text.Length * .63)), 10, max);
        c.Text(text, x, y, size, 1, bold, false, false, face);
    }


    public static void Draw(Canvas c, string title, PrintOptions options, string? exported = null, bool sideBySide = false)
    {
        var data = options.TitleBlock!;
        int left = PageLayout.Margin, right = PageLayout.Right(options), bottom = PageLayout.Bottom(options), top = bottom - 600;
        int split = left + (int)((right - left) * .60f), col = right - split, footer = bottom - 52, brand = top + 240, creator = brand + 60, approver = creator + 124;
        void Line(int x, int y, int x2, int y2) => c.Line(x, y, x2, y2, .65f);
        Line(left, top, right, top);
        Line(split, top, split, footer);
        Line(left, footer, right, footer);
        Text(c, title, left + 35, top + 75, split - left - 70, 72, true);
        Text(c, "GENERAL ARRANGEMENT", left + 35, top + 175, split - left - 70, 27);
        Text(c, SheetLayout.ViewSummary(options), left + 35, top + 405, split - left - 70, 24);
        Text(c, "NOT TO SCALE", left + 35, top + 455, split - left - 70, 24);
        if (sideBySide)
            SideBySide(c, data, split, top, col);
        else
        {
            Text(c, data.Company, split + 24, top + 16, col - 48, 32, true);
            if (data.Logo != null)
                CompanyLogo.Draw(c, data.Logo, new(split + 24, top + 64, col - 48, 156));
            Line(split, brand, right, brand);
            Maker(c, split, brand, col, data.Paws);
        }
        Line(split, creator, right, creator);
        for (int person = 0; person < 2; person++)
        {
            int y = person == 0 ? creator : approver, initial = split + (int)(col * .64f);
            Text(c, person == 0 ? "DRAWN BY" : "APPROVED BY", split + 20, y + 7, col - 40, 15);
            Text(c, person == 0 ? data.Creator : data.Approver, split + 20, y + 29, col - 40, 28);
            Line(split, y + 64, right, y + 64);
            Line(initial, y + 64, initial, y + 124);
            Text(c, "DATE", split + 20, y + 70, initial - split - 40, 14);
            Text(c, person == 0 ? data.DrawingDate : data.ApprovalDate, split + 20, y + 91, initial - split - 40, 23);
            Text(c, "INITIAL", initial + 15, y + 70, right - initial - 30, 14);
            InitialsFont.Draw(c, person == 0 ? data.CreatorInitials : data.ApproverInitials, new(initial + 4, y + 66, right - initial - 8, 56), person == 0 ? data.InitialsStyle : InitialsFont.Normalize(data.ApproverInitialsStyle, InitialsFont.ApproverFace));
            if (person == 0)
                Line(split, y + 124, right, y + 124);
        }
        var spans = PrintIdentity.CreditSpans(2);
        int creditWidth = PageLayout.InnerWidth(options) - 300, font = Math.Clamp((int)(creditWidth / (spans.Sum(s => s.Text.Length * s.Scale) * .64)), 12, 23);
        c.InlineText(spans, left + 20, footer + 18, font);
        ExportDate(c, options, exported ?? LocalDate());
    }


    static void SideBySide(Canvas c, TitleBlockData data, int x, int y, int width)
    {
        int half = width / 2, divider = x + half;
        c.Line(divider, y, divider, y + 300, .65f);
        Text(c, data.Company, x + 20, y + 22, half - 40, 30, true);
        if (data.Logo != null)
            CompanyLogo.Draw(c, data.Logo, new(x + 24, y + 83, half - 48, 185));
        float scale = Math.Min(1, (half - 48) / 300f);
        int center = divider + half / 2;
        int pawFont = (int)(48 * scale), dymndFont = (int)(52 * scale);
        c.Text("PawPrints", center - (int)(9 * pawFont * .55f / 2), y + 60, pawFont, 1, true, true, false, "Consolas");
        c.Text("DYMND", center - (int)(5 * dymndFont * .55f / 2), y + 200, dymndFont, 1, true, true, true, "Consolas");
        // The selected 4D arc fits tightly between the two wordmarks.
        for (int i = 0; i < 4; i++)
        {
            float t = i / 3f, px = center + (-87.5f + 175 * t) * scale, py = y + 157 + (17 - 33 * MathF.Sin(t * MathF.PI)) * scale;
            PawMark.Draw(c, new(px, py), .82f * scale, 10 + 80 * t, true);
        }
    }


    // Compact layout: title/branding share the top band; the generation credit
    // occupies only the lower-left cell beside the signing block.
    public static void DrawCompact(Canvas c, string title, PrintOptions options, string? exported = null, Action<Canvas, Box>? maker = null, int? makerWidth = null, int topBand = 210, Action<Canvas, string, Box>? initials = null, string? calculatedScale = null)
    {
        var layout = Layout(options);
        var data = options.TitleBlock!;
        int left = (int)layout.Bounds.X, right = PageLayout.Right(options), bottom = PageLayout.Bottom(options), top = bottom - (topBand + 4 * SignoffRowHeight);
        int split = layout.Split, col = right - split, logoSplit = makerWidth.HasValue ? right - makerWidth.Value : split + col / 2, middle = top + topBand, footer = FooterTop(options);
        void Line(int x, int y, int x2, int y2) => c.Line(x, y, x2, y2, .65f);
        Line(left, top, right, top);
        Line(left, bottom, right, bottom);
        Line(left, top, left, bottom);
        Line(right, top, right, bottom);
        Line(split, top, split, bottom);
        Line(logoSplit, top, logoSplit, middle);
        Line(left, middle, right, middle);
        Line(left, footer, split, footer);
        int textLeft = TitleLeft(options), textWidth = split - textLeft - 30;
        Text(c, PrintScale.Title(title, data), textLeft, top + 14, textWidth, 68, true);
        Text(c, data.Subtitle, textLeft, top + 94, textWidth, 27);
        string scaleText = PrintScale.Text(data, calculatedScale);
        if (scaleText.Length > 0)
            Text(c, "SCALE   " + scaleText + "   /   " + PrintScale.Paper(options).ToUpperInvariant(), textLeft, middle + 10, textWidth, 24);
        Text(c, SheetLayout.ViewSummary(options), textLeft, middle + 54, textWidth, 21);
        ExportDate(c, options, exported ?? LocalDate());
        Text(c, data.Company, split + 20, top + 8, logoSplit - split - 40, 26, true);
        if (data.Logo != null)
            CompanyLogo.Draw(c, data.Logo, new(split + 24, top + 40, logoSplit - split - 48, topBand - 48));
        if (maker != null)
            maker(c, new(logoSplit, top, right - logoSplit, topBand));
        else
        {
            float scale = Math.Min(1, (right - logoSplit - 48) / 300f);
            int center = (right + logoSplit) / 2, pawFont = (int)(36 * scale), dymndFont = (int)(38 * scale);
            c.Text("PawPrints", center - (int)(9 * pawFont * .55f / 2), top + 18, pawFont, 1, true, true, false, "Consolas");
            for (int i = 0; i < 4; i++)
            {
                float t = i / 3f;
                PawMark.Draw(c, new(center + (-70 + 140 * t) * scale, top + 105 + (14 - 27 * MathF.Sin(t * MathF.PI)) * scale), .64f * scale, 10 + 80 * t, true);
            }
            c.Text("DYMND", center - (int)(5 * dymndFont * .55f / 2), top + 153, dymndFont, 1, true, true, true, "Consolas");
        }
        for (int person = 0; person < 2; person++)
        {
            int y = middle + person * 2 * SignoffRowHeight, dateTop = y + SignoffRowHeight, rowBottom = dateTop + SignoffRowHeight;
            int initial = split + (int)(col * .64f), value = split + Math.Min(210, (int)(col * .25f));
            Text(c, person == 0 ? "DRAWN BY" : "APPROVED", split + 20, y + 9, value - split - 40, 22);
            Text(c, person == 0 ? data.Creator : data.Approver, value, y + 9, right - value - 20, 26);
            Line(split, dateTop, right, dateTop);
            Line(initial, dateTop, initial, rowBottom);
            Text(c, "DATE", value - 110, dateTop + 9, 90, 22);
            Text(c, person == 0 ? data.DrawingDate : data.ApprovalDate, value, dateTop + 9, initial - value - 20, 23);
            Text(c, "INITIAL", initial + 15, dateTop + 5, right - initial - 30, 13);
            int initialsLeft = initial + 15 + (int)("INITIAL".Length * NativeText.MonoWidth(13)) - 3;
            if (initials != null)
                initials(c, person == 0 ? data.CreatorInitials : data.ApproverInitials, new(initial + 4, dateTop + 1, right - initial - 8, SignoffRowHeight - 2));
            else
                InitialsFont.Draw(c, person == 0 ? data.CreatorInitials : data.ApproverInitials, new(initialsLeft, dateTop + 1, right - initialsLeft - 8, SignoffRowHeight - 2), person == 0 ? data.InitialsStyle : InitialsFont.Normalize(data.ApproverInitialsStyle, InitialsFont.ApproverFace), person == 0 ? data.CreatorInitialsSize : data.ApproverInitialsSize, leftAligned: true);
            if (person == 0)
                Line(split, rowBottom, right, rowBottom);
        }
        int revLabel = left + 64, revEnd = left + 164;
        Line(revLabel, footer, revLabel, bottom);
        Line(revEnd, footer, revEnd, bottom);
        string revision = AnnotationText.Clean(data.Revision, 12);
        int revFont = RevisionFont(data);
        int revY = footer + (bottom - footer - revFont) / 2;
        c.Text("REV", (int)(left + (revLabel - left - 3 * NativeText.MonoWidth(revFont)) / 2), revY, revFont, 1);
        c.Text(revision, (int)(revLabel + (revEnd - revLabel - revision.Length * NativeText.MonoWidth(revFont)) / 2), revY, revFont, 1);
        var spans = PrintIdentity.CreditSpans(2);
        int font = Math.Clamp((int)((split - revEnd - 28) / (spans.Sum(s => s.Text.Length * s.Scale) * .64)), 9, 22);
        c.InlineText(spans, revEnd + 14, CreditTop(options), font);
    }


    static void Maker(Canvas c, int x, int y, int width, int count)
    {
        float s = Math.Min(1, (width - 36) / 500f);
        int start = x + (int)((width - 460 * s) / 2), font = (int)(28 * s);
        c.Text("PawPrints", start, y + 17, font, 1, true, true, false, "Consolas");
        c.Text("DYMND", start + (int)(335 * s), y + 17, font + 2, 1, true, true, true, "Consolas");
        int paws = count == 4 ? 4 : 3;
        for (int i = 0; i < paws; i++)
        {
            float t = i / (float)(paws - 1), px = start + (200 + 100 * t) * s, py = y + 30 + (paws == 3 ? (i % 2 == 0 ? 9 : -9) : 11 - 22 * MathF.Sin(t * MathF.PI)) * s;
            float angle = paws == 3 ? new[] { 55, 85, 65 }[i] : 10 + 80 * t;
            PawMark.Draw(c, new(px, py), .4f * s, angle, true);
        }
    }


    public static Box DateArea(PrintOptions options) => new(TitleLeft(options), PageLayout.Bottom(options) - 2 * SignoffRowHeight, Layout(options).Split - TitleLeft(options) - 20, SignoffRowHeight - 1);


    static void ExportDate(Canvas c, PrintOptions options, string date)
    {
        var b = DateArea(options);
        Text(c, "EXPORTED " + date, (int)b.X, (int)b.Y + 11, (int)b.Width, 19);
    }


    public static byte[] StampExportDate(Sheet sheet, string date)
    {
        if (sheet.Options.TitleBlock == null)
            return sheet.Rgb;
        var colors = SheetLayout.Colors(sheet.Options.Theme);
        var background = new Canvas(sheet.PixelWidth, sheet.PixelHeight, colors.Paper, colors.Ink, sheet.Options);
        var output = (byte[])sheet.Rgb.Clone();
        var b = DateArea(sheet.Options);
        for (int y = (int)b.Y; y < (int)b.Bottom; y++)
            Array.Copy(background.Pixels, (y * sheet.PixelWidth + (int)b.X) * 3, output, (y * sheet.PixelWidth + (int)b.X) * 3, (int)b.Width * 3);
        var c = new Canvas(output, sheet.PixelWidth, sheet.PixelHeight, colors.Ink);
        ExportDate(c, sheet.Options, date);
        NativeText.Apply(output, sheet.PixelWidth, sheet.PixelHeight, c.Runs);
        return output;
    }
}
