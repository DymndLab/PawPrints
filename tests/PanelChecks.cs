using Blueprint.Rendering;
using System.Numerics;

static class PanelChecks
{

    public static void Run(Action<bool, string> check, string output)
    {
        var area = new Box(130, 280, 2140, 2180);
        var neighbor = new Box(1640, 300, 620, 500);
        var moved = FrameLayout.Snap(new(1644, 829, 620, 400), [neighbor], area, false, 20);
        check(moved.X == 1640 && moved.Y == 824, "Moving blocks snaps equal left edges and a 24-pixel stacking gap");
        var resized = FrameLayout.Snap(new(1640, 824, 610, 490), [neighbor], area, true, 20);
        check(resized.Width == 620 && resized.Height == 500, "Resizing blocks snaps matching widths and heights");
        var resizeOptions = new PrintOptions(PartsTable: true, TitleBlock: new());
        var atEdge = FrameLayout.Resize(new(1640, 2200, 999, 999), resizeOptions, 280, 100);
        check(atEdge.X == 1640 && atEdge.Y == 2200 && atEdge.Right == FrameLayout.Area(resizeOptions).Right && atEdge.Bottom == FrameLayout.Area(resizeOptions).Bottom, "Resizing at paper edges preserves the block's top-left anchor");
        var far = new Box(500, 1100, 400, 300);
        check(FrameLayout.Snap(far, [neighbor], area, false, 10) == far, "Distant blocks do not attract unrelated movement");
        var stopped = FrameLayout.Clear(new(1640, 600, 620, 300), [neighbor], area, false, 280, 100, 24);
        check(stopped.HasValue && stopped.Value.Y == 824 && !stopped.Value.Overlaps(neighbor), "Moving a table into another block resolves to its nearest clear stacking edge");
        var above = new Box(1640, 280, 620, 250);
        var below = new Box(1640, 554, 620, 300);
        var limited = FrameLayout.Clear(above with
        {
            Height = 400
        }, [below], area, true, 280, 100, 24);
        check(limited == above, "Resizing a table toward another stops at the shared gap without moving its anchor");
        check(FrameLayout.Clear(new(130, 280, 400, 300), [area], area, false, 280, 100) == null, "A fully occupied drawing area never accepts an overlapping block");
        var options = new PrintOptions(PartsTable: true, FlagTable: true, DrawingSize: 100, TitleBlock: new());
        check(ExplodedPainting.ClampFrame(new(1640, 900, 620, 500), options).X == 1640, "Exploded details can enter the table column without shifting main views");
        var colors = SheetLayout.Colors(0);
        var paper = new Canvas(2400, 3000, colors.Paper, colors.Ink, options);
        LabelPart[] parts = Enumerable.Range(0, 9).Select(i => new LabelPart(i, "Service Module Insulated Assembly " + i, "T" + i, [])).ToArray();
        var scene = new RenderScene(paper.Pixels, Enumerable.Repeat(-1, 2400 * 3000).ToArray(), parts, [], options: options);
        PartLabel[] notes = [new(1, 0, 0, "REMOVE BEFORE FLIGHT", 0, 0, FlagNote: true, Key: "F1")];
        check(PrintTables.Fits(scene, notes, out _), "Default parts and flagnote tables fit their content");
        var layout = new TableLayout(new(220, 350, 480, 850), new(220, 1224, 480, 240));
        var relocated = scene.WithTables(layout);
        check(!PrintTables.Fits(scene.WithTables(new(new(200, 300, 620, 650), new(200, 600, 620, 240))), notes, out _), "Composition rejects overlapping tables even without a UI gesture");
        check(PrintTables.Bounds(relocated, notes, 0) == layout.Parts && PrintTables.Bounds(relocated, notes, 1) == layout.Flagnotes, "Both tables accept independent custom positions and sizes");
        var c = new Canvas((byte[])paper.Pixels.Clone(), 2400, 3000, colors.Ink);
        PrintTables.Paint(c, relocated, notes);
        check(c.Runs.Any(r => r.Text == "PARTS") && c.Runs.Any(r => r.Text == "FLAGNOTES") && c.Runs.Any(r => r.Text == "REMOVE BEFORE FLIGHT"), "Relocated tables preserve headings, quantities and note text");
        check(c.Runs.All(r => layout.Parts!.Value.Contains(new(r.X, r.Y)) || layout.Flagnotes!.Value.Contains(new(r.X, r.Y))), "Table text originates inside resized panels");
        check(PrintTables.Font(relocated, notes, 0, layout.Parts!.Value) >= 14 && PrintTables.Height(relocated, notes, 0, 480, PrintTables.Font(relocated, notes, 0, layout.Parts!.Value)) <= 850, "Resized table wraps rows and fits readable text without truncation");
        var tooSmall = relocated.WithTables(layout with
        {
            Parts = new(220, 350, 280, 100)
        });
        check(!PrintTables.Fits(tooSmall, notes, out _), "Too-small tables fail explicitly instead of silently clipping rows");
        var doc = new AnnotationDocument(scene);
        doc.Rebind(relocated, [], []);
        doc.Undo();
        check(doc.Tables == new TableLayout() && doc.Scene.Tables == doc.Tables, "Undo restores both tables' previous layout and routing state");
        doc.Redo();
        check(doc.Tables == layout && doc.Scene.Tables == layout, "Redo restores table layout as one edit");
        var baseSheet = new Sheet("Panel layout", options, paper.Pixels, Sheet.MakePreview(paper.Pixels), []) { Scene = scene };
        var emptyScene = new RenderScene(paper.Pixels, Enumerable.Repeat(-1, 2400 * 3000).ToArray(), [], [], options: options);
        var emptyTables = new TableLayout(PrintTables.Bounds(emptyScene, [], 0), PrintTables.Bounds(emptyScene, [], 1));
        var emptySheet = baseSheet with
        {
            Scene = emptyScene.WithTables(emptyTables)
        };
        check(!PrintTables.Fits(scene.WithTables(emptyTables), [], out _), "Reproduced: retained empty table cannot fit a newly loaded craft catalog");
        var regenerated = CompositionRegeneration.Rebuild(baseSheet, emptySheet, default);
        check(PrintTables.Fits(regenerated.Scene!, [], out _), "Same-paper regeneration grows stale tables for the new catalog");
        check(regenerated.Scene!.Tables.Parts!.Value.Height > emptyTables.Parts!.Value.Height,
            "Regeneration enlarges the parts box instead of clipping rows");
        check(!regenerated.Scene.Tables.Parts.Value.Overlaps(regenerated.Scene.Tables.Flagnotes!.Value),
            "Regenerated parts and flagnotes remain separated after catalog growth");
        var retained = PrintTables.Refit(relocated, notes, []);
        check(retained == layout, "Already valid user-positioned tables retain their exact bounds");
        var before = AnnotationPainting.Compose(baseSheet, [], default);
        var after = AnnotationPainting.Compose(before, [], default, layout);
        var old = PrintTables.Bounds(scene, [], 0);
        bool cleared = true;
        for (int y = (int)old.Y; y < old.Bottom; y++)
            for (int x = (int)old.X; x < old.Right; x++)
                for (int i = 0; i < 3; i++)
                    cleared &= after.Rgb[(y * 2400 + x) * 3 + i] == paper.Pixels[(y * 2400 + x) * 3 + i];
        check(cleared && ReferenceEquals(after.Scene!.BaseRgb, scene.BaseRgb), "Moving tables clears old ink while preserving the unannotated drawing baseline");
        bool sizes = true;
        for (int orientation = 0; orientation < 3; orientation++)
            for (int aspect = 0; aspect < 6; aspect++)
            {
                var o = options with
                {
                    Orientation = orientation,
                    Aspect = aspect
                };
                int density = PrintScale.Density(o);
                double inchesX = o.Page.Width / (density * .0254), inchesY = o.Page.Height / (density * .0254);
                sizes &= inchesX <= (orientation == 2 ? 11 : 8) + .001 && inchesY <= (orientation == 2 ? 8 : 11) + .001;
                sizes &= Math.Abs(inchesX - (orientation == 2 ? 11 : 8)) < .001 || Math.Abs(inchesY - (orientation == 2 ? 8 : 11)) < .001;
            }
        check(sizes, "All page shapes fit the 8x11 paper reference uniformly with encoded physical density");
        var f = new ProjectionFrame(100, 100, 100, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        float Nice(double ratio) => PrintScale.FitScale((float)(PrintScale.Density(options) / ratio), options);
        check(PrintScale.Ratio(f with
        {
            Scale = Nice(105.28)
        }, options) == "1:110" && PrintScale.Ratio(f with
        {
            Scale = Nice(79.02)
        }, options) == "1:80", "Physical projection uses clean tens above 100 and fives below, rather than rounding the caption alone");
        check(PrintScale.Ratio(f with
        {
            Scale = Nice(45)
        }, options) == "1:45" && PrintScale.Ratio(f with
        {
            Scale = Nice(100)
        }, options) == "1:100", "Exact clean ratios remain unchanged across the step boundary");
        bool fit = true, stable = true;
        for (int orientation = 0; orientation < 3; orientation++)
            for (int aspect = 0; aspect < 6; aspect++)
                foreach (double ratio in new[] { .12, .5, 1.0, 2.5, 4.99, 5.0, 9.1, 49.9, 99.99, 100.0, 105.28, 1000.01 })
                {
                    var o = options with
                    {
                        Orientation = orientation,
                        Aspect = aspect
                    };
                    float raw = (float)(PrintScale.Density(o) / ratio), clean = PrintScale.FitScale(raw, o);
                    fit &= clean > 0 && clean <= raw * 1.000002f;
                    stable &= clean == PrintScale.FitScale(clean, o);
                }
        check(fit, "Clean scales fit every paper shape without enlarging or clipping the geometry");
        check(stable, "Recomposing a clean scale is stable, including small objects and magnified drawings");
        var blockedScene = new RenderScene(scene.BaseRgb, Enumerable.Repeat(-1, 2400 * 3000).ToArray(), parts, [], options: options)
        {
            Details = [new(new(1, 1, PrintTables.Bounds(scene, notes, 0), "Fixture"), f, [], [], new(1, 1, [0], [-1]))]
        };
        check(!PrintTables.Fits(blockedScene, notes, out _), "Composition rejects table overlap with an exploded detail frame");
        check(PrintScale.Summary([new(100, "Front", default, f), new(101, "Side", default, f with { Scale = 90 })], options) == "", "Inconsistent legacy view scales never produce a VARIES BY BOX caption");
        var title = new Canvas(2400, 3000, colors.Paper, colors.Ink, options);
        PageLayout.Decorate(title, "Test", options, "1:50");
        check(title.Runs.Any(r => r.Text.Contains("1:50") && r.Text.Contains("8 X 11 IN")), "Title block shows global ratio and physical paper reference");
        NativeText.Apply(c.Pixels, 2400, 3000, c.Runs);
        string dir = Path.Combine(output, "panels-0.4.3");
        Directory.CreateDirectory(dir);
        string temp = Path.Combine(dir, Guid.NewGuid() + ".png");
        Png.Write(temp, 2400, 3000, c.Pixels, PrintScale.Density(options));
        File.Move(temp, Path.Combine(dir, "tables.png"), true);
    }
}
