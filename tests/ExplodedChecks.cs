using Blueprint.Rendering;
using System.Numerics;
using System.Text.Json;

static class ExplodedChecks
{

    public static void Sampling(Action<bool, string> check, Capture capture, IReadOnlyDictionary<string, Texture> maps, string output)
    {
        using var json = JsonDocument.Parse(File.ReadAllText("tests/fixtures/lander-exploded-hierarchy.json"));
        var data = json.RootElement;
        var nodes = data.GetProperty("nodes").Deserialize<AssemblyNode[]>(new JsonSerializerOptions { IncludeFields = true })!;
        var assembly = new AssemblyCapture(capture, nodes, data.GetProperty("meshNodes").Deserialize<uint[]>()!, data.GetProperty("internalMeshes").Deserialize<bool[]>()!);
        var detail = new ExplodedDetail(1, nodes[0].Id, new(0, 0, 600, 1100), "Small Lander B");
        var geometry = ExplodedGeometry.Build(assembly, detail);
        var baseFrame = ExplodedGeometry.Render(new(assembly, maps), detail, default, new()).Frame;
        string folder = Path.Combine(output, "detail-sampling");
        Directory.CreateDirectory(folder);
        foreach (int ratio in new[] { 120, 480 })
        {
            var frame = baseFrame with
            {
                Width = 600,
                Height = 1100,
                Scale = PrintScale.Density(new()) / ratio
            };
            var before = new BlueprintRenderer(geometry.Capture, maps, "Detail", detail.View, default, frame).RenderView(_ => { });
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var after = DetailSampling.Render(geometry.Capture, maps, "Detail", detail.View, frame, default);
            watch.Stop();
            check(after.Width == before.Width && after.Height == before.Height && after.MeshOwners.Length == before.MeshOwners.Length, "Detail sampling retains output and picking dimensions at 1:" + ratio);
            check(after.MeshOwners.All(m => m >= -1 && m < geometry.Capture.Meshes.Length) && after.Ink.Any(v => v > 0) && !after.Ink.SequenceEqual(before.Ink), "Detail ink is resampled with valid source mesh IDs at 1:" + ratio);
            var options = new PrintOptions(Theme: 1, Grid: false);
            var colors = SheetLayout.Colors(1);
            var canvas = new Canvas(1200, 1100, colors.Paper, colors.Ink, options);
            for (int y = 0; y < 1100; y++)
                for (int x = 0; x < 600; x++)
                {
                    canvas.Ink(x, y, before.Ink[y * 600 + x] / 255f);
                    canvas.Ink(x + 600, y, after.Ink[y * 600 + x] / 255f);
                }
            canvas.Text("BEFORE / 1:" + ratio, 20, 20, 24, 1);
            canvas.Text("ADAPTIVE FINE / 1:" + ratio, 620, 20, 24, 1);
            NativeText.Apply(canvas.Pixels, 1200, 1100, canvas.Runs);
            string path = Path.Combine(folder, "lander-" + ratio + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            Png.Write(path, 1200, 1100, canvas.Pixels);
            Console.WriteLine($"Detail 1:{ratio}: {watch.ElapsedMilliseconds}ms; {path}");
        }
        check(DetailSampling.Owner(-1, -1, 8, -1) == 8 && DetailSampling.Owner(2, 8, 8, -1) == 8 && DetailSampling.Owner(-1, -1, -1, -1) == -1, "Subpixel parts remain pickable, majority mesh wins, empty paper stays empty");
        check(DetailSampling.Rate(666, 666) == 3 && DetailSampling.Rate(667, 667) == 2 && DetailSampling.Rate(1000, 1000) == 2 && DetailSampling.Rate(1001, 1000) == 1, "3x Fine is preferred, falling back to 2x/1x within the four-million-sample cap");
        check(DetailSampling.Owner([-1, -1, -1, -1, 8, -1, -1, -1, -1]) == 8 && DetailSampling.Owner([2, 2, 8, 8, 8, 8, 2, -1, -1]) == 8, "3x ownership resolves all nine samples, preserving tiny parts and the majority visible mesh");
    }

    public static void Lander(Action<bool, string> check, Capture capture, IReadOnlyDictionary<string, Texture> maps, string output)
    {
        using var json = JsonDocument.Parse(File.ReadAllText("tests/fixtures/lander-exploded-hierarchy.json"));
        var data = json.RootElement;
        var settings = new JsonSerializerOptions { IncludeFields = true };
        var nodes = data.GetProperty("nodes").Deserialize<AssemblyNode[]>(settings)!;
        var owners = data.GetProperty("meshNodes").Deserialize<uint[]>()!;
        var internals = data.GetProperty("internalMeshes").Deserialize<bool[]>()!;
        var assembly = new AssemblyCapture(capture, nodes, owners, internals);
        var detail = new ExplodedDetail(1, nodes[0].Id, new(0, 0, 1200, 1400), "Small Lander B");
        uint shell = nodes.Single(n => n.TemplateId == "CoreCommandB_Subpart_SmallLanderStructureA").Id;
        check(owners.Length == capture.Meshes.Length && ExplodedGeometry.AnchorGroup(assembly, detail) == detail.Scope, "Recorded Lander root includes its complete native model as the fixed base");
        var geometry = ExplodedGeometry.Build(assembly, detail);
        int index = Array.IndexOf(geometry.GroupNodes, detail.Scope);
        check(geometry.OriginalCenters[index] == geometry.MovedCenters[index], "Real Lander can stays in its original centered position instead of shooting sideways");
        uint blanket = nodes.Single(n => n.TemplateId == "CoreSurfacesA_Subpart_SmallLanderClothA").Id;
        check(AssemblyGraph.GroupFor(assembly, detail, blanket) == detail.Scope && AssemblyGraph.GroupFor(assembly, detail, shell) == detail.Scope, "Real Lander blanket and can remain one complete part instead of a duplicated-looking peeled model");
        check(geometry.GroupNodes.Distinct().All(id => !nodes.First(n => n.Id == id).Subpart), "Every default Lander explosion group is an actual craft part, never an authored model subpart");
        var expanded = detail with
        {
            Expanded = [detail.Scope]
        };
        check(AssemblyGraph.GroupFor(assembly, expanded, blanket) == blanket && ExplodedGeometry.AnchorGroup(assembly, expanded) == shell, "Peeling the real Lander blanket requires explicit model-component expansion");
        check(AssemblyGraph.PartAncestors(assembly, blanket).SequenceEqual(new[] { detail.Scope }), "Clicking the Lander blanket offers its whole craft part in the scope picker");
        var options = new PrintOptions();
        var tile = ExplodedGeometry.Render(new(assembly, maps), detail, default, options);
        var colors = SheetLayout.Colors(0);
        var canvas = new Canvas(1200, 1400, colors.Paper, colors.Ink, options);
        for (int y = 0; y < tile.Pixels.Height; y++)
            for (int x = 0; x < tile.Pixels.Width; x++)
                canvas.Ink(x + 12, y + 56, tile.Pixels.Ink[y * tile.Pixels.Width + x] / 255f);
        canvas.Text("DETAIL A - SMALL LANDER B", 16, 12, 27, 1);
        canvas.Text("SCALE " + PrintScale.Ratio(tile.Frame, options), 16, 36, 15, 1);
        NativeText.Apply(canvas.Pixels, 1200, 1400, canvas.Runs);
        string dir = Path.Combine(output, "whole-parts-0.4.4");
        Directory.CreateDirectory(dir);
        Png.Write(Path.Combine(dir, Guid.NewGuid() + "-lander.png"), 1200, 1400, canvas.Pixels, PrintScale.Density(options));
        var reviewOptions = new PrintOptions(Layout: 2, DrawingSize: 100, TitleBlock: new(Title: "Lander 1", Creator: "Your name", CreatorInitials: "YN"), Boxes: new(A: 2, B: -1, C: -1, D: -1));
        var labelParts = nodes.Select((n, i) => new LabelPart(i, n.Name, n.TemplateId, assembly.MeshNodes.Select((id, mi) => (id, mi)).Where(p => p.id == n.Id).Select(p => p.mi).ToArray(), NodeId: n.Id)).Where(p => p.Meshes.Length > 0).Select((p, i) => p with { Id = i }).ToArray();
        var baseline = SheetLayout.Render(capture, maps, "Lander 1", labelParts, reviewOptions, [], default, _ => { }) with
        {
            DetailSource = new(assembly, maps)
        };
        baseline.Scene!.MeshNodes = assembly.MeshNodes;
        var borderless = ExplodedPainting.Compose(baseline, baseline, [detail with { Bounds = new(1260, 360, 960, 1500) }], [], default);
        var rendered = borderless.Scene!.Details.Single();
        var visible = borderless.Scene.Visible.Where(p => p.Key.View == detail.ViewId).Select(p => p.Value.Bounds).ToArray();
        check(rendered.CaptionBounds.Y >= visible.Max(b => b.Bottom) + 16, "Actual Lander detail caption follows below its rendered assembly");
        var paper = new Canvas(borderless.PixelWidth, borderless.PixelHeight, colors.Paper, colors.Ink, reviewOptions);
        var b = rendered.Definition.Bounds;
        bool borderFree = true;
        for (int x = (int)b.X; x < b.Right; x++)
            for (int channel = 0; channel < 3; channel++)
                borderFree &= borderless.Rgb[((int)b.Y * borderless.PixelWidth + x) * 3 + channel] == paper.Pixels[((int)b.Y * borderless.PixelWidth + x) * 3 + channel];
        check(borderFree, "Exploded export has no printed frame border; selection outlines belong only to the editor");
        string review = Path.Combine(output, "composition-0.4.11");
        Directory.CreateDirectory(review);
        string detailPath = Path.Combine(review, "lander-borderless-detail.png");
        if (File.Exists(detailPath))
            File.Delete(detailPath);
        Png.Write(detailPath, borderless.PixelWidth, borderless.PixelHeight, borderless.Rgb);
    }

    // Synthetic nested assembly fixture: topology is deliberately explicit,
    // not inferred from mesh names or presented as a captured KSA craft.
    static Mesh Cube(string name, double x, double y, double z)
    {
        double[][] points = [[-.4, -.4, -.4], [.4, -.4, -.4], [.4, .4, -.4], [-.4, .4, -.4], [-.4, -.4, .4], [.4, -.4, .4], [.4, .4, .4], [-.4, .4, .4]];
        int[] indices = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5];
        return new(name, points.Select(p => new[] { p[0] + x, p[1] + y, p[2] + z }).ToArray(), indices, points, points, points.Select(_ => new[] { 0d, 0d }).ToArray(), "structure", null, null);
    }

    public static void Run(Action<bool, string> check, string output)
    {
        var guidePixels = new ViewPixels(160, 40, new byte[160 * 40], Enumerable.Repeat(-1, 160 * 40).ToArray());
        for (int y = 0; y < 40; y++)
            for (int x = 70; x < 80; x++)
            {
                guidePixels.MeshOwners[y * 160 + x] = 0;
                guidePixels.Ink[y * 160 + x] = 200;
            }
        var guidePaths = new[] { (new Vector2(10, 20), new Vector2(150, 20)) };
        var guideStyles = Enum.GetValues<LeaderLine>().Select(line => AssemblyGuidePainting.Paint(guidePixels, guidePaths, line, default)).ToArray();
        check(!guideStyles[0].Ink.SequenceEqual(guideStyles[1].Ink) && !guideStyles[1].Ink.SequenceEqual(guideStyles[2].Ink),
            "Assembly guide dots, dashes and solid lines produce distinct patterns");
        check(guideStyles.All(p => Enumerable.Range(0, p.Ink.Length).Where(i => p.MeshOwners[i] >= 0).All(i => p.Ink[i] == guidePixels.Ink[i]))
            && guideStyles.All(p => ReferenceEquals(p.MeshOwners, guidePixels.MeshOwners)),
            "Every guide pattern preserves geometry ink and picking ownership");
        check(Enumerable.Range(10, 140).Where(x => x < 70 || x >= 80).All(x => guideStyles[2].Ink[20 * 160 + x] > 0)
            && guidePixels.Ink.Where((v, i) => guidePixels.MeshOwners[i] < 0).All(v => v == 0),
            "Solid guides fill the unobscured path without mutating captured pixels");
        using (var canceledGuide = new CancellationTokenSource())
        {
            canceledGuide.Cancel();
            try { AssemblyGuidePainting.Paint(guidePixels, guidePaths, LeaderLine.Solid, canceledGuide.Token); check(false, "Canceled guide update must stop"); }
            catch (OperationCanceledException) { check(true, "Guide painting observes render cancellation"); }
        }

        var capture = new Capture(new[] { Cube("Root", 0, 0, 0), Cube("Bay", 0, 2, 0), Cube("Payload", 0, 3, 0), Cube("Panel", 0, -2, 0), Cube("Internal", 0, 2, 1) });
        AssemblyNode[] nodes = [new(1, null, "Root", "root", false), new(2, 1, "Bay", "bay", false), new(3, 2, "Payload", "payload", true), new(4, 1, "Panel", "panel", false), new(5, 2, "Internal", "inner", true)];
        var assembly = new AssemblyCapture(capture, nodes, [1, 2, 3, 4, 5], [false, false, false, false, true]);
        var source = new ExplodedSource(assembly, new Dictionary<string, Texture>());
        var detail = new ExplodedDetail(1, 1, new(180, 300, 800, 700), "Nested assembly", Seed: 3);
        var container = new AssemblyCapture(new([Cube("Central shell", .0000024, .0000032, -.0000001), Cube("Cap", 0, 2, 0), Cube("Side fitting", 0, 0, 2)]),
            [new(1, null, "Container", "container", false), new(2, 1, "Shell", "shell", true), new(3, 1, "Cap", "cap", true), new(4, 1, "Fitting", "fitting", true)], [2, 3, 4], [false, false, false]);
        var opened = detail with
        {
            Expanded = [1]
        };
        check(AssemblyGraph.Groups(container, detail).SequenceEqual(new uint[] { 1 }), "Container root owns all unexpanded authored meshes as one whole craft part");
        check(ExplodedGeometry.AnchorGroup(container, opened) == 2, "Explicitly expanded prefab anchors its central shell despite microscopic off-center geometry");
        var anchored = ExplodedGeometry.Build(container, opened with
        {
            Groups = [new(2, new(50, 60, 70))]
        });
        check(anchored.OriginalCenters[0] == anchored.MovedCenters[0] && ExplodedGeometry.GroupDirection(container, opened, 2) == Vector3.Zero, "Visible assembly base neither automatically separates nor accepts an old drag offset");
        check(anchored.OriginalCenters[1] != anchored.MovedCenters[1] && anchored.OriginalCenters[2] != anchored.MovedCenters[2], "Caps and peripheral fittings still separate around the fixed body");
        check(ExplodedGeometry.AnchorGroup(container, opened with
        {
            Groups = [new(2, Vector3.Zero, true)]
        }) == 2 && ExplodedGeometry.AnchorGroup(container with
        {
            Nodes = container.Nodes.Reverse().ToArray()
        }, opened) == 2, "Hiding or reordering groups cannot silently choose a new assembly base");
        check(ExplodedGeometry.AnchorGroup(assembly, detail) == 1, "A drawable root retains priority over any surrogate central shell");
        check(ExplodedGeometry.AnchorGroup(container with
        {
            Geometry = new(container.Geometry.Meshes.Select(m => m with { Vertices = m.Vertices.Select(v => new[] { v[0] + 20, v[1], v[2] }).ToArray() }).ToArray())
        }, opened) == 1, "Empty root with no centered body does not pin an unrelated peripheral part");
        var attached = assembly with
        {
            Nodes = nodes.Select(n => n.Id == 3 ? n with { Subpart = false } : n).ToArray()
        };
        check(AssemblyGraph.GroupFor(attached, detail, 3) == 3 && AssemblyGraph.GroupFor(assembly, detail, 3) == 2, "Native attachment boundaries separate nested craft parts while model subparts remain with their owner");
        var separated = ExplodedGeometry.Build(attached, detail);
        int parentIndex = Array.IndexOf(separated.GroupNodes, 2u), childIndex = Array.IndexOf(separated.GroupNodes, 3u);
        var parentShift = separated.MovedCenters[parentIndex] - separated.OriginalCenters[parentIndex];
        var childShift = separated.MovedCenters[childIndex] - separated.OriginalCenters[childIndex];
        check(AssemblyGraph.Depth(attached, detail, 2) == 1 && AssemblyGraph.Depth(attached, detail, 3) == 2 && Vector3.Distance(childShift, parentShift) > 1, "Successive actual parts receive distinct axis distances instead of translating together while still joined");
        var mounts = new Dictionary<uint, ExplodedMount>
        {
            [3] = new(2, new(1, 2, 0), Vector3.UnitX, Vector3.UnitY, false),
            [4] = new(2, new(-1, 2, 0), -Vector3.UnitX, Vector3.UnitY, false)
        };
        var radial = attached with
        {
            Topology = new(new(Guid.NewGuid(), 1), nodes.Select(n => new SourcePart(n.Id, n.TemplateId, null, false)), [])
            {
                Mounts = mounts,
                RadialSymmetry = new Dictionary<uint, uint[]> { [3] = [3, 4] }
            }
        };
        var mounted = ExplodedGeometry.Build(radial, detail with { Groups = [new(3, new(.5f, .35f, 20))] });
        var guide = mounted.MountGuides[3];
        check(guide.Start == mounts[3].Point + parentShift && guide.End.X > guide.Start.X &&
            Math.Abs(guide.End.Y - guide.Start.Y - .35f) < .0001f && guide.End.Z == guide.Start.Z,
            "Mounted guide starts on the displaced host and separates along its normal plus the tangent offset");
        var tile = new RenderedDetail(detail, new(1, 1, 1, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
            [], [1, 3, 4], new(1, 1, [0], [-1])) { Anchor = 1, Mounts = mounts };
        check(ExplodedGeometry.DragAxes(tile, radial, 3, false, false).OrderBy(a => a.Key).Select(a => a.Value)
            .SequenceEqual(new[] { Vector3.UnitX, -Vector3.UnitX }), "Symmetry members drag along their own outward normals");
        check(ExplodedGeometry.DragAxes(tile, radial, 3, false, true).Values.All(a => a == Vector3.UnitY) &&
            ExplodedGeometry.DragAxes(tile, radial, 3, true, true).Keys.SequenceEqual(new[] { 3u }),
            "Alt uses the host tangent and Shift isolates the grabbed member");
        check(ExplodedGeometry.DragAxes(tile with { Groups = [1, 3] }, radial, 3, false, false).Keys.SequenceEqual(new[] { 3u }),
            "Omitted symmetry members do not receive drag offsets");
        var outwardAxes = ExplodedGeometry.DragAxes(tile, radial, 3, false, false);
        var slideAxes = ExplodedGeometry.DragAxes(tile, radial, 3, false, true);
        var fromPress = new Vector2(5, -4);
        var outward = outwardAxes[3] * ExplodedGeometry.DragDistance(outwardAxes, 3, tile.Frame, fromPress);
        var slide = slideAxes[3] * ExplodedGeometry.DragDistance(slideAxes, 3, tile.Frame, fromPress);
        check(outward == new Vector3(5, 0, 0) && slide == new Vector3(0, 4, 0),
            "Alt reprojects the same click-to-pointer delta onto slide without retaining the outward preview");
        var soloSlide = ExplodedGeometry.DragAxes(tile, radial, 3, true, true);
        check(soloSlide.Count == 1 && soloSlide[3] * ExplodedGeometry.DragDistance(soloSlide, 3, tile.Frame, fromPress) == slide,
            "Shift during a slide keeps the original click anchor and excludes the other symmetry members");
        var interiorMounts = mounts.ToDictionary(p => p.Key, p => p.Value with { Interior = true });
        check(ExplodedGeometry.DragAxes(tile with { Mounts = interiorMounts }, radial, 3, false, true).Count == 2,
            "Perpendicular cargo-bay mounts can also slide along the bay tangent");
        CheckRootedScope(check);
        check(AssemblyGraph.PartAncestors(assembly, 3).SequenceEqual(new uint[] { 2, 1 }), "Scope picker walks actual craft parts, omitting authored model components");
        check(AssemblyGraph.Ancestors(assembly, 3).SequenceEqual(new uint[] { 3, 2, 1 }), "Detail scope walks authored subpart and attached ancestor chain to root");
        check(AssemblyGraph.Subtree(assembly, 2).SetEquals(new uint[] { 2, 3, 5 }), "Assembly scope excludes siblings while retaining nested contents");
        check(AssemblyGraph.GroupFor(assembly, detail, 3) == 2 && AssemblyGraph.GroupFor(assembly, detail with
        {
            Expanded = [2]
        }, 3) == 3, "Nested assemblies remain grouped until explicitly expanded");
        string before = JsonSerializer.Serialize(capture);
        var geometry = ExplodedGeometry.Build(assembly, detail);
        check(geometry.OriginalMeshes.SequenceEqual(new[] { 0, 1, 2, 3 }) && geometry.GroupNodes.SequenceEqual(new uint[] { 1, 2, 2, 4 }), "Detail geometry tracks source meshes and grouped owners");
        check(ExplodedGeometry.Build(assembly, detail with
        {
            IncludeInternal = true
        }).OriginalMeshes.Contains(4), "Internal geometry is opt-in");
        check(!ExplodedGeometry.Build(assembly, detail with
        {
            Groups = [new(2, Vector3.Zero, true)]
        }).GroupNodes.Contains(2u), "Hiding a group removes its complete nested geometry");
        var positioned = ExplodedGeometry.Build(assembly, detail with
        {
            Separation = 0,
            Groups = [new(2, new(2, 3, 4))]
        });
        check(positioned.Capture.Meshes[1].Vertices[0].Zip(capture.Meshes[1].Vertices[0], (a, b) => a - b).SequenceEqual(new[] { 0d, 2d, 0d }) && JsonSerializer.Serialize(capture) == before, "Manual offsets stay on the root axis and affect detached vertices only");
        check(ReferenceEquals(positioned.Capture.Meshes[1].SourceNormals, capture.Meshes[1].SourceNormals) && ReferenceEquals(positioned.Capture.Meshes[1].UV, capture.Meshes[1].UV), "Exploded geometry retains authored normals and UVs");
        foreach (int axis in new[] { 1, 2, 3 })
        {
            var g = ExplodedGeometry.Build(assembly, detail with
            {
                Axis = axis
            });
            var delta = g.MovedCenters[1] - g.OriginalCenters[1];
            check(delta.Length() > 0 && (axis == 1 ? delta.Y == 0 && delta.Z == 0 : axis == 2 ? delta.X == 0 && delta.Z == 0 : delta.X == 0 && delta.Y == 0), $"Axis {axis} separation translates only along its chosen axis");
        }
        Vector3 Delta(DetailGeometry g, uint id)
        {
            int i = Array.IndexOf(g.GroupNodes, id);
            return g.MovedCenters[i] - g.OriginalCenters[i];
        }
        check(Enumerable.Range(1, 4).All(view => ExplodedGeometry.Build(assembly, detail with { View = view }).MovedCenters.SequenceEqual(geometry.MovedCenters)), "Changing camera never changes explosion coordinates");
        check(Delta(ExplodedGeometry.Build(assembly, detail with
        {
            Groups = [new(1, new(50, 60, 70))]
        }), 1) == Vector3.Zero, "Root remains stationary even with a manual offset");
        check(Delta(geometry, 2).X > 0 && Delta(geometry, 2).Y == 0 && Delta(geometry, 2).Z == 0 && Delta(geometry, 4).X < 0, "Automatic separation uses one signed root axis, preserving transverse coordinates");
        var rotated = assembly with
        {
            Nodes = nodes.Select(n => n.Id == 1 ? n with { Pose = new(new(10, 0, 0), Vector3.UnitY, -Vector3.UnitX, Vector3.UnitZ) } : n).ToArray()
        };
        var rotatedGeometry = ExplodedGeometry.Build(rotated, detail);
        check(ExplodedGeometry.GroupDirection(rotated, detail, 2) == -Vector3.UnitX && Delta(rotatedGeometry, 2).X < 0, "Root pose origin, not whole-assembly center, determines outward direction");
        var rotatedX = ExplodedGeometry.Build(rotated, detail with
        {
            Axis = 1
        });
        check(Delta(rotatedX, 2).Y > 0 && Delta(rotatedX, 2).X == 0 && Delta(rotatedX, 2).Z == 0, "Explicit X follows rotated root-local X rather than world X");
        var overridden = ExplodedGeometry.Build(assembly, detail with
        {
            Axis = 1,
            Groups = [new(2, Vector3.Zero, Axis: 3)]
        });
        check(Delta(overridden, 2).Z > 0 && Delta(overridden, 2).X == 0 && Delta(overridden, 4).X < 0, "Per-group axis override does not alter other groups");
        var reordered = assembly with
        {
            Nodes = nodes.Reverse().ToArray()
        };
        check(ExplodedGeometry.Build(reordered, detail).MovedCenters.SequenceEqual(geometry.MovedCenters), "Hierarchy enumeration order does not change spread");
        var basis = SheetLayout.Basis(4);
        var dragFrame = new ProjectionFrame(800, 700, 100, Vector3.Zero, basis.Right, basis.Up, basis.Toward);
        var dragOffset = ExplodedGeometry.DragOffset(dragFrame, Vector3.UnitX, new(100, 80));
        var projected = new Vector2(Vector3.Dot(Vector3.UnitX, basis.Right), -Vector3.Dot(Vector3.UnitX, basis.Up));
        check(dragOffset.Y == 0 && dragOffset.Z == 0 && Math.Abs(dragOffset.X - Vector2.Dot(new(100, 80), projected) / (100 * projected.LengthSquared())) < .0001f, "Isometric drag projects onto the chosen 3D axis without sideways drift");
        check(ExplodedGeometry.DragOffset(dragFrame, basis.Toward, new(100, 80)) == Vector3.Zero, "End-on axis refuses unstable drag movement");
        var concentric = assembly with
        {
            Geometry = new Capture(capture.Meshes.Select(m => Cube(m.Name, 0, 0, 0)).ToArray())
        };
        check(ExplodedGeometry.GroupDirection(concentric, detail, 2) == Vector3.UnitY, "Concentric shells use deterministic local Y instead of a circular screen-plane fallback");
        var options = new PrintOptions(Layout: 2, DrawingSize: 100, TitleBlock: new());
        var parts = Enumerable.Range(0, 4).Select(i => new LabelPart(i, nodes[i].Name, nodes[i].TemplateId, [i], NodeId: nodes[i].Id)).ToArray();
        var mainCapture = new Capture(capture.Meshes.Take(4).ToArray());
        var original = SheetLayout.Render(mainCapture, source.Maps, "Synthetic assembly fixture", parts, options, [], default, _ => { }) with
        {
            DetailSource = source
        };
        original.Scene!.MeshNodes = assembly.MeshNodes;
        var initial = ExplodedPainting.Compose(original, original, [detail], [], default, true);
        // A distinctive background pixel in the unused frame margin must survive.
        int marginPixel = ((int)detail.Bounds.Y + 2) * original.PixelWidth + (int)detail.Bounds.X + 2;
        int rgbIndex = marginPixel * 3;
        byte priorPixel = original.Scene!.BaseRgb[rgbIndex];
        original.Scene.BaseRgb[rgbIndex] = 123;
        var cropped = ExplodedPainting.Compose(original, initial, [detail], [], default);
        check(cropped.Scene!.BaseRgb[rgbIndex] == 123, "Unused detail frame margin preserves underlying drawing pixels");
        original.Scene.BaseRgb[rgbIndex] = priorPixel;
        var transparentTile = initial.Scene!.Details.Single().Pixels;
        int blank = Enumerable.Range(0, transparentTile.Ink.Length).First(q =>
            q / transparentTile.Width > transparentTile.Height / 3
            && q / transparentTile.Width < transparentTile.Height * 2 / 3
            && q % transparentTile.Width > transparentTile.Width / 3
            && q % transparentTile.Width < transparentTile.Width * 2 / 3
            && transparentTile.Ink[q] == 0 && transparentTile.MeshOwners[q] < 0);
        int hole = (((int)detail.Bounds.Y + 12 + blank / transparentTile.Width) * original.PixelWidth
            + (int)detail.Bounds.X + 12 + blank % transparentTile.Width) * 3;
        byte priorHole = original.Scene.BaseRgb[hole];
        original.Scene.BaseRgb[hole] = 123;
        var transparent = ExplodedPainting.Compose(original, initial, [detail], [], default);
        check(transparent.Scene!.BaseRgb[hole] == 123, "Empty pixels inside exploded geometry preserve lower drawing layers");
        original.Scene.BaseRgb[hole] = priorHole;

        var renamed = ExplodedPainting.Compose(original, initial, [detail with { Caption = "A longer assembly caption" }], [], default);
        var captionTile = renamed.Scene!.Details.Single();
        check(ReferenceEquals(captionTile.Pixels, initial.Scene!.Details.Single().Pixels), "Caption length does not resize or rerender exploded geometry");
        check(captionTile.CaptionBounds.Height == 2 * captionTile.CaptionFont + 27,
            "Exploded caption reserves separate heading, centered name and scale lines");
        var freeNote = FreeTextPainting.Fit(new(42, "Assembly handling note", new(1700, 150, 500, 100), 32, true, true, true, 2), options);
        var noteSpace = TextBoxPlacement.Space(initial.Scene!, [], []);
        freeNote = freeNote with
        {
            Bounds = FrameLayout.Clear(freeNote.Bounds, noteSpace.Obstacles,
            FrameLayout.Area(options), false, freeNote.Bounds.Width, freeNote.Bounds.Height, fits: noteSpace.Clear)
            ?? throw new Exception("No clear space for the independent text fixture")
        };
        var notedDetail = AnnotationPainting.Compose(initial with
        {
            Scene = initial.Scene!.WithFreeText([freeNote])
        }, [], default);
        var styledRequest = PrintStyles.Apply(notedDetail, LeaderLine.Solid, LeaderTip.None);
        var styledDetail = PreviewUpdates.Render(styledRequest, original, styledRequest.Options, default);
        check(styledDetail.Details.Single().GuideLine == LeaderLine.Solid
            && !styledDetail.Scene!.Details[0].Pixels.Ink.SequenceEqual(notedDetail.Scene!.Details[0].Pixels.Ink),
            "Line-only live update redraws existing exploded guide pixels instead of reusing dotted tiles");
        check(styledDetail.Scene!.Details[0].Frame == notedDetail.Scene!.Details[0].Frame
            && styledDetail.Scene.Details[0].Pixels.MeshOwners.SequenceEqual(notedDetail.Scene.Details[0].Pixels.MeshOwners)
            && styledDetail.Scene.FreeText.Single() == freeNote,
            "Guide style update preserves geometry, camera, picking and independent text");
        var regeneratedStyle = CompositionRegeneration.Rebuild(original, styledRequest, default);
        check(regeneratedStyle.Rgb.SequenceEqual(styledDetail.Rgb), "Manual regeneration and live guide style update produce identical pixels");
        var styleHistory = new AnnotationDocument(notedDetail.Scene!, notedDetail.Labels);
        styleHistory.Rebind(notedDetail.Scene!, notedDetail.Labels, notedDetail.Details, false);
        styleHistory.Rebind(styledDetail.Scene!, styledDetail.Labels, styledDetail.Details);
        styleHistory.Undo();
        var undoneStyle = PreviewUpdates.Render(styledDetail with
        {
            Scene = styleHistory.Scene, Details = styleHistory.Details, Labels = styleHistory.Labels
        }, original, styleHistory.Scene.Options, default);
        check(styleHistory.Details.Single().GuideLine == LeaderLine.Dotted && undoneStyle.Rgb.SequenceEqual(notedDetail.Rgb),
            "Undo recomposes the original guide pattern and exact pixels");
        styleHistory.Rebind(undoneStyle.Scene!, undoneStyle.Labels, undoneStyle.Details, false);
        styleHistory.Redo();
        var redoneStyle = PreviewUpdates.Render(undoneStyle with
        {
            Scene = styleHistory.Scene, Details = styleHistory.Details, Labels = styleHistory.Labels
        }, original, styleHistory.Scene.Options, default);
        check(styleHistory.Details.Single().GuideLine == LeaderLine.Solid && redoneStyle.Rgb.SequenceEqual(styledDetail.Rgb),
            "Redo recomposes solid assembly guides and exact pixels");
        check(notedDetail.Details.Single().GuideLine == LeaderLine.Dotted, "Pending global styles leave the accepted detail definition untouched");
        var hiddenGuides = detail with { Guides = false };
        var hiddenDots = ExplodedGeometry.Render(source, hiddenGuides, default, options);
        var hiddenSolid = ExplodedGeometry.Render(source, hiddenGuides with { GuideLine = LeaderLine.Solid }, default, options);
        check(hiddenDots.Pixels.Ink.SequenceEqual(hiddenSolid.Pixels.Ink), "Disabled assembly guides remain hidden for every line preference");
        var appearance = original.Options with
        {
            Theme = 1,
            Grid = false,
            TitleBlock = original.Options.TitleBlock! with
            {
                Title = "Updated assembly",
                Revision = "4"
            }
        };
        var liveDetail = PreviewUpdates.Render(notedDetail, original, appearance, default);
        check(ReferenceEquals(liveDetail.Scene!.Details[0].Pixels, notedDetail.Scene!.Details[0].Pixels)
            && liveDetail.Details.SequenceEqual(notedDetail.Details) && liveDetail.Scene.FreeText.Single() == freeNote,
            "Appearance updates reuse exploded geometry and retain detail/text placement");
        var directMain = SheetLayout.Render(mainCapture, source.Maps, original.Title, parts, appearance, [], default, _ => { }, composeAnnotations: false) with
        {
            DetailSource = source
        };
        directMain.Scene!.MeshNodes = assembly.MeshNodes;
        var directAppearance = CompositionRegeneration.Rebuild(directMain, notedDetail, default);
        check(liveDetail.Rgb.SequenceEqual(directAppearance.Rgb), "Cached main and exploded appearance matches a full geometry regeneration");
        var liveDocument = new AnnotationDocument(original.Scene!);
        liveDocument.Rebind(notedDetail.Scene!, notedDetail.Labels, notedDetail.Details);
        liveDocument.Rebind(liveDetail.Scene, liveDetail.Labels, liveDetail.Details);
        liveDocument.Undo();
        var restoredAppearance = PreviewUpdates.Render(liveDetail with
        {
            Scene = liveDocument.Scene,
            Details = liveDocument.Details
        }, liveDetail.PoseMain!, liveDocument.Scene.Options, default);
        check(restoredAppearance.Rgb.SequenceEqual(notedDetail.Rgb), "Appearance undo restores exact drawing/detail/text pixels");
        var movedWithNote = ExplodedPainting.Compose(original, notedDetail, [detail with { Bounds = detail.Bounds with { Y = detail.Bounds.Y + 20 } }], [], default, true);
        check(movedWithNote.Scene!.FreeText.Single() == freeNote, "Exploded recomposition preserves independent free-text boxes and formatting");
        var detailAnchor = ViewAlignment.Anchors(initial.Scene!).Single(a => a.ViewId == detail.ViewId);
        var mainAnchor = ViewAlignment.Anchors(initial.Scene!).Where(a => a.ViewId < 1000).OrderBy(a => a.Position.Y).First();
        var nearDelta = new Vector2(0, mainAnchor.Position.Y - detailAnchor.Position.Y - 7);
        var captionSnap = ViewAlignment.Snap([detailAnchor with { Position = detailAnchor.Position + nearDelta }], [mainAnchor], 10, lockX: true);
        var alignedDetail = detail with
        {
            Bounds = detail.Bounds with
            {
                Y = detail.Bounds.Y + nearDelta.Y + captionSnap.Delta.Y
            },
            Camera = initial.Scene!.Details[0].Frame
        };
        var alignedSheet = ExplodedPainting.Compose(original, initial, [alignedDetail], [], default, true);
        var alignedAnchor = ViewAlignment.Anchors(alignedSheet.Scene!).Single(a => a.ViewId == detail.ViewId);
        check(Math.Abs(alignedAnchor.Position.Y - mainAnchor.Position.Y) < 1 && captionSnap.Guides.Length == 1,
            "Snapped exploded caption stays on the main-view baseline after actual recomposition");
        check(ReferenceEquals(initial.Scene.Details[0].Pixels, alignedSheet.Scene!.Details[0].Pixels),
            "Caption alignment reuses exploded geometry rather than rerendering the assembly");
        var countedParts = initial.Scene!.Parts.Select(p => p.DetailOnly && p.NodeId is 2 or 4 ? p with { Name = "Repeated fitting", TemplateId = "repeat" } : p).ToArray();
        var countedScene = new RenderScene(initial.Scene.BaseRgb, initial.Scene.Owners, countedParts, initial.Scene.Views, options: options) { Details = initial.Scene.Details };
        int countedPart = countedParts.Single(p => p.DetailOnly && p.NodeId == 2).Id;
        check(LabelQuantities.Count(countedScene, detail.ViewId, countedPart) == 2, "Exploded QTY counts matching distinct assembly groups, not meshes or other drawing views");
        var actualDetail = initial.Scene!.Details[0].Frame;
        var sizing = initial.Scene.Details[0].Sizing!;
        var fixedParts = DetailPartSizing.Place(geometry, sizing, actualDetail.Width, actualDetail.Height, detail.View, options);
        var largerBox = DetailPartSizing.Place(geometry, sizing, (int)(actualDetail.Width * 1.3f), (int)(actualDetail.Height * 1.3f), detail.View, options);
        var tighterBox = DetailPartSizing.Place(geometry, sizing, (int)(actualDetail.Width * .85f), (int)(actualDetail.Height * .85f), detail.View, options);
        check(largerBox.Frame.Scale == actualDetail.Scale && tighterBox.Frame.Scale == actualDetail.Scale && largerBox.Sizing.Spread > sizing.Spread && tighterBox.Sizing.Spread < sizing.Spread,
            "Resizing an exploded box changes spread while retaining the physical part scale");
        Vector2 ProjectPoint(Vector3 p, ProjectionFrame f) => new(Vector3.Dot(p - f.Center, f.Right) * f.Scale, -Vector3.Dot(p - f.Center, f.Up) * f.Scale);
        var smallerParts = DetailPartSizing.Place(geometry, sizing with
        {
            Ratio = sizing.Ratio * 1.25f
        }, actualDetail.Width, actualDetail.Height, detail.View, options);
        check(Enumerable.Range(0, geometry.GroupNodes.Length).All(i => Vector2.Distance(ProjectPoint(smallerParts.Geometry.MovedCenters[i], smallerParts.Frame), ProjectPoint(fixedParts.Geometry.MovedCenters[i], fixedParts.Frame)) < .001f),
            "Changing part scale leaves exploded group centres at the same paper positions");
        float ScreenLength(DetailGeometry g, ProjectionFrame f) => Vector3.Distance(ExplodedGeometry.World(g.Capture.Meshes[0].Vertices[0]), ExplodedGeometry.World(g.Capture.Meshes[0].Vertices[1])) * f.Scale;
        check(Math.Abs(ScreenLength(smallerParts.Geometry, smallerParts.Frame) / ScreenLength(fixedParts.Geometry, fixedParts.Frame) - .8f) < .0001f
            && Math.Abs(ScreenLength(largerBox.Geometry, largerBox.Frame) - ScreenLength(fixedParts.Geometry, fixedParts.Frame)) < .001f,
            "Part vertices change apparent size only with the part-scale control, not box dimensions");
        var loose = DetailPartSizing.Place(geometry, sizing with
        {
            Spread = sizing.Spread * .5f
        }, actualDetail.Width, actualDetail.Height, detail.View, options);
        var partLimit = ScaleAdjustment.Detail(geometry, loose.Sizing, detail.View, options);
        var atLimit = DetailPartSizing.Place(geometry, loose.Sizing with
        {
            Ratio = loose.Sizing.Ratio * 100 / partLimit.Maximum
        }, actualDetail.Width, actualDetail.Height, detail.View, options);
        check(partLimit.Maximum > 100 && atLimit.Sizing.Spread == loose.Sizing.Spread, "Detail scale marker allows larger parts while holding group centres fixed");
        try
        {
            DetailPartSizing.Place(geometry, loose.Sizing with
            {
                Ratio = loose.Sizing.Ratio * 100 / (partLimit.Maximum + .2f)
            }, actualDetail.Width, actualDetail.Height, detail.View, options);
            throw new Exception("Beyond-marker part size accepted");
        }
        catch (InvalidOperationException) { check(true, "Detail scale marker stops at the actual frame fit limit"); }
        var bigParts = DetailPartSizing.Place(geometry, loose.Sizing with
        {
            Ratio = sizing.Ratio * .8f
        }, actualDetail.Width, actualDetail.Height, detail.View, options);
        check(Math.Abs(ScreenLength(bigParts.Geometry, bigParts.Frame) / ScreenLength(loose.Geometry, loose.Frame) - 1.25f) < .0001f && bigParts.Sizing.Spread == loose.Sizing.Spread,
            "Larger parts can occupy a tighter spread without changing the chosen centre spacing");
        try
        {
            DetailPartSizing.Place(geometry, sizing with
            {
                Ratio = .01f
            }, actualDetail.Width, actualDetail.Height, detail.View, options);
            throw new Exception("Oversized parts were accepted");
        }
        catch (InvalidOperationException e) { check(e.Message.Contains("too big for the detail box"), "Impossible part sizes report a fit error instead of silently shrinking parts"); }
        var resizedDetail = detail with
        {
            Bounds = detail.Bounds with
            {
                Width = 960,
                Height = 900
            },
            Camera = null,
            Sizing = sizing
        };
        var resizedPrint = ExplodedPainting.Compose(original, initial, [resizedDetail], [], default, true);
        check(resizedPrint.Scene!.Details[0].Frame.Scale == actualDetail.Scale && resizedPrint.Details[0].Sizing == resizedPrint.Scene.Details[0].Sizing
            && resizedPrint.Details[0].Sizing!.Width == 936, "Committed resize stores the resolved spacing and keeps part scale in the exported projection");
        var sizeRegenerated = CompositionRegeneration.Rebuild(original, resizedPrint, default);
        check(sizeRegenerated.Details[0].Sizing == resizedPrint.Details[0].Sizing && sizeRegenerated.Scene!.Details[0].Frame.Scale == actualDetail.Scale,
            "Generate retains independent detail part scale and spread");
        var sizeHistory = new AnnotationDocument(original.Scene, []);
        sizeHistory.Rebind(initial.Scene, [], initial.Details);
        sizeHistory.Rebind(resizedPrint.Scene, [], resizedPrint.Details);
        sizeHistory.Undo();
        check(sizeHistory.Details.Single().Bounds == detail.Bounds, "Undo restores the exploded box before independent resizing");
        sizeHistory.Redo();
        check(sizeHistory.Details.Single().Sizing == resizedPrint.Details[0].Sizing, "Redo retains independent part scale and resolved spread");
        check(actualDetail.Scale == PrintScale.FitScale(actualDetail.Scale, options), "Runtime detail projection uses its independent clean physical scale");
        var tableLayout = new TableLayout(new(1640, 300, 620, 400), new(1640, 724, 620, 200));
        var relocatedTables = AnnotationPainting.Compose(initial, [], default, tableLayout);
        var tablesRetained = ExplodedPainting.Compose(original, relocatedTables, [detail with { Bounds = detail.Bounds with { Y = detail.Bounds.Y + 40 } }], [], default, true);
        check(tablesRetained.Scene!.Tables == tableLayout, "Detail recomposition preserves independently moved tables");
        var linkedReference = KittenReferences.FromView(tablesRetained.Scene!, 1, detail.ViewId, kind: ScaleReferenceKind.Banana);
        var withReference = tablesRetained with { Scene = tablesRetained.Scene!.WithKittens([linkedReference]) };
        var noDetails = ExplodedPainting.Compose(original, withReference, [], [], default);
        check(noDetails.Scene!.Kittens.Length == 0,
            "Deleting an exploded view removes its linked scale reference within the same edit");
        check(noDetails.Scene!.Tables == tableLayout, "Removing the last detail preserves table positions");
        var distant = detail with
        {
            Camera = initial.Scene!.Details[0].Frame,
            Groups = [new(2, new(999, 555, 0))]
        };
        var distantRendered = ExplodedGeometry.Render(source, distant, default);
        var distantGeometry = ExplodedGeometry.Build(assembly, distant);
        var camera = distantRendered.Frame;
        check(distantGeometry.Capture.Meshes.SelectMany(m => m.Vertices).Select(ExplodedGeometry.World).All(p => Math.Abs(Vector3.Dot(p - camera.Center, camera.Right)) * camera.Scale < camera.Width * .5f && Math.Abs(Vector3.Dot(p - camera.Center, camera.Up)) * camera.Scale < camera.Height * .5f), "Dragging beyond the old camera reframes all geometry without clipping export");
        check(initial.Scene!.Visible.Any(p => p.Key.View == detail.ViewId) && initial.Scene.Details.Length == 1, "Exploded frame renders pickable group geometry with the runtime renderer");
        bool outsideSame = true;
        for (int y = 0; y < original.PixelHeight; y++)
            for (int x = 0; x < original.PixelWidth; x++)
                if (!detail.Bounds.Contains(new(x, y)))
                    for (int c = 0; c < 3; c++)
                        if (original.Scene.BaseRgb[(y * original.PixelWidth + x) * 3 + c] != initial.Scene.BaseRgb[(y * original.PixelWidth + x) * 3 + c])
                            outsideSame = false;
        check(outsideSame, "Detail frame leaves all underlying print pixels outside its rectangle unchanged");
        check(PrintTables.Catalog(initial.Scene).Sum(p => p.Quantity) == parts.Length, "Detail instances do not inflate parts-table quantities");
        CheckTableLabels(check, initial, detail.ViewId, parts.Length, tableLayout);
        var target = initial.Scene.Visible.First(p => p.Key.View == detail.ViewId);
        var document = new AnnotationDocument(original.Scene);
        document.Rebind(initial.Scene, [], [detail]);
        var label = document.Draft(detail.ViewId, target.Key.Part, out _)! with
        {
            X = 220,
            Y = 350,
            Socket = new(.5f, 0)
        };
        check(document.Apply([label], out _), "Manual part labels can attach to exploded groups");
        var annotated = AnnotationPainting.Compose(initial, document.Labels, default);
        var customCaption = initial.Scene.Captions.First();
        var editedCaptions = initial.Scene.Captions.Select(c => c.ViewId == customCaption.ViewId ? ViewCaption.Resize(c, initial.Scene.Views.Single(v => v.Id == c.ViewId), 60) : c).ToArray();
        var mainTarget = initial.Scene.Visible.First(p => p.Key.View < 1000);
        var mainLabel = new PartLabel(2, mainTarget.Key.View, mainTarget.Key.Part, "Keep my wording", 180, 480, ManualPlacement: true);
        var flag = label with
        {
            Id = 3,
            Text = "My custom flagnote",
            FlagNote = true,
            X = 300,
            Y = 550,
            AttachmentPinned = true,
            Attachment = new(target.Value.Bounds.X, target.Value.Bounds.Y)
        };
        var composition = AnnotationPainting.Compose(annotated with
        {
            Scene = annotated.Scene!.WithCaptions(editedCaptions)
        }, [label, mainLabel, flag], default, tableLayout);
        var reorderedParts = parts.Reverse().Select((p, i) => p with { Id = i }).ToArray();
        var fresh = SheetLayout.Render(mainCapture, source.Maps, "Synthetic assembly fixture", reorderedParts, options, [], default, _ => { })
            with
        {
            DetailSource = source with
            {
                Assembly = assembly with
                {
                    Nodes = assembly.Nodes.Reverse().ToArray()
                }
            }
        };
        fresh.Scene!.MeshNodes = assembly.MeshNodes;
        var regenerated = CompositionRegeneration.Rebuild(fresh, composition, default);
        check(regenerated.Details.Single().Bounds == detail.Bounds && regenerated.Labels.Length == 3 && regenerated.Scene!.Tables == tableLayout, "Generate retains exploded frames, main/detail labels, custom flagnotes and moved tables");
        check(regenerated.Labels.All(l => l.X == composition.Labels.Single(p => p.Id == l.Id).X && l.Y == composition.Labels.Single(p => p.Id == l.Id).Y && l.Text == composition.Labels.Single(p => p.Id == l.Id).Text), "Generate preserves manual text positions and user wording");
        check(regenerated.Labels.All(l => regenerated.Scene!.Parts.Single(p => p.Id == l.PartId).NodeId == composition.Scene!.Parts.Single(p => p.Id == composition.Labels.Single(old => old.Id == l.Id).PartId).NodeId), "Stable native identities retain main and detail anchors when capture enumeration changes");
        check(regenerated.Scene!.Captions.Single(c => c.ViewId == customCaption.ViewId).FontSize == 60 && regenerated.Labels.Single(l => l.Id == 3).AttachmentPinned, "Generation retains caption sizing and explicit endpoint pins");
        var twice = CompositionRegeneration.Rebuild(fresh, regenerated with
        {
            RegeneratedBase = null
        }, default);
        check(twice.Details.SequenceEqual(regenerated.Details) && twice.Labels.SequenceEqual(regenerated.Labels), "Repeated Generate is stable without losing or duplicating composition");
        var kept = composition.Labels.ToArray();
        try
        {
            CompositionRegeneration.Rebuild(fresh with
            {
                DetailSource = source with
                {
                    Assembly = assembly with
                    {
                        Nodes = assembly.Nodes.Where(n => n.Id != detail.Scope).ToArray()
                    }
                }
            }, composition, default);
            throw new Exception("Lost detail accepted");
        }
        catch (InvalidOperationException ex) { check(ex.Message.Contains("previous print is unchanged") && composition.Labels.SequenceEqual(kept), "A missing assembly aborts generation without mutating the prior composition"); }
        var missingView = fresh.Scene.Views.Where(v => v.Id != mainLabel.ViewId).ToArray();
        var removedScene = new RenderScene(fresh.Scene.BaseRgb, fresh.Scene.Owners, reorderedParts, missingView, options: options) { MeshOwners = fresh.Scene.MeshOwners, MeshNodes = assembly.MeshNodes };
        try
        {
            CompositionRegeneration.Rebuild(fresh with
            {
                Scene = removedScene
            }, composition, default);
            throw new Exception("Lost label accepted");
        }
        catch (InvalidOperationException ex) { check(ex.Message.Contains("previous print is unchanged") && composition.Labels.SequenceEqual(kept), "Removing an annotated view reports the conflict and keeps the current print"); }
        var movedDetail = detail with
        {
            Bounds = detail.Bounds with
            {
                X = detail.Bounds.X + 100,
                Y = detail.Bounds.Y + 120
            }
        };
        var moved = ExplodedPainting.Compose(original, annotated, [movedDetail], document.Labels, default, true);
        document.Rebind(moved.Scene!, moved.Labels, moved.Details);
        check(moved.Labels[0].X == label.X + 100 && moved.Labels[0].Y == label.Y + 120 && moved.Labels[0].Socket == label.Socket, "Moving detail carries annotation text once and preserves normalized socket coordinates");
        check(ReferenceEquals(initial.Scene.Details[0].Pixels, moved.Scene!.Details[0].Pixels), "Moving a detail reuses rendered geometry without rerasterization");
        document.Undo();
        var undone = ExplodedPainting.Compose(original, moved, document.Details, document.Labels, default);
        document.Rebind(undone.Scene!, undone.Labels, undone.Details);
        check(document.Labels[0] == label && document.Details[0].Bounds == detail.Bounds, "Undo restores frame and annotations without applying their translation twice");
        document.Redo();
        var redone = ExplodedPainting.Compose(original, undone, document.Details, document.Labels, default);
        document.Rebind(redone.Scene!, redone.Labels, redone.Details);
        check(document.Labels[0] == moved.Labels[0] && document.Details[0] == movedDetail, "Redo restores translated frame and label together");
        var deleted = ExplodedPainting.Compose(original, redone, [], document.Labels, default, true);
        document.Rebind(deleted.Scene!, deleted.Labels, deleted.Details);
        check(document.Labels.Length == 0 && document.Details.Length == 0, "Deleting a detail removes its labels in the same transaction");
        document.Undo();
        var restored = ExplodedPainting.Compose(original, deleted, document.Details, document.Labels, default);
        document.Rebind(restored.Scene!, restored.Labels, restored.Details);
        check(document.Labels.Length == 1 && document.Details.Length == 1, "Undo deletion restores frame and annotations after geometry regeneration");
        var size = ExplodedPainting.Compose(original, restored, [movedDetail with { Bounds = movedDetail.Bounds with { Width = 1000, Height = 900 } }], document.Labels, default, true);
        check(size.Scene!.Details[0].Pixels.Width == 976 && size.Scene.Details[0].Pixels.Height == 900 - ExplodedPainting.CaptionSpace, "Resizing detail rerenders its actual viewport resolution with space for a lower caption");
        var allShapes = Enumerable.Range(0, 3).SelectMany(o => Enumerable.Range(0, 6).Select(a => options with { Orientation = o, Aspect = a }));
        check(allShapes.All(o => { var b = ExplodedPainting.ClampFrame(new(-999, -999, 9999, 9999), o); var a = FrameLayout.Area(o); return b.X >= a.X && b.Y >= a.Y && b.Right <= a.Right && b.Bottom <= a.Bottom; }), "Movable frame stays in drawable paper for every orientation and ratio");
        var stacked = ExplodedPainting.Compose(original, initial, [detail, detail with { Id = 2 }], [], default);
        check(stacked.Details.Length == 2, "Transparent exploded details may occupy the same frame");
        string folder = Path.Combine(output, "exploded-fixture");
        Directory.CreateDirectory(folder);
        PrintFiles.Save(size, folder);
        Png.Write(Path.Combine(folder, "guides-dotted.png"), notedDetail.PixelWidth, notedDetail.PixelHeight, notedDetail.Rgb);
        Png.Write(Path.Combine(folder, "guides-solid.png"), styledDetail.PixelWidth, styledDetail.PixelHeight, styledDetail.Rgb);
    }
    static void CheckTableLabels(Action<bool, string> check, Sheet exploded, int viewId, int physicalPartCount, TableLayout tables)
    {
        var options = exploded.Options with { PartsTable = true, FlagTable = true, FreeLayout = true };
        var scene = exploded.Scene!.WithOptions(options).WithTables(tables);
        var target = scene.Visible.First(p => p.Key.View == viewId && PrintTables.PartKey(scene, p.Key.Part) != null);
        var alias = scene.Parts.Single(p => p.Id == target.Key.Part);
        var physical = scene.Parts.Single(p => !p.DetailOnly && p.NodeId == alias.NodeId);
        var doc = new AnnotationDocument(scene);
        var circle = doc.Draft(viewId, alias.Id, out _, symbol: LabelSymbol.Circle)! with { X = 240, Y = 320 };
        check(alias.DetailOnly && circle.Key != null && circle.Key == PrintTables.PartKey(scene, physical.Id) &&
            PrintTables.Catalog(scene).Sum(row => row.Quantity) == physicalPartCount,
            "Exploded labels use the matching craft part's table symbol without adding quantity");
        var flag = doc.Draft(viewId, alias.Id, out _, LeaderLine.Solid, LeaderTip.None, "Safety check", LabelSymbol.Flag)! with { Id = 2, X = 360, Y = 420 };
        check(doc.Apply([circle, flag], out _), "The drawing accepts a circular part symbol and a picket flagnote together");
        var image = AnnotationPainting.Compose(exploded with { Scene = scene, Options = options }, doc.Labels, default);
        var circleRoute = doc.Routes.Single(r => r.Label.Id == circle.Id);
        var circleBox = circleRoute.TextBox;
        check(circleBox.Width == circleBox.Height && Math.Abs(Vector2.Distance(circleBox.Center, circleRoute.Start) - circleBox.Width / 2) < .001f &&
            image.ObjectHits![new(DrawingObjectKind.Label, circle.Id)].Hit(new(circleBox.Center.X, circleBox.Y), 2),
            "The circle leader meets the round outline, and the exported outline is selectable");
        var flagRoute = doc.Routes.Single(r => r.Label.Id == flag.Id);
        var flagBox = flagRoute.TextBox;
        foreach (var anchor in new[] { new Vector2(flagBox.X - 50, flagBox.Y), new Vector2(flagBox.Right + 50, flagBox.Y) })
        {
            var outline = AnnotationText.SymbolOutline(LabelSymbol.Flag, flagBox, anchor);
            check(outline.Length == 5 && outline[0] == LeaderRouting.TextSocket(flagRoute.Label, flagBox, anchor) &&
                outline[0].X == (anchor.X < flagBox.X ? flagBox.X : flagBox.Right),
                "The flag's leader connects at the picket tip on the side facing the part");
        }

        check(doc.Change(circle with { FullText = true }, out _) && doc.Labels[0].Key == null &&
            string.Join(" ", doc.Routes.Single(r => r.Label.Id == circle.Id).Lines) == circle.Text &&
            PrintTables.Rows(scene, doc.Labels, 0).Any(row => row.Key == circle.Key),
            "Converting a bubble displays the label text and keeps its parts-table row");
        doc.Undo();
        check(doc.Labels[0] == circle, "Undo restores the circle and table letter");
        doc.Redo();
        var withoutTable = scene.WithOptions(options with { PartsTable = false });
        var restored = new AnnotationDocument(scene, PrintTables.ResolveLabelKeys(withoutTable, doc.Labels));
        check(restored.Labels[0] is { FullText: true, Key: null, Symbol: LabelSymbol.Circle } &&
            restored.Change(restored.Labels[0] with { FullText = false }, out _) && restored.Labels[0] == circle,
            "The full-label override survives table toggles and can be switched back to the original symbol");
        var oldLabel = circle with { Key = null, Symbol = LabelSymbol.Square };
        check(new AnnotationDocument(scene, [oldLabel]).Labels.Single().Key == circle.Key,
            "Existing exploded labels acquire their catalog letter when reopened with a parts table");
    }

    static void CheckRootedScope(Action<bool, string> check)
    {
        static JointKey Joint(uint a, uint b) => JointKey.Between(new(a, "end"), new(b, "end"));
        var nodes = new AssemblyNode[] { new(1, null, "Tank", "tank", false),
            new(2, 1, "Cargo bay", "bay", false), new(3, 2, "Capsule", "capsule", false),
            new(4, 2, "Payload", "payload", true), new(5, 1, "Engine", "engine", false) };
        var links = new SourceLink[] { new(SourceLinkKind.Structural, Joint(2, 1)),
            new(SourceLinkKind.Structural, Joint(3, 2)), new(SourceLinkKind.Ownership, Joint(2, 4)),
            new(SourceLinkKind.Structural, Joint(1, 5)), new(SourceLinkKind.Fuel, Joint(3, 5)) };
        var binding = new SourceBinding(Guid.NewGuid(), 1);
        var parts = nodes.Select(n => new SourcePart(n.Id, n.TemplateId, n.Subpart ? n.Parent : null, n.Subpart));
        var assembly = new AssemblyCapture(new([]), nodes, [], []) { Topology = new(binding, parts, links) };
        var detail = new ExplodedDetail(1, 2, default, "Cargo") { Selection = new(2, [Joint(1, 2), Joint(2, 3)], []) };
        check(AssemblyGraph.Included(assembly, detail).SetEquals(new uint[] { 2, 4 }), "Both cargo ends can be cut across opposite hierarchy directions while retaining payload");
        var open = detail with { Selection = detail.Selection! with { Cuts = [] } };
        check(AssemblyGraph.Included(assembly, open with { Selection = open.Selection! with { Removed = [1] } }).Contains(5),
            "Removing the tank alone keeps its engine");
        var branch = open with { Selection = open.Selection! with { RemovedBranches = [1] } };
        check(!AssemblyGraph.Included(assembly, branch).Contains(5), "Removing a branch blocks traversal through the removed part, ignoring fuel links");
        var loop = assembly with { Topology = new(binding, parts, [.. links, new(SourceLinkKind.Structural, Joint(3, 5))]) };
        check(AssemblyGraph.Included(loop, branch).Contains(5), "A separate structural path keeps a downstream part included");
        var toggled = AssemblyGraph.ToggleBranch(assembly, open, 1);
        check(AssemblyGraph.Included(assembly, toggled).SetEquals(new uint[] { 2, 3, 4 }),
            "Clicking a part excludes its branch relative to the cargo root");
        check(AssemblyGraph.Included(assembly, AssemblyGraph.ToggleBranch(assembly, toggled, 5)).SetEquals(nodes.Select(n => n.Id)),
            "Clicking a dimmed descendant restores its excluded branch");
        check(AssemblyGraph.ToggleBranch(assembly, open, 2) == open, "The root stays included when clicked");
        var whole = new ExplodedDetail(1, 1, new(5, 10, 300, 200), "Tank", Seed: 1, Expanded: [1],
            Groups: [new(1, Vector3.One)], Sizing: new(1)) { Selection = new(1, [], [4]) };
        var rerooted = AssemblyGraph.RootSubassembly(loop, whole, 4);
        check(rerooted.Scope == 2 && rerooted.Seed == 2 && rerooted.Caption == "Cargo bay" &&
            rerooted.Selection?.Root == 2 && rerooted.Id == whole.Id && rerooted.Bounds == whole.Bounds,
            "Rerooting from an owned mesh uses the whole part and retains the draft frame");
        check(AssemblyGraph.Included(loop, rerooted).SetEquals(new uint[] { 2, 3, 4 }) &&
            rerooted.Selection!.Cuts.Length == 2 && rerooted.Expanded == null && rerooted.Groups == null && rerooted.Sizing == null,
            "Rerooting includes the new subtree, cutting alternate structural paths and clearing old root-dependent edits");
        check(ExplodedGeometry.AnchorGroup(loop, rerooted) == 2 && whole.Scope == 1 && whole.Selection!.Removed.Contains(4),
            "The new root anchors the explosion and the previous draft stays available for undo");
        check(AssemblyGraph.RootSubassembly(loop, rerooted, 4) == rerooted,
            "Selecting the current whole-part root leaves its scope unchanged");
        var upper = AssemblyGraph.ToggleBranch(assembly, detail, 5);
        check(AssemblyGraph.Included(assembly, upper).SetEquals(new uint[] { 1, 2, 4, 5 }) &&
            upper.Selection!.Cuts.SequenceEqual(new[] { Joint(2, 3) }) && upper.Scope == 2,
            "Clicking a dimmed part past the former root restores just that side of the new root");
        var lower = AssemblyGraph.ToggleBranch(assembly, detail, 3);
        check(AssemblyGraph.Included(assembly, lower).SetEquals(new uint[] { 2, 3, 4 }) &&
            lower.Selection!.Cuts.SequenceEqual(new[] { Joint(1, 2) }),
            "Branches behind a cut can also be restored on the other side of the new root");
        check(AssemblyGraph.Included(loop, AssemblyGraph.ToggleBranch(loop, rerooted, 5)).SetEquals(nodes.Select(n => n.Id)),
            "Restoring a dimmed component reconnects each of its joints to the shown assembly");
        check(AssemblyGraph.Included(assembly, AssemblyGraph.ToggleBranch(assembly, upper, 1)).SetEquals(new uint[] { 2, 4 }),
            "A restored branch can be excluded again without reopening the other cut");
        check(AssemblyGraph.GroupFor(assembly, open, 1) == 1 && AssemblyGraph.GroupFor(assembly, open, 4) == 2,
            "Scoped geometry groups upstream parts and keeps owned meshes with their parent");
    }
}
