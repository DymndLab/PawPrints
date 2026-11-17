using Blueprint.Rendering;

static class InputChecks
{
    public static void Run(Action<bool, string> Check)
    {
        Check(ExportShortcut.Matches(true, true, 6, true, false), "Native Ctrl+Alt+P press opens exporter without frame polling");
        Check(ExportShortcut.Matches(true, true, 6 | 16 | 32, true, false), "Caps/Num Lock do not suppress export shortcut");
        Check(!ExportShortcut.Matches(true, false, 6, true, false), "Release/repeat do not activate export shortcut");
        Check(Enumerable.Range(0, 16).All(m => ExportShortcut.Matches(true, true, m, true, false) == (m == 6)), "Only Ctrl+Alt+P activates PawPrints; other modifier combinations remain native");
        Check(new[] { 0, 16, 32, 48 }.All(locks => ExportShortcut.Matches(true, true, 6 | locks, true, false) && !ExportShortcut.Matches(true, false, 6 | locks, true, false) && !ExportShortcut.Matches(true, true, 6 | locks, false, false) && !ExportShortcut.Matches(true, true, 6 | locks, true, true)), "Shortcut accepts lock keys and rejects repeat, flight mode and blocking dialogs");
        Check(!ExportShortcut.Matches(false, true, 6, true, false) && !ExportShortcut.Matches(true, true, 6, false, false) && !ExportShortcut.Matches(true, true, 6, true, true), "Unrelated keys, flight mode and blocking dialogs do not open exporter");
        Check(AnnotationShortcuts.Match(true, false, false, 0, true, false) == 1 && AnnotationShortcuts.Match(false, true, false, 2, true, false) == 2 && AnnotationShortcuts.Match(false, false, true, 2, true, false) == 3, "Delete, Ctrl+Z and Ctrl+Y map to annotation editing commands");
        Check(AnnotationShortcuts.Match(false, true, false, 2, true, true) == 0 && AnnotationShortcuts.Match(true, false, false, 0, false, false) == 0, "Text entry and unfocused print editor retain their native keyboard behavior");
        Check(AnnotationShortcuts.Match(false, true, false, 2 | 16 | 32, true, false) == 2 && AnnotationShortcuts.Match(false, true, false, 6, true, false) == 0, "Annotation shortcuts accept lock keys but reject unrelated modifier combinations");
        Check(new[] { 0, 16, 32, 48 }.All(m => AnnotationShortcuts.Match(false, false, false, m, true, false, true) == 4), "F flips references with or without Caps/Num Lock");
        Check(Enumerable.Range(1, 15).All(m => AnnotationShortcuts.Match(false, false, false, m, true, false, true) == 0) && AnnotationShortcuts.Match(false, false, false, 0, true, true, true) == 0 && AnnotationShortcuts.Match(false, false, false, 0, false, false, true) == 0, "Flip shortcut leaves modified F, text entry and unfocused editor input untouched");
        Check(PreviewNavigation.Zoom(1, 1) > 1 && Math.Abs(PreviewNavigation.Zoom(PreviewNavigation.Zoom(1, 1), -1) - 1) < .00001f && PreviewNavigation.Zoom(.5f, -8) == .5f && PreviewNavigation.Zoom(6, 8) == 6, "Mouse-wheel zoom reverses cleanly and stays within supported bounds");
        var scrollStart = new System.Numerics.Vector2(100, 200);
        var imagePoint = new System.Numerics.Vector2(400, 500);
        var zoomScroll = PreviewNavigation.ZoomScroll(scrollStart, imagePoint, 1.5f);
        Check((imagePoint * 1.5f - zoomScroll) == (imagePoint - scrollStart), "Wheel zoom preserves the image point under the cursor when unclamped");
        Check(PreviewNavigation.PanScroll(new(100, 200), new(400, 500), new(430, 480)) == new System.Numerics.Vector2(70, 220) && PreviewNavigation.PanScroll(new(10, 10), new(0, 0), new(50, 50)) == System.Numerics.Vector2.Zero, "Grab-hand panning follows mouse movement and respects the page scroll origin");
        // Simulate a toolbar row changing the canvas fit during an isometric click.
        // A stationary pointer consequently moves >4 screen-equivalent pixels on paper.
        var stationary = new System.Numerics.Vector2(850, 328);
        var oldPaper = (stationary - new System.Numerics.Vector2(410, 240)) / (820f / 2400);
        var reflowPaper = (stationary - new System.Numerics.Vector2(410, 250)) / (810f / 2400);
        var clickGesture = new LabelPlacementGesture(stationary, true);
        Check(System.Numerics.Vector2.Distance(oldPaper, reflowPaper) * (810f / 2400) > 4 &&
            !clickGesture.Update(stationary, false, false, true) && !clickGesture.Held,
            "Canvas reflow under a stationary pointer cannot commit a clicked part label");
        Check(!clickGesture.Update(new(1020, 460), false, false, true) && clickGesture.Update(new(1020, 460), true, true, true),
            "Click placement follows the pointer and commits only on the destination click");
        var dragGesture = new LabelPlacementGesture(stationary, true);
        Check(!dragGesture.Update(new(1020, 460), true, false, true) && dragGesture.Update(new(1020, 460), false, false, true),
            "A real screen-space drag commits on release, never while held");
        var jitterGesture = new LabelPlacementGesture(stationary, true);
        Check(!jitterGesture.Update(stationary + new System.Numerics.Vector2(2, 1), false, false, true),
            "Small physical click jitter keeps the label awaiting a destination");
        var menuGesture = new LabelPlacementGesture(stationary, false);
        Check(!menuGesture.Update(new(1020, 460), false, false, true) && !menuGesture.Update(new(1020, 460), true, true, false) && menuGesture.Update(new(1020, 460), true, true, true),
            "Context placement ignores menu release and requires a subsequent paper click");
        var name = PrintFiles.SafeStem("../CON:<craft>?/\\\n");
        Check(!name.Any(c => Path.GetInvalidFileNameChars().Contains(c)), "Export filename removes path/control characters");
        var cancelled = new CancellationToken(true);
        try
        {
            new RawTexture(new("test", 1, 1), 37, [1, 2, 3, 255]).Decode(cancelled);
            throw new Exception("Cancellation ignored");
        }
        catch (OperationCanceledException) { Check(true, "Texture decode respects cancellation"); }
        var rgba = new RawTexture(new("test", 1, 1), 37, [10, 20, 30, 255]).Decode(default);
        Check(rgba.Pixels.SequenceEqual(new byte[] { 30, 20, 10, 255 }), "RGBA -> BGRA channel order");
        // Single constant-color BC5 block: R=200, G=100. Normal decoding must preserve both channels.
        var bc5 = new byte[16];
        bc5[0] = 200;
        bc5[1] = 200;
        bc5[8] = 100;
        bc5[9] = 100;
        var decoded = new RawTexture(new("bc5", 4, 4), 141, bc5).Decode(default);
        Check(Enumerable.Range(0, 16).All(i => decoded.Pixels[i * 4 + 2] == 200 && decoded.Pixels[i * 4 + 1] == 100), "BC5 authored normal channels survive decoding");
    }
}
