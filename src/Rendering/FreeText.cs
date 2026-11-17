namespace Blueprint.Rendering;

sealed record FreeTextBox(int Id, string Text, Box Bounds, int FontSize = 32, bool Bold = false, bool Italic = false, bool Underline = false, int Alignment = 0, TextFormat[]? Formats = null, bool Outline = false);
static class FreeTextPainting
{
    public const int Limit = 64, TextLimit = 2048;

    public static string[] Lines(FreeTextBox box)
    {
        int columns = Math.Max(1, (int)((box.Bounds.Width - 16) / NativeText.MonoWidth(box.FontSize)));
        return RichText.Lines(box, columns).Select(line => string.Concat(line.Select(r => r.Text))).ToArray();
    }

    public static FreeTextBox Fit(FreeTextBox box, PrintOptions options)
    {
        if (box.Text.Length > TextLimit)
            throw new InvalidOperationException("Use up to 2048 characters per text box.");
        if (string.IsNullOrWhiteSpace(box.Text))
            throw new InvalidOperationException("Enter text before accepting.");
        int font = Math.Clamp(box.FontSize, 12, 144);
        var page = options.Page;
        box = box with
        {
            FontSize = font,
            Alignment = Math.Clamp(box.Alignment, 0, 2),
            Bounds = box.Bounds with
            {
                Width = Math.Clamp(box.Bounds.Width, Math.Min(160, page.Width), page.Width)
            }
        };
        float height = Math.Max(Math.Min(box.Bounds.Height, page.Height), Math.Max(font + 16, new TextEditorLayout(box).Height));
        if (height > page.Height)
            throw new InvalidOperationException("This text is taller than the page. Reduce its size or use fewer lines.");
        var b = box.Bounds;
        return box with
        {
            Bounds = new(MathF.Round(Math.Clamp(b.X, 0, page.Width - b.Width)), MathF.Round(Math.Clamp(b.Y, 0, page.Height - height)), b.Width, MathF.Ceiling(height))
        };
    }

    public static void Paint(Canvas canvas, IEnumerable<FreeTextBox> boxes)
    {
        foreach (var box in boxes)
        {
            if (box.Outline)
            {
                var b = box.Bounds;
                canvas.Line((int)b.X, (int)b.Y, (int)b.Right, (int)b.Y, 1);
                canvas.Line((int)b.X, (int)b.Bottom, (int)b.Right, (int)b.Bottom, 1);
                canvas.Line((int)b.X, (int)b.Y, (int)b.X, (int)b.Bottom, 1);
                canvas.Line((int)b.Right, (int)b.Y, (int)b.Right, (int)b.Bottom, 1);
            }
            var layout = new TextEditorLayout(box);
            for (int row = 0; row < layout.Lines.Length; row++)
            {
                var line = layout.Lines[row];
                float x = box.Bounds.X + layout.Left(row);
                foreach (var run in line.Runs(RichText.Default(box)))
                {
                    int font = run.FontSize > 0 ? run.FontSize : box.FontSize;
                    float y = box.Bounds.Y + layout.Top(row) + (line.FontSize - font);
                    canvas.Text(run.Text, (int)x, (int)y, font, 1, (run.Style & FreeTextStyle.Bold) != 0, (run.Style & FreeTextStyle.Italic) != 0, face: "Consolas", underline: (run.Style & FreeTextStyle.Underline) != 0);
                    x += run.Text.Length * NativeText.MonoWidth(font);
                }
            }
        }
    }

    // Render only the editor viewport; long notes never allocate a page-sized texture.
    public static PlacementPreview EditorPreview(FreeTextBox box, int theme, int width, int height, float scale, float scroll)
    {
        var colors = SheetLayout.Colors(theme);
        var layout = new Canvas(new byte[3], 1, 1, colors.Ink);
        Paint(layout, [box with { Bounds = box.Bounds with { X = 0, Y = -scroll } }]);
        var paper = new Canvas(width, height, colors.Paper, colors.Ink, new(Grid: false, Zones: false));
        var runs = layout.Runs.Where(r => r.Y + r.Height >= 0 && r.Y * scale < height)
            .Select(r => r with { X = (int)(r.X * scale), Y = (int)(r.Y * scale), Height = Math.Max(1, (int)(r.Height * scale)) }).ToList();
        NativeText.Apply(paper.Pixels, width, height, runs);
        return new(width, height, Sheet.MakePreview(paper.Pixels), new(0, 0, width, height));
    }

    public static PlacementPreview Preview(FreeTextBox box, int theme)
    {
        if (box.Text.Length > TextLimit)
            throw new InvalidOperationException("Use up to 2048 characters per text box.");
        var colors = SheetLayout.Colors(theme);
        var layout = new Canvas(new byte[3], 1, 1, colors.Ink);
        Paint(layout, [box with { Bounds = box.Bounds with { X = 0, Y = 0 } }]);
        float width = Math.Max(160, box.Bounds.Width), height = layout.Runs.Select(r => r.Y + r.Height + 12).DefaultIfEmpty(40).Max();
        float scale = Math.Min(1, Math.Min(4096 / height, Math.Min(1000 / width, MathF.Sqrt(2000000 / (width * height)))));
        int w = Math.Max(1, (int)MathF.Ceiling(width * scale)), h = Math.Max(1, (int)MathF.Ceiling(height * scale));
        var paper = new Canvas(w, h, colors.Paper, colors.Ink, new(Grid: false, Zones: false));
        var runs = layout.Runs.Select(r => r with { X = (int)(r.X * scale), Y = (int)(r.Y * scale), Height = Math.Max(1, (int)(r.Height * scale)) }).ToList();
        NativeText.Apply(paper.Pixels, w, h, runs);
        return new(w, h, Sheet.MakePreview(paper.Pixels), new(0, 0, w, h));
    }
}
