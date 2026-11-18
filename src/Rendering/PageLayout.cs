namespace Blueprint.Rendering;

readonly record struct PageSize(int Width, int Height)
{
    public static readonly string[] Shapes = ["Portrait", "Square", "Landscape", "PC Desktop"];

    public static PageSize Custom(int width, int height)
    {
        if (width < 640 || height < 640 || width > 8192 || height > 8192 || (long)width * height > 16777216 || Math.Max(width, height) > 4 * Math.Min(width, height))
            throw new InvalidOperationException("Choose 640-8192 pixels per side, up to 16 megapixels, and an aspect ratio between 1:4 and 4:1.");
        return new(width, height);
    }

    public static PageSize Workspace(int width, int height)
    {
        var output = Custom(width, height);
        double factor = Math.Max(1, Math.Max(3000d / Math.Max(width, height), 1688d / Math.Min(width, height)));
        return new((int)Math.Round(output.Width * factor), (int)Math.Round(output.Height * factor));
    }
    public static readonly string[] Ratios = ["4:5", "3:4", "2:3", "9:16", "A-series", "US Letter"];
    public static readonly string[] WideRatios = ["5:4", "4:3", "3:2", "16:9", "A-series", "US Letter"];

    public static PageSize From(int orientation, int aspect)
    {
        if (orientation is < 0 or > 2 || aspect is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(orientation));
        double ratio = aspect switch
        {
            1 => 4d / 3,
            2 => 1.5,
            3 => 16d / 9,
            4 => Math.Sqrt(2),
            5 => 11d / 8.5,
            _ => 1.25
        };
        int shortSide = (int)Math.Round(3000 / ratio);
        return orientation switch
        {
            1 => new(3000, 3000),
            2 => new(3000, shortSide),
            _ => new(shortSide, 3000)
        };
    }
}

static class PrintIdentity
{
    public const string Version = "0.5.39";

    public static string Mark(int variant) => variant == 1 ? "Dy M N D" : "Dy M & D";

    public static TextSpan[] CreditSpans(int variant) => Style([
        new($"Print generated in PawPrints V{Version} - "),new("Dy",true),new("namic "),new("M",true),new("anufacturing "),
        ..(variant==2?new TextSpan[]{new("N",true),new("' ")}:variant==1?new TextSpan[]{new("a"),new("N",true),new("d ")}:new TextSpan[]{new("&",true),new(" ")}),
        new("D",true),new("esign")]);

    static TextSpan[] Style(TextSpan[] spans) => spans.Select(s => s.Outline ? s with { Scale = 1.5f, Stroke = 2 } : s).ToArray();

    public static string Credit(int variant) => $"Print generated in PawPrints V{Version} - " + (variant == 2 ? "Dynamic Manufacturing N' Design" : variant == 1 ? "Dynamic Manufacturing aNd Design" : "Dynamic Manufacturing & Design");
}

static class PageLayout
{
    public const int Margin = 60;

    public static int Right(PrintOptions options) => options.Page.Width - Margin;

    public static int Bottom(PrintOptions options) => options.Page.Height - Margin;

    public static int InnerWidth(PrintOptions options) => options.Page.Width - 2 * Margin;

    public static int TitleTop(PrintOptions options) => options.TitleBlock == null ? Bottom(options) - 360 : CompanyTitleBlock.Top(options);

    // Manual placement uses the printed inner frame, not automatic-layout padding.
    public static Box UpperArea(PrintOptions options) => new(Margin, Margin, InnerWidth(options), TitleTop(options) - Margin);

    public static Box Placement(PrintOptions options) => CompanyTitleBlock.ContentSized(options) ? new(Margin, Margin, InnerWidth(options), Bottom(options) - Margin) : UpperArea(options);

    public static Box TitleBounds(PrintOptions options) => options.TitleBlock != null ? CompanyTitleBlock.Layout(options).Bounds : new(Margin, TitleTop(options), InnerWidth(options), Bottom(options) - TitleTop(options));

    public static bool Fits(Box box, PrintOptions options)
    {
        var area = Placement(options);
        return box.X >= area.X && box.Y >= area.Y && box.Right <= area.Right && box.Bottom <= area.Bottom && !box.Overlaps(TitleBounds(options));
    }

    public static void ClearPanel(Canvas canvas, Canvas paper, Box box, PrintOptions options)
    {
        var title = TitleBounds(options);
        int width = options.Page.Width;
        for (int y = Math.Max((int)box.Y, Margin + 1); y < Math.Min(box.Bottom, Bottom(options)); y++)
            for (int x = Math.Max((int)box.X, Margin + 1); x < Math.Min(box.Right, Right(options)); x++)
                if (!(x >= title.X && x <= title.Right && y >= title.Y && y <= title.Bottom))
                    Array.Copy(paper.Pixels, (y * width + x) * 3, canvas.Pixels, (y * width + x) * 3, 3);
    }

    public static Box Drawing(PrintOptions options) => new(Margin + 10, Margin + 160, InnerWidth(options) - 20 - (options.PartsTable || options.FlagTable ? 670 : 0), TitleTop(options) - 40 - (Margin + 160));

    public static Box Table(PrintOptions options) => new(Right(options) - 640, Margin + 160, 620, Drawing(options).Height);

    public static void Decorate(Canvas canvas, string title, PrintOptions options, string? calculatedScale = null)
    {
        if (options.TitleBlock is { } data && string.IsNullOrWhiteSpace(data.Title))
            options = options with
            {
                TitleBlock = data with
                {
                    Title = title
                }
            };
        int w = options.Page.Width, right = Right(options), bottom = Bottom(options), top = TitleTop(options), split = (int)(w * .7f);
        canvas.Line(Margin, Margin, right, Margin, .6f);
        canvas.Line(Margin, bottom, right, bottom, .6f);
        canvas.Line(Margin, Margin, Margin, bottom, .6f);
        canvas.Line(right, Margin, right, bottom, .6f);
        SheetZones.Paint(canvas, options);
        if (options.TitleBlock != null)
        {
            CompactPrintTitle.Draw(canvas, title, options, calculatedScale: calculatedScale);
            return;
        }
        canvas.Line(Margin, top, right, top, .6f);
        canvas.Line(split, top, split, bottom - 110, .6f);
        string display = AnnotationText.Clean(title, 100);
        canvas.Text(display, Margin + 40, bottom - 250, Math.Clamp((split - Margin - 100) / Math.Max(display.Length, 1), 18, 72), 1);
        canvas.Text("GENERAL ARRANGEMENT / EDITOR SNAPSHOT", Margin + 40, bottom - 155, Math.Min(27, (split - Margin - 80) / 23), .8f);
        int metadataSize = Math.Min(30, (right - split - 60) / 10);
        canvas.Text(options.Layout == 2 ? "MULTI-VIEW" : "ORTHOGRAPHIC", split + 30, bottom - 250, metadataSize, 1);
        canvas.Text(options.Layout == 0 ? SheetLayout.ViewName(options.View) : "ALIGNED ORTHO VIEWS", split + 30, bottom - 195, Math.Min(24, (right - split - 60) / 12), .85f);
        canvas.Text("NOT TO SCALE", split + 30, bottom - 150, Math.Min(24, metadataSize), .8f);
        Footer(canvas, w, options.Page.Height, options.Brand);
    }

    public static void Footer(Canvas canvas, int width, int height, int brand)
    {
        int top = height - Margin - 110;
        canvas.Line(Margin, top, width - Margin, top, .6f);
        string credit = PrintIdentity.Credit(brand);
        int font = Math.Clamp((int)((width - 2 * Margin - 80) / (PrintIdentity.CreditSpans(brand).Sum(s => s.Text.Length * s.Scale) * .63)), 14, 28);
        canvas.InlineText(PrintIdentity.CreditSpans(brand), Margin + 40, top + 43, font);
    }
}
