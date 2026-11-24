using Blueprint.Rendering;
using System.Numerics;

static class GroupChecks
{
    public static void Run(Action<bool, string> check)
    {
        var options = new PrintOptions(FreeLayout: true);
        var drawing = new DrawingPlacement(100, [new(100, 1, .2f, .2f), new(101, 2, .5f, .2f, 100), new(102, 3, .2f, .5f, 101), new(103, 4, .7f, .7f, Ratio: 200)]);
        var resized = ProjectionGroups.SetRatio(drawing, 102, 50);
        check(ProjectionGroups.Ratio(resized, 100) == 50 && ProjectionGroups.Ratio(resized, 102) == 50 && ProjectionGroups.Ratio(resized, 103) == 200,
            "Nested projections share their root scale; independent reference retains its scale");
        var boxes = drawing.Views.ToDictionary(v => v.Id, v => new Box(v.X * options.Page.Width - 116, v.Y * options.Page.Height - 216, 232, 432));
        var members = ProjectionGroups.Members(drawing, 101);
        var bounds = ProjectionGroups.Bounds(members.Select(id => boxes[id]));
        Vector2[] anchors = [new(bounds.X, bounds.Y), new(bounds.Right, bounds.Y), new(bounds.Right, bounds.Bottom), new(bounds.X, bounds.Bottom), bounds.Center];
        foreach (var mode in Enum.GetValues<GroupResizeMode>())
            foreach (var anchor in anchors)
            {
                var moved = ProjectionGroups.Resize(drawing, 101, boxes, options, 1.5f, mode, anchor);
                check(moved.Views[3] == drawing.Views[3], $"{mode} resize leaves other groups untouched");
                if (mode == GroupResizeMode.Sizes)
                    check(moved.Views.Select(v => (v.X, v.Y)).SequenceEqual(drawing.Views.Select(v => (v.X, v.Y))), "Sizes-only pins each view center");
                else
                {
                    var actual = ProjectionGroups.Bounds(moved.Views.Where(v => members.Contains(v.Id)).Select(v =>
                    {
                        var b = boxes[v.Id];
                        float w = mode == GroupResizeMode.Spacing ? b.Width : (b.Width - 32) * 1.5f + 32;
                        float h = mode == GroupResizeMode.Spacing ? b.Height : (b.Height - 32) * 1.5f + 32;
                        return new Box(v.X * options.Page.Width - w / 2, v.Y * options.Page.Height - h / 2, w, h);
                    }));
                    var fraction = new Vector2((anchor.X - bounds.X) / bounds.Width, (anchor.Y - bounds.Y) / bounds.Height);
                    check(Vector2.Distance(anchor, new(actual.X + actual.Width * fraction.X, actual.Y + actual.Height * fraction.Y)) < .01f, $"{mode} preserves the chosen outer anchor including fixed padding");
                }
            }
        var kept = ProjectionGroups.Delete(resized, 100, false);
        check(kept.Views.Where(v => v.Id is 101 or 102).All(v => v.Parent == null && v.Ratio == 50), "Deleting root can retain every descendant as its own primary with unchanged scale");
        check(ProjectionGroups.Delete(drawing, 100, true).Views.Select(v => v.Id).SequenceEqual([103]), "Deleting root and dependents leaves independent references intact");
        var single = new DrawingPlacement(100, [drawing.Views[0]]);
        check(ProjectionGroups.Resize(single, 100, boxes, options, 2, GroupResizeMode.Spacing, Vector2.Zero) == single, "Spacing-only single-view resize is a no-op");
        DrawingPlacements.Validate([new(100, "A", new(-500, -200, 200, 200), null), new(101, "B", new(-500, -200, 200, 200), null)], options);
        check(true, "Free-layout validation permits overlap and off-page positions");
        check(!CanvasBounds.Safe(new(float.NaN, 0, 200, 200)) && !CanvasBounds.Safe(new(1e20f, 0, 200, 200)), "Nonfinite and unsafe positions remain rejected");
        var text = FreeTextPainting.Fit(new(1, "Overlapping text", new(-200, -50, 400, 150)), options);
        check(text.Bounds.X == -200 && text.Bounds.Y == -50 && new TextPlacementSpace(options, [text.Bounds], []).Clear(text.Bounds), "Free text keeps off-page position and allows intentional overlap");
        var canvas = new Canvas(32, 32, Vector3.Zero, new(255), options);
        canvas.Line(int.MinValue, 16, int.MaxValue, 16, 1);
        check(Enumerable.Range(0, 32).All(x => canvas.Pixels[(16 * 32 + x) * 3] == 255), "Extreme line endpoints clip before raster iteration and integer arithmetic");
        double[][] points = [[0, 0, 0], [4, 0, 0], [0, 4, 0], [0, 0, 4]];
        var geometry = new Capture([new Mesh("Fixture",points,[0,2,1,0,1,3,0,3,2,1,2,3],points,
            points.Select(_=>new double[]{1,0,0}).ToArray(),points.Select(_=>new double[]{0,0}).ToArray(),"structure",null,null)]);
        var source = new PoseSource(new(geometry, [new(1, null, "Part", "fixture", false)], [1], [false]), [], 1,
            [new(0, "Part", "fixture", [0], NodeId: 1)], new Dictionary<string, Texture>());
        var small = options with
        {
            Orientation = 3,
            CustomWidth = 1280,
            CustomHeight = 720
        };
        float ratio = DrawingPlacements.SuggestRatio(geometry, 4, small);
        var layout = new DrawingPlacement(ratio, [new(100, 4, .35f, .4f), new(101, 4, .35f, .4f)]);
        var sheet = PrintGeneration.Render(source, geometry, "Overlap", small with
        {
            Drawing = layout
        }, [], null, false, default);
        var tile = sheet.MainInk![0];
        int pixel = Array.FindIndex(tile.Pixels, p => p > 0);
        var inkPoint = new Vector2(tile.Bounds.X + pixel % tile.Width, tile.Bounds.Y + pixel / tile.Width);
        check(CanvasObjects.Pick(sheet, sheet.Scene!, [], inkPoint, 1)?.Id == 101, "Overlapping view ink selects the last layer");
        check(CanvasObjects.Pick(sheet, sheet.Scene!, [], new(tile.Bounds.X + 2, tile.Bounds.Y + 2), 1) == null, "Empty view pixels do not capture clicks");
        var reordered = sheet.Scene!.WithDrawing(layout with
        {
            Layers = [new(DrawingObjectKind.View, 101), new(DrawingObjectKind.View, 100)]
        });
        check(CanvasObjects.Pick(sheet, reordered, [], inkPoint, 1)?.Id == 100, "Explicit layer order controls coincident view selection");
        var off = layout with
        {
            Views = [layout.Views[0] with { X = -.4f }, layout.Views[1] with { X = 1.4f }]
        };
        var outside = PrintGeneration.Render(source, geometry, "Off page", small with
        {
            Drawing = off
        }, [], sheet, false, default);
        check(outside.Scene!.Views.All(v => v.Bounds.Right < 0 || v.Bounds.X > outside.PixelWidth), "Compositor retains completely off-page view positions");
        check(outside.Scene.Visible.ContainsKey((100, 0)) && outside.Scene.Visible.ContainsKey((101, 0)), "Off-page layers retain per-view part anchors for history and labels");
        var doc = new AnnotationDocument(sheet.Scene);
        var accepted = DrawingAcceptance.Prepare(outside with
        {
            RegeneratedBase = null
        }, doc, true, false);
        accepted.Document.Undo();
        check(accepted.Document.Scene.Drawing == layout, "Undo off-page movement restores the original arrangement");
        accepted.Document.Redo();
        check(accepted.Document.Scene.Drawing == off, "Redo restores exact off-page positions");
        var dimension = new HeightDimension(100, X: -100);
        var dimScene = outside.Scene.WithDimensions([dimension]);
        var decorated = AnnotationPainting.Compose(outside with
        {
            Scene = dimScene.WithFreeText([text])
        }, [], default);
        check(decorated.Rgb.Length == outside.Rgb.Length, "Off-page dimensions and text clip into a fixed-size export buffer");
        var detail = new ExplodedDetail(1, 1, new(-250, -100, 500, 450), "A long assembly caption independent of geometry width");
        var withDetail = ExplodedPainting.Compose(sheet, sheet, [detail], [], default);
        check(withDetail.Details[0].Bounds.X < 0 && withDetail.Scene!.Visible.ContainsKey((1001, 1)), "Exploded detail clips safely and retains independent off-page part anchors");
        var renderedDetail = withDetail.Scene!.Details[0];
        check(renderedDetail.GeometryBounds is { } tight && tight.Width < detail.Bounds.Width && tight.Bottom < renderedDetail.CaptionBounds.Y
            && renderedDetail.CaptionBounds.Width > tight.Width,
            "Exploded geometry has tight independent bounds with its centered caption below it");
        var both = small with
        {
            PartsTable = true,
            FlagTable = true
        };
        var tables = sheet.Scene.WithOptions(both).WithTables(new(new(-40, -20, 620, 600), new(-40, -20, 620, 600)));
        var overlap = AnnotationPainting.Compose(sheet with
        {
            Options = both,
            Scene = tables.WithFreeText([new(4, "OVERLAP", new(-10, 10, 300, 100))])
        }, [], default);
        check(overlap.Rgb.Length == sheet.Rgb.Length, "Overlapping tables and free text can share an off-page position");
        var whitespace = new FreeTextBox(9, "A", new(tile.Bounds.X, tile.Bounds.Y, tile.Width, tile.Height), 12);
        var textScene = sheet.Scene.WithFreeText([whitespace]);
        check(CanvasObjects.Pick(sheet, textScene, [], inkPoint, 1)?.Kind == DrawingObjectKind.View, "Unused space in a text box does not block underlying view ink");
        var tinyLayout = layout with
        {
            Ratio = 100000
        };
        var tinyPlan = DrawingPlacements.Plan(geometry, small, tinyLayout);
        var tinyBoxes = tinyPlan.ToDictionary(v => v.Id, v => v.Bounds);
        var tinyExtents = tinyPlan.ToDictionary(v => v.Id, v => v.Frame!.ContentExtent!.Value);
        var tinyAnchor = new Vector2(tinyBoxes[100].Right, tinyBoxes[100].Bottom);
        var tinyResize = ProjectionGroups.Resize(tinyLayout, 100, tinyBoxes, small, 2, GroupResizeMode.Arrangement, tinyAnchor, tinyExtents);
        var tinyAfter = DrawingPlacements.Plan(geometry, small, tinyResize).Single(v => v.Id == 100).Bounds;
        check(Vector2.Distance(tinyAnchor, new(tinyAfter.Right, tinyAfter.Bottom)) <= 1,
            "Minimum-sized render tiles retain the opposite resize anchor using exact projected extents");
        var tableId = new DrawingObject(DrawingObjectKind.PartsTable, 0);
        check(overlap.ObjectHits!.ContainsKey(tableId) && !overlap.ObjectHits[tableId].Hit(new(400, 500), 1),
            "Table hit regions leave unused panel space transparent");
        check(overlap.ObjectHits[tableId].Hit(new(0, 28), 1), "Table printed rules remain selectable");
        var removedTable = PreviewUpdates.Render(overlap, sheet, overlap.Options with
        {
            PartsTable = false
        }, default);
        check(!removedTable.Options.PartsTable && removedTable.Options.FlagTable && removedTable.Scene!.FreeText.Length == 1,
            "Removing a table preserves the other table and overlapping text");
        var tableDoc = new AnnotationDocument(overlap.Scene!);
        var removedAccepted = DrawingAcceptance.Prepare(removedTable, tableDoc, true, false);
        removedAccepted.Document.Undo();
        check(removedAccepted.Document.Scene.Options.PartsTable, "Undo restores the deleted table as one accepted edit");
        removedAccepted.Document.Redo();
        check(!removedAccepted.Document.Scene.Options.PartsTable, "Redo removes the selected table again");
        var hitShape = new CanvasHitShape();
        hitShape.Pixel(31, 31);
        hitShape.Pixel(32, 32);
        check(hitShape.Hit(new(31, 31), 1) && hitShape.Hit(new(32, 32), 1) && !hitShape.Hit(new(15, 15), 1),
            "Sparse silhouette picking crosses tile boundaries without filling empty interiors");
        var topTableBox = new Box(tile.Bounds.X - 10, tile.Bounds.Y - 100, 620, 600);
        var topTableOptions = sheet.Options with
        {
            PartsTable = true
        };
        var topTableScene = sheet.Scene.WithOptions(topTableOptions).WithTables(new(topTableBox));
        var topTable = AnnotationPainting.Compose(sheet with
        {
            Options = topTableOptions,
            Scene = topTableScene
        }, [], default);
        var openInk = Enumerable.Range(0, tile.Pixels.Length).Where(i => tile.Pixels[i] > 0)
            .Select(i => new Vector2(tile.Bounds.X + i % tile.Width, tile.Bounds.Y + i / tile.Width))
            .First(p => topTableBox.Contains(p) && !topTable.ObjectHits![tableId].Hit(p, 1));
        check(CanvasObjects.Pick(topTable, topTable.Scene!, [], openInk, 1)?.Kind == DrawingObjectKind.View,
            "Empty table space selects underlying view ink even when the table is topmost");

        var movedOnPage = CanvasMotion.MoveViews(sheet.Scene, [], layout, 100, new(-5000, 9000), small);
        var movedPlan = DrawingPlacements.Plan(geometry, small, movedOnPage).Single(v => v.Id == 100);
        var oldContent = CanvasObjects.ContentBounds(sheet.Scene, [], new(DrawingObjectKind.View, 100));
        var shift = movedPlan.Bounds.Center - sheet.Scene.Views.Single(v => v.Id == 100).Bounds.Center;
        check(oldContent.X + shift.X >= -1 && oldContent.Bottom + shift.Y <= small.Page.Height + 1,
            "Pointer movement stops visible view content at the page edge");
        var largeBox = CanvasMotion.KeepOnPage(new(-9000, 9000, small.Page.Width + 500, small.Page.Height + 500), small);
        check(largeBox.X <= 0 && largeBox.Right >= small.Page.Width && largeBox.Y <= 0 && largeBox.Bottom >= small.Page.Height,
            "An oversized object stays reachable while moving without shrinking its dimensions");
        var panel = CanvasMotion.KeepOnPage(new(-400, 9000, 300, 100), small);
        check(panel.X == 0 && panel.Bottom == small.Page.Height && panel.Width == 300 && panel.Height == 100,
            "Text and panel pointer movement preserves size and keeps the box on page");
        var linked = layout with
        {
            Views = [layout.Views[0], layout.Views[1] with { Parent = 100, X = .7f }]
        };
        var linkedSheet = PrintGeneration.Render(source, geometry, "Linked drag", small with
        {
            Drawing = linked
        }, [], null, false, default);
        var linkedMoved = CanvasMotion.MoveViews(linkedSheet.Scene!, [], linked, 100, new(8000, -8000), small);
        check(Math.Abs((linkedMoved.Views[1].X - linkedMoved.Views[0].X) - (linked.Views[1].X - linked.Views[0].X)) < .0001f
            && linkedMoved.Views[1].Y == linkedMoved.Views[0].Y,
            "Clamping a primary drag preserves linked projection spacing");
        var aligned = linked with
        {
            Views = [linked.Views[0] with { Y = 1.02f }, linked.Views[1] with { Y = 1.02f, Alignment = 2 }]
        };
        var alignedScene = PrintGeneration.Render(source, geometry, "Aligned edge", small with
        {
            Drawing = aligned
        }, [], null, false, default).Scene!;
        var alignedMoved = CanvasMotion.MoveViews(alignedScene, [], aligned, 101, new(9000, 9000), small);
        check(alignedMoved.Views[1].Y == alignedMoved.Views[0].Y,
            "A secondary drag preserves its primary alignment even when scaling previously crossed a page edge");
        check(renderedDetail.Definition.Bounds.Width <= renderedDetail.GeometryBounds!.Value.Width + 64,
            "Exploded edit frame follows actual geometry width rather than the requested empty frame");
        var inflated = renderedDetail.Definition with
        {
            Bounds = renderedDetail.Definition.Bounds with
            {
                X = renderedDetail.Definition.Bounds.X - 200,
                Width = renderedDetail.Definition.Bounds.Width + 400
            }
        };
        var refitted = ExplodedGeometry.Render(new(source.Assembly, source.Maps), inflated, default, small);
        check(Math.Abs(refitted.Definition.Bounds.Width - renderedDetail.Definition.Bounds.Width) < 1
            && Math.Abs(refitted.Frame.Scale - renderedDetail.Frame.Scale) < .001f,
            "Empty frame enlargement cannot leave a tiny assembly in a large selection window");
        var resizedDetail = DetailFrames.Resize(renderedDetail, 1.5f, small, true);
        var resizedTile = ExplodedGeometry.Render(new(source.Assembly, source.Maps), resizedDetail, default, small);
        check(Math.Abs(resizedTile.Frame.Scale / renderedDetail.Frame.Scale - 1.5f) < .001f
            && resizedTile.Pixels.Width > renderedDetail.Pixels.Width && resizedTile.Pixels.Height > renderedDetail.Pixels.Height,
            "Exploded corner resize scales physical geometry and its fitted frame together");
        check(Math.Abs(resizedTile.Definition.Bounds.X - renderedDetail.Definition.Bounds.X) < 1
            && Math.Abs(resizedTile.Definition.Bounds.Y - renderedDetail.Definition.Bounds.Y) < 1,
            "Exploded corner resize preserves the opposite top-left anchor");
        var edgeDetail = renderedDetail with
        {
            Definition = renderedDetail.Definition with
            {
                Bounds = renderedDetail.Definition.Bounds with
                {
                    X = small.Page.Width - 50,
                    Y = small.Page.Height - 50
                }
            }
        };
        var grown = DetailFrames.Resize(edgeDetail, 1.5f, small);
        check(grown.Bounds.Right > small.Page.Width && grown.Bounds.Bottom > small.Page.Height,
            "Scale changes may extend beyond the page and are never rejected by its edge");
        var movedDetail = renderedDetail.Definition with
        {
            Bounds = renderedDetail.Definition.Bounds with
            {
                X = 100,
                Y = 100
            }
        };
        var detailMoved = ExplodedPainting.Compose(sheet, withDetail, [movedDetail], [], default);
        check(ReferenceEquals(detailMoved.Scene!.Details[0].Pixels, renderedDetail.Pixels)
            && detailMoved.Details[0].Bounds == movedDetail.Bounds,
            "Dragging fitted exploded views reuses exact pixels without changing scale or frame size");
        var ddoc = DrawingAcceptance.Prepare(withDetail with
        {
            RegeneratedBase = null
        }, null, false, false).Document;
        var changedDetailSheet = ExplodedPainting.Compose(sheet, withDetail, [resizedDetail], [], default);
        var detailHistory = DrawingAcceptance.Prepare(changedDetailSheet with
        {
            RegeneratedBase = null
        }, ddoc, true, false).Document;
        detailHistory.Undo();
        check(detailHistory.Details.Single().Sizing == renderedDetail.Sizing, "Undo restores exact exploded scale and framing");
        detailHistory.Redo();
        check(detailHistory.Details.Single().Sizing!.Ratio == resizedDetail.Sizing!.Ratio, "Redo restores the resized exploded view");
        // A custom explosion spread must survive dragging a single group; other
        // geometry stays put while the raster frame grows around the moved part.
        Mesh Shifted(string name, double x) => geometry.Meshes[0] with
        {
            Name = name,
            Vertices = points.Select(p => new[] { p[0], p[1] + x, p[2] }).ToArray()
        };
        var assembly = new AssemblyCapture(new([Shifted("Root", 0), Shifted("Moved", 6), Shifted("Stationary", 12)]),
            [new(1, null, "Root", "root", false), new(2, 1, "Moved", "moved", false), new(3, 1, "Stationary", "stationary", false)],
            [1, 2, 3], [false, false, false]);
        var explosionSource = new ExplodedSource(assembly, source.Maps);
        var initial = ExplodedGeometry.Render(explosionSource, new(2, 1, new(250, 250, 650, 650), "Custom spread", Axis: 1), default, small);
        var custom = ExplodedGeometry.Render(explosionSource, initial.Definition with
        {
            Sizing = initial.Sizing! with
            {
                Spread = initial.Sizing.Spread!.Value * .65f
            }
        }, default, small);
        var groupOffset = new Vector3(35, 0, 0);
        var dragged = DetailFrames.PrepareEdit(custom.Definition, custom.Definition with
        {
            Groups = [new(2, groupOffset)],
            Camera = custom.Frame
        }, small);
        var expanded = ExplodedGeometry.Render(explosionSource, dragged, default, small);
        Vector2 Vertex(RenderedDetail t, int mesh, int vertex = 0)
        {
            var g = ExplodedGeometry.Build(assembly, t.Definition);
            var placed = DetailPartSizing.Free(g, t.Sizing!, t.Definition.View, small);
            var v = ExplodedGeometry.World(placed.Geometry.Capture.Meshes[mesh].Vertices[vertex]) - t.Frame.Center;
            return new(t.Definition.Bounds.X + 12 + t.Frame.Width * .5f + Vector3.Dot(v, t.Frame.Right) * t.Frame.Scale,
                t.Definition.Bounds.Y + 12 + t.Frame.Height * .5f - Vector3.Dot(v, t.Frame.Up) * t.Frame.Scale);
        }
        check(expanded.Frame.Width > custom.Frame.Width && expanded.Sizing!.Ratio == custom.Sizing!.Ratio
            && expanded.Sizing.Spread == custom.Sizing.Spread,
            "Dragging an exploded part beyond its frame expands the frame without changing scale or custom spread");
        check(Vector2.Distance(Vertex(expanded, 0), Vertex(custom, 0)) < .02f
            && Vector2.Distance(Vertex(expanded, 2), Vertex(custom, 2)) < .02f,
            "Expanding an exploded frame leaves the root and other parts at their exact paper positions");
        var expectedDelta = new Vector2(Vector3.Dot(groupOffset, custom.Frame.Right), -Vector3.Dot(groupOffset, custom.Frame.Up)) * custom.Sizing!.Spread!.Value;
        check(Vector2.Distance(Vertex(expanded, 1) - Vertex(custom, 1), expectedDelta) < .02f,
            "Exploded part movement matches the drag distance at the existing custom spread");
        var scaled = ExplodedGeometry.Render(explosionSource, DetailFrames.Resize(expanded, 1.4f, small, true), default, small);
        check(Vector2.Distance(Vertex(scaled, 1) - Vertex(scaled, 0), (Vertex(expanded, 1) - Vertex(expanded, 0)) * 1.4f) < .02f
            && Vector2.Distance(Vertex(scaled, 2) - Vertex(scaled, 0), (Vertex(expanded, 2) - Vertex(expanded, 0)) * 1.4f) < .02f
            && Vector2.Distance(Vertex(scaled, 1, 1) - Vertex(scaled, 1), (Vertex(expanded, 1, 1) - Vertex(expanded, 1)) * 1.4f) < .02f,
            "Resizing an edited exploded arrangement scales parts and all relative spacing by one factor");
        check(Vector2.Distance(new(scaled.Definition.Bounds.X, scaled.Definition.Bounds.Y), new(expanded.Definition.Bounds.X, expanded.Definition.Bounds.Y)) < 1,
            "Resizing an expanded exploded frame preserves its opposite corner");

        // Flagnotes use the same deletion path whether rendered as table keys or
        // full text. Their stable numbers and undo history must survive deletion.
        foreach (bool hasTable in new[] { false, true })
        {
            var flagOptions = small with
            {
                FlagTable = hasTable
            };
            var flagScene = sheet.Scene!.WithOptions(flagOptions);
            var flagDoc = new AnnotationDocument(flagScene);
            var first = flagDoc.Draft(100, 0, out _, note: "REMOVE BEFORE FLIGHT")! with
            {
                X = 1200,
                Y = 300
            };
            check(flagDoc.Apply([first], out _), "First flagnote commits with table=" + hasTable);
            var second = flagDoc.Draft(100, 0, out _, note: "VERIFY CONNECTION")! with
            {
                X = 1200,
                Y = 450
            };
            check(flagDoc.Apply([.. flagDoc.Labels, second], out _), "Second flagnote commits with table=" + hasTable);
            var flags = AnnotationPainting.Compose(sheet with
            {
                Options = flagOptions,
                Scene = flagDoc.Scene
            }, flagDoc.Labels, default);
            var textBox = AnnotationText.Bounds(flagDoc.Labels.Single(l => l.Id == first.Id));
            bool selectable = false;
            for (float y = textBox.Y; y < textBox.Bottom && !selectable; y += 3)
                for (float x = textBox.X; x < textBox.Right && !selectable; x += 3)
                    selectable = CanvasObjects.Pick(flags, flags.Scene!, flagDoc.Labels, new(x, y), 2) == new DrawingObject(DrawingObjectKind.Label, first.Id);
            check(selectable, "Flagnote ink can be selected for deletion with table=" + hasTable);
            check(flagDoc.Delete(first.Id, out _) && flagDoc.Labels.Single().Id == second.Id
                && flagDoc.Labels.Single().FlagnoteNumber == 2
                && PrintTables.Rows(flagDoc.Scene, flagDoc.Labels, 1).All(r => r.Key != "F1"),
                "Deleting a flagnote removes its table row and preserves the other note with table=" + hasTable);
            flagDoc.Undo();
            check(flagDoc.Labels.Select(l => l.Text).SequenceEqual(new[] { first.Text, second.Text }),
                "Undo restores the deleted flagnote and its text with table=" + hasTable);
            flagDoc.Redo();
            check(flagDoc.Labels.Length == 1 && flagDoc.Labels[0].Id == second.Id,
                "Redo removes the same flagnote with table=" + hasTable);
        }
        bool resourceRejected = false;
        try
        {
            ExplodedGeometry.Render(new(source.Assembly, source.Maps), resizedDetail, default, small, remainingPixels: 10);
        }
        catch (InvalidOperationException) { resourceRejected = true; }
        check(resourceRejected, "Fitted detail dimensions are checked against remaining layer pixels before raster allocation");

    }
}
