using Blueprint.Rendering;
using System.Numerics;

static class PageChecks
{

    public static void Run(Action<bool, string> check, Capture capture, Dictionary<string, Texture> maps, string output)
    {
        check(PageSize.From(0, 0) == new PageSize(2400, 3000) && PageSize.From(1, 0) == new PageSize(3000, 3000) && PageSize.From(2, 3) == new PageSize(3000, 1688), "Portrait, square and 16:9 landscape have independent native pixel dimensions");
        for (int shape = 0; shape < 3; shape++)
            for (int aspect = 0; aspect < 6; aspect++)
                foreach (bool tables in new[] { false, true })
                {
                    var options = new PrintOptions(Layout: 2, DrawingSize: 100, FlagTable: tables, Orientation: shape, Aspect: aspect);
                    var views = SheetLayout.Plan(capture, options);
                    var page = options.Page;
                    check(views.All(v => v.Bounds.X >= 60 && v.Bounds.Y >= 60 && v.Bounds.Right <= page.Width - 60 && v.Bounds.Bottom + 56 <= PageLayout.TitleTop(options) && v.Frame!.Scale > 0) &&
                        (!tables || views.All(v => !v.Bounds.Overlaps(PageLayout.Table(options)))) &&
                        views.Where(v => v.Id != 4).Select(v => v.Frame!.Scale).Distinct().Count() == 1,
                        $"Shape {shape}, ratio {aspect}, tables {tables}: projections fit page, clear footer/tables and retain aligned scale");
                }
        var dir = Path.Combine(output, "page-options");
        Directory.CreateDirectory(dir);
        bool companyFits = true;
        for (int shape = 0; shape < 3; shape++)
            for (int aspect = 0; aspect < 6; aspect++)
                for (int layout = 0; layout < 3; layout++)
                    foreach (bool tables in new[] { false, true })
                    {
                        var options = new PrintOptions(Layout: layout, DrawingSize: 100, FlagTable: tables, Orientation: shape, Aspect: aspect, TitleBlock: new());
                        var views = SheetLayout.Plan(capture, options);
                        companyFits &= views.All(v => v.Bounds.Bottom + (layout == 0 ? 0 : 56) <= CompanyTitleBlock.Top(options) && v.Frame!.Scale > 0 && (!tables || !v.Bounds.Overlaps(PageLayout.Table(options))));
                    }
        check(companyFits, "Company title block clears all three layouts across every shape/ratio, with and without tables");
        var wideOptions = new PrintOptions(Layout: 2, DrawingSize: 100, Orientation: 2, Aspect: 3, TitleBlock: new());
        var wideViews = SheetLayout.Plan(capture, wideOptions);
        var frontFrame = wideViews.Single(v => v.Id == 1).Frame!;
        var world = capture.Meshes.SelectMany(m => m.Vertices).Select(v => new Vector3((float)v[1], (float)v[2], (float)v[0])).ToArray();
        float drawnHeight = world.Max(p => Math.Abs(Vector3.Dot(p - frontFrame.Center, frontFrame.Up))) * 2 * frontFrame.Scale;
        check(drawnHeight > 600, "16:9 Lander engineering sheet fills its height instead of shrinking into fixed quadrants");
        var frontOnly = SheetLayout.Plan(capture, wideOptions with
        {
            VisibleViews = 1
        });
        check(frontOnly.Length == 1 && frontOnly[0].Bounds.Width == PageLayout.Drawing(wideOptions).Width && frontOnly[0].Frame!.Scale >= frontFrame.Scale,
            "A single enabled projection reclaims hidden-view slots and is not limited by hidden projections");
        bool offeredFit = true;
        for (int shape = 0; shape < 3; shape++)
            for (int aspect = 0; aspect < 6; aspect++)
                foreach (int layout in new[] { 1, 2 })
                    foreach (bool tables in new[] { false, true })
                        foreach (bool captions in new[] { false, true })
                            for (int mask = 1; mask < 16; mask++)
                            {
                                var o = new PrintOptions(Layout: layout, VisibleViews: mask, Orientation: shape, Aspect: aspect, PartsTable: tables, Captions: captions, TitleBlock: new());
                                var ids = ViewArrangements.Views(o);
                                var choices = ViewArrangements.Choices(o);
                                offeredFit &= choices.Select(c => c.Id).Distinct().Count() == choices.Length && (!choices.Any(c => c.Id == 4) || ids.Contains(4) && ids.Length > 1);
                                foreach (var choice in choices.Where(c => c.Id != 0))
                                {
                                    var cells = ViewArrangements.Cells(o, choice.Id, ids);
                                    var area = PageLayout.Drawing(o);
                                    offeredFit &= cells.Length == ids.Length && cells.All(b => b.X >= area.X && b.Y >= area.Y && b.Right <= area.Right && b.Bottom + (captions ? 56 : 0) <= area.Bottom);
                                }
                            }
        check(offeredFit, "Every offered arrangement fits all orientations/ratios, table/caption states and visible-view subsets");
        check(ViewArrangements.Choices(wideOptions).Any(c => c.Id == 3) && ViewArrangements.Choices(wideOptions).Any(c => c.Id == 4)
            && ViewArrangements.Cells(wideOptions, 3, ViewArrangements.Views(wideOptions)).All(b => b.Height >= 140),
            "Reduced 16:9 margins now fit four readable rows while retaining Large isometric");
        check(!ViewArrangements.Choices(wideOptions with
        {
            Orientation = 0,
            PartsTable = true
        }).Any(c => c.Id == 2),
            "9:16 portrait with tables omits a four-column strip too narrow for captions");
        var narrowTables = wideOptions with
        {
            Orientation = 0,
            PartsTable = true
        };
        check(SheetLayout.Plan(capture, narrowTables with
        {
            Arrangement = 2
        }).SequenceEqual(SheetLayout.Plan(capture, narrowTables)),
            "An arrangement made unavailable by settings falls back safely to Best fit");
        bool summaries = true;
        for (int mask = 1; mask < 16; mask++)
        {
            var o = wideOptions with
            {
                VisibleViews = mask
            };
            string expected = string.Join(" / ", Enumerable.Range(1, 4).Where(id => (mask & (1 << (id - 1))) != 0).Select(SheetLayout.ViewName));
            var c = new Canvas(o.Page.Width, o.Page.Height, new(19, 51, 87), new(214, 232, 242), o);
            PageLayout.Decorate(c, "View subset", o);
            summaries &= SheetLayout.ViewSummary(o) == expected && c.Runs.Any(r => r.Text == expected);
        }
        check(summaries, "All 15 view subsets print only their selected views in the compact title block");
        check(new[] { 0, 1 }.All(v => string.Concat(PrintIdentity.CreditSpans(v).Select(s => s.Text)) == PrintIdentity.Credit(v)) &&
            string.Concat(PrintIdentity.CreditSpans(0).Where(s => s.Outline).Select(s => s.Text)) == "DyM&D" &&
            string.Concat(PrintIdentity.CreditSpans(1).Where(s => s.Outline).Select(s => s.Text)) == "DyMND",
            "Maker initials are hollow spans within the full credit sentence, with ampersand and aNd variants");
        check(PrintIdentity.CreditSpans(0).Where(s => s.Outline).All(s => s.Scale == 1.5f && s.Stroke == 2), "Selected option 04 uses the reduced 1.5x initials and medium outline");
        // Actual mod typography, including outlined glyphs over the same grid.
        var colors = SheetLayout.Colors(2);
        var comparison = new Canvas(2400, 600, colors.Paper, colors.Ink, new(Theme: 2));
        comparison.Text("01 / AMPERSAND", 160, 25, 27, 1);
        PageLayout.Footer(comparison, 2400, 300, 0);
        comparison.Text("02 / aNd", 160, 325, 27, 1);
        PageLayout.Footer(comparison, 2400, 600, 1);
        check(comparison.Runs.Where(r => r.Spans != null).All(r => r.Bold && r.Italic && r.Face == "Consolas"), "Inline hollow initials and filled footer type share the same bold italic font");
        NativeText.Apply(comparison.Pixels, 2400, 600, comparison.Runs);
        string comparisonFile = Path.Combine(dir, $"maker-mark-{Guid.NewGuid():N}.png");
        Png.Write(comparisonFile, 2400, 600, comparison.Pixels);
        File.Copy(comparisonFile, Path.Combine(dir, "maker-mark-comparison.png"), true);
        var owned = capture.Meshes.Select((m, i) => new LabelPart(i, "Captured part " + i, "fixture" + i, [i])).ToArray();
        foreach (var spec in new[] { (Name: "portrait", Shape: 0, Aspect: 0, Brand: 0), (Name: "square", Shape: 1, Aspect: 0, Brand: 1), (Name: "landscape-16x9", Shape: 2, Aspect: 3, Brand: 0) })
        {
            var options = new PrintOptions(Layout: 2, DrawingSize: 100, FlagTable: true, Theme: 2, Orientation: spec.Shape, Aspect: spec.Aspect, Brand: spec.Brand);
            var sheet = SheetLayout.Render(capture, maps, "Lander 1", owned, options, [], default, _ => { });
            var scene = sheet.Scene!;
            check(sheet.Rgb.Length == sheet.PixelWidth * sheet.PixelHeight * 3 && sheet.PreviewRgba.Length == sheet.PixelWidth * sheet.PixelHeight * 4 && scene.Owners.Length == sheet.PixelWidth * sheet.PixelHeight,
                spec.Name + ": pixels, preview and owner map use the same dimensions");
            var pixel = Array.FindIndex(scene.Owners, id => id >= 0);
            var picked = scene.Pick(new(pixel % scene.Width, pixel / scene.Width));
            check(picked != null && picked.Value.Part == scene.Owners[pixel], spec.Name + ": visible part picking uses the new row stride");
            var doc = new AnnotationDocument(scene);
            var draft = doc.Draft(picked!.Value.View, picked.Value.Part, out _, note: "PAGE SIZE TEST")!;
            var flow = new FlagnotePlacement(draft, true, scene.Width, scene.Height);
            flow.PickStart(new(pixel % scene.Width, pixel / scene.Width));
            flow.PickEnd(new(scene.Width + 200, scene.Height + 200));
            flow.SetText("PAGE SIZE TEST");
            var bounds = AnnotationText.Bounds(flow.Label);
            check(bounds.Right <= scene.Width && bounds.Bottom <= scene.Height && doc.Apply([flow.Label], out _), spec.Name + ": manual note clamps to actual page and commits");
            var printed = AnnotationPainting.Compose(sheet, doc.Labels, default);
            var file = PrintFiles.Save(printed, dir);
            check(Png.ReadOwn(file).SequenceEqual(printed.Rgb), spec.Name + ": annotated export round-trips at selected ratio");
            File.Copy(file, Path.Combine(dir, spec.Name + ".png"), true);
        }
    }
}
