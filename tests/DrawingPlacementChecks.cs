using Blueprint.Rendering;
using System.Numerics;
static class DrawingPlacementChecks
{

    public static void Run(Action<bool, string> check, string output)
    {
        CaptionAnchor[] movingCaption = [new(1001, new(900, 600))];
        var rowSnap = ViewAlignment.Snap(movingCaption, [new(100, new(300, 607))], 10);
        check(rowSnap.Delta == new Vector2(0, 7) && rowSnap.Guides.Length == 1, "Exploded caption snaps horizontally to a main-view text baseline");
        var columnSnap = ViewAlignment.Snap(movingCaption, [new(100, new(907, 300))], 10);
        check(columnSnap.Delta == new Vector2(7, 0) && columnSnap.Guides.Length == 1, "View captions snap vertically by text centerline");
        var bothSnap = ViewAlignment.Snap(movingCaption, [new(100, new(907, 300)), new(101, new(400, 607))], 10);
        check(bothSnap.Delta == new Vector2(7, 7) && bothSnap.Guides.Length == 2, "Independent horizontal and vertical caption guides can snap together");
        check(ViewAlignment.Snap(movingCaption, [new(100, new(911, 611))], 10).Guides.Length == 0, "Caption snapping releases beyond the screen-distance threshold");
        check(ViewAlignment.Snap(movingCaption, [new(1001, new(902, 602))], 10).Guides.Length == 0, "Caption snapping never targets the same view");
        check(ViewAlignment.Snap(movingCaption, [new(100, new(907, 607))], 10, lockX: true).Delta == new Vector2(0, 7)
            && ViewAlignment.Snap(movingCaption, [new(100, new(907, 607))], 10, lockY: true).Delta == new Vector2(7, 0), "Projection alignment locks take precedence over caption snaps");
        check(ViewAlignment.Snap(movingCaption, [new(100, new(907, 607))], 10, valid: d => d.Y == 0).Delta == new Vector2(7, 0)
            && ViewAlignment.Snap(movingCaption, [new(100, new(907, 607))], 10, valid: _ => false).Guides.Length == 0, "Snap falls back to a clear axis and never forces overlap or off-sheet placement");
        check(ViewAlignment.Snap(movingCaption, [new(100, new(940, 1000))], 10 / .2f).Delta.X == 40
            && ViewAlignment.Snap(movingCaption, [new(100, new(940, 1000))], 10 / 2f).Guides.Length == 0, "Snap tolerance scales with preview zoom rather than export pixel count");
        double[][] points = [[0, -1, -1], [0, 1, -1], [0, 1, 1], [0, -1, 1], [8, -1, -1], [8, 1, -1], [8, 1, 1], [8, -1, 1]];
        var mesh = new Mesh("Craft", points, [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7], points, points.Select(_ => new double[] { 1, 0, 0 }).ToArray(), points.Select(_ => new double[] { 0, 0 }).ToArray(), "structure", null, null);
        var capture = new Capture([mesh]);
        var assembly = new AssemblyCapture(capture, [new(1, null, "Craft", "craft", false)], [1], [false]);
        // Templates are adopted into ordinary drawings before the user switches
        // orientation. Exercise that exact path, not a fresh template render.
        int repageCases = 0;
        foreach (int from in new[] { 0, 2 })
            foreach (int aspect in Enumerable.Range(0, 6))
                foreach (int template in Enumerable.Range(0, 12))
                    foreach (bool captions in new[] { false, true })
                    {
                        var before = new PrintOptions(Orientation: from, Aspect: aspect, Captions: captions, TitleBlock: new(), DrawingSize: 100, Boxes: new(template, 1, 2, 3, 4, 0, 1));
                        var adopted = DrawingPlacements.FromViews(SheetLayout.Plan(capture, before), before);
                        var tight = DrawingPlacements.Plan(capture, before, adopted);
                        DrawingPlacements.Validate(tight, before);
                        var after = before with
                        {
                            Orientation = from == 0 ? 2 : 0
                        };
                        var converted = DrawingPlacements.Repage(before, tight, adopted, after);
                        try
                        {
                            DrawingPlacements.Validate(DrawingPlacements.Plan(capture, after, converted), after);
                        }
                        catch (InvalidOperationException e) { throw new Exception($"Template {template}, orientation {from}, aspect {aspect}, captions {captions}: {e.Message}"); }
                        if (!converted.Views.Select(v => v.Id).SequenceEqual(adopted.Views.Select(v => v.Id)))
                            throw new Exception("Paper conversion changed view identities");
                        repageCases++;
                    }
        check(repageCases == 288, "All 288 adopted-template orientation changes fit without overlaps, preserving assigned views");
        var side = new DrawingView(100, 2, .3f, .5f);
        var draft = new DrawingView(101, 0, .7f, .5f);
        var right = DrawingPlacements.Roll(draft, side, 0, 3);
        var above = DrawingPlacements.Roll(draft, side, 1, 3);
        check(right.Camera == 1 && right.Alignment == 2 && above.Camera == 3 && above.Alignment == 1, "Third-angle side projections roll to Front on the right and Top above, never Bottom on the right");
        check(DrawingPlacements.Roll(draft, side, 1, 1).Camera == 7 && DrawingPlacements.Roll(draft, side, 0, 1).Camera == 5, "First-angle reverses the rolled faces: Bottom above and Rear to the right of Side");
        check(above.Axes!.Right == Vector3.UnitX && above.Axes.Up == Vector3.UnitY, "Rolling preserves the parent horizontal axis instead of resetting Top to its canonical rotation");
        foreach (int camera in Enumerable.Range(0, 8))
            foreach (int angle in new[] { 1, 3 })
            {
                var root = side with
                {
                    Camera = camera
                };
                var b = DrawingPlacements.Axes(root);
                var current = root;
                for (int i = 0; i < 4; i++)
                    current = DrawingPlacements.Roll(current with
                    {
                        Id = current.Id + 1
                    }, current, 0, angle);
                var result = DrawingPlacements.Axes(current);
                check(Vector3.Distance(result.Right, b.Right) < .00001f && Vector3.Distance(result.Up, b.Up) < .00001f && Vector3.Distance(result.Toward, b.Toward) < .00001f, $"Four chained horizontal rolls return camera {camera}, angle {angle} to its original orientation");
                foreach (int direction in Enumerable.Range(0, 4))
                {
                    var rolled = DrawingPlacements.Roll(draft, root, direction, angle);
                    var rb = DrawingPlacements.Axes(rolled);
                    var inverse = DrawingPlacements.Axes(DrawingPlacements.Roll(draft with
                    {
                        Id = 102
                    }, rolled, (direction + 2) % 4, angle));
                    if (Vector3.Distance(Vector3.Cross(rb.Right, rb.Up), rb.Toward) > .00001f || Vector3.Distance(inverse.Toward, b.Toward) > .00001f || Vector3.Distance(inverse.Up, b.Up) > .00001f)
                        throw new Exception("Roll handedness/inverse failure");
                    if (direction % 2 == 0 ? rb.Up != b.Up : rb.Right != b.Right)
                        throw new Exception("Shared projection axis changed");
                }
            }
        check(true, "All 64 rolled direction/convention/camera combinations retain handedness, alignment axes and inverse rotation");
        check(DrawingPlacements.Direction(new(40, 10)) == 0 && DrawingPlacements.Direction(new(10, -40)) == 1 && DrawingPlacements.Direction(new(-40, 10)) == 2 && DrawingPlacements.Direction(new(10, 40)) == 3, "Pointer sectors select right, above, left and below");
        check(DrawingPlacements.ProjectionDirection(new(100, -60), 0) == 4 && DrawingPlacements.ProjectionDirection(new(-100, -60), 1) == 5 && DrawingPlacements.ProjectionDirection(new(-100, 60), 2) == 6 && DrawingPlacements.ProjectionDirection(new(100, 60), 3) == 7, "Off-axis projection gestures select all four corner views");
        check(DrawingPlacements.ProjectionDirection(new(100, -48), 0) == 0 && DrawingPlacements.ProjectionDirection(new(100, -48), 4) == 4 && DrawingPlacements.ProjectionDirection(new(100, -39), 4) == 0, "Diagonal entry/exit hysteresis prevents boundary flicker and returns to aligned views");
        foreach (int camera in new[] { 1, 2, 3, 5, 6, 7 })
            foreach (int angle in new[] { 1, 3 })
                foreach (int corner in Enumerable.Range(4, 4))
                {
                    var root = side with
                    {
                        Camera = camera
                    };
                    var b = DrawingPlacements.Axes(root);
                    var diagonal = DrawingPlacements.Roll(draft, root, corner, angle);
                    var d = DrawingPlacements.Axes(diagonal);
                    float sign = angle == 3 ? 1 : -1;
                    var expected = Vector3.Normalize(b.Toward + (corner is 4 or 7 ? sign : -sign) * b.Right + (corner is 4 or 5 ? sign : -sign) * b.Up);
                    if (diagonal.Camera != 4 || diagonal.Alignment != 0 || diagonal.Parent != root.Id || Vector3.Distance(d.Toward, expected) > .00001f || Vector3.Distance(Vector3.Cross(d.Right, d.Up), d.Toward) > .00001f || Math.Abs(d.Right.Length() - 1) > .00001f || Math.Abs(d.Up.Length() - 1) > .00001f)
                        throw new Exception("Invalid diagonal projection");
                }
        check(true, "All 48 orthographic-source corner/convention combinations produce orthonormal isometric cameras with free placement and retained parent");
        var diagonalPlan = DrawingPlacements.Plan(capture, new PrintOptions(), new(300, [side, DrawingPlacements.Roll(draft, side, 4, 3)]));
        check(diagonalPlan[1].Frame!.Toward == DrawingPlacements.Roll(draft, side, 4, 3).Axes!.Toward && diagonalPlan[0].Frame!.Scale == diagonalPlan[1].Frame!.Scale, "Corner camera axes reach rendering at the shared drawing scale");
        check(DrawingPlacements.Name(DrawingPlacements.Roll(draft, side with
        {
            Camera = 0
        }, 1, 3)) == "Auxiliary", "An oblique source produces an honestly named auxiliary projection");
        var source = new PoseSource(assembly, [], 1, [new(0, "Craft", "craft", [0], NodeId: 1)], new Dictionary<string, Texture>());
        // A roomy isometric view still shares scale with an edge-bound front
        // view. Template metadata must not impose hidden cell limits after moving.
        var groupedOptions = new PrintOptions(Orientation: 2, Boxes: new(6, 1, 2, 4));
        var grouped = new DrawingPlacement(400, [new(100, 4, .25f, .45f), new(101, 1, .6f, .3f), new(102, 2, .8f, .3f)]);
        var initialGroup = DrawingPlacements.Plan(capture, groupedOptions, grouped);
        float top = DrawingPlacements.Area(groupedOptions).Y;
        grouped = grouped with { Views = grouped.Views.Select(v => v.Id == 100 ? v : v with
        {
            Y = (top + initialGroup.Single(p => p.Id == v.Id).Bounds.Height / 2) / groupedOptions.Page.Height
        }).ToArray() };
        ScaleFit GroupFit(DrawingPlacement placement, PrintOptions settings)
        {
            settings = settings with { Drawing = placement };
            var regions = DrawingPlacements.Plan(capture, settings, placement);
            DrawingPlacements.Validate(regions, settings);
            var scene = new RenderScene([], Enumerable.Repeat(-1, settings.Page.Width * settings.Page.Height).ToArray(), [], regions, options: settings);
            return ScaleAdjustment.ForMain(source, scene, placement, []);
        }
        var edgeFit = GroupFit(grouped, groupedOptions);
        var soloFit = GroupFit(grouped with { Views = [grouped.Views[0]] }, groupedOptions);
        // Pixel rounding permits a fraction of a percent at the opening edge.
        check(edgeFit.Maximum < 101 && edgeFit.Warning.Contains("FRONT") && soloFit.Maximum > 110,
            $"Shared scale names an edge-bound view despite room around ISOMETRIC (group {edgeFit.Maximum}%, solo {soloFit.Maximum}%): {edgeFit.Warning}");
        var movedGroup = grouped with { Views = grouped.Views.Select(v => v.Id == 100 ? v : v with { Y = .4f }).ToArray() };
        var movedFit = GroupFit(movedGroup, groupedOptions);
        check(movedFit.Maximum > 110 && movedFit.Maximum == GroupFit(movedGroup, groupedOptions with { Boxes = null }).Maximum,
            "Moving the limiting views inward permits growth; old template cells impose no hidden scale restriction");
        // Compare the advertised marker to production footprint validation,
        // including the next increment beyond it, without rasterizing sheets.
        foreach (int shape in new[] { 0, 1, 2 })
            foreach (bool captions in new[] { false, true })
            {
                var scaleDrawing = new DrawingPlacement(200, [new(100, 1, .38f, .4f), new(101, 2, .62f, .4f)]);
                var scaleOptions = new PrintOptions(Orientation: shape, Captions: captions, Drawing: scaleDrawing, TitleBlock: new());
                var regions = DrawingPlacements.Plan(capture, scaleOptions, scaleDrawing);
                var scaleScene = new RenderScene([], Enumerable.Repeat(-1, scaleOptions.Page.Width * scaleOptions.Page.Height).ToArray(), [], regions, options: scaleOptions);
                DrawingPlacements.Validate(regions, scaleOptions);
                var limit = ScaleAdjustment.ForMain(source, scaleScene, scaleDrawing, []);
                bool Fits(float percent, Box[] obstacles)
                {
                    try
                    {
                        DrawingPlacements.Validate(DrawingPlacements.Plan(capture, scaleOptions, scaleDrawing with
                        {
                            Ratio = 200 * 100 / percent
                        }), scaleOptions, obstacles);
                        return true;
                    }
                    catch (InvalidOperationException) { return false; }
                }
                check(limit.Maximum >= 100 && Fits(limit.Maximum, []) && (limit.Maximum == 400 || !Fits(limit.Maximum + .2f, [])),
                    $"Scale marker matches shared-view and canvas limits for shape {shape}, captions {captions}");
                var first = DrawingPlacements.Footprint(regions[0], captions);
                Box[] panels = [new(first.X - 95, first.Y, 75, first.Height)];
                var panelLimit = ScaleAdjustment.ForMain(source, scaleScene, scaleDrawing, panels);
                check(panelLimit.Maximum <= limit.Maximum && Fits(panelLimit.Maximum, panels) && !Fits(panelLimit.Maximum + .2f, panels),
                    "Scale marker respects supporting panels before accepting a resize");
            }
        var poseControl = new PoseControl(1, "extend", "Extend", true, 0, [new(1, 0, [1], Enumerable.Range(0, 101).Select(i => Matrix4x4.CreateScale(1 + i * .01f)).ToArray())]);
        var posedSource = source with
        {
            Controls = [poseControl]
        };
        PartPose[] scalePoses = [new(1, "extend", -1, 100)];
        var posedCapture = PartPoses.Exterior(posedSource, PartPoses.Apply(posedSource, scalePoses, 100));
        var poseDrawing = new DrawingPlacement(200, [new(100, 1, .5f, .4f)]);
        var poseOptions = new PrintOptions(Drawing: poseDrawing);
        var poseScene = new RenderScene([], Enumerable.Repeat(-1, poseOptions.Page.Width * poseOptions.Page.Height).ToArray(), [], DrawingPlacements.Plan(posedCapture, poseOptions, poseDrawing), options: poseOptions).WithPoses(scalePoses);
        var poseLimit = ScaleAdjustment.ForMain(posedSource, poseScene, poseDrawing, []);
        DrawingPlacements.Validate(DrawingPlacements.Plan(posedCapture, poseOptions, poseDrawing with
        {
            Ratio = 200 * 100 / poseLimit.Maximum
        }), poseOptions);
        check(poseLimit.Maximum < 400, "Scale limit uses the actuated geometry, not the original captured pose");
        var portraitTemplate = new PrintOptions(Orientation: 0, Aspect: 0, DrawingSize: 100, TitleBlock: new(Title: "Template orientation regression"), Boxes: new(7, 1, 2, 3, 4));
        var templateSheet = PosePainting.RenderMain(source, "Craft", portraitTemplate, [], [], default);
        var adoptedDrawing = DrawingPlacements.FromScene(templateSheet.Scene!);
        var portraitSheet = PosePainting.RenderMain(source, "Craft", templateSheet.Options with
        {
            Drawing = adoptedDrawing
        }, [], [], default);
        var keptNote = new PartLabel(1, 100, 0, "Craft body", 150, 500, ManualPlacement: true);
        portraitSheet = AnnotationPainting.Compose(portraitSheet, [keptNote], default);
        var landscapeOptions = portraitSheet.Options with
        {
            Orientation = 2
        };
        landscapeOptions = landscapeOptions with
        {
            Drawing = DrawingPlacements.Repage(portraitSheet.Scene!, landscapeOptions)
        };
        var landscapeFresh = PosePainting.RenderMain(source, "Craft", landscapeOptions, [], [], default, composeAnnotations: false);
        var landscapeSheet = CompositionRegeneration.Rebuild(landscapeFresh, portraitSheet, default);
        check(landscapeSheet.PixelWidth == 3000 && landscapeSheet.PixelHeight == 2400 && landscapeSheet.Scene!.Views.Length == 4
            && landscapeSheet.Labels.Length == 1 && landscapeSheet.Labels[0].Id == keptNote.Id && landscapeSheet.Labels[0].Text == keptNote.Text,
            "Generate converts an adopted portrait three-above/isometric-below template to landscape and retains its note");
        check(portraitSheet.PixelWidth == 2400 && portraitSheet.PixelHeight == 3000 && portraitSheet.Labels[0] == keptNote,
            "Orientation regeneration leaves the original document intact until the new sheet is accepted");
        var pageHistory = new AnnotationDocument(portraitSheet.Scene!, portraitSheet.Labels);
        pageHistory.Regenerate(landscapeSheet.Scene!, landscapeSheet.Labels, landscapeSheet.Details);
        check(pageHistory.CanUndo, "Generating another paper orientation retains an undo step");
        pageHistory.Undo();
        var restoredPaper = PosePainting.Rebuild(landscapeSheet with
        {
            Options = pageHistory.Scene.Options,
            Scene = pageHistory.Scene,
            Labels = pageHistory.Labels,
            Details = pageHistory.Details
        }, pageHistory.Scene.Poses, default, true);
        check(restoredPaper.Options.OutputPage == portraitSheet.Options.OutputPage && restoredPaper.Scene!.Drawing == portraitSheet.Scene!.Drawing
            && restoredPaper.Labels.SequenceEqual(portraitSheet.Labels) && restoredPaper.Rgb.SequenceEqual(portraitSheet.Rgb),
            "Paper undo rerenders the exact original portrait geometry, labels and pixels");
        pageHistory.Rebind(restoredPaper.Scene!, restoredPaper.Labels, restoredPaper.Details, false);
        pageHistory.Redo();
        var redonePaper = PosePainting.Rebuild(restoredPaper with
        {
            Options = pageHistory.Scene.Options,
            Scene = pageHistory.Scene,
            Labels = pageHistory.Labels,
            Details = pageHistory.Details
        }, pageHistory.Scene.Poses, default, true);
        check(redonePaper.Options.OutputPage == landscapeSheet.Options.OutputPage && redonePaper.Rgb.SequenceEqual(landscapeSheet.Rgb),
            "Paper redo restores the accepted landscape pixels without fitting twice");
        var crowdedDrawing = landscapeOptions.Drawing! with
        {
            Ratio = landscapeOptions.Drawing!.Ratio * .5f
        };
        var recoveredDrawing = DrawingPlacements.FitToPage(capture, landscapeOptions, crowdedDrawing);
        DrawingPlacements.Validate(DrawingPlacements.Plan(capture, landscapeOptions, recoveredDrawing), landscapeOptions);
        check(recoveredDrawing.Ratio > crowdedDrawing.Ratio && recoveredDrawing.Views.Select(v => v.Id).SequenceEqual(crowdedDrawing.Views.Select(v => v.Id)),
            "Oversized repage shrinks all views at one shared scale and preserves view identity");
        check(DrawingPlacements.FitToPage(capture, landscapeOptions, recoveredDrawing).Ratio == recoveredDrawing.Ratio,
            "Already fitting views are never shrunk again");
        var reservedPanel = new Box(2100, 200, 600, 1200);
        var clearDrawing = DrawingPlacements.FitToPage(capture, landscapeOptions, recoveredDrawing, [reservedPanel]);
        DrawingPlacements.Validate(DrawingPlacements.Plan(capture, landscapeOptions, clearDrawing), landscapeOptions, [reservedPanel]);
        check(clearDrawing.Ratio >= recoveredDrawing.Ratio, "Repaging keeps supporting blocks clear without enlarging craft views");
        var drawing = new DrawingPlacement(300, [new(100, 0, .28f, .48f), new(101, 3, .28f, .22f, 100, 1), new(102, 2, .65f, .22f, 101, 2), new(103, 4, .7f, .57f)]);
        var options = new PrintOptions(Drawing: drawing, DrawingSize: 100);
        var upperRight = new ViewRegion(104, "ISOMETRIC", new(1700, 160, 380, 600), null, 4);
        DrawingPlacements.Validate([upperRight], options);
        check(true, "Clear upper-right placement is valid above the former invisible 280px header strip");
        foreach (int orientation in new[] { 0, 1, 2, 3 })
            foreach (int aspect in Enumerable.Range(0, 6))
                foreach (bool captions in new[] { false, true })
                    foreach (bool title in new[] { false, true })
                    {
                        var o = options with
                        {
                            Orientation = orientation,
                            Aspect = aspect,
                            Captions = captions,
                            TitleBlock = title ? new() : null
                        };
                        int bottom = PageLayout.TitleTop(o);
                        var area = DrawingPlacements.Area(o);
                        if (area != FrameLayout.Area(o) || area.X != 60 || area.Y != 60 || area.Right != o.Page.Width - 60 || area.Bottom != (CompanyTitleBlock.ContentSized(o) ? PageLayout.Bottom(o) : bottom))
                            throw new Exception("Placement area differs from printed frame");
                        foreach (var bounds in new Box[] { new(60, 60, 380, 300), new(o.Page.Width - 440, 60, 380, 300), new(60, bottom - 300 - (captions ? ViewCaption.Band : 0), 380, 300) })
                            DrawingPlacements.Validate([upperRight with { Bounds = bounds }], o);
                        foreach (var bounds in new Box[] { new(59, 60, 380, 300), new(60, 59, 380, 300), new(o.Page.Width - 439, 60, 380, 300) })
                        {
                            try
                            {
                                DrawingPlacements.Validate([upperRight with { Bounds = bounds }], o);
                                throw new Exception("Outside-frame placement accepted");
                            }
                            catch (InvalidOperationException e) { if (!e.Message.Contains("drawing area")) throw; }
                        }
                        var touching = new ViewRegion(104, "SIDE", new(o.Page.Width - 440, bottom - 300 - (captions ? ViewCaption.Band : 0), 380, 300), null, 2);
                        DrawingPlacements.Validate([touching], o);
                        if (DrawingPlacements.Footprint(touching, captions).Bottom != bottom || PageLayout.TitleBounds(o).Y != bottom)
                            throw new Exception("Title boundary mismatch");
                        try
                        {
                            DrawingPlacements.Validate([touching with { Bounds = touching.Bounds with { Y = touching.Bounds.Y + 1 } }], o);
                            throw new Exception("Title overlap accepted");
                        }
                        catch (InvalidOperationException e) { if (!e.Message.Contains("drawing area")) throw; }
                    }
        check(true, "All page shapes/ratios allow exact title-rule contact with captions on/off and reject crossing into either title style");
        check(true, "All shapes/ratios use printed top/left/right borders for both views and support frames; edge contact is valid and one pixel outside is rejected");
        DrawingPlacements.Validate([upperRight with { Bounds = new(160, 130, 380, 600) }], options);
        check(FrameLayout.Clamp(new(160, 130, 380, 600), options).Y == 130, "Views and support frames may occupy the former upper-left heading space");
        try
        {
            DrawingPlacements.Validate([upperRight], options, [new(1800, 180, 400, 100)]);
            throw new Exception("Panel overlap accepted");
        }
        catch (InvalidOperationException e) { check(e.Message.Contains("tables"), "Clear-paper fix retains supporting-panel collision protection"); }
        var rolledPlan = DrawingPlacements.Plan(capture, options, new(300, [side, above]));
        check(rolledPlan[1].Frame!.Right == above.Axes.Right && rolledPlan[1].Frame!.Up == above.Axes.Up, "Render planning carries rolled camera axes into the actual projection frame");
        var plans = DrawingPlacements.Plan(capture, options, drawing);
        DrawingPlacements.Validate(plans, options);
        check(plans.Select(v => v.Id).SequenceEqual(new[] { 100, 101, 102, 103 }) && plans.Select(v => v.CameraView).SequenceEqual(new[] { 0, 3, 2, 4 }), "Projection chains retain drawing identity independently of camera direction");
        check(plans.Select(v => v.Frame!.Scale).Distinct().Count() == 1 && PrintScale.Summary(plans, options) == "1:300", "All main drawings use the chosen physical paper scale, including isometric");
        var moved = DrawingPlacements.Move(drawing, 100, new(drawing.Views[0].X * 2400 + 120, drawing.Views[0].Y * 3000 + 40), options);
        check(Math.Abs(moved.Views[2].X - drawing.Views[2].X - .05f) < .00001 && moved.Views[3] == drawing.Views[3], "Moving a source carries its complete projection chain while independent views stay put");
        var constrained = DrawingPlacements.Move(drawing, 101, new(1100, 600), options);
        check(constrained.Views[1].X == drawing.Views[0].X && constrained.Views[1].Y == .2f, "Dragging a projected child preserves its locked alignment axis");
        var unlocked = DrawingPlacements.Move(drawing, 101, new(1100, 600), options, true);
        check(Math.Abs(unlocked.Views[1].X - 1100f / 2400) < .00001 && unlocked.Views[1].Alignment == 0, "Unlocked projection can move across its previous alignment guide");
        var removed = DrawingPlacements.Remove(drawing, 100);
        check(removed.Views.Single(v => v.Id == 101).Parent == null && removed.Views.Single(v => v.Id == 102).Parent == 101, "Removing a source detaches its direct children without deleting their views");
        var repeated = drawing with
        {
            Views = [drawing.Views[0] with { Camera = 2 }, drawing.Views[1] with { Camera = 2 }]
        };
        check(DrawingPlacements.Plan(capture, options, repeated).Length == 2, "Multiple drawings of the same direction remain distinct and selectable");
        foreach (int cam in new[] { 1, 2, 3 })
        {
            int opposite = cam == 1 ? 5 : cam == 2 ? 6 : 7;
            check(Vector3.Dot(SheetLayout.Basis(cam).Toward, SheetLayout.Basis(opposite).Toward) < -.99, "Opposite camera " + opposite + " reverses the correct craft axis");
        }
        try
        {
            DrawingPlacements.Plan(capture, options, drawing with
            {
                Ratio = 0
            });
            throw new Exception("Zero accepted");
        }
        catch (InvalidOperationException) { check(true, "Invalid scale is rejected before renderer allocation"); }
        try
        {
            DrawingPlacements.Plan(capture, options, drawing with
            {
                Views = drawing.Views.Select(v => v.Id == 100 ? v with { Parent = 102 } : v).ToArray()
            });
            throw new Exception("Cycle accepted");
        }
        catch (InvalidOperationException) { check(true, "Projection cycles are rejected"); }
        try
        {
            DrawingPlacements.Validate([plans[0], plans[0] with { Id = 104 }], options);
            throw new Exception("Overlap accepted");
        }
        catch (InvalidOperationException) { check(true, "Overlapping view placements do not commit"); }
        var blank = PosePainting.RenderMain(source, "Craft", options with
        {
            Drawing = new(200, [])
        }, [], [], default);
        check(blank.Scene!.Views.Length == 0 && blank.Scene.Parts.Length == 1 && blank.PoseSource != null, "An empty paper retains captured geometry for the primary placement");
        var original = PosePainting.RenderMain(source, "Craft", options, [], [], default);
        foreach (var view in drawing.Views)
        {
            var captionGhost = PlacementPreviews.View(source, view, drawing.Ratio, options, [], default);
            var actual = ViewAlignment.Anchors(original.Scene!).Single(a => a.ViewId == view.Id).Position;
            check(captionGhost.CaptionAnchor is { } anchor && Vector2.Distance(anchor + new Vector2(captionGhost.Bounds.X, captionGhost.Bounds.Y), actual) < 1,
                "New-view ghost caption anchor matches committed rendering for camera " + view.Camera);
        }
        var label = new PartLabel(1, 100, 0, "Craft", 250, 1150, ManualPlacement: true);
        original = AnnotationPainting.Compose(original, [label], default);
        var next = PosePainting.Rebuild(original with
        {
            Scene = original.Scene!.WithDrawing(moved)
        }, [], default);
        check(Math.Abs(next.Labels[0].X - label.X - 120) < 2 && Math.Abs(next.Labels[0].Y - label.Y - 40) < 2, "Moving a view carries its attached label position and regenerates its part anchor");
        check(next.Scene!.Drawing == moved && next.Scene.Views.Select(v => v.Id).SequenceEqual(plans.Select(v => v.Id)), "Recomposition retains stable view IDs and projection relationships");
        var doc = new AnnotationDocument(original.Scene!, original.Labels);
        doc.Rebind(next.Scene!, next.Labels, []);
        doc.Undo();
        check(doc.Scene.Drawing == drawing && doc.Labels[0] == label, "Undo restores view placement and attached notes together");
        var undone = PosePainting.Rebuild(next with
        {
            Scene = doc.Scene,
            Labels = doc.Labels
        }, [], default, true);
        check(undone.Labels[0].X == label.X && undone.Labels[0].Y == label.Y, "History recomposition does not move already-restored labels a second time");
        doc.Redo();
        check(doc.Scene.Drawing == moved, "Redo restores projected placement");
        var smaller = drawing with
        {
            Ratio = drawing.Ratio * 1.25f
        };
        var scaled = PosePainting.Rebuild(original with
        {
            Scene = original.Scene!.WithDrawing(smaller)
        }, [], default);
        check(scaled.Scene!.Drawing == smaller && scaled.Scene.Views.All(v =>
            Vector2.Distance(v.Bounds.Center, original.Scene.Views.Single(old => old.Id == v.Id).Bounds.Center) < 1.5f
            && Math.Abs(v.Frame!.Scale / original.Scene.Views.Single(old => old.Id == v.Id).Frame!.Scale - .8f) < .001f),
            "View-size rebuild changes every main projection at shared scale while retaining centres and links");
        var sizedHistory = new AnnotationDocument(original.Scene, original.Labels);
        sizedHistory.Rebind(scaled.Scene, scaled.Labels, []);
        sizedHistory.Undo();
        check(sizedHistory.Scene.Drawing == drawing && sizedHistory.Labels.SequenceEqual(original.Labels), "Undo of view size restores projection scale and attached labels together");
        sizedHistory.Redo();
        check(sizedHistory.Scene.Drawing == smaller && sizedHistory.Labels.SequenceEqual(scaled.Labels), "Redo of view size restores the accepted projection and labels");
        // Regression: custom tables fit, but temporary default tables do not.
        // Moving views/Generate/pose rebuilds must restore the real tables before
        // composing annotations instead of failing in the intermediate render.
        var tableSource = source with
        {
            Parts = [source.Parts[0], .. Enumerable.Range(1, 29).Select(i => new LabelPart(i, "Part " + i + " " + new string('A', 85), "part-" + i, []))]
        };
        var tableDrawing = drawing with
        {
            Views = drawing.Views.Take(2).ToArray()
        };
        var tableOptions = options with
        {
            Drawing = tableDrawing,
            PartsTable = true,
            FlagTable = true
        };
        var tableBase = PosePainting.RenderMain(tableSource, "Craft", tableOptions, [], [], default, composeAnnotations: false);
        check(!PrintTables.Fits(tableBase.Scene!, [], out _), "Regression fixture has unusable default table positions");
        var tableBounds = new TableLayout(new(1250, 140, 1000, 2100), new(1250, 2290, 1000, 180));
        var tableOriginal = AnnotationPainting.Compose(tableBase, [label], default, tableBounds);
        var tableMove = DrawingPlacements.Move(tableDrawing, 100, new(tableDrawing.Views[0].X * 2400 + 120, tableDrawing.Views[0].Y * 3000 + 40), tableOptions);
        var tableFresh = PosePainting.RenderMain(tableSource, "Craft", tableOptions with
        {
            Drawing = tableMove
        }, [], [], default, composeAnnotations: false);
        var tableMoved = CompositionRegeneration.Rebuild(tableFresh, tableOriginal, default);
        check(tableMoved.Scene!.Drawing == tableMove && tableMoved.Scene.Tables == tableBounds && PrintTables.Fits(tableMoved.Scene, tableMoved.Labels, out _), "Moving linked views restores fitting custom tables before annotation validation");
        check(tableMoved.Scene.Views.All(v => Math.Abs(v.Bounds.X - tableOriginal.Scene!.Views.Single(p => p.Id == v.Id).Bounds.X - 120) < 2 && Math.Abs(v.Bounds.Y - tableOriginal.Scene.Views.Single(p => p.Id == v.Id).Bounds.Y - 40) < 2)
            && Math.Abs(tableMoved.Labels[0].X - label.X - 120) < 2, "Linked view pixels and attached labels move together with custom tables enabled");
        var tableRegenerated = CompositionRegeneration.Rebuild(tableBase, tableOriginal, default);
        check(tableRegenerated.Scene!.Tables == tableBounds && tableRegenerated.Labels[0].X == label.X, "Generate preserves custom tables and label positions despite invalid default tables");
        var tablePosed = PosePainting.Rebuild(tableOriginal, [], default);
        check(tablePosed.Scene!.Tables == tableBounds, "Pose rebuild also defers temporary table composition");
        var tableHistory = new AnnotationDocument(tableOriginal.Scene!, tableOriginal.Labels);
        tableHistory.Rebind(tableMoved.Scene, tableMoved.Labels, []);
        tableHistory.Undo();
        var tableUndone = PosePainting.Rebuild(tableMoved with
        {
            Scene = tableHistory.Scene,
            Labels = tableHistory.Labels
        }, [], default, true);
        check(tableUndone.Scene!.Drawing == tableDrawing && tableUndone.Scene.Tables == tableBounds && tableUndone.Labels[0].X == label.X, "Undo linked movement restores original views and notes while retaining custom tables");
        var deleted = PosePainting.RenderMain(source, "Craft", options with
        {
            Drawing = removed
        }, [], [], default);
        var deletionHistory = new AnnotationDocument(original.Scene!, original.Labels);
        deletionHistory.Rebind(deleted.Scene!, [], []);
        deletionHistory.Undo();
        var restored = PosePainting.Rebuild(deleted with
        {
            Scene = deletionHistory.Scene,
            Labels = deletionHistory.Labels
        }, [], default, true);
        check(restored.Scene!.Views.Length == 4 && restored.Labels[0].X == label.X && restored.Labels[0].Y == label.Y, "Undo deleting a view restores its caption and notes even when its raster was removed");
        deletionHistory.Rebind(restored.Scene!, restored.Labels, [], false);
        deletionHistory.Redo();
        var deletedAgain = PosePainting.Rebuild(restored with
        {
            Scene = deletionHistory.Scene,
            Labels = deletionHistory.Labels
        }, [], default, true);
        check(deletedAgain.Scene!.Views.Length == 3 && deletedAgain.Labels.Length == 0, "Redo removing a view restores the deletion without stale caption references");
        var singleDrawing = new DrawingPlacement(drawing.Ratio, [drawing.Views[0]]);
        var single = PosePainting.RenderMain(source, "Craft", options with
        {
            Drawing = singleDrawing
        }, [], [], default);
        single = AnnotationPainting.Compose(single, [label], default);
        var emptyDrawing = DrawingPlacements.Remove(singleDrawing, 100);
        var empty = PosePainting.RenderMain(source, "Craft", single.Options with
        {
            Drawing = emptyDrawing
        }, [], [], default);
        empty = CompositionRegeneration.Rebuild(empty, single with
        {
            Labels = []
        }, default);
        var lastHistory = new AnnotationDocument(single.Scene!, single.Labels);
        lastHistory.Rebind(empty.Scene!, empty.Labels, []);
        check(lastHistory.Scene.Views.Length == 0 && lastHistory.Labels.Length == 0 && empty.PoseSource == source, "Deleting the only primary view returns to blank paper with captured craft ready for a replacement");
        lastHistory.Undo();
        var lastRestored = PosePainting.Rebuild(empty with
        {
            Scene = lastHistory.Scene,
            Labels = lastHistory.Labels
        }, [], default, true);
        check(lastRestored.Scene!.Views.Single().Id == 100 && lastRestored.Labels.Single() == label, "Undo restores the deleted last primary view and its attached note");
        lastHistory.Rebind(lastRestored.Scene!, lastRestored.Labels, [], false);
        lastHistory.Redo();
        var lastDeletedAgain = PosePainting.Rebuild(lastRestored with
        {
            Scene = lastHistory.Scene,
            Labels = lastHistory.Labels
        }, [], default, true);
        check(lastDeletedAgain.Scene!.Views.Length == 0 && lastDeletedAgain.Labels.Length == 0, "Redo returns the last-view deletion to editable blank paper");
        var ghost = PlacementPreviews.View(source, drawing.Views[0], drawing.Ratio, options, [], default);
        check(ghost.Rgba.Length == ghost.Width * ghost.Height * 4 && ghost.Rgba.Where((_, i) => i % 4 == 3).Any(a => a == 255) && ghost.Rgba.Where((_, i) => i % 4 == 3).Any(a => a == 0), "Placement preview contains actual rendered craft ink and transparent paper");
        Directory.CreateDirectory(output);
        Png.Write(Path.Combine(output, "drawing-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"), next.PixelWidth, next.PixelHeight, next.Rgb);
        foreach (int orientation in new[] { 0, 1, 2 })
            foreach (int aspect in Enumerable.Range(0, 6))
            {
                var o = options with
                {
                    Orientation = orientation,
                    Aspect = aspect
                };
                float ratio = DrawingPlacements.SuggestRatio(capture, 0, o);
                var area = FrameLayout.Area(o);
                var one = new DrawingPlacement(ratio, [new(100, 0, area.Center.X / o.Page.Width, (area.Center.Y - ViewCaption.Band / 2f) / o.Page.Height)]);
                DrawingPlacements.Validate(DrawingPlacements.Plan(capture, o, one), o);
            }
        check(true, "Suggested primary scale fits all 18 paper orientation/aspect combinations");
        var wide = options with
        {
            Orientation = 3,
            CustomWidth = 3440,
            CustomHeight = 1440
        };
        var repaged = DrawingPlacements.Repage(original.Scene!, wide);
        DrawingPlacements.Validate(DrawingPlacements.Plan(capture, wide, repaged), wide);
        check(repaged.Views[2].Parent == 101 && repaged.Views[1].X == repaged.Views[0].X && repaged.Views[2].Y == repaged.Views[1].Y, "Changing to ultrawide paper preserves the projection chain and its alignment while fitting the group");
        var landscape = options with
        {
            Orientation = 2,
            Aspect = 5,
            Zones = true
        };
        var portrait = landscape with
        {
            Orientation = 0
        };
        check(SheetZones.Count(landscape) == (6, 5) || SheetZones.Count(landscape) == (7, 5), "Letter landscape chooses roughly seven columns and five rows");
        check(SheetZones.Count(portrait).Columns == 5 && SheetZones.Count(portrait).Rows >= 6 && SheetZones.Row(0, 7) == "G" && SheetZones.Row(6, 7) == "A" && SheetZones.Column(0, 7) == "7", "Zone labels run right to left and bottom to top");
        var axis = SheetZones.GridAxis(3000, 7);
        check(axis.Count(x => x == 10) >= 8 && axis[(int)MathF.Round(60 + (3000 - 120) / 7f)] == 10, "Zone boundaries align to ten-square graph blocks");
        check(new PrintOptions().Zones, "New prints default to Zoned sheet");
        foreach (int theme in Enumerable.Range(0, 4))
        {
            var o = options with
            {
                Theme = theme,
                Orientation = theme,
                Zones = true,
                Emphasis = theme % 2 == 0 ? 5 : 10,
                Archive = theme == 2
            };
            var colors = SheetLayout.Colors(theme);
            var page = o.Page;
            var on = new Canvas(page.Width, page.Height, colors.Paper, colors.Ink, o);
            var off = new Canvas(page.Width, page.Height, colors.Paper, colors.Ink, o with
            {
                Zones = false
            });
            check(on.Pixels.SequenceEqual(off.Pixels), "Zoned sheet toggle preserves every background/grid pixel, theme/shape " + theme);
            SheetZones.Paint(off, o with
            {
                Zones = false
            });
            check(on.Pixels.SequenceEqual(off.Pixels) && off.Runs.Count == 0, "Disabling Zoned sheet omits only zone border ink and labels, theme " + theme);
        }
        foreach (var size in new[] { new PageSize(1920, 1080), new(3840, 2160), new(3440, 1440), new(5120, 1440), new(1080, 1920) })
        {
            var desktop = options with
            {
                Orientation = 3,
                CustomWidth = size.Width,
                CustomHeight = size.Height
            };
            check(desktop.OutputPage == size && Math.Abs((double)desktop.Page.Width / desktop.Page.Height - (double)size.Width / size.Height) < .002 && FrameLayout.Area(desktop).Height > 700, "Desktop dimensions and usable drawing area: " + size);
        }
        var desktopOptions = options with
        {
            Orientation = 3,
            CustomWidth = 1920,
            CustomHeight = 1080,
            Drawing = new(100, []),
            Zones = true
        };
        var desktopSheet = PosePainting.RenderMain(source, "Desktop zones", desktopOptions, [], [], default);
        string exported = PrintFiles.Save(desktopSheet, output);
        var header = File.ReadAllBytes(exported);
        check(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)) == 1920 && System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)) == 1080, "Desktop PNG exports the requested pixels instead of internal preview size");
    }
}
