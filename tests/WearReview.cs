using Blueprint.Rendering;

static class WearReview
{
    public static void Write(string output)
    {
        output = Path.Combine(output, "wear-review");
        Directory.CreateDirectory(output);
        const int cellWidth = 320, cellHeight = 180, columns = 6, rows = 4;
        var comparison = new byte[columns * cellWidth * rows * cellHeight * 3];
        for (int theme = 0; theme < rows; theme++)
            for (int level = 0; level < columns; level++)
            {
                bool fullSize = theme == 2 && level is 0 or 5;
                var options = new PrintOptions(Theme: theme, Archive: level > 0, WearLevel: level, Orientation: 3,
                    CustomWidth: fullSize ? 3840 : 1280, CustomHeight: fullSize ? 2160 : 720,
                    TitleBlock: new(CreatorInitials: "YN", Title: "S04 PAPER SAMPLE"));
                var page = options.Page;
                var colors = SheetLayout.Colors(theme);
                var canvas = new Canvas(page.Width, page.Height, colors.Paper, colors.Ink, options);
                PageLayout.Decorate(canvas, "Paper sample", options);
                var draw = PageLayout.Drawing(options);
                int x = (int)draw.Center.X, y = (int)draw.Center.Y;
                int w = page.Width / 8, h = page.Height / 8;
                canvas.Line(x - w, y - h, x + w, y - h, 1);
                canvas.Line(x + w, y - h, x + w, y + h, 1);
                canvas.Line(x + w, y + h, x - w, y + h, 1);
                canvas.Line(x - w, y + h, x - w, y - h, 1);
                canvas.Line(x - w, y + h, x + w, y - h, .7f);
                canvas.Text("WEAR " + level, 100, 105, page.Width / 48, 1);
                NativeText.Apply(canvas.Pixels, page.Width, page.Height, canvas.Runs);
                if (fullSize)
                    Png.Write(Path.Combine(output, $"cream-{level}-4k.png"), page.Width, page.Height, canvas.Pixels);
                var tile = ImageResampling.ResizeRgb(canvas.Pixels, page.Width, page.Height, cellWidth, cellHeight);
                for (int row = 0; row < cellHeight; row++)
                    Array.Copy(tile, row * cellWidth * 3, comparison,
                        ((theme * cellHeight + row) * columns + level) * cellWidth * 3, cellWidth * 3);
            }
        Png.Write(Path.Combine(output, "wear-comparison.png"), cellWidth * columns, cellHeight * rows, comparison);
    }
}
