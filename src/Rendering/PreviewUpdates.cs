namespace Blueprint.Rendering;

sealed record PreviewRequest(PrintOptions Options, int Line, int Tip, bool StylePending);

// One settled request at a time; failed requests are not retried every frame.
sealed class PreviewUpdateGate
{
    PreviewRequest? pending, attempted;
    long changedAt;
    public void Reset()
    {
        pending = attempted = null;
    }
    public bool Ready(PreviewRequest request, long now, bool enabled, bool busy, bool interacting)
    {
        if (request != pending)
        {
            pending = request;
            changedAt = now;
        }
        if (!enabled || busy || interacting || now - changedAt < 650 || request == attempted)
            return false;
        attempted = request;
        return true;
    }
}

static class PreviewUpdates
{
    public static Sheet RenderBlank(Sheet previous, PrintOptions options, CancellationToken token)
    {
        var fresh = BlankDrawing.Create(previous.Title, options, token);
        var scene = previous.Scene!.WithBase(fresh.Rgb, fresh.Options);
        return AnnotationPainting.Compose(fresh with { Scene = scene }, previous.Labels, token);
    }

    public static bool CanRepaint(Sheet main, PrintOptions desired)
    {
        var old = main.Options;
        return main.MainInk != null && main.Scene != null
            && (old.Drawing != null || PageLayout.TitleBounds(old) == PageLayout.TitleBounds(desired)) && desired with
            {
                Theme = old.Theme,
                Grid = old.Grid,
                Archive = old.Archive,
                Emphasis = old.Emphasis,
                WearLevel = old.WearLevel,
                Zones = old.Zones,
                TitleBlock = old.TitleBlock,
                TitleBlockVisible = old.TitleBlockVisible,
                Brand = old.Brand,
                PartsTable = old.PartsTable,
                FlagTable = old.FlagTable
            } == old;
    }

    public static Sheet Repaint(Sheet main, PrintOptions options, CancellationToken token)
    {
        if (!CanRepaint(main, options))
            throw new InvalidOperationException("This change needs a geometry render.");
        var scene = main.Scene!;
        if (options.Drawing != null)
            DrawingPlacements.Validate(scene.Views, options);
        var colors = SheetLayout.Colors(options.Theme);
        var canvas = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
        foreach (var tile in main.MainInk!)
            for (int y = 0; y < tile.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < tile.Width; x++)
                    canvas.Ink((int)tile.Bounds.X + x, (int)tile.Bounds.Y + y, tile.Pixels[y * tile.Width + x] / 255f);
            }
        string title = PrintScale.Title(main.Title, options.TitleBlock);
        PageLayout.Decorate(canvas, title, options, PrintScale.Summary(scene.Views, options));
        NativeText.Apply(canvas.Pixels, options.Page.Width, options.Page.Height, canvas.Runs);
        return main with
        {
            Title = title,
            Options = options,
            Rgb = canvas.Pixels,
            PreviewRgba = Sheet.MakePreview(canvas.Pixels),
            Scene = scene.WithBase(canvas.Pixels, options),
            Labels = [],
            Details = []
        };
    }

    public static Sheet Render(Sheet previous, Sheet main, PrintOptions desired, CancellationToken token)
    {
        // Label/endpoint edits reuse the background. Assembly guide patterns
        // are in detail tiles, so a changed definition must rebuild those tiles.
        if (previous.Options == desired)
        {
            if (previous.Scene != null && !previous.Details.SequenceEqual(previous.Scene.Details.Select(d => d.Definition)))
                return ExplodedPainting.Compose(main, previous, previous.Details, previous.Labels, token,
                    tables: previous.Scene.Tables, captions: previous.Scene.Captions);
            return AnnotationPainting.Compose(previous, previous.Labels, token);
        }
        var options = desired;
        bool cached = CanRepaint(main, options);
        Sheet fresh;
        if (cached)
            fresh = Repaint(main, options, token);
        else
        {
            var source = previous.PoseSource ?? throw new InvalidOperationException("Generate PawPrint to capture this craft first.");
            var poses = previous.Scene!.Poses;
            if (previous.Options.Page != options.Page && options.Drawing != null)
            {
                var drawing = DrawingPlacements.Repage(previous.Scene, options);
                var envelope = poses.Length == 0 ? PartPoses.Exterior(source, source.Assembly)
                    : new Capture(drawing.Views.SelectMany(v => PartPoses.Exterior(source, PartPoses.Apply(source, poses, v.Id)).Meshes).ToArray());
                options = options with
                {
                    Drawing = options.FreeLayout ? drawing : DrawingPlacements.FitToPage(envelope, options, drawing,
                    CompositionRegeneration.RepageObstacles(previous.Scene, options))
                };
            }
            fresh = PosePainting.RenderMain(source, previous.Title, options, previous.Warnings, poses, token, composeAnnotations: false);
        }
        var result = CompositionRegeneration.Rebuild(fresh, previous, token, reuseDetailPixels: cached);
        return result with
        {
            RegeneratedBase = null,
            PoseMain = fresh,
            Notice = "Preview updated. Ctrl+Z undoes the change."
        };
    }
}

// Apply a pending print-wide style to detached composition data. Accepted
// scene/detail pixels stay untouched until the replacement render succeeds.
static class PrintStyles
{
    public static Sheet Apply(Sheet current, LeaderLine line, LeaderTip tip) => current.Scene == null ? current : current with
    {
        Labels = current.Labels.Select(l => l with { Line = line, Tip = tip, ManualPlacement = true }).ToArray(),
        Details = current.Details.Select(d => d with { GuideLine = line }).ToArray(),
        Scene = current.Scene.WithSpecs(current.Scene.Specs.Select(s => s with { Line = line, Tip = tip }).ToArray())
    };
}
