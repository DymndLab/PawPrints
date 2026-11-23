using Blueprint.Rendering;
using System.Numerics;

static class GenerationRecoveryChecks
{
    public static void Run(Action<bool, string> check)
    {
        double[][] points = [[0, 0, 0], [4, 0, 0], [0, 4, 0], [0, 0, 4]];
        var mesh = new Mesh("Fixture", points, [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3], points,
            points.Select(_ => new double[] { 1, 0, 0 }).ToArray(),
            points.Select(_ => new double[] { 0, 0 }).ToArray(), "structure", null, null);
        var geometry = new Capture([mesh]);
        var assembly = new AssemblyCapture(geometry, [new(1, null, "Original part", "fixture", false)], [1], [false]);
        var source = new PoseSource(assembly, [], 1, [new(0, "Original part", "fixture", [0], NodeId: 1)], new Dictionary<string, Texture>());
        var options = new PrintOptions(Boxes: new(1, 4), DrawingSize: 100, TitleBlock: new(Title: "Retained title"));
        var original = PrintGeneration.Render(source, geometry, "Craft", options, [], null, false, default);
        var dimension = new HeightDimension(original.Scene!.Views.Single().Id);
        var dimensioned = AnnotationPainting.Compose(original with
        {
            Scene = original.Scene.WithDimensions([dimension])
        }, [], default);
        var dimensionHistory = new AnnotationDocument(dimensioned.Scene!, []);
        var cleared = AnnotationPainting.Compose(dimensioned with
        {
            Scene = dimensioned.Scene!.WithDimensions([])
        }, [], default);
        dimensionHistory.Rebind(cleared.Scene!, [], []);
        check(cleared.Scene!.Dimensions.Length == 0 && !cleared.Rgb.SequenceEqual(dimensioned.Rgb)
            && cleared.Scene.Views.SequenceEqual(dimensioned.Scene!.Views),
            "Deleting a height removes its rendered ink while retaining its craft view");
        dimensionHistory.Undo();
        check(dimensionHistory.Scene.Dimensions.Single() == dimension, "Undo restores the deleted height dimension");
        dimensionHistory.Redo();
        check(dimensionHistory.Scene.Dimensions.Length == 0, "Redo removes the height again");
        var note = new PartLabel(1, original.Scene!.Views.Single().Id, 0, "Removed panel", 150, 500, ManualPlacement: true);
        original = AnnotationPainting.Compose(original, [note], default);
        var before = original.Rgb.ToArray();
        var changed = source with
        {
            Assembly = assembly with { Nodes = [new(2, null, "Replacement part", "fixture", false)], MeshNodes = [2] },
            Parts = [new(0, "Replacement part", "fixture", [0], NodeId: 2)]
        };
        try
        {
            PrintGeneration.Render(changed, geometry, "Craft", original.Options, [], original, false, default);
            check(false, "Ordinary generation must reject the removed part's label");
        }
        catch (InvalidOperationException e)
        {
            check(e.Message.Contains("Cannot keep label") && e.Message.Contains(note.Text),
                "Changed craft retains the missing-label safeguard during ordinary generation");
        }
        check(original.Labels.Single() == note && original.Rgb.SequenceEqual(before),
            "Failed generation retains the accepted label and exact original pixels");
        // A bad scale is intentionally retained in the request. Starting fresh must
        // use template fitting instead of reusing the stuck drawing placement.
        var stuck = original.Options with { Drawing = original.Options.Drawing! with { Ratio = .001f } };
        var fresh = PrintGeneration.Render(changed, geometry, "Craft", stuck, [], original, true, default);
        check(fresh.Labels.Length == 0 && fresh.Details.Length == 0 && fresh.Scene!.Poses.Length == 0
            && fresh.Scene.Parts.Single().NodeId == 2 && fresh.RegeneratedBase == null,
            "Explicit new drawing uses the current craft and clears old composition rather than rebinding it");
        check(fresh.Options.Drawing!.Ratio > stuck.Drawing!.Ratio && fresh.Options.TitleBlock == options.TitleBlock
            && fresh.Options.Page == options.Page && fresh.Scene!.Views.Length == 1,
            "New drawing fits the selected template while retaining paper and title settings");
        check(original.Labels.Single() == note && original.Rgb.SequenceEqual(before),
            "Successful candidate generation does not replace or mutate the accepted print before publication");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            PrintGeneration.Render(changed, geometry, "Craft", stuck, [], original, true, canceled.Token);
            check(false, "Canceled fresh drawing must not publish a candidate");
        }
        catch (OperationCanceledException)
        {
            check(original.Labels.Single() == note && original.Rgb.SequenceEqual(before),
                "Canceled new drawing preserves the accepted print");
        }
        var document = new AnnotationDocument(original.Scene!, original.Labels);
        check(document.Delete(note.Id, out _), "Retained document permits deletion of the obsolete part note");
        var repaired = PrintGeneration.Render(changed, geometry, "Craft", original.Options, [], original with
        {
            Scene = document.Scene, Labels = document.Labels, Details = document.Details
        }, false, default);
        check(repaired.Labels.Length == 0 && repaired.Scene!.Parts.Single().NodeId == 2,
            "Deleting the obsolete note from the retained document allows ordinary generation with the changed craft");
        // A larger craft on a smaller sheet: Repage uses the old scene's bounds,
        // so the newly captured geometry must be fitted before validating Plan.
        var largeOptions = new PrintOptions(Orientation: 2, Aspect: 2, Boxes: new(1, 1), DrawingSize: 100);
        var large = PrintGeneration.Render(source, geometry, "Craft", largeOptions, [], null, false, default);
        var enlargedGeometry = new Capture([mesh with { Vertices = points.Select(p => p.Select(v => v * 4).ToArray()).ToArray() }]);
        var enlargedSource = source with { Assembly = assembly with { Geometry = enlargedGeometry } };
        var smallOptions = large.Options with { Orientation = 0, Aspect = 0 };
        var repaged = smallOptions with { Drawing = DrawingPlacements.Repage(large.Scene!, smallOptions) };
        bool tooLarge = false;
        try { SheetLayout.Plan(enlargedGeometry, repaged); }
        catch (InvalidOperationException e) { tooLarge = e.Message.Contains("too big for the canvas"); }
        check(tooLarge, "Smaller-page fixture reproduces the premature canvas-size failure");
        var smaller = PrintGeneration.Render(enlargedSource, enlargedGeometry, "Craft", smallOptions, [], large, false, default);
        DrawingPlacements.Validate(SheetLayout.Plan(enlargedGeometry, smaller.Options), smaller.Options);
        check(smaller.Options.Page == smallOptions.Page && smaller.Options.Drawing!.Ratio > large.Options.Drawing!.Ratio,
            "Production generation fits a smaller page before validating its view plan");
    }
}
