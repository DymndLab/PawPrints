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
    }
}
