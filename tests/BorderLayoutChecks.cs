using Blueprint.Rendering;
static class BorderLayoutChecks
{

    public static void Run(Action<bool, string> check, string output)
    {
        foreach (int theme in Enumerable.Range(0, 4))
        {
            var colors = SheetLayout.Colors(theme);
            byte[] Paper(bool archive, int level) => new Canvas(512, 512, colors.Paper, colors.Ink,
                new PrintOptions(Theme: theme, Grid: false, Archive: archive, WearLevel: level)).Pixels;
            var clean = Paper(false, 3);
            check(clean.SequenceEqual(Paper(true, 0)), $"Wear Off preserves the clean background for theme {theme}");
            double previous = 0;
            foreach (int level in Enumerable.Range(1, 5))
            {
                var aged = Paper(true, level);
                double difference = aged.Select((value, i) => Math.Abs(value - clean[i])).Average();
                check(difference > previous, $"Archival wear {level} visibly increases on theme {theme}");
                previous = difference;
            }
        }
        Landscape(check, output);
        var cases = (from shape in Enumerable.Range(0, 3) from aspect in Enumerable.Range(0, 6) select new PrintOptions(Orientation: shape, Aspect: aspect, TitleBlock: new(), Grid: false))
            .Concat(new[] { new PrintOptions(Orientation: 3, CustomWidth: 5120, CustomHeight: 1440, TitleBlock: new(), Grid: false), new PrintOptions(Orientation: 3, CustomWidth: 1440, CustomHeight: 5120, TitleBlock: new(), Grid: false) });
        foreach (var o in cases)
        {
            var page = o.Page;
            int bottom = page.Height - 60, split = CompanyTitleBlock.Layout(o).Split;
            var area = PageLayout.Placement(o);
            check(area.X == 60 && area.Y == 60 && area.Right == page.Width - 60 && area.Bottom == (page.Width > page.Height ? bottom : CompanyTitleBlock.Top(o)), "60px frame and placement agree with zoned title: " + page);
            var colors = SheetLayout.Colors(0);
            var c = new Canvas(page.Width, page.Height, colors.Paper, colors.Ink, o);
            PageLayout.Decorate(c, "Frame check", o, "1:100");
            // Before typography: sample through blank padding left of all row text.
            bool Rule(int x, int y)
            {
                int k = (y * page.Width + x) * 3;
                return Math.Abs(c.Pixels[k] - colors.Paper.X) > 10;
            }
            int ab = (int)MathF.Round(60 + (page.Height - 120) * (SheetZones.Count(o).Rows - 1) / (float)SheetZones.Count(o).Rows);
            var title = PageLayout.TitleBounds(o);
            check(title.Y == ab && Rule((int)title.X + 5, ab) && Rule(page.Width - 40, ab), "Title top and A/B zone ticks share the exact printed row: " + page);
            check(CompanyTitleBlock.BrandingHeight(o) >= 132 && title.Bottom == bottom, "Branding band absorbs zone height while signoffs stay anchored: " + page);
            check(SheetZones.GridAxis(page.Height, SheetZones.Count(o).Rows)[ab] == 10 && PageLayout.TitleBounds(o with
            {
                Zones = false
            }) == title, "Title/grid alignment remains identical with zoned border on or off: " + page);
            var rows = Enumerable.Range(bottom - 176, 177).Where(y => Rule(split + 5, y)).ToArray();
            check(rows.SequenceEqual(Enumerable.Range(0, 5).Select(i => bottom - 176 + i * 44)), "Four signoff rows are exactly 44px: " + page);
            check(Rule(split - 10, bottom - 44) && Rule(split + 10, bottom - 44) && !Rule(split + 10, bottom - 42), "Footer joins final date row with no 2px step: " + page);
            var text = c.Runs.Where(r => r.Text is "DRAWN BY" or "APPROVED" or "DATE").ToArray();
            check(text.Length == 4 && text.Select(r => r.Y).SequenceEqual(Enumerable.Range(0, 4).Select(i => bottom - 176 + i * 44 + 9)), "Name/date labels have consistent reduced row padding: " + page);
            var unchanged = new Dictionary<string, int> { { "FRAME CHECK", 68 }, { "GENERAL ARRANGEMENT", 27 }, { "THREE-QUARTER", 21 }, { "DRAWN BY", 22 }, { "APPROVED", 22 }, { "DATE", 22 }, { "INITIAL", 13 }, { "REV", 20 }, { "PawPrints", 36 }, { "DYMND", 64 } };
            check(unchanged.All(p => c.Runs.Any(r => r.Text == p.Key) && c.Runs.Where(r => r.Text == p.Key).All(r => r.Height == p.Value))
                && c.Runs.Where(r => r.Text.StartsWith("SCALE ")).All(r => r.Height == 24) && c.Runs.Where(r => r.Text.StartsWith("EXPORTED ")).All(r => r.Height == 19), "Tighter rows preserve title, branding, metadata and label font sizes: " + page);
            var zones = SheetZones.Count(o);
            var axis = SheetZones.GridAxis(page.Width, zones.Columns);
            check(axis[60] == 10 && axis[page.Width - 60] == 10, "Grid terminates at the shared frame: " + page);
        }
        Directory.CreateDirectory(output);
        foreach (int theme in Enumerable.Range(0, 4))
        {
            var options = new PrintOptions(Orientation: 2, Aspect: 5, Theme: theme, TitleBlock: new(Company: "EXAMPLE AEROSPACE LAB", Creator: "YOUR NAME", CreatorInitials: "YN", DrawingDate: "2026-11-13", ApprovalDate: "2026-11-13"));
            var color = SheetLayout.Colors(theme);
            var c = new Canvas(options.Page.Width, options.Page.Height, color.Paper, color.Ink, options);
            PageLayout.Decorate(c, "LANDER 1", options, "1:100");
            NativeText.Apply(c.Pixels, options.Page.Width, options.Page.Height, c.Runs);
            var file = Path.Combine(output, $"theme-{theme}.png");
            if (File.Exists(file))
                File.Delete(file);
            Png.Write(file, options.Page.Width, options.Page.Height, c.Pixels);
            int x = 1500, y = PageLayout.Bottom(options) - 220, w = 1500, h = 280;
            var crop = new byte[w * h * 3];
            for (int row = 0; row < h; row++)
                Array.Copy(c.Pixels, ((y + row) * options.Page.Width + x) * 3, crop, row * w * 3, w * 3);
            file = Path.Combine(output, $"signoffs-{theme}.png");
            if (File.Exists(file))
                File.Delete(file);
            Png.Write(file, w, h, crop);
            int titleY = CompanyTitleBlock.Top(options) - 12, titleHeight = PageLayout.Bottom(options) - titleY + 13;
            var title = new byte[options.Page.Width * titleHeight * 3];
            Array.Copy(c.Pixels, titleY * options.Page.Width * 3, title, 0, title.Length);
            file = Path.Combine(output, $"title-{theme}.png");
            if (File.Exists(file))
                File.Delete(file);
            Png.Write(file, options.Page.Width, titleHeight, title);
        }
    }

    static void Landscape(Action<bool, string> check, string output)
    {
        Directory.CreateDirectory(output);
        var normal = new PrintOptions(Orientation: 2, Aspect: 3, PartsTable: true, TitleBlock: new(Title: "NEWROCKET_1", Subtitle: "BREAK ON THROUGH TO THE OTHER SIDE!", Company: "EXAMPLE AEROSPACE", Creator: "EXAMPLE DESIGNER", CreatorInitials: "ED", DrawingDate: "2026-11-13", ApprovalDate: "2026-11-13"));
        var wide = normal with
        {
            Orientation = 3,
            CustomWidth = 5120,
            CustomHeight = 1440
        };
        var longTitle = wide with
        {
            TitleBlock = wide.TitleBlock! with
            {
                Title = "LONG RANGE INTERPLANETARY EXPLORATION VEHICLE"
            }
        };
        var hugeTitle = normal with
        {
            TitleBlock = wide.TitleBlock! with
            {
                Title = new string('W', 100)
            }
        };
        var a = CompanyTitleBlock.Layout(normal);
        var b = CompanyTitleBlock.Layout(wide);
        var longer = CompanyTitleBlock.Layout(longTitle);
        var huge = CompanyTitleBlock.Layout(hugeTitle);
        check(a.Bounds.Width == b.Bounds.Width && a.Bounds.X > PageLayout.Margin + 500 && b.Bounds.X > PageLayout.Margin + 2000, "Landscape width follows content, freeing more space on ultra-wide sheets");
        check(new[] { normal, wide, longTitle, hugeTitle }.All(o => CompanyTitleBlock.Layout(o).Bounds.Right == PageLayout.Right(o) && CompanyTitleBlock.Layout(o).Bounds.Bottom == PageLayout.Bottom(o)), "Landscape title stays anchored at A1 for every text length");
        check(longer.Bounds.Width > b.Bounds.Width && longer.Bounds.X < b.Bounds.X && huge.Bounds.X == PageLayout.Margin, "Long titles expand left, capped at the actual paper border");
        check(CompanyTitleBlock.Layout(longTitle with
        {
            TitleBlock = normal.TitleBlock
        }).Bounds == b.Bounds, "Shortening the title returns the unused strip to the drawing");
        foreach (var (name, o) in new[] { ("landscape", normal), ("ultrawide", wide), ("long-title", longTitle), ("full-width-title", hugeTitle) })
        {
            var layout = CompanyTitleBlock.Layout(o);
            var colors = SheetLayout.Colors(o.Theme);
            var c = new Canvas(o.Page.Width, o.Page.Height, colors.Paper, colors.Ink, o);
            PageLayout.Decorate(c, "ignored fallback", o, "1:100");
            var credit = c.Runs.Where(r => r.Y == CompanyTitleBlock.CreditTop(o)).ToArray();
            check(string.Concat(credit.Select(r => r.Text)) == PrintIdentity.Credit(2).ToUpperInvariant() && credit.All(r => r.Italic) && credit.Max(r => r.X + NativeText.MonoWidth(r.Height)) < layout.Split - 8, name + ": complete italic generation credit fits its actual cell");
            if (name != "full-width-title")
                check(c.Runs.Single(r => r.Text == o.TitleBlock!.Title).Height == 68, name + ": title retains its font size as the block expands");
            var titleCell = CompanyTitleBlock.DateArea(o);
            check(titleCell.X > layout.Bounds.X && titleCell.Right < layout.Split && titleCell.Y == PageLayout.Bottom(o) - 88, name + ": export date stamp follows the moved title block");
            if (name == "landscape")
            {
                // A table can occupy the lower-left strip beside a compact title block.
                var table = new Box(PageLayout.Margin, PageLayout.Bottom(o) - 220, layout.Bounds.X - PageLayout.Margin, 220);
                var scene = new RenderScene(c.Pixels, Enumerable.Repeat(-1, o.Page.Width * o.Page.Height).ToArray(), [new(0, "Docking Port 1W Androgynous A", "port", [])], [], options: o).WithTables(new(table));
                check(PrintTables.Fits(scene, [], out _) && PageLayout.Fits(table, o), "A parts table fits below title-top in reclaimed space, touching the title's left edge");
                var copy = (byte[])c.Pixels.Clone();
                PrintTables.Paint(c, scene, []);
                bool intact = true;
                for (int y = (int)layout.Bounds.Y; y <= layout.Bounds.Bottom; y++)
                    for (int x = (int)layout.Bounds.X; x <= layout.Bounds.Right; x++)
                        for (int k = 0; k < 3; k++)
                            intact &= c.Pixels[(y * o.Page.Width + x) * 3 + k] == copy[(y * o.Page.Width + x) * 3 + k];
                check(intact, "Adjacent table clearing preserves every title-block pixel including its border");
                check(!PrintTables.Fits(scene.WithTables(new(table with
                {
                    Width = table.Width + 1
                })), [], out _), "Even one pixel of table/title overlap is rejected");
                var view = new ViewRegion(100, "TOP", table with
                {
                    Height = table.Height - ViewCaption.Band
                }, null);
                DrawingPlacements.Validate([view], o);
                check(true, "View plus caption can touch bottom and title-left borders in the freed strip");
                bool rejected = false;
                try
                {
                    DrawingPlacements.Validate([view with { Bounds = view.Bounds with { Width = view.Bounds.Width + 1 } }], o);
                }
                catch (InvalidOperationException) { rejected = true; }
                check(rejected, "View/caption may touch but not cross the title block");
                var free = FrameLayout.Clear(table, [layout.Bounds], FrameLayout.Area(o), false, 200, 100, FrameLayout.Gap, boundary: layout.Bounds);
                check(free == table, "Frame placement accepts the same lower-left space without shifting it even with panel gaps enabled");
                var resize = FrameLayout.Clear(table with
                {
                    Width = table.Width + 10
                }, [], FrameLayout.Area(o), true, 200, 100, FrameLayout.Gap, boundary: layout.Bounds);
                check(resize == table, "Resizing against the title stops exactly at its left border with no hidden gap");
                var snap = FrameLayout.Snap(table with
                {
                    Width = table.Width - 3
                }, [layout.Bounds], FrameLayout.Area(o), true, 10, layout.Bounds);
                check(snap.Right == layout.Bounds.X, "Panel snapping targets the actual title border, not an inset clearance line");
            }
            NativeText.Apply(c.Pixels, o.Page.Width, o.Page.Height, c.Runs);
            var file = Path.Combine(output, name + ".png");
            if (File.Exists(file))
                File.Delete(file);
            Png.Write(file, o.Page.Width, o.Page.Height, c.Pixels);
            var sheet = new Sheet("Title test", o, c.Pixels, [], []);
            var stamped = CompanyTitleBlock.StampExportDate(sheet, "2027-01-01");
            bool onlyDate = true, changed = false;
            for (int y = 0; y < o.Page.Height; y++)
                for (int x = 0; x < o.Page.Width; x++)
                    for (int k = 0; k < 3; k++)
                        if (stamped[(y * o.Page.Width + x) * 3 + k] != c.Pixels[(y * o.Page.Width + x) * 3 + k])
                        {
                            changed = true;
                            onlyDate &= titleCell.Contains(new(x, y));
                        }
            check(changed && onlyDate, name + ": export restamps only the relocated date cell");
        }
    }
}
