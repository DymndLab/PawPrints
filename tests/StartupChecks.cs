using Blueprint.Rendering;
using System.Buffers.Binary;

static class StartupChecks
{
    static void CheckSnapGrid(Action<bool, string> check, PoseSource source, Capture geometry, PrintOptions options)
    {
        source = source with { Assembly = source.Assembly with
        {
            Topology = new(new SourceBinding(Guid.NewGuid(), 1), [new SourcePart(1, "fixture", null, false)], [])
        } };
        static int Cell(SetupViewGrid grid, int camera) => grid.Slots.Single(s => s.Camera == camera).Cell;
        foreach (int angle in new[] { 1, 3 })
        {
            var empty = SetupViewGrid.Empty(angle);
            var rear = empty.Arrange([5], 2, 1, angle, 5)!;
            check(rear.Arrange([5, 3], 2, 1, angle, 5) == null && rear.Arrange([5, 7], 2, 1, angle, 5) == null &&
                rear.Slots.Length == 1 && rear.Arrange([5, 3], 2, 2, angle, 5) != null,
                $"Snap grid retains Rear when Top or Bottom cannot fit one row under angle {angle}");
            var pair = empty.Arrange([1, 2], 2, 2, angle, 1)!;
            int front = Cell(pair, 1), side = Cell(pair, 2);
            var group = pair.Move(1, front + 2, out bool unlink)!;
            check(!unlink && Cell(group, 1) == front + 2 && Cell(group, 2) == side + 2 &&
                group.ToTemplate().Layout.Groups.Single().Members.Length == 2 &&
                group.Move(1, side + 2, out _) == null, "Dragging the primary translates the group without wrapping an edge");
            var detached = group.Move(2, side, out unlink)!;
            check(unlink && Cell(group, 2) == side + 2 && group.Slots.All(s => !s.Detached) &&
                Cell(detached, 2) == side && detached.Slots.Single(s => s.Camera == 2).Detached &&
                detached.ToTemplate().Layout.Views.All(v => v.Parent == null && v.Group == null),
                "An off-axis secondary drop leaves the accepted state untouched until confirmation");
            check(group.ProjectionGuides().Single() is { From.Camera: 1, To.Camera: 2 } &&
                !detached.ProjectionGuides().Any() && !rear.ProjectionGuides().Any(),
                $"Angle {angle} preview guides move with the group and disappear after unlinking");
            var projected = empty.Arrange([1, 2, 3, 4, 6], 3, 2, angle, 1)!;
            var guides = projected.ProjectionGuides().ToArray();
            int outward = angle == 3 ? 1 : -1;
            check(guides.Length == 3 && guides.All(g => g.From.Camera == 1) &&
                guides.Single(g => g.To.Camera == 3) is var topGuide &&
                (topGuide.From.Cell / 3 - topGuide.To.Cell / 3) * outward > 0 &&
                guides.Single(g => g.To.Camera == 6) is var rightGuide &&
                (rightGuide.To.Cell % 3 - rightGuide.From.Cell % 3) * outward > 0,
                $"Angle {angle} guides connect the correct horizontal and vertical faces and skip Isometric");
            var extra = detached.Arrange([1, 2, 4], 2, 2, angle, 1)!;
            int iso = Cell(extra, 4);
            var swapped = extra.Move(2, iso, out unlink)!;
            check(!unlink && Cell(swapped, 4) == side && Cell(swapped, 2) == iso &&
                swapped.Move(4, Cell(swapped, 1), out _) == null &&
                swapped.Arrange([1, 2, 4], 2, 2, angle, 2)!.ToTemplate().Layout.Groups.Single().Members.Length == 2,
                "Independent cells swap, preserve the primary, and can be promoted to rejoin the group");
            var three = empty.Arrange([2, 3, 4], 3, 3, angle, 2)!;
            var top = three.Slots.Single(s => s.Camera == 3);
            var target = Enumerable.Range(0, 9).Select(k => (Plan: three.Move(3, k, out var ask), Ask: ask))
                .First(p => p.Ask).Plan!;
            var saved = target.ToTemplate();
            check(saved.Layout.Views.Single(v => v.Camera == 3).Axes == three.Axes(top) &&
                target.Arrange([2, 3, 4, 8], 3, 3, angle, 2)!.Slots.Where(s => s.Camera != 8)
                    .All(s => s.Cell == Cell(target, s.Camera)), "Adding another view preserves hand placement and the detached face orientation");

            var optionsForGrid = options with { ProjectionAngle = angle, Captions = false, PartsTable = false, FlagTable = false };
            var sheet = StarterDrawings.Render(source, geometry, "Snap grid", optionsForGrid, [], saved, true, default);
            var work = PageLayout.Drawing(optionsForGrid);
            var page = optionsForGrid.Page;
            var drawing = sheet.Scene!.Drawing!;
            check(target.Slots.All(s =>
            {
                var v = drawing.Views.Single(v => v.Camera == s.Camera);
                return Math.Abs(v.X * page.Width - (work.X + (s.Cell % target.Columns + .5f) / target.Columns * work.Width)) < .1f &&
                    Math.Abs(v.Y * page.Height - (work.Y + (s.Cell / target.Columns + .5f) / target.Rows * work.Height)) < .1f;
            }) && drawing.Views.Single(v => v.Camera == 3).Parent == null &&
                DrawingProjectCodec.Read(DrawingProjectCodec.Write(DrawingProject.Capture(sheet, new AnnotationDocument(sheet.Scene), null)))
                    .Content.Drawing!.Views.Single(v => v.Camera == 3).Axes == three.Axes(top),
                "Start editing renders the accepted cells and independent axes, and the editable project retains them");
            var longRow = empty.Arrange([1, 2], 4, 2, angle, 1)!;
            int far = Cell(longRow, 2) + (angle == 3 ? -1 : 1);
            if (far < 0) { longRow = longRow.Move(1, Cell(longRow, 1) + 1, out _)!; far = 0; }
            var onAxis = longRow.Move(2, far, out unlink);
            check(onAxis != null && !unlink && onAxis.ToTemplate().Layout.Groups.Length == 1, "An on-axis secondary drag keeps its link");
            var all = empty.Arrange(Enumerable.Range(0, 9).ToArray(), 0, 0, angle, null);
            check(all?.Slots.Length == 9 && all.Slots.Select(s => s.Cell).Distinct().Count() == 9 &&
                all.ToTemplate().Layout.Views.Single(v => v.Exploded).Id == 1001, "Automatic sizing fits every view including Exploded");
            var largest = all!.Arrange(Enumerable.Range(0, 9).ToArray(), 5, 4, angle, null);
            check(largest is { Columns: 5, Rows: 4 } && largest.Slots.Select(s => s.Cell).Distinct().Count() == 9 &&
                largest.ToTemplate().Layout.Views.Length == 9,
                $"The 5 x 4 picker limit retains all views under angle {angle}");
            var single = largest!.Arrange([1], 1, 1, angle, 1);
            check(single is { Columns: 1, Rows: 1, Primary: 1 } && Cell(single, 1) == 0 &&
                largest.Arrange([1, 2], 1, 1, angle, 1) == null,
                $"The 1 x 1 picker fits one view and rejects a second under angle {angle}");
        }
    }

    public static void Run(Action<bool, string> check, string output)
    {
        var sameRay = new SetupViewGrid(4, 2, 3, 1, [new(5, 3), new(2, 4, Detached: true), new(1, 0), new(6, 1)]);
        check(sameRay.ProjectionGuides().Select(g => (g.From.Camera, g.To.Camera)).SequenceEqual(new[] { (1, 6), (6, 5) }),
            "Opposite faces link through the nearest view on their ray and skip detached views");
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
        CheckSnapGrid(check, source, geometry, blank.Options);
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

        check(Enumerable.Range(0, 3).All(i =>
        {
            var wide = ImagePresets.Size(0, i);
            var tall = ImagePresets.Size(1, i);
            var square = ImagePresets.Size(2, i);
            return wide.Width > wide.Height && tall == new PageSize(wide.Height, wide.Width) &&
                square.Width == square.Height && ImagePresets.Labels(2)[i] == $"{square.Width} x {square.Height}" &&
                ImagePresets.OrientationOf(wide) == 0 && ImagePresets.OrientationOf(tall) == 1 &&
                ImagePresets.OrientationOf(square) == 2 && ImagePresets.IndexOf(2, square) == i;
        }), "The size menu matches the output dimensions for each image orientation");
        check(ImagePresets.OrientationOf(new(1280, 1600)) == 1 && ImagePresets.IndexOf(1, new(1280, 1600)) == -1,
            "A previously accepted non-preset size retains its own orientation and dimensions");
        var squareSize = ImagePresets.Size(2, 0);
        var squareSheet = BlankDrawing.Create("Square", blank.Options with
        {
            CustomWidth = squareSize.Width, CustomHeight = squareSize.Height
        }, default);
        var squarePng = File.ReadAllBytes(PrintFiles.Save(squareSheet, output));
        check(squareSheet.PixelWidth == squareSheet.PixelHeight &&
            BinaryPrimitives.ReadInt32BigEndian(squarePng.AsSpan(16, 4)) == squareSize.Width &&
            BinaryPrimitives.ReadInt32BigEndian(squarePng.AsSpan(20, 4)) == squareSize.Height,
            "Square setup produces a square drawing and writes a 1:1 PNG");

        var freshOptions = blank.Options with { TitleBlockVisible = false, PartsTable = true, FlagTable = true };
        var selected = StarterDrawings.FromSelection([0, 2]);
        var drawing = StarterDrawings.Render(source, geometry, "Fixture", freshOptions, [], selected, true, default);
        check(drawing.Scene!.Drawing!.Views.Select(v => v.Camera).SequenceEqual(new[] { 0, 2 }) &&
            drawing.Options.PartsTable && drawing.Options.FlagTable && !drawing.Options.TitleBlockVisible,
            "Select Views renders exactly the chosen cameras and tables with the current title visibility");
        foreach (int angle in new[] { 1, 3 })
        {
            check(StarterDrawings.TryGrid([1, 2], angle, 1, 2, 1, out var pair, out _) &&
                pair!.Layout.Views.Length == 2 && pair.Layout.Views[1].Parent == pair.Layout.Views[0].Id &&
                (pair.Layout.Views[1].Bounds.Center.X < pair.Layout.Views[0].Bounds.Center.X) == (angle == 3),
                $"Angle {angle} fills a 2 x 1 grid with linked Front and Left side");
            check(!StarterDrawings.TryGrid([1, 2, 3], angle, 1, 2, 1, out var rejected, out var warning) &&
                rejected == null && warning == "You do not have space for additional views.",
                $"Angle {angle} rejects Top with the full-grid message");
            check(StarterDrawings.TryGrid([1, 2, 3], angle, 1, 2, 2, out var larger, out _) &&
                larger!.Layout.Views[2].Parent == larger.Layout.Views[0].Id &&
                (larger.Layout.Views[2].Bounds.Center.Y < larger.Layout.Views[0].Bounds.Center.Y) == (angle == 3),
                $"Angle {angle} accepts Top on a larger grid with its projection link");
            check(!StarterDrawings.TryGrid([1, 3], angle, 1, 2, 1, out _, out var shape) &&
                shape != StarterDrawings.GridFullMessage &&
                StarterDrawings.TryGrid([1, 3], angle, 1, 1, 2, out _, out _),
                $"Angle {angle} distinguishes projection shape from exhausted capacity");

            check(StarterDrawings.TryGrid([2, 3], angle, null, 2, 1, out var inferred, out _) &&
                inferred!.Layout.Views.Single(v => v.Id == inferred.Layout.Groups.Single().Primary).Camera == 3 &&
                !StarterDrawings.TryGrid([2, 3], angle, 2, 2, 1, out _, out _),
                $"Angle {angle} infers Top for a horizontal Left side pair and respects an explicit Left side choice");
            check(StarterDrawings.ChoosePrimary([1, 5, 2]) == 2 &&
                StarterDrawings.ChoosePrimary([1, 5, 2], 5) == 5 && StarterDrawings.ChoosePrimary([0, 4, 8]) == null,
                "Automatic primary prefers the view adjacent to both other faces and leaves standalone views unmarked");

            foreach (int camera in new[] { 3, 7 })
            {
                string side = (camera == 3) == (angle == 3) ? "above" : "below";
                string expected = $"{DrawingPlacements.Names[camera]} projects {side} Rear. Choose a grid with at least 2 rows.";
                check(StarterDrawings.ViewAdditionIssue([5], camera, angle, 5, 5, 2, 1) == expected &&
                    StarterDrawings.ViewAdditionIssue([5], camera, angle, 5, 5, 2, 2) == null,
                    $"Angle {angle} explains the unavailable {DrawingPlacements.Names[camera]} projection from Rear and enables it with a second row");
            }
            check(new[] { 2, 6, 4, 0, 8, 5 }.All(camera =>
                    StarterDrawings.ViewAdditionIssue([5], camera, angle, 5, 5, 2, 1) == null) &&
                StarterDrawings.ViewAdditionIssue([5, 2], 3, angle, 5, 5, 2, 1) == StarterDrawings.GridFullMessage &&
                StarterDrawings.ViewAdditionIssue([5, 2], 2, angle, 5, 5, 2, 1) == null &&
                StarterDrawings.ViewAdditionIssue([2], 3, angle, null, 2, 2, 1) == null,
                $"Angle {angle} keeps fitting views, selected rows and inferred parents available, and reports a full grid separately");

            check(StarterDrawings.ViewAdditionIssue([2], 3, angle, 2, 2, 2, 1) != null &&
                StarterDrawings.PrimaryChoiceIssue([2], 3, angle, 2, 1) == null &&
                StarterDrawings.PrimaryChoiceIssue([2, 3], 2, angle, 2, 1) != null,
                $"Angle {angle} checks a new primary independently from adding the same view as a secondary");
            check(StarterDrawings.PrimaryChoiceIssue([5, 2], 3, angle, 2, 1) == StarterDrawings.GridFullMessage &&
                StarterDrawings.PrimaryChoiceIssue([5, 2], 2, angle, 2, 1) == null &&
                new[] { 0, 4, 8 }.All(camera => StarterDrawings.PrimaryChoiceIssue([], camera, angle, 0, 0) != null),
                $"Angle {angle} allows promotion in a full grid, blocks extra views, and explains standalone marks");

            var sidePrimary = StarterDrawings.FromSelection([1, 2, 3], angle, primaryCamera: 2).Layout;
            var root = sidePrimary.Views.Single(v => v.Camera == 2);
            var parentOnly = sidePrimary with { Groups = [], Views = [.. sidePrimary.Views.Select(v => v with { Group = null })] };
            check(StarterDrawings.PrimaryViewIds(parentOnly).SetEquals([root.Id]) &&
                StarterDrawings.PrimaryViewIds(parentOnly with { Views = [.. parentOnly.Views.Select(v => v with { Parent = null })] }).Count == 0,
                "Template marks use parent links when a group record is absent and leave independent views alone");
            check(sidePrimary.Groups.Single().Primary == root.Id && sidePrimary.Views.Where(v => v.Id != root.Id)
                .All(v => v.Parent == root.Id && v.ProjectionAngle == angle && v.RollDirection >= 0),
                $"Select Views makes Left side the actual parent under angle {angle}");

            var sheetOptions = blank.Options with { ProjectionAngle = angle };
            var colors = SheetLayout.Colors(sheetOptions.Theme);
            var conventionCanvas = new Canvas(sheetOptions.Page.Width, sheetOptions.Page.Height, colors.Paper, colors.Ink, sheetOptions);
            PageLayout.Decorate(conventionCanvas, "Fixture", sheetOptions);
            check(conventionCanvas.Runs.Any(r => r.Text == (angle == 1 ? "FIRST ANGLE" : "THIRD ANGLE")) &&
                conventionCanvas.Runs.Any(r => r.Text == "DECIMAL  ±.0001") && conventionCanvas.Runs.Any(r => r.Text == "ANGLES   ±1°") &&
                conventionCanvas.Runs.All(r => r.Text != "Ⓟ"), $"Angle {angle} prints both general tolerances and the drawing convention");

            var project = DrawingProject.Capture(blank, new AnnotationDocument(blank.Scene), null);
            project = project with { Content = project.Content with { Options = project.Content.Options with { ProjectionAngle = angle } } };
            var reopenedBlank = DrawingProjectCodec.Read(DrawingProjectCodec.Write(project)).Render(null, null, null, default);
            check(reopenedBlank.Options.DrawingAngle == angle, $"Angle {angle} survives save and reopen even on a blank sheet");

            foreach (var angleLayout in new[]
            {
                StarterDrawings.FromSelection([1, 3, 6], angle),
                StarterDrawings.ForAngle(StarterDrawings.All[0].Template, angle)
            })
            {
                var front = angleLayout.Layout.Views.Single(v => v.Camera == 1);
                var top = angleLayout.Layout.Views.Single(v => v.Camera == 3);
                var right = angleLayout.Layout.Views.Single(v => v.Camera == 6);
                bool third = angle == 3;
                check((top.Bounds.Center.Y < front.Bounds.Center.Y) == third &&
                    (right.Bounds.Center.X > front.Bounds.Center.X) == third &&
                    top.RollDirection == (third ? 1 : 3) && right.RollDirection == (third ? 0 : 2) &&
                    top.Parent == front.Id && right.Parent == front.Id,
                    $"New angle {angle} layouts position the named Top and Right side around their linked Front");
            }
        }
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
        var detailBounds = whole.Details[0].Bounds;
        var detailTarget = new DrawingObject(DrawingObjectKind.View, whole.Details[0].ViewId);
        check(CanvasObjects.Pick(whole, whole.Scene!, [], new(detailBounds.Right, detailBounds.Bottom), 5, detailTarget) == detailTarget,
            "The selected exploded view owns its resize handle as soon as it is placed");

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
