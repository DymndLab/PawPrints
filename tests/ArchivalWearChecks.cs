using System.Numerics;
using System.Text.Json.Nodes;
using Blueprint.Rendering;

static class ArchivalWearChecks
{
    public static void Run(Action<bool, string> check)
    {
        Distribution(check);
        Raster(check);
        EditableDrawing(check);
    }

    static void Distribution(Action<bool, string> check)
    {
        int[] lower = [0, 1, 1, 2, 2, 3], upper = [0, 1, 2, 2, 3, 4];
        int[] any = [0, 5, 15, 40, 65, 100], two = [0, 0, 5, 10, 25, 40], rings = [0, 0, 5, 8, 15, 20];
        Vector2[] fields = [new(.2f,.1f), new(.8f,.2f), new(.25f,.8f), new(.8f,.9f)];
        const int samples = 12000;
        var variations = new HashSet<(WearKind, bool, bool)>();
        var finishes = new HashSet<WearKind>();
        bool tip = false, full = false, partial = false;
        for (int level = 0; level <= 5; level++)
        {
            int hasTear = 0, hasTwo = 0, hasRing = 0;
            bool placements = true;
            var options = new PrintOptions(Archive: true, WearLevel: level);
            for (uint seed = 1; seed <= samples; seed++)
            {
                var marks = WearPlacement.Resolve(options with { WearSeed = seed }, "Fixture", fields, []);
                var tears = marks.Where(m => WearPlacement.Tear(m.Kind)).ToArray();
                var rubs = marks.Where(m => m.Kind is >= WearKind.SmallScuff and <= WearKind.BroadFade).ToArray();
                hasTear += tears.Length > 0 ? 1 : 0;
                hasTwo += tears.Length == 2 ? 1 : 0;
                hasRing += marks.Count(m => m.Kind == WearKind.CoffeeRing);
                var anchors = rubs.Select(m => new Vector2(m.X,m.Y)).ToArray();
                placements &= marks.All(WearPlacement.Valid) && rubs.Length >= lower[level] && rubs.Length <= upper[level]
                    && anchors.All(fields.Contains) && anchors.Distinct().Count() == rubs.Length
                    && tears.Length <= 2 && tears.Select(m=>m.Edge).Distinct().Count() == tears.Length
                    && tears.Select(m=>m.Kind).Distinct().Count() == tears.Length;
                foreach (var rub in rubs) finishes.Add(rub.Kind);
                foreach (var m in tears)
                {
                    variations.Add((m.Kind, m.FlipX, m.FlipY));
                    tip |= m.Depth < .22f;
                    partial |= m.Depth > .22f && m.Depth < .94f;
                    full |= m.Depth == 1;
                }
            }
            check(placements, $"Level {level} respects rub counts, printed anchors and distinct tear edges and shapes");
            bool Matches(int count, int expected) => expected is 0 or 100
                ? count == samples * expected / 100 : Math.Abs(count * 100d / samples - expected) < 1.5;
            check(Matches(hasTear, any[level]) && Matches(hasTwo, two[level]) && Matches(hasRing, rings[level]),
                $"Level {level} any tear, two tears and ring across {samples} seeds: {hasTear}, {hasTwo}, {hasRing}");
        }
        check(variations.Count == 16 && finishes.Count == 4 && tip && partial && full,
            "All four tear shapes mirror both ways, all depth bands occur, and all four rub finishes appear");
    }

    static void Raster(Action<bool, string> check)
    {
        const int width = 320, height = 200;
        var page = new PageSize(width,height);
        var snapped = WearPlacement.Manual(WearKind.CurvedTear, new(275, 192), page, 1, 0, true, 19);
        var free = WearPlacement.Manual(WearKind.CurvedTear, new(275, 130), page, 1, 0, false, 19);
        check(snapped.Edge == 3 && snapped.Y == 1 && free.Edge == -1 && free.Y == .65f,
            "The placement tool snaps a tear to the nearest edge and supports free placement");
        for (int theme = 0; theme < 4; theme++)
        {
            bool allCores = true;
            foreach (WearKind kind in Enum.GetValues<WearKind>().Where(WearPlacement.Tear))
            {
                var mark = WearPlacement.Stamp(kind) with { X=0, Y=.5f, Width=.4f, Edge=0 };
                var black = new byte[width * height * 3];
                var white = Enumerable.Repeat((byte)255,black.Length).ToArray();
                WearPainting.Apply(black,width,height,theme,[mark],default);
                WearPainting.Apply(white,width,height,theme,[mark],default);
                int covered = 0;
                for (int i=0; i<black.Length; i+=3)
                    if (black.AsSpan(i,3).SequenceEqual(white.AsSpan(i,3))) covered++;
                allCores &= covered > 8;
            }
            check(allCores, $"Solid fibers on all four tear shapes cover light and dark ink on theme {theme}");
        }
        foreach (var kind in Enum.GetValues<WearKind>())
        {
            var preview = WearPainting.Preview(WearPlacement.Stamp(kind), 0);
            check(preview.Rgba.Where((_, i) => i % 4 == 3).Any(a => a > 12), $"{kind} has a visible placement preview");
        }
    }

    static void EditableDrawing(Action<bool, string> check)
    {
        var options = new PrintOptions(Orientation: 3, CustomWidth: 1280, CustomHeight: 720,
            Theme: 3, Archive: true, WearLevel: 5, WearSeed: 49, TitleBlock: new(Title: "Survey"));
        var sheet = BlankDrawing.Create("Survey", options, default);
        var scene = sheet.Scene!;
        check(sheet.AppliedWear.Length >= 4 && !sheet.Rgb.AsSpan().SequenceEqual(scene.BaseRgb),
            "A fresh level-five drawing displays wear and retains a clean base for editing");
        var bounds = CompanyTitleBlock.DateArea(options);
        var page = options.Page;
        var mark = WearPlacement.Manual(WearKind.BroadFade, bounds.Center, page, 1.5f, 0, false, 2);
        var document = new AnnotationDocument(scene);
        document.Rebind(scene.WithWear([mark]), [], []);
        document.Undo();
        check(document.Scene.WearMarks.Length == 0, "Undo removes the last placed wear mark");
        document.Redo();
        check(document.Scene.WearMarks.SequenceEqual([mark]), "Redo restores the same size, position and finish");
        var accepted = AnnotationPainting.Compose(sheet with { Scene = document.Scene }, [], default);
        var project = DrawingProject.Capture(accepted, document, null);
        var bytes = DrawingProjectCodec.Write(project);
        var reopened = DrawingProjectCodec.Read(bytes).Render(null,null,null,default);
        check(reopened.AppliedWear.SequenceEqual(accepted.AppliedWear) && reopened.Rgb.AsSpan().SequenceEqual(accepted.Rgb),
            "Saving and reopening preserves automatic and manual wear pixel for pixel");
        var cleanExport = CompanyTitleBlock.StampExportDate(accepted with { Rgb = scene.BaseRgb, AppliedWear = [] }, "2028-02-29");
        WearPainting.Apply(cleanExport, page.Width, page.Height, options.Theme, accepted.AppliedWear, default);
        var export = CompanyTitleBlock.StampExportDate(accepted, "2028-02-29");
        check(export.AsSpan().SequenceEqual(cleanExport), "Export date redraw keeps the original wear and applies it exactly once");
        var json = JsonNode.Parse(bytes)!;
        var content = json["Content"]!.AsObject();
        content.Remove("WearMarks");
        content["Options"]!.AsObject().Remove("WearSeed");
        var old = DrawingProjectCodec.Read(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
        var fallback = WearPlacement.Resolve(old.Content.Options, old.Title, scene.BaseLettering, []);
        check(old.Content.WearMarks.Length == 0 && old.Content.Options.WearSeed == 0 &&
            fallback.SequenceEqual(WearPlacement.Resolve(old.Content.Options, old.Title, scene.BaseLettering, [])),
            "Older editable files load with deterministic automatic wear");
        check(WearPlacement.Resolve(options with { Archive=false }, "Survey", [], [mark]).SequenceEqual([mark]),
            "Manual wear remains on a drawing with automatic wear off");

        var tear = WearPlacement.Manual(WearKind.CurvedTear, new(310, page.Height - 2), page, 1, 0, true, 19);
        var ring = WearPlacement.Manual(WearKind.CoffeeRing, new(140, 160), page, 1, 0, false, 2);
        var manual = new[] { mark, tear, mark, ring };
        var several = AnnotationPainting.Compose(sheet with { Scene = scene.WithWear(manual) }, [], default);
        var before = DrawingAcceptance.Prepare(several, null, false, false);
        var shortened = before.Document.Scene.WithoutWearMark(2);
        var rendered = AnnotationPainting.Compose(several with { Scene = shortened }, [], default);
        var after = DrawingAcceptance.Prepare(rendered, before.Document, true, false);
        check(before.Document.Scene.WearMarks.SequenceEqual(manual) &&
            after.Document.Scene.WearMarks.SequenceEqual([mark, tear, ring]) &&
            after.Sheet.AppliedWear.SequenceEqual(several.AppliedWear.Where((_, index) => index != several.AppliedWear.Length - 2)),
            "Removing one duplicate mark preserves the earlier mark, other placements and automatic wear");
        after.Document.Undo();
        check(after.Document.Scene.WearMarks.SequenceEqual(manual), "Undo restores the removed wear mark in its original order");
        after.Document.Redo();
        var reopenedRemoval = DrawingProjectCodec.Read(DrawingProjectCodec.Write(DrawingProject.Capture(after.Sheet, after.Document, null)))
            .Render(null, null, null, default);
        check(reopenedRemoval.Scene!.WearMarks.SequenceEqual([mark, tear, ring]) && reopenedRemoval.Rgb.AsSpan().SequenceEqual(after.Sheet.Rgb),
            "Redo and drawing reopen preserve the removal and its rendered appearance");
    }
}
