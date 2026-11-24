using Blueprint.Rendering;
using System.Buffers.Binary;

static class StartupChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var options = new PrintOptions(Orientation: 3, CustomWidth: 1280, CustomHeight: 720,
            PartsTable: true, FlagTable: true, Theme: 2, TitleBlock: new(Title: "My drawing"));
        var blank = BlankDrawing.Create("Fixture", options, default);
        check(blank.Scene!.Views.Length == 0 && blank.Options.Drawing!.Views.Length == 0 && blank.PoseSource == null,
            "Blank drawing has no craft views and requires no capture");
        check(!blank.Options.PartsTable && !blank.Options.FlagTable && blank.Options.TitleBlock == options.TitleBlock
            && blank.Options.Theme == 2 && blank.Rgb.Any(p => p != blank.Rgb[0]),
            "Blank preserves appearance and title/frame ink while omitting optional tables");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { BlankDrawing.Create("Cancelled", options, cancelled.Token); check(false, "Cancelled blank rejected"); }
        catch (OperationCanceledException) { check(true, "Cancelled blank preparation leaves existing drawing untouched"); }
        double[][] points = [[0,0,0],[4,0,0],[0,4,0],[0,0,4]];
        var geometry = new Capture([new Mesh("Fixture", points, [0,2,1,0,1,3,0,3,2,1,2,3], points,
            points.Select(_ => new double[] {1,0,0}).ToArray(), points.Select(_ => new double[] {0,0}).ToArray(), "structure", null, null)]);
        var source = new PoseSource(new(geometry, [new(1,null,"Part","fixture",false)], [1], [false]), [], 1,
            [new(0,"Part","fixture",[0], NodeId: 1)], new Dictionary<string,Texture>());
        var captured = PrintGeneration.Render(source, geometry, "Fixture", blank.Options, [], blank, false, default);
        check(captured.Scene!.Views.Length == 0 && captured.PoseSource != null, "On-demand capture retains an empty sheet until placement");
        float ratio = DrawingPlacements.SuggestRatio(geometry, 4, captured.Options);
        var view = new DrawingView(100, 4, .4f, .4f);
        var ghost = PlacementPreviews.View(source, view, ratio, captured.Options, [], default);
        check(Enumerable.Range(0, ghost.Rgba.Length / 4).Any(i => ghost.Rgba[i*4+3] > 0 &&
            ghost.Rgba[i*4] == 255 && ghost.Rgba[i*4+1] == 221 && ghost.Rgba[i*4+2] == 68),
            "Direct placement preview has yellow linework");
        var placed = PrintGeneration.Render(source, geometry, "Fixture", captured.Options with { Drawing = new(ratio,[view]) }, [], captured, false, default);
        var document = new AnnotationDocument(captured.Scene);
        var accepted = DrawingAcceptance.Prepare(placed with { RegeneratedBase = null }, document, true, false);
        check(accepted.Document.Scene.Drawing!.Views.Length == 1 && document.Scene.Drawing!.Views.Length == 0,
            "Placing the first view commits one view without mutating the blank checkpoint");
        accepted.Document.Undo();
        check(accepted.Document.Scene.Drawing!.Views.Length == 0, "Undo first placement restores the empty drawing");
        accepted.Document.Redo();
        check(accepted.Document.Scene.Drawing!.Views.Length == 1, "Redo restores first placement");
        string file = PrintFiles.Save(placed, output);
        var png = File.ReadAllBytes(file);
        check(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4)) == 1280 && BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20,4)) == 720,
            "Blank-to-one-view workflow exports the selected PNG dimensions");

        var freshOptions = blank.Options with { TitleBlockVisible = false, PartsTable = true, FlagTable = true };
        var selected = StarterDrawings.FromSelection([0, 2]);
        var drawing = StarterDrawings.Render(source, geometry, "Fixture", freshOptions, [], selected, true, default);
        check(drawing.Scene!.Drawing!.Views.Select(v => v.Camera).SequenceEqual(new[] { 0, 2 }) &&
            drawing.Options.PartsTable && drawing.Options.FlagTable && !drawing.Options.TitleBlockVisible,
            "Select Views renders exactly the chosen cameras and tables with the current title visibility");
        var tablesOnly = StarterDrawings.Render(source, geometry, "Fixture", freshOptions, [],
            StarterDrawings.FromSelection([]), true, default);
        check(tablesOnly.Scene!.Views.Length == 0 && tablesOnly.Scene.Parts.Length == 1 && tablesOnly.Options.PartsTable,
            "Tables-only setup keeps the requested table and adds no views");
        var orthographic = StarterDrawings.All[0].Template;
        var ortho = StarterDrawings.Render(source, geometry, "Fixture", blank.Options, [], orthographic, true, default);
        var arrangement = ortho.Scene!.Drawing!;
        check(arrangement.Views.Length == 3 && arrangement.Views.Count(v => v.Parent.HasValue) == 2 &&
            ortho.Scene.Views.Select(v => v.Frame!.Scale).Distinct().Count() == 1,
            "Orthographic starter has exactly three linked views sharing a fitted scale");
        var isoExploded = StarterDrawings.Render(source, geometry, "Fixture", blank.Options, [],
            StarterDrawings.All[2].Template, true, default);
        check(isoExploded.Scene!.Drawing!.Views is [{ Camera: 4, Parent: null }] &&
            isoExploded.Details is [{ Scope: 1 }] && isoExploded.PoseMain != null,
            "ISO plus Exploded starts one standalone ISO and a whole-craft detail");

        uint? wholeRoot = ExplodedPainting.WholeCraftRoot(captured.PoseSource!.Assembly);
        var whole = ExplodedPainting.Compose(captured, captured,
            [new(1, wholeRoot!.Value, new(64, 64, 820, 520), "Whole craft", Seed: wholeRoot)], [], default);
        check(wholeRoot == 1 && whole.Scene!.Drawing!.Views.Length == 0 && whole.Details is [{ Scope: 1, Seed: 1 }],
            "A captured blank sheet accepts a whole-craft exploded view without adding main views");

        var mixed = StarterDrawings.Render(source, geometry, "Fixture", blank.Options, [],
            StarterDrawings.All[1].Template, true, default);
        var orthographicScale = PrintScale.Ratio(mixed.Scene!.Views.First(v => v.Name.Equals("Front", StringComparison.OrdinalIgnoreCase)).Frame, mixed.Options);
        var isoScale = PrintScale.Ratio(mixed.Scene.Views.Single(v => v.Name.Equals("Isometric", StringComparison.OrdinalIgnoreCase)).Frame, mixed.Options);
        check(isoScale != orthographicScale && PrintScale.PrintedTitleScale(mixed.Scene.Views, mixed.Options) == orthographicScale &&
            PrintScale.PrintedTitleScale(mixed.Scene.Views, mixed.Options with { TitleBlockVisible = false }) == null &&
            PrintScale.PrintedTitleScale(mixed.Scene.Views, mixed.Options with { TitleBlock = new(Scale: "1:999") }) == "1:999",
            "Mixed scales use the linked orthographic ratio in the visible title, with hidden and custom titles respected");
        var titleCanvas = new Canvas(new byte[checked(mixed.PixelWidth * mixed.PixelHeight * 3)], mixed.PixelWidth, mixed.PixelHeight, new(0));
        CompanyTitleBlock.DrawCompact(titleCanvas, mixed.Title, mixed.Options, calculatedScale: orthographicScale);
        check(titleCanvas.Runs.Single(r => r.Text.StartsWith("SCALE")).Y >
            titleCanvas.Runs.Single(r => r.Text == SheetLayout.ViewSummary(mixed.Options)).Y,
            "The title block renders the scale below its view list");
        var layout = DrawingDefinitions.ToTemplate(DrawingDefinitions.FromAccepted(ortho, new AnnotationDocument(ortho.Scene)));
        var profile = new Blueprint.Storage.ProfileStorage(Path.Combine(output, "starter-template-profile-" + Guid.NewGuid().ToString("N")));
        new Blueprint.Storage.LayoutTemplateStore(profile).Save("My layout", layout);
        var reopened = new Blueprint.Storage.LayoutTemplateStore(profile).ReadAll().Single();
        var custom = StarterDrawings.Render(source, geometry, "Fixture", blank.Options with { Theme = 3, TitleBlockVisible = false },
            [], reopened.Template, false, default);
        check(reopened.Name == "My layout" && custom.Options.Theme == 3 && !custom.Options.TitleBlockVisible &&
            custom.Scene!.Drawing!.Views.Zip(arrangement.Views).All(pair =>
                Math.Abs(pair.First.X - pair.Second.X) * custom.PixelWidth <= 1 &&
                Math.Abs(pair.First.Y - pair.Second.Y) * custom.PixelHeight <= 1 &&
                pair.First with { X = pair.Second.X, Y = pair.Second.Y } == pair.Second),
            "Custom layout reopens across store instances with its view geometry and current session appearance");
        var invisibleTitle = PreviewUpdates.Render(ortho, ortho, ortho.Options with { TitleBlockVisible = false }, default);
        check(PageLayout.TitleBounds(invisibleTitle.Options) == default &&
            invisibleTitle.Scene!.Views.Select(v => v.Bounds).SequenceEqual(ortho.Scene.Views.Select(v => v.Bounds)) &&
            ReferenceEquals(CompanyTitleBlock.StampExportDate(invisibleTitle, "2027-01-01"), invisibleTitle.Rgb),
            "Hiding the title releases its space, preserves positions, and leaves the export title hidden");
    }
}
