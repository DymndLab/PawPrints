namespace Blueprint.Rendering;

static class BlankDrawing
{
    public static Sheet Create(string title, PrintOptions appearance, CancellationToken token, bool composeWear = true)
    {
        var options = appearance with { FreeLayout = true, Drawing = new(100, []), PartsTable = false, FlagTable = false };
        token.ThrowIfCancellationRequested();
        var colors = SheetLayout.Colors(options.Theme);
        var canvas = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
        PageLayout.Decorate(canvas, title, options);
        NativeText.Apply(canvas.Pixels, options.Page.Width, options.Page.Height, canvas.Runs);
        token.ThrowIfCancellationRequested();
        var scene = new RenderScene(canvas.Pixels, Enumerable.Repeat(-1, checked(options.Page.Width * options.Page.Height)).ToArray(), [], [], options: options)
            { BaseLettering = WearPlacement.Lettering(canvas.Runs, options.Page) }.WithDrawing(options.Drawing);
        bool finish = composeWear && options.Archive && options.WearLevel > 0;
        var sheet = new Sheet(title, options, canvas.Pixels, finish ? [] : Sheet.MakePreview(canvas.Pixels), []) { Scene = scene };
        return finish ? AnnotationPainting.Compose(sheet, [], token) : sheet;
    }
}
