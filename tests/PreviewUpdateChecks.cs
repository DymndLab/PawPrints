using Blueprint.Rendering;
using System.Numerics;

static class PreviewUpdateChecks
{
    public static void Run(Action<bool, string> check)
    {
        var options = new PrintOptions(Drawing: new(100, []), TitleBlock: new(Title: "Fixture"));
        var request = new PreviewRequest(options, 0, 0, false);
        var gate = new PreviewUpdateGate();
        check(!gate.Ready(request, 0, true, false, false) && !gate.Ready(request, 649, true, false, false),
            "Live updates wait for a settled edit instead of rendering on every keystroke");
        check(gate.Ready(request, 650, true, false, false) && !gate.Ready(request, 2000, true, false, false),
            "One settled request runs once, including after a failed render");
        var changed = request with
        {
            Options = options with
            {
                Theme = 2
            }
        };
        check(!gate.Ready(changed, 2100, true, false, false) && !gate.Ready(changed, 3000, false, false, false),
            "Manual mode keeps changed settings pending without a background job");
        check(!gate.Ready(changed, 3000, true, true, false) && !gate.Ready(changed, 3000, true, false, true)
            && gate.Ready(changed, 3001, true, false, false),
            "Live queue waits for the current job and slider release, then submits the latest values");
        gate.Reset();
        check(!gate.Ready(request, 4000, true, false, false) && gate.Ready(request, 4650, true, false, false),
            "Explicit reset allows a deliberate retry after settings or live mode change");
        var newest = request with
        {
            Options = options with
            {
                Theme = 3
            }
        };
        check(!gate.Ready(changed, 5000, true, false, false) && !gate.Ready(newest, 5400, true, false, false)
            && !gate.Ready(newest, 6000, true, false, false) && gate.Ready(newest, 6050, true, false, false),
            "Rapid settings edits collapse to the latest settled snapshot");
        int width = options.Page.Width, height = options.Page.Height;
        var bounds = new Box(700, 500, 80, 80);
        var ink = new byte[80 * 80];
        for (int i = 0; i < 80; i++)
        {
            ink[i * 80 + i] = 255;
            ink[i * 80 + 79 - i] = 180;
        }
        var view = new ViewRegion(0, "FRONT", bounds, new(80, 80, 40, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ));
        Sheet Build(PrintOptions o)
        {
            var colors = SheetLayout.Colors(o.Theme);
            var c = new Canvas(width, height, colors.Paper, colors.Ink, o);
            for (int y = 0; y < 80; y++)
                for (int x = 0; x < 80; x++)
                    c.Ink((int)bounds.X + x, (int)bounds.Y + y, ink[y * 80 + x] / 255f);
            PageLayout.Decorate(c, "Fixture", o, PrintScale.Summary([view], o));
            NativeText.Apply(c.Pixels, width, height, c.Runs);
            var scene = new RenderScene(c.Pixels, Enumerable.Repeat(-1, width * height).ToArray(), [], [view], options: o);
            return new("Fixture", o, c.Pixels, Sheet.MakePreview(c.Pixels), [])
            {
                MainInk = [new(bounds, 80, 80, ink)],
                Scene = scene
            };
        }
        var original = Build(options);
        var originalPixels = original.Rgb.ToArray();
        var times = new List<long>();
        foreach (int theme in Enumerable.Range(0, 4))
        {
            var appearance = options with
            {
                Theme = theme,
                Grid = false,
                Zones = false,
                Archive = true,
                WearLevel = 2,
                TitleBlock = options.TitleBlock! with
                {
                    Title = "New title",
                    Subtitle = "Live",
                    Revision = "2"
                }
            };
            check(PreviewUpdates.CanRepaint(original, appearance), "Appearance/title edits reuse captured line art: " + theme);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var repaint = PreviewUpdates.Repaint(original, appearance, default);
            times.Add(watch.ElapsedMilliseconds);
            var direct = Build(appearance);
            check(repaint.Rgb.SequenceEqual(direct.Rgb) && repaint.Title == "New title",
                "Cached repaint matches directly painted pixels for theme/title/grid/wear/zones: " + theme);
            check(ReferenceEquals(repaint.Scene!.Owners, original.Scene!.Owners) && ReferenceEquals(repaint.MainInk, original.MainInk),
                "Appearance repaint retains picking geometry and line-art buffers: " + theme);
        }
        Console.WriteLine("Cached appearance repaint (2400x3000, synthetic, excludes GPU upload): " + string.Join(", ", times) + " ms");
        check(original.Rgb.SequenceEqual(originalPixels), "Cached repaint never mutates the currently displayed sheet");
        check(!PreviewUpdates.CanRepaint(original, options with
        {
            Orientation = 2
        })
            && !PreviewUpdates.CanRepaint(original, options with
            {
                Captions = false
            })
            && !PreviewUpdates.CanRepaint(original, options with
            {
                Drawing = new(50, [])
            }),
            "Paper, camera/scale and caption-layout changes require geometry rendering");
        var text = FreeTextPainting.Fit(new(1, "A boxed note", new(200, 200, 400, 150), Outline: true), options);
        var composed = AnnotationPainting.Compose(original with
        {
            Scene = original.Scene!.WithFreeText([text])
        }, [], default);
        var target = options with
        {
            Theme = 1,
            TitleBlock = options.TitleBlock! with
            {
                Revision = "3"
            }
        };
        var updated = PreviewUpdates.Render(composed, original, target, default);
        check(updated.Scene!.FreeText.Single() == text && updated.Options == target && updated.RegeneratedBase == null,
            "Live repaint preserves text placement and formatting without clearing document history");
        var doc = new AnnotationDocument(composed.Scene!);
        doc.Rebind(updated.Scene!, updated.Labels, updated.Details);
        doc.Undo();
        check(doc.Scene.Options == options && doc.Scene.FreeText.Single() == text,
            "Undo restores appearance and title settings with the composition");
        doc.Redo();
        check(doc.Scene.Options == target, "Redo restores automatically applied settings");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            PreviewUpdates.Repaint(original, target, canceled.Token);
            throw new Exception("Canceled repaint returned a partial sheet");
        }
        catch (OperationCanceledException) { check(original.Rgb.SequenceEqual(originalPixels), "Cancellation preserves the previous sheet"); }
    }
}
