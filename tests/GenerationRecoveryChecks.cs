using Blueprint.Rendering;
using Blueprint.Storage;
using System.Numerics;

static class GenerationRecoveryChecks
{
    public static void Run(Action<bool, string> check)
    {
        double[][] points = [[0, 0, 0], [4, 0, 0], [0, 4, 0], [0, 0, 4]];
        var mesh = new Mesh("Fixture", points, [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3], points,
            points.Select(_ => new double[] { 1, 0, 0 }).ToArray(),
            points.Select(_ => new double[] { 0, 0 }).ToArray(), "structure", null, null);
        var geometry = new Capture([mesh]);
        var assembly = new AssemblyCapture(geometry, [new(1, null, "Original part", "fixture", false)], [1], [false]);
        var source = new PoseSource(assembly, [], 1, [new(0, "Original part", "fixture", [0], NodeId: 1)], new Dictionary<string, Texture>());
        var options = new PrintOptions(Boxes: new(1, 4), DrawingSize: 100, TitleBlock: new(Title: "Retained title"));
        var original = PrintGeneration.Render(source, geometry, "Craft", options, [], null, false, default);
        var dimension = new HeightDimension(original.Scene!.Views.Single().Id);
        var dimensioned = AnnotationPainting.Compose(original with
        {
            Scene = original.Scene.WithDimensions([dimension])
        }, [], default);
        var dimensionHistory = new AnnotationDocument(dimensioned.Scene!, []);
        var cleared = AnnotationPainting.Compose(dimensioned with
        {
            Scene = dimensioned.Scene!.WithDimensions([])
        }, [], default);
        dimensionHistory.Rebind(cleared.Scene!, [], []);
        check(cleared.Scene!.Dimensions.Length == 0 && !cleared.Rgb.SequenceEqual(dimensioned.Rgb)
            && cleared.Scene.Views.SequenceEqual(dimensioned.Scene!.Views),
            "Deleting a height removes its rendered ink while retaining its craft view");
        dimensionHistory.Undo();
        check(dimensionHistory.Scene.Dimensions.Single() == dimension, "Undo restores the deleted height dimension");
        dimensionHistory.Redo();
        check(dimensionHistory.Scene.Dimensions.Length == 0, "Redo removes the height again");
        var note = new PartLabel(1, original.Scene!.Views.Single().Id, 0, "Removed panel", 150, 500, ManualPlacement: true);
        original = AnnotationPainting.Compose(original, [note], default);
        var before = original.Rgb.ToArray();
        var changed = source with
        {
            Assembly = assembly with { Nodes = [new(2, null, "Replacement part", "fixture", false)], MeshNodes = [2] },
            Parts = [new(0, "Replacement part", "fixture", [0], NodeId: 2)]
        };
        try
        {
            PrintGeneration.Render(changed, geometry, "Craft", original.Options, [], original, false, default);
            check(false, "Ordinary generation must reject the removed part's label");
        }
        catch (InvalidOperationException e)
        {
            check(e.Message.Contains("Cannot keep label") && e.Message.Contains(note.Text),
                "Changed craft retains the missing-label safeguard during ordinary generation");
        }
        check(original.Labels.Single() == note && original.Rgb.SequenceEqual(before),
            "Failed generation retains the accepted label and exact original pixels");
        // A bad scale is intentionally retained in the request. Starting fresh must
        // use template fitting instead of reusing the stuck drawing placement.
        var stuck = original.Options with { Drawing = original.Options.Drawing! with { Ratio = .001f } };
        var fresh = PrintGeneration.Render(changed, geometry, "Craft", stuck, [], original, true, default);
        check(fresh.Labels.Length == 0 && fresh.Details.Length == 0 && fresh.Scene!.Poses.Length == 0
            && fresh.Scene.Parts.Single().NodeId == 2 && fresh.RegeneratedBase == null,
            "Explicit new drawing uses the current craft and clears old composition rather than rebinding it");
        check(fresh.Options.Drawing!.Ratio > stuck.Drawing!.Ratio && fresh.Options.TitleBlock == options.TitleBlock
            && fresh.Options.Page == options.Page && fresh.Scene!.Views.Length == 1,
            "New drawing fits the selected template while retaining paper and title settings");
        check(original.Labels.Single() == note && original.Rgb.SequenceEqual(before),
            "Successful candidate generation does not replace or mutate the accepted print before publication");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            PrintGeneration.Render(changed, geometry, "Craft", stuck, [], original, true, canceled.Token);
            check(false, "Canceled fresh drawing must not publish a candidate");
        }
        catch (OperationCanceledException)
        {
            check(original.Labels.Single() == note && original.Rgb.SequenceEqual(before),
                "Canceled new drawing preserves the accepted print");
        }
        var document = new AnnotationDocument(original.Scene!, original.Labels);
        check(document.Delete(note.Id, out _), "Retained document permits deletion of the obsolete part note");
        var repaired = PrintGeneration.Render(changed, geometry, "Craft", original.Options, [], original with
        {
            Scene = document.Scene, Labels = document.Labels, Details = document.Details
        }, false, default);
        check(repaired.Labels.Length == 0 && repaired.Scene!.Parts.Single().NodeId == 2,
            "Deleting the obsolete note from the retained document allows ordinary generation with the changed craft");
        // A larger craft on a smaller sheet: Repage uses the old scene's bounds,
        // so the newly captured geometry must be fitted before validating Plan.
        var largeOptions = new PrintOptions(Orientation: 2, Aspect: 2, Boxes: new(1, 1), DrawingSize: 100);
        var large = PrintGeneration.Render(source, geometry, "Craft", largeOptions, [], null, false, default);
        var enlargedGeometry = new Capture([mesh with { Vertices = points.Select(p => p.Select(v => v * 4).ToArray()).ToArray() }]);
        var enlargedSource = source with { Assembly = assembly with { Geometry = enlargedGeometry } };
        var smallOptions = large.Options with { Orientation = 0, Aspect = 0 };
        var repaged = smallOptions with { Drawing = DrawingPlacements.Repage(large.Scene!, smallOptions) };
        bool tooLarge = false;
        try { SheetLayout.Plan(enlargedGeometry, repaged); }
        catch (InvalidOperationException e) { tooLarge = e.Message.Contains("too big for the canvas"); }
        check(tooLarge, "Smaller-page fixture reproduces the premature canvas-size failure");
        var smaller = PrintGeneration.Render(enlargedSource, enlargedGeometry, "Craft", smallOptions, [], large, false, default);
        DrawingPlacements.Validate(SheetLayout.Plan(enlargedGeometry, smaller.Options), smaller.Options);
        check(smaller.Options.Page == smallOptions.Page && smaller.Options.Drawing!.Ratio > large.Options.Drawing!.Ratio,
            "Production generation fits a smaller page before validating its view plan");
        RefreshChecks(check, mesh);
    }

    static void RefreshChecks(Action<bool, string> check, Mesh mesh)
    {
        var binding = new SourceBinding(Guid.NewGuid(), 1);
        var cut = JointKey.Between(new(10, "end"), new(20, "end"));
        var extraMesh = mesh with { Vertices = mesh.Vertices.Select(p => new[] { p[0], p[1] + 6, p[2] }).ToArray() };
        PoseSource Source(bool edited, SourceBinding revision)
        {
            AssemblyNode[] nodes = [new(10, null, "Root", "root", false), new(20, 10, "Retained", "kept", false)];
            Mesh[] meshes = [mesh, extraMesh];
            if (edited)
            {
                nodes = [nodes[1] with { Parent = null }];
                meshes = [extraMesh];
            }
            var assembly = new AssemblyCapture(new(meshes), nodes, nodes.Select(n => n.Id).ToArray(), new bool[nodes.Length])
            {
                Topology = new(revision, nodes.Select(n => new SourcePart(n.Id, n.TemplateId, null, false)),
                    edited ? [] : [new(SourceLinkKind.Structural, cut)])
            };
            return new(assembly, [], meshes.Length,
                nodes.Select((n, i) => new LabelPart(i, n.Name, n.TemplateId, [i], NodeId: n.Id)).ToArray(),
                new Dictionary<string, Texture>());
        }

        var source = Source(false, binding);
        var next = Source(true, binding with { Revision = 2 });
        var options = new PrintOptions(Orientation: 3, CustomWidth: 1024, CustomHeight: 768, FreeLayout: true,
            Drawing: new(160, [new(7, 4, .25f, .45f)]));
        var main = PrintGeneration.Render(source, source.Assembly.Geometry, "Refresh", options, [], null, false, default);
        var detail = new ExplodedDetail(0, 10, new(520, 80, 420, 480), "Old root", Seed: 10) { Selection = new(10, [], []) };
        var deleted = new PartLabel(1, 7, 0, "Deleted", 95, 80, ManualPlacement: true, FlagNote: true);
        var kept = new PartLabel(2, 7, 1, "Survives", 140, 135, ManualPlacement: true);
        var original = ExplodedPainting.Compose(main, main, [detail], [deleted, kept], default);
        ProjectChecks(check, source, main, detail, cut, deleted);

        var refreshed = PrintGeneration.Render(next, next.Assembly.Geometry, original.Title, original.Options, [], original, false, default, refreshCraft: true);
        var refreshedLabel = refreshed.Labels.Single();
        check(refreshedLabel.Id == kept.Id && refreshedLabel.PartId == 0 && refreshedLabel.X == kept.X && refreshedLabel.Y == kept.Y &&
            refreshed.Refresh is { Labels: 1, Poses: 0, Specs: 0 },
            "Same-source refresh prunes deleted notes and remaps surviving labels without moving them");
        var stale = refreshed.Scene!.Details.Single();
        check(stale.Definition.SourceIssue == DetailSourceIssue.MissingRoot && stale.Definition.Bounds == original.Details[0].Bounds &&
            ReferenceEquals(stale.Pixels.Ink, original.Scene!.Details[0].Pixels.Ink) && stale.Pixels.MeshOwners.Length == 0 &&
            refreshed.Scene.PickSurface(stale.Definition.ViewId, stale.Definition.Bounds.Center) == null,
            "Missing root retains its view frame and prior ink without exposing old picking data");
        var reload = Source(true, new(Guid.NewGuid(), 2));
        try
        {
            PrintGeneration.Render(reload, reload.Assembly.Geometry, original.Title, options, [], original, false, default, refreshCraft: true);
            check(false, "Refresh must reject the same numeric part IDs in a different craft session");
        }
        catch (InvalidOperationException ex)
        {
            check(ex.Message.Contains("craft identity") && original.Labels.Length == 2 && original.Details[0].SourceIssue == DetailSourceIssue.None,
                "Unverified refresh leaves the accepted source, labels and views intact");
        }
        try
        {
            PrintFiles.Save(refreshed, "unused");
            check(false, "A stale exploded view must block PNG output");
        }
        catch (InvalidOperationException ex)
        {
            check(ex.Message.Contains("exploded views"), "Stale exploded geometry cannot export as current");
        }
        var revised = PreviewUpdates.Render(refreshed, refreshed.PoseMain!, refreshed.Options with { Theme = 2 }, default);
        check(revised.Details.Single().SourceIssue == DetailSourceIssue.MissingRoot && revised.Labels.Single().Id == kept.Id,
            "An unrelated drawing edit retains the unresolved view and surviving annotation");
        var replaced = ExplodedPainting.Compose(refreshed.PoseMain!, refreshed,
            [stale.Definition with { Scope = 20, Seed = 20, Selection = new(20, [], []), Sizing = null, SourceIssue = DetailSourceIssue.None }],
            refreshed.Labels, default);
        check(replaced.Details.Single().Scope == 20 && replaced.Details.Single().SourceIssue == DetailSourceIssue.None &&
            !ReferenceEquals(replaced.Scene!.Details.Single().Pixels.Ink, stale.Pixels.Ink),
            "Explicit root replacement regenerates the view from current geometry");
        var refresh = new CraftRefresh(source.Assembly.Topology!, next.Assembly.Topology!);
        check(refresh.Detail(detail with { Scope = 20, Selection = new(20, [cut], []) }).SourceIssue == DetailSourceIssue.ChangedScope,
            "An invalid assembly cut requires explicit review");
    }

    static void ProjectChecks(Action<bool, string> check, PoseSource source, Sheet main, ExplodedDetail detail, JointKey cut, PartLabel note)
    {
        var primary = new DrawingView(7, 1, .25f, .45f);
        var side = DrawingPlacements.Roll(new(8, 0, .48f, .45f), primary, 0, 3);
        var options = main.Options with
        {
            Drawing = new(160, [primary, side]),
            TitleBlock = new(Company: "Fixture", Creator: "Author", Title: "Saved drawing", Revision: "3")
        };
        var baseSheet = PosePainting.RenderMain(source, main.Title, options, [], [], default, composeAnnotations: false);
        var drawn = ExplodedPainting.Compose(baseSheet, baseSheet, [detail with { Selection = new(10, [cut], []) }], [note], default);
        var text = new FreeTextBox(10, "Editable test note", new(120, 850, 390, 100));
        var scene = drawn.Scene!.WithFreeText([text]);
        var doc = new AnnotationDocument(scene, drawn.Labels);
        doc.Rebind(scene, drawn.Labels, drawn.Details, false);
        var project = DrawingProject.Capture(drawn, doc, null);
        var folder = Path.Combine(Path.GetTempPath(), "PawPrints-project-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DrawingProjectStore(ProfileStorage.FromResolvedProfile(folder));
            string name = store.Save(project, null);
            var saved = store.Read(name);
            var opened = saved.Render(source, null, null, default);
            check(store.List().SequenceEqual([name]) && opened.Scene!.Drawing!.Views.SequenceEqual(new[] { primary, side }) &&
                opened.Details.Single().Selection!.Cuts.Single() == cut && opened.Labels.Single().Id == note.Id &&
                opened.Scene.FreeText.Single() == text && opened.Options.TitleBlock == options.TitleBlock,
                "Editable project reopens on the same detached capture with linked cameras, scoped detail, annotations and title");
            var restartedBinding = new SourceBinding(Guid.NewGuid(), 1);
            uint NewId(uint id) => 130 - id;
            var restartedAssembly = source.Assembly with
            {
                Nodes = source.Assembly.Nodes.Reverse().Select(n => n with
                {
                    Id = NewId(n.Id), Parent = n.Parent is { } parent ? NewId(parent) : null
                }).ToArray(),
                MeshNodes = source.Assembly.MeshNodes.Select(NewId).ToArray(),
                Topology = new(restartedBinding, source.Assembly.Topology!.Parts.Select(p => p with { Id = NewId(p.Id) }),
                    [new(SourceLinkKind.Structural, JointKey.Between(cut.A with { Part = NewId(cut.A.Part) }, cut.B with { Part = NewId(cut.B.Part) }))])
            };
            var restarted = source with
            {
                Assembly = restartedAssembly,
                Parts = source.Parts.Reverse().Select((p, i) => p with { Id = i, NodeId = NewId(p.NodeId!.Value) }).ToArray()
            };
            var afterRestart = saved.Render(restarted, null, null, default);
            var reopenedDetail = afterRestart.Details.Single();
            var reopenedPart = afterRestart.Scene!.Parts.Single(p => p.Id == afterRestart.Labels.Single().PartId);
            check(reopenedDetail.Scope == NewId(detail.Scope) && reopenedPart.NodeId == NewId(detail.Scope) &&
                reopenedPart.Id != note.PartId &&
                reopenedDetail.Selection!.Cuts.Single() == JointKey.Between(cut.A with { Part = NewId(cut.A.Part) }, cut.B with { Part = NewId(cut.B.Part) }),
                "Restart binding ignores enumeration order and remaps the flagnote, detail root and both joint endpoints");
            var changedAssembly = restartedAssembly with
            {
                Nodes = restartedAssembly.Nodes.Select(n => n.Id == NewId(20) ? n with { Scale = new(1, 2, 1) } : n).ToArray()
            };
            try
            {
                saved.Render(restarted with { Assembly = changedAssembly }, null, null, default);
                check(false, "A structurally edited vehicle cannot inherit a cross-session binding");
            }
            catch (InvalidOperationException e)
            {
                check(e.Message.Contains("differs"), "A changed scale rejects the edited cross-session vehicle before rendering");
            }
            var recaptured = source with
            {
                Assembly = source.Assembly with
                {
                    Topology = new(source.Assembly.Topology.Binding with { Revision = 2 }, source.Assembly.Topology.Parts, source.Assembly.Topology.Links)
                }
            };
            check(ProjectCraftIdentity.Rebind(saved.Content, saved.Vehicle!, saved.Source!.Value, recaptured).Labels.Single().Part == detail.Scope,
                "Recapture in the same session keeps the verified runtime part identity");
            var topology = source.Assembly.Topology;
            var duplicate = source.Assembly.Nodes.Single(n => n.Id == 20) with { Id = 30 };
            var duplicateParts = topology.Parts.Add(topology.Parts.Single(p => p.Id == 20) with { Id = 30 });
            var duplicateLinks = topology.Links.Add(new(SourceLinkKind.Structural,
                JointKey.Between(cut.A, cut.B with { Part = 30 })));
            var ambiguousAssembly = source.Assembly with
            {
                Nodes = [.. source.Assembly.Nodes, duplicate],
                Topology = new(topology.Binding, duplicateParts, duplicateLinks)
            };
            var ambiguousLabel = saved.Content with { Labels = [note with { PartId = 1 }] };
            var ambiguousIdentity = ProjectCraftIdentity.Capture(ambiguousAssembly, scene, ambiguousLabel);
            try
            {
                ProjectCraftIdentity.Rebind(ambiguousLabel, ambiguousIdentity, topology.Binding, source with
                {
                    Assembly = ambiguousAssembly with { Topology = new(restartedBinding, duplicateParts, duplicateLinks) }
                });
                check(false, "Coincident repeated parts cannot be assigned by traversal order");
            }
            catch (InvalidOperationException e)
            {
                check(e.Message.Contains("unique match"), "Restart binding refuses ambiguous repeated parts even on the identical assembly");
            }
            string file = Directory.EnumerateFiles(folder, "*.pawprint", SearchOption.AllDirectories).Single();
            byte[] before = File.ReadAllBytes(file);
            try
            {
                store.Save(project with { Version = -1 }, name);
                check(false, "An unsupported project cannot replace a saved drawing");
            }
            catch (InvalidDataException)
            {
                check(before.SequenceEqual(File.ReadAllBytes(file)), "A rejected project write leaves the existing file intact");
            }
            byte[] corrupt = "{\"Version\":1,\"Kind\":\"pawprints.project\"}"u8.ToArray();
            try
            {
                DrawingProjectCodec.Read(corrupt);
                check(false, "An incomplete drawing file must be rejected");
            }
            catch (InvalidDataException)
            {
                check(true, "An incomplete drawing file is rejected before rendering");
            }
            var blank = BlankDrawing.Create("Blank project", main.Options, default);
            var blankProject = DrawingProject.Capture(blank, new AnnotationDocument(blank.Scene!), null);
            var openedBlank = store.Read(store.Save(blankProject, null)).Render(null, null, null, default);
            check(openedBlank.Scene!.Views.Length == 0 && openedBlank.PoseSource == null &&
                openedBlank.Options.OutputPage == blank.Options.OutputPage, "An empty project reopens without a vehicle capture");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
