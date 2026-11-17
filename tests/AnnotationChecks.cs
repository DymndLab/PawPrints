using System.Numerics;
using System.Text.Json;
using Blueprint.Rendering;

static class AnnotationChecks
{

    public static void Run(Action<bool, string> check, Capture capture, Dictionary<string, Texture> maps, string output, string captureRoot)
    {
        check(PartNames.Display("CoreServiceModuleA_Prefab_SetA2DoubleA", null) == "Service Module / Set A2 Double A" && PartNames.Display("CoreFuelTankA_Prefab_LF3W6HC", null) == "Fuel Tank / LF3W6HC" && PartNames.Display("EngineId", "LR91 Vac") == "LR91 Vac", "Part names use native names or the configured template formatting without losing variant codes");
        var options = new PrintOptions(Layout: 2);
        var views = SheetLayout.Plan(capture, options);
        var front = views.Single(v => v.Id == 1).Frame!;
        var side = views.Single(v => v.Id == 2).Frame!;
        var top = views.Single(v => v.Id == 3).Frame!;
        var iso = views.Single(v => v.Id == 4).Frame!;
        check(views.Length == 4 && front.Scale == side.Scale && side.Scale == top.Scale, "Engineering sheet has four views; all orthographic scales are identical");
        var p = new Vector3(4, 7, 9) - front.Center;
        check(Vector3.Dot(p, front.Right) == Vector3.Dot(p, top.Right) && Vector3.Dot(p, front.Up) == Vector3.Dot(p, side.Up), "Front/top horizontal and front/side vertical coordinates align");
        float ProjectionLength(Vector3 axis) => new Vector2(Vector3.Dot(axis, iso.Right), Vector3.Dot(axis, iso.Up)).Length();
        check(Math.Abs(ProjectionLength(Vector3.UnitX) - ProjectionLength(Vector3.UnitY)) < 1e-6 && Math.Abs(ProjectionLength(Vector3.UnitY) - ProjectionLength(Vector3.UnitZ)) < 1e-6, "Isometric projects all three axes at equal length");
        var hidden = SheetLayout.Plan(capture, options with
        {
            VisibleViews = 5
        });
        check(hidden.Select(v => v.Id).Order().SequenceEqual(new[] { 1, 3 }) && hidden.All(v => v.Frame!.Scale == front.Scale), "Hiding views preserves orthographic scale and slot alignment");
        try
        {
            SheetLayout.Plan(capture, options with
            {
                VisibleViews = 0
            });
            throw new Exception("Accepted empty layout");
        }
        catch (InvalidOperationException) { check(true, "Empty multi-view layout is rejected"); }

        var owners = Enumerable.Repeat(-1, Sheet.Width * Sheet.Height).ToArray();
        void Rect(int id, int x, int y, int w, int h)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    owners[yy * Sheet.Width + xx] = id;
        }
        Rect(0, 800, 800, 400, 300);
        LabelPart[] parts = [new(0, "Tank A", "TankA", [0]), new(1, "Nearby hardware", "Hardware", [1])];
        ViewRegion[] single = [new(1, "FRONT", new(130, 280, 2140, 2200), null)];
        var scene = new RenderScene(new byte[Sheet.Width * Sheet.Height * 3], owners, parts, single);
        var tightViewScene = new RenderScene(new byte[owners.Length * 3], owners, parts,
            [new(1, "FRONT", new(800, 800, 400, 300), null)]);
        var tightDoc = new AnnotationDocument(tightViewScene);
        int outsideLabel = tightDoc.Add(1, 0, out var outsideReason);
        check(outsideLabel >= 0 && tightDoc.Labels.Length == 1,
            "Automatic full-name label can use clear paper beyond tight craft bounds: " + outsideReason);
        var outsideBox = AnnotationText.Bounds(tightDoc.Labels.Single());
        check(PageLayout.Fits(outsideBox, tightViewScene.Options) && !outsideBox.Overlaps(tightViewScene.Views.Single().Bounds)
            && LeaderRouting.TryLayout(tightViewScene, tightDoc.Labels, out var outsideRoutes, out _)
            && outsideRoutes.All(r => LeaderRouting.ClearStroke(tightViewScene, r.Start, r.End, r.Label.PartId, r.Label.Tip)),
            "Expanded automatic label area preserves paper bounds and clear near-edge leaders");
        var flagDoc = new AnnotationDocument(scene);
        var flagDraft = new FlagnotePlacement(flagDoc.Draft(1, 0, out _, note: "Custom flagnote")!, true);
        check(flagDraft.Stage == FlagnoteStage.Start && !flagDraft.SetText("Too early") && flagDoc.Labels.Length == 0, "Custom flagnote starts with geometry; text cannot commit before endpoint selection");
        flagDraft.PickStart(new(800, 950));
        check(flagDraft.Stage == FlagnoteStage.End && flagDraft.Label.AttachmentPinned && flagDraft.Label.Attachment == new LeaderPoint(800, 950), "First flagnote click pins the chosen leader start without advancing both steps");
        flagDraft.PickEnd(new(1400, 920));
        check(flagDraft.Stage == FlagnoteStage.Text && flagDraft.Label.X == 1400 && flagDraft.Label.Y == 920 && flagDoc.Labels.Length == 0 && !flagDoc.CanUndo, "Second custom flagnote click requests text without creating a label or history entry");
        check(!flagDraft.SetText("  \n ") && flagDraft.Stage == FlagnoteStage.Text, "Empty custom text keeps the geometry draft uncommitted");
        check(flagDraft.SetText("Custom acceptance note") && flagDraft.Stage == FlagnoteStage.Ready && flagDraft.Label.Attachment == new LeaderPoint(800, 950) && flagDraft.Label.X == 1400, "Custom text confirmation preserves the chosen geometry");
        check(flagDoc.Apply([flagDraft.Label], out _) && flagDoc.Labels.Single().Text == "Custom acceptance note", "Completed custom flagnote commits its text and leader together");
        flagDoc.Undo();
        check(flagDoc.Labels.Length == 0 && !flagDoc.CanUndo, "One Undo removes the entire flagnote; endpoint picks never enter history");
        var presetDraft = new FlagnotePlacement(flagDoc.Draft(1, 0, out _, note: "REMOVE BEFORE FLIGHT")!, false);
        presetDraft.PickStart(new(800, 950));
        presetDraft.PickEnd(new(400, 900));
        check(presetDraft.Stage == FlagnoteStage.Ready && presetDraft.Label.Text == "REMOVE BEFORE FLIGHT" && flagDoc.Labels.Length == 0, "Preset flagnote becomes ready after its two clicks without requesting custom text");
        var label = new PartLabel(1, 1, 0, "Tank A", 400, 900);
        check(LeaderRouting.TryLayout(scene, [label], out var route, out _) && route[0].End.X == 800, "Leader stops at incoming near edge, never far edge");
        check(LeaderRouting.FirstHit(scene, new(400, 950), new(1200, 950), out var hit, out var id) && hit.X == 800 && id == 0, "First-hit ray terminates at first occupied surface");
        check(!LeaderRouting.TryLayout(scene, [label with { X = 900 }], out _, out _), "Label footprint cannot cover craft geometry");
        check(!LeaderRouting.TryLayout(scene, [label, label with { Id = 2, X = 410 }], out _, out _), "Overlapping label text boxes are rejected");
        check(!LeaderRouting.TryLayout(scene, [label with { X = PageLayout.Margin - 1 }], out _, out _), "Automatic label cannot escape the printed drawing area");
        check(Enum.GetValues<LeaderTip>().All(t => LeaderRouting.TryLayout(scene, [label with { Tip = t }], out _, out _)), "Dot and both closed triangle tips support clear routes");
        var pinned = label with
        {
            Attachment = new(800, 960),
            Socket = new(1, .5f),
            AttachmentPinned = true
        };
        check(LeaderRouting.TryLayout(scene, [pinned], out var pinnedRoute, out _) && pinnedRoute[0].End == new Vector2(800, 960), "Manually pinned attachment keeps the exact chosen near edge");
        check(!LeaderRouting.TryLayout(scene, [pinned with { Attachment = new(1199, 960) }], out _, out _), "Manual attachment rejects far side instead of silently choosing a different edge");
        check(LeaderRouting.TryLayout(scene, [pinned with { Y = 940 }], out var movedRoute, out _) && movedRoute[0].End == new Vector2(800, 960), "Moving label-end handle preserves pinned craft attachment");
        check(LeaderRouting.TryLayout(scene, [pinned with { Socket = new(0, .5f) }], out var nearSocket, out _) && nearSocket[0].Start.X == AnnotationText.Bounds(pinned).Right, "Old generated socket is replaced with the near-side midpoint instead of crossing text");
        var snap = LeaderRouting.SnapAttachment(scene, label, new(793, 957), 12);
        check(snap == new LeaderPoint(800, 957) && LeaderRouting.SnapAttachment(scene, label, new(500, 500), 12) == null, "Attachment snapping uses full-resolution boundary and a bounded pickup radius");
        var pinDoc = new AnnotationDocument(scene, [label]);
        pinDoc.Change(pinned, out _);
        pinDoc.Undo();
        check(pinDoc.Labels[0].Attachment == null, "Undo restores automatic attachment");
        pinDoc.Redo();
        check(pinDoc.Labels[0].Attachment == pinned.Attachment && pinDoc.Labels[0].Socket == pinned.Socket, "Redo restores both endpoint choices");
        var tight = new RenderScene(scene.BaseRgb, owners, parts, [new(1, "FRONT", new(790, 790, 420, 320), null)], [PageLayout.Placement(new())]);
        var manualDoc = new AnnotationDocument(tight);
        int failed = manualDoc.Add(1, 0, out _);
        var draft = manualDoc.Draft(1, 0, out _);
        check(failed < 0 && draft != null && manualDoc.Labels.Length == 0 && !manualDoc.CanUndo, "Reserved paper blocks automatic placement but still offers a non-committed manual draft");
        check(manualDoc.Apply([draft! with { X = 400, Y = 900 }], out _) && manualDoc.Labels[0].ManualPlacement, "Manual fallback places label in clear paper outside cramped view slot");
        var reserved = new RenderScene(scene.BaseRgb, owners, parts, single, [new(400, 900, 200, 50)]);
        check(!LeaderRouting.TryLayout(reserved, [pinned], out _, out _), "Manual text placement preserves view captions");
        var pinSheet = new Sheet("Pinned fixture", new(), scene.BaseRgb, Sheet.MakePreview(scene.BaseRgb), []) { Scene = scene };
        check(AnnotationPainting.Compose(pinSheet, [pinned], default).Labels[0].Attachment == pinned.Attachment, "Export retains chosen endpoint geometry");
        check(LeaderRouting.NearestBoxEdge(new(100, 100, 200, 50), new(40, 120)) == new Vector2(100, 120), "Leader reaches nearest label edge rather than following a ray through its centre");
        var generated = (pinned with
        {
            AttachmentPinned = false
        }).MoveTo(1250, 900);
        check(generated.Attachment == null && generated.Socket == null && generated.ManualPlacement, "Dragging an auto-generated label releases its generated endpoint and socket");
        var explicitPin = (pinned with
        {
            AttachmentPinned = true,
            ManualPlacement = true
        }).MoveTo(1250, 900);
        check(explicitPin.Attachment == pinned.Attachment && explicitPin.Socket == null, "Dragging text preserves only an explicitly hand-positioned craft endpoint");
        var plain = label with
        {
            Text = "Fuel Tank / Baypart Tank B",
            ManualPlacement = true,
            Attachment = new(700, 700)
        };
        check(LeaderRouting.TryLayout(scene, [plain], out var plainRoute, out _) && plainRoute[0].Start.Y == AnnotationText.Bounds(plain).Center.Y && plainRoute[0].Start.X == AnnotationText.Bounds(plain).Right && plainRoute[0].End.X == 800, "Plain multiline labels connect at near-side midpoint and replace stale generated part endpoints");
        var socketKey = plain with
        {
            Key = "A"
        };
        var keyedBox = AnnotationText.Bounds(socketKey);
        check(LeaderRouting.TextSocket(socketKey, keyedBox, new(keyedBox.Right + 40, keyedBox.Bottom + 40)) == new Vector2(keyedBox.Right, keyedBox.Bottom), "Boxed keys connect at a nearest corner or side midpoint");
        var coneOwners = Enumerable.Repeat(-1, Sheet.Width * Sheet.Height).ToArray();
        var coneBoundary = new List<Vector2>();
        for (int y = 500; y <= 1400; y++)
            for (int x = 800 - (y - 500) / 3; x <= 800 + (y - 500) / 3; x++)
                coneOwners[y * Sheet.Width + x] = 0;
        for (int y = 500; y <= 1400; y++)
            for (int x = 500; x <= 1100; x++)
            {
                int q = y * Sheet.Width + x;
                if (coneOwners[q] == 0 && (coneOwners[q - 1] < 0 || coneOwners[q + 1] < 0 || coneOwners[q - Sheet.Width] < 0 || coneOwners[q + Sheet.Width] < 0))
                    coneBoundary.Add(new(x, y));
            }
        var coneScene = new RenderScene(scene.BaseRgb, coneOwners, parts, single);
        var coneLabel = new PartLabel(1, 1, 0, "Cone", 1150, 850, ManualPlacement: true, Key: "A");
        var coneBox = AnnotationText.Bounds(coneLabel);
        float expectedDistance = coneBoundary.Min(e => Vector2.DistanceSquared(e, LeaderRouting.TextSocket(coneLabel, coneBox, e)));
        check(LeaderRouting.TryLayout(coneScene, [coneLabel], out var coneRoute, out _) && Math.Abs(Vector2.DistanceSquared(coneRoute[0].Start, coneRoute[0].End) - expectedDistance) < .001f && coneRoute[0].End.Y > 850, "Sloped cone leader uses the exact shortest boundary-to-box path instead of shooting upward");
        var oldConePin = coneLabel with
        {
            Attachment = new(800, 500),
            Socket = new(0, 0)
        };
        check(LeaderRouting.TryLayout(coneScene, [oldConePin.MoveTo(1150, 850)], out var releasedRoute, out _) && releasedRoute[0].End == coneRoute[0].End, "Moving an automatically pinned cone label recalculates the nearest craft edge");
        var heldCone = (oldConePin with
        {
            AttachmentPinned = true
        }).MoveTo(1150, 850);
        check(LeaderRouting.TryLayout(coneScene, [heldCone], out var heldRoute, out _) && heldRoute[0].End == new Vector2(800, 500), "An explicitly chosen cone endpoint remains pinned even when it is not the shortest path");
        Rect(1, 650, 700, 20, 500);
        var blocked = new RenderScene(scene.BaseRgb, owners, parts, single);
        check(!LeaderRouting.TryLayout(blocked, [label], out _, out _), "Intervening hardware blocks all routes to an occluded target");
        // A sliver off the ray catches a stroke/endpoint collision that a center-only test misses.
        var corridorOwners = Enumerable.Repeat(-1, Sheet.Width * Sheet.Height).ToArray();
        for (int y = 900; y <= 910; y++)
            for (int x = 800; x <= 900; x++)
                corridorOwners[y * Sheet.Width + x] = 0;
        for (int x = 795; x <= 800; x++)
            corridorOwners[897 * Sheet.Width + x] = 1;
        var corridor = new RenderScene(scene.BaseRgb, corridorOwners, parts, single);
        var narrow = new PartLabel(1, 1, 0, "Tank A", 400, 881);
        if (LeaderRouting.TryLayout(corridor, [narrow], out var safe, out _))
            check(safe[0].End.Y >= 903, "Endpoint footprint avoids hardware just outside the center ray");
        else
            check(true, "Unsafe narrow endpoint route is rejected");
        // Restore the simple, unobstructed fixture for edit history and export checks.
        for (int y = 700; y < 1200; y++)
            for (int x = 650; x < 670; x++)
                owners[y * Sheet.Width + x] = -1;
        var doc = new AnnotationDocument(scene);
        check(doc.Apply([label], out _), "Add label enters document history");
        check(doc.Change(label with
        {
            Y = 1000
        }, out _), "Valid label move commits");
        doc.Undo();
        check(doc.Labels.Single().Y == 900 && doc.CanRedo, "Undo restores the prior placement");
        doc.Redo();
        check(doc.Labels.Single().Y == 1000, "Redo restores the moved placement");
        check(!doc.Change(label with
        {
            X = 900
        }, out _) && doc.Labels.Single().Y == 1000, "Rejected placement leaves document unchanged");
        doc.Undo();
        doc.Change(label with
        {
            Text = "Renamed tank"
        }, out _);
        check(!doc.CanRedo && doc.Labels.Single().Text == "Renamed tank", "Rename after undo clears the redo branch");
        doc.Delete(1, out _);
        doc.Undo();
        check(doc.Labels.Length == 1, "Deleted labels can be restored");
        doc.Change(doc.Labels.Single() with
        {
            Visible = false
        }, out _);
        check(!doc.Labels.Single().Visible, "Hide keeps label identity and placement");
        doc.Undo();
        var blank = new Sheet("Fixture", new(), scene.BaseRgb, Sheet.MakePreview(scene.BaseRgb), []) { Scene = scene };
        var painted = AnnotationPainting.Compose(blank, doc.Labels, default);
        var cleared = AnnotationPainting.Compose(painted, [], default);
        check(!painted.Rgb.SequenceEqual(blank.Rgb) && cleared.Rgb.SequenceEqual(blank.Rgb), "Export paints annotations; removing them restores pristine paper without grid erasure");
        check(scene.Pick(new(900, 900)) == (1, 0) && scene.Pick(new(700, 700)) == null, "Click picking returns visible part identity only");
        var highlight = scene.Highlight(1, 0);
        check(highlight.Sum(r => r.Width * r.Height) == 400 * 300 && highlight.All(r => scene.Owner((int)r.X, (int)r.Y) == 0), "Hover highlight contains exactly visible pixels of the selected part");
        var tableOptions = new PrintOptions(PartsTable: true, FlagTable: true, DrawingSize: 100);
        LabelPart[] repeated = [new(0, "Tank A", "TankA", [0]), new(1, "Tank A", "TankA", [1]), new(2, "Engine B", "EngineB", [2])];
        var tableScene = new RenderScene(scene.BaseRgb, owners, repeated, single, [new(1640, 280, 620, 2200)], tableOptions);
        var rows = PrintTables.Catalog(tableScene);
        var quantityLabel = new PartLabel(41, 1, 0, "My custom tank name", 400, 800, ManualPlacement: true);
        var withQuantity = LabelQuantities.Set(tableScene, quantityLabel, true);
        check(withQuantity.Quantity == 2 && withQuantity.Text == quantityLabel.Text && string.Join(" ", AnnotationText.Lines(withQuantity)).EndsWith("(QTY 2)"), "QTY uses actual matching craft parts without rewriting custom label text");
        check(!string.Join(" ", AnnotationText.Lines(LabelQuantities.Set(tableScene, quantityLabel with
        {
            PartId = 2
        }, true))).Contains("QTY") && !string.Join(" ", AnnotationText.Lines(withQuantity with
        {
            FlagNote = true
        })).Contains("QTY") && AnnotationText.Lines(withQuantity with
        {
            Key = "B"
        }).SequenceEqual(new[] { "B" }), "Singletons, flagnotes and table keys do not receive duplicate quantity text");
        var qtyDoc = new AnnotationDocument(tableScene, [quantityLabel]);
        check(qtyDoc.Change(withQuantity, out _), "Quantity toggle is an annotation edit");
        qtyDoc.Undo();
        check(!qtyDoc.Labels.Single().ShowQuantity && qtyDoc.Labels.Single().Text == quantityLabel.Text, "Undo quantity toggle restores plain text");
        qtyDoc.Redo();
        check(qtyDoc.Labels.Single().ShowQuantity && qtyDoc.Labels.Single().Quantity == 2, "Redo restores QTY display and count");
        check(rows.Length == 2 && rows.Single(r => r.Name == "Tank A").Quantity == 2 && PrintTables.PartKey(tableScene, 0) == PrintTables.PartKey(tableScene, 1), "Parts table groups native template/name identities and counts instances once, across all views");
        var tableDoc = new AnnotationDocument(tableScene);
        var keyed = tableDoc.Draft(1, 0, out _, LeaderLine.Solid, LeaderTip.None)! with
        {
            X = 500,
            Y = 900
        };
        check(keyed.Key == "B" && AnnotationText.Bounds(keyed).Width == 54 && tableDoc.Apply([keyed], out _), "Parts-table labels use compact local letter keys and selected print style");
        var flag = tableDoc.Draft(1, 0, out _, note: "REMOVE BEFORE FLIGHT")! with
        {
            X = 900,
            Y = 650
        };
        check(flag.FlagNote && flag.Key == "F1" && tableDoc.Apply([.. tableDoc.Labels, flag], out _), "Placed flagnotes have stable numbered references independent of part keys");
        var priorPartLabels = Enumerable.Range(1, 11).Select(i => keyed with { Id = i, Visible = false }).ToArray();
        var numbered = new AnnotationDocument(tableScene, priorPartLabels);
        var firstFlag = numbered.Draft(1, 0, out _, note: "First note")! with
        {
            X = 900,
            Y = 650
        };
        check(firstFlag.Id == 12 && firstFlag.FlagnoteNumber == 1 && firstFlag.Key == "F1",
            "Eleven part annotations do not consume flagnote numbers");
        check(numbered.Apply([.. numbered.Labels, firstFlag], out _), "First independent flagnote commits");
        var secondFlag = numbered.Draft(1, 0, out _, note: "Second note")! with
        {
            X = 1250,
            Y = 650
        };
        check(secondFlag.Key == "F2" && numbered.Apply([.. numbered.Labels, secondFlag], out _),
            "Second flagnote uses F2 regardless of its annotation ID");
        var withoutFlagTable = tableScene.WithOptions(tableScene.Options with
        {
            FlagTable = false
        });
        var unkeyed = FlagnoteNumbers.Normalize(numbered.Labels, false);
        check(unkeyed.Where(l => l.FlagNote).All(l => l.Key == null) &&
            PrintTables.Rows(withoutFlagTable, unkeyed, 1).Select(r => r.Key).SequenceEqual(new[] { "F1", "F2" }),
            "Hidden flagnote table retains its independent reference numbers");
        var rekeyed = FlagnoteNumbers.Normalize(unkeyed, true);
        check(rekeyed.Where(l => l.FlagNote).Select(l => l.Key).SequenceEqual(new[] { "F1", "F2" }),
            "Showing the table restores F1/F2 rather than annotation IDs");
        check(numbered.Delete(firstFlag.Id, out _) && numbered.Labels.Single(l => l.FlagNote).Key == "F2",
            "Deleting one flagnote does not silently renumber surviving references");
        numbered.Undo();
        check(numbered.Labels.Where(l => l.FlagNote).Select(l => l.Key).SequenceEqual(new[] { "F1", "F2" }),
            "Undo restores the same flagnote references");
        numbered.Redo();
        check(numbered.Draft(1, 0, out _, note: "Replacement")!.Key == "F1",
            "A new flagnote can reuse a deleted number without collisions");
        var legacyFlag = firstFlag with
        {
            Key = "F12",
            FlagnoteNumber = 0
        };
        check(FlagnoteNumbers.Normalize([.. priorPartLabels, legacyFlag], true).Last().Key == "F1",
            "Legacy label-ID-derived flagnote keys normalize to their own sequence");
        var noTableNotes = new AnnotationDocument(withoutFlagTable, priorPartLabels);
        check(noTableNotes.Draft(1, 0, out _, note: "Text note") is { Key: null, FlagnoteNumber: 1 },
            "Notes placed with the table disabled also start their own numbering at one");
        var tableCanvas = new Canvas((byte[])scene.BaseRgb.Clone(), Sheet.Width, Sheet.Height, new(255, 255, 255));
        PrintTables.Paint(tableCanvas, tableScene, tableDoc.Labels);
        check(tableCanvas.Runs.Count(r => r.Text == "REMOVE BEFORE FLIGHT") == 1 && tableCanvas.Runs.Any(r => r.Text == "2"), "Tables print actual placed note text and part quantities");
        var hiddenNotes = new Canvas((byte[])scene.BaseRgb.Clone(), Sheet.Width, Sheet.Height, new(255, 255, 255));
        PrintTables.Paint(hiddenNotes, tableScene, [keyed, flag with { Visible = false }]);
        check(!hiddenNotes.Runs.Any(r => r.Text == "REMOVE BEFORE FLIGHT"), "Hidden or unplaced flagnotes do not appear in the flagnote table");
        foreach (int theme in Enumerable.Range(0, 4))
            foreach (bool zoned in new[] { false, true })
            {
                var borderOptions = tableOptions with
                {
                    Theme = theme,
                    Zones = zoned
                };
                var colors = SheetLayout.Colors(theme);
                var page = borderOptions.Page;
                var borderPaper = new Canvas(page.Width, page.Height, colors.Paper, colors.Ink, borderOptions);
                PageLayout.Decorate(borderPaper, "Border regression", borderOptions);
                NativeText.Apply(borderPaper.Pixels, page.Width, page.Height, borderPaper.Runs);
                var borderScene = new RenderScene(borderPaper.Pixels, owners, repeated, [], options: borderOptions)
                    .WithTables(new(new(60, 60, 620, 400), new(page.Width - 680, PageLayout.TitleTop(borderOptions) - 200, 620, 200)));
                var borderPainted = new Canvas((byte[])borderPaper.Pixels.Clone(), page.Width, page.Height, colors.Ink);
                // Interior geometry must still be erased, unlike the border itself.
                borderPainted.Ink(70, 440, 1);
                PrintTables.Paint(borderPainted, borderScene, []);
                bool SamePixel(int x, int y) => borderPainted.Pixels.AsSpan((y * page.Width + x) * 3, 3).SequenceEqual(borderPaper.Pixels.AsSpan((y * page.Width + x) * 3, 3));
                check(Enumerable.Range(12, page.Width - 24).All(x => SamePixel(x, 60)) && SamePixel(60, 61)
                    && SamePixel(page.Width - 60, PageLayout.TitleTop(borderOptions) - 199)
                    && SamePixel(page.Width - 500, PageLayout.TitleTop(borderOptions)) && SamePixel(70, 440),
                    $"Parts/flagnotes table clearing preserves frame and zone ticks at edge contact while erasing interior geometry: theme {theme}, zoned {zoned}");
            }
        check(LeaderRouting.TryLayout(tableScene, [keyed with { X = 1700 }], out _, out _), "User can deliberately place a manual label over the tables column");
        check(!LeaderRouting.TryLayout(tableScene, [keyed with { X = 1700, ManualPlacement = false }], out _, out _), "Automatic labels still avoid the tables column");
        var interiorOwners = (int[])owners.Clone();
        for (int y = 920; y < 980; y++)
            for (int x = 950; x < 1010; x++)
                interiorOwners[y * Sheet.Width + x] = 1;
        var interior = new RenderScene(scene.BaseRgb, interiorOwners, parts, single);
        var innerLabel = new PartLabel(9, 1, 1, "Interior hardware", 400, 910, ManualPlacement: true);
        check(!LeaderRouting.TryLayout(interior, [innerLabel with { ManualPlacement = false }], out _, out _), "Automatic leader cannot cut through outer hardware to an interior part");
        check(LeaderRouting.TryLayout(interior, [innerLabel], out var innerRoute, out _) && interior.Owner((int)innerRoute[0].End.X, (int)innerRoute[0].End.Y) == 1 && LeaderRouting.FirstHit(interior, innerRoute[0].Start, innerRoute[0].End, out _, out int firstOwner) && firstOwner == 0, "Manual leader reaches a visible interior part even when it must cross other hardware");
        check(LeaderRouting.TryLayout(interior, [label, innerLabel with { X = label.X, Y = label.Y }], out _, out _), "Manual overlap does not invalidate an existing automatic annotation");
        check(LeaderRouting.TryLayout(interior, [innerLabel with { X = 20, Y = 2750, Attachment = new(2300, 100), AttachmentPinned = true }], out var freeRoute, out _) && freeRoute[0].End == new Vector2(2300, 100), "Manual text and endpoint can be placed freely across the whole sheet");
        check(LeaderRouting.SnapAttachment(interior, innerLabel, new(947, 947), 12)?.X == 950, "Endpoint snapping includes interior boundaries next to other parts");
        var styledSheet = new Sheet("Tables", tableOptions, scene.BaseRgb, Sheet.MakePreview(scene.BaseRgb), []) { Scene = tableScene };
        var styled = AnnotationPainting.Compose(styledSheet, tableDoc.Labels, default);
        check(styled.Labels[0].Line == LeaderLine.Solid && styled.Labels[0].Tip == LeaderTip.None && styled.Rgb.Where((_, i) => i % 3 == 0).Any(x => x != 0), "Solid leaders, no-tip endpoints, keys and tables compose into exported pixels");
        var crowdedParts = Enumerable.Range(0, 80).Select(i => new LabelPart(i, "Long component name variant " + i, "T" + i, [0])).ToArray();
        var overfull = new RenderScene(scene.BaseRgb, owners, crowdedParts, single, options: tableOptions);
        check(!PrintTables.Fits(overfull, [], out _), "Overfull tables fail explicitly instead of dropping rows or clipping text");
        var automatic = new AnnotationDocument(scene);
        string summary = automatic.AddAll(LeaderLine.Dashed, LeaderTip.OutlineTriangle, default);
        check(automatic.Labels.Length == 1 && automatic.Labels[0].Line == LeaderLine.Dashed && summary.Contains("1 added"), "Auto label attempts visible parts using print-wide style");
        int before = automatic.Labels.Length;
        automatic.AddAll(LeaderLine.Dotted, LeaderTip.Dot, default);
        check(automatic.Labels.Length == before && automatic.Labels[0].Line == LeaderLine.Dashed, "Repeated auto label preserves existing edits and does not duplicate labels");
        var bulk = new AnnotationDocument(scene);
        bulk.Apply(automatic.Labels, out _);
        bulk.Undo();
        check(bulk.Labels.Length == 0 && bulk.CanRedo, "Auto-label results can be committed and undone as a single edit");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                automatic.AddAll(LeaderLine.Dotted, LeaderTip.Dot, cancelled.Token);
                throw new Exception("Ignored cancellation");
            }
            catch (OperationCanceledException) { check(true, "Auto label cancellation interrupts placement without committing a partial result"); }
        }
        var fullViews = SheetLayout.Plan(capture, options with
        {
            DrawingSize = 100
        });
        check(fullViews.Single(v => v.Id == 1).Frame!.Scale > front.Scale, "Fit drawing area enlarges craft compared with the previous label-margin layout");
        var tableViews = SheetLayout.Plan(capture, options with
        {
            DrawingSize = 100,
            PartsTable = true,
            FlagTable = true
        });
        check(tableViews.All(v => v.Bounds.Right < PageLayout.Table(options).X) && tableViews.Where(v => v.Id != 4).Select(v => v.Frame!.Scale).Distinct().Count() == 1, "Tables reserve their own column while orthographic views retain common scale");
        var largeSingle = SheetLayout.Plan(capture, tableOptions)[0];
        check(largeSingle.Frame != null && largeSingle.Bounds.Right <= PageLayout.Drawing(tableOptions).Right && largeSingle.Bounds.Right < PageLayout.Table(tableOptions).X, "Single-view drawing fits the available area beside tables");

        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(captureRoot, "annotation-studies/part-catalog.json")));
        var used = new HashSet<int>();
        var catalogParts = new List<LabelPart>();
        foreach (var item in catalog.RootElement.GetProperty("lander-1").GetProperty("parts").EnumerateArray())
        {
            var meshes = item.GetProperty("meshIndices").EnumerateArray().Select(x => x.GetInt32()).Where(i => i < capture.Meshes.Length && used.Add(i)).ToArray();
            if (meshes.Length > 0)
                catalogParts.Add(new(catalogParts.Count, item.GetProperty("name").GetString()!, item.GetProperty("id").GetString()!, meshes));
        }
        int catalogCount = catalogParts.Count;
        for (int i = 0; i < capture.Meshes.Length; i++)
            if (used.Add(i))
                catalogParts.Add(new(catalogParts.Count, capture.Meshes[i].Name, capture.Meshes[i].Name, [i]));
        var multi = SheetLayout.Render(capture, maps, "Lander 1", catalogParts.ToArray(), options, [], default, Console.WriteLine);
        var annotations = new AnnotationDocument(multi.Scene!);
        annotations.AddMajorParts();
        check(annotations.Labels.Length >= 4 && annotations.Labels.Select(l => l.ViewId).Distinct().Count() >= 3, "Major-part labels populate multiple Lander views");
        check(LeaderRouting.TryLayout(multi.Scene!, annotations.Labels, out var routes, out _), "Every generated Lander leader passes full routing validation");
        check(routes.All(r => LeaderRouting.FirstHit(multi.Scene!, r.Start, r.End, out _, out int owner) && owner == r.Label.PartId), "All Lander leaders hit their intended part first");
        var export = AnnotationPainting.Compose(multi, annotations.Labels, default);
        File.Copy(PrintFiles.Save(export, output), Path.Combine(output, "lander-engineering.png"), true);
        File.WriteAllText(Path.Combine(output, "lander-engineering-labels.json"), JsonSerializer.Serialize(annotations.Labels, new JsonSerializerOptions { WriteIndented = true }));
        check(export.PreviewRgba.Length == 2400 * 3000 * 4, "Preview retains full print resolution without changing export dimensions");
        // This capture includes meshes outside its part catalog.
        // Group those explicitly as fixture geometry; actual quantities are checked in the live capture.
        var tableParts = catalogParts.Take(catalogCount).ToList();
        if (catalogParts.Count > catalogCount)
            tableParts.Add(new(catalogCount, "Uncatalogued fixture geometry", "FixtureOnly", catalogParts.Skip(catalogCount).SelectMany(p => p.Meshes).ToArray()));
        var controls = SheetLayout.Render(capture, maps, "Lander table fixture", tableParts.ToArray(), options with
        {
            PartsTable = true,
            FlagTable = true,
            DrawingSize = 100
        }, [], default, Console.WriteLine);
        var allLabels = new AnnotationDocument(controls.Scene!);
        string report = allLabels.AddAll(LeaderLine.Dotted, LeaderTip.Dot, default);
        check(allLabels.Labels.Length > 0 && allLabels.Labels.All(l => l.Key != null), "Real Lander auto label places compact table keys on the enlarged layout");
        var controlled = AnnotationPainting.Compose(controls, allLabels.Labels, default);
        File.Copy(PrintFiles.Save(controlled, output), Path.Combine(output, "lander-print-controls.png"), true);
        Console.WriteLine(report);
    }
}
