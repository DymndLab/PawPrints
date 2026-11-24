namespace Blueprint.Rendering;

static class PrintGeneration
{
    // Starting over is explicit. Never strip missing references from an ordinary
    // regeneration, and never mutate the accepted sheet while rendering a candidate.
    public static Sheet Render(PoseSource source, Capture geometry, string craft, PrintOptions options,
        string[] warnings, Sheet? previous, bool startFresh, CancellationToken token)
    {
        if (startFresh)
        {
            previous = null;
            options = options with { Drawing = null, Boxes = options.Boxes?.ClearBlankFits() };
        }
        if (previous?.PoseSource?.Assembly.Topology is { } oldSource && source.Assembly.Topology is { } newSource &&
            oldSource.Binding.Session != newSource.Binding.Session)
            throw new InvalidOperationException("This drawing's craft identity could not be retained. Start a new drawing to use this craft.");
        if (previous?.Scene?.Drawing != null && options.Drawing != null && previous.Options.Page != options.Page)
            options = options with { Drawing = DrawingPlacements.Repage(previous.Scene, options) };
        var poses = previous?.Scene?.Poses ?? [];
        if (previous != null && previous.Options.Page != options.Page && options.Drawing is { } repaged)
        {
            var envelope = poses.Length == 0 ? geometry : new Capture(repaged.Views.SelectMany(v =>
                PartPoses.Exterior(source, PartPoses.Apply(source, poses, v.Id)).Meshes).ToArray());
            options = options with
            {
                Drawing = DrawingPlacements.FitToPage(envelope, options, repaged,
                    CompositionRegeneration.RepageObstacles(previous.Scene!, options))
            };
        }
        // Fit to a changed page before validating its plan: the old scale can be
        // larger than the new canvas even though the fitted scale is valid.
        PartPoses.Validate(source, poses, SheetLayout.Plan(geometry, options).Select(v => v.Id)
            .Concat(previous?.Details.Select(d => d.ViewId) ?? []));
        var result = PosePainting.RenderMain(source, craft, options, warnings, poses, token,
            composeAnnotations: previous == null);
        if (options.Drawing == null)
            result = PosePainting.RenderMain(source, craft, options with
            {
                Drawing = DrawingPlacements.FromScene(result.Scene!)
            }, warnings, poses, token, composeAnnotations: previous == null);
        return previous == null ? result : CompositionRegeneration.Rebuild(result, previous, token);
    }
}
