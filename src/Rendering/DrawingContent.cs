namespace Blueprint.Rendering;

// Editable definitions only. History never owns a Sheet, RenderScene, native
// object, mesh, texture map or page raster. Lists are copied; their definition
// records (including nested arrays) are immutable by convention and shared,
// matching the legacy editor's record equality. Logo is a shared immutable asset.
sealed record DrawingContent(PartLabel[] Labels, ExplodedDetail[] Details, TableLayout Tables,
    ViewCaptionMark[] Captions, HeightDimension[] Dimensions, KittenReference[] Kittens,
    PartPose[] Poses, SpecBlock[] Specs, FreeTextBox[] FreeText, DrawingPlacement? Drawing, PrintOptions Options)
{
    public WearMark[] WearMarks { get; init; } = [];
    public DrawingContent Copy() => this with
    {
        Labels = [.. Labels], WearMarks = [.. WearMarks],
        Details = [.. Details],
        Captions = [.. Captions], Dimensions = [.. Dimensions], Kittens = [.. Kittens], Poses = [.. Poses],
        Specs = [.. Specs], FreeText = [.. FreeText]
    };

    public RenderScene ApplyTo(RenderScene scene) => scene.WithTables(Tables).WithCaptions(Captions, false)
        .WithDimensions(Dimensions).WithKittens(Kittens).WithPoses(Poses).WithSpecs(Specs)
        .WithFreeText(FreeText).WithWear(WearMarks).WithDrawing(Drawing).WithOptions(Options);
}

sealed record AcceptedDrawing(Sheet Sheet, Sheet? RegeneratedBase, AnnotationDocument Document, DrawingDefinition Definition);

static class DrawingAcceptance
{
    // Validate and prepare against a fork. GPU upload and the UI's atomic swap
    // occur only after this succeeds; failed candidates cannot mutate history.
    public static AcceptedDrawing Prepare(Sheet result, AnnotationDocument? previous, bool keep, bool history)
    {
        var regenerated = result.RegeneratedBase;
        result = result with { RegeneratedBase = null };
        if (result.Scene == null) throw new InvalidDataException("Rendered drawing has no editable scene.");
        if (result.Scene.Drawing == null)
        {
            var drawing = DrawingPlacements.FromScene(result.Scene);
            var options = result.Options with { Drawing = drawing };
            result = result with { Options = options, Scene = result.Scene.WithOptions(options).WithDrawing(drawing) };
            if (regenerated?.Scene != null)
                regenerated = regenerated with { Options = options, Scene = regenerated.Scene.WithOptions(options).WithDrawing(drawing) };
        }
        AnnotationDocument next;
        if (previous != null && (keep || regenerated != null))
        {
            next = previous.Fork();
            if (regenerated != null) next.Regenerate(result.Scene, result.Labels, result.Details, result.Refresh != null);
            else next.Rebind(result.Scene, result.Labels, result.Details, !history);
        }
        else
        {
            next = new(result.Scene, result.Labels);
            next.Rebind(result.Scene, result.Labels, result.Details, false);
        }
        return new(result, regenerated, next, DrawingDefinitions.FromAccepted(result, next));
    }
}
