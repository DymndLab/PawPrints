using Blueprint.Rendering;
using System.Numerics;
static class PerformanceChecks
{

    public static void NativeRoute(string folder, bool expectOccluded = false)
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "route-scene.json")));
        var settings = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        T Read<T>(string key) => System.Text.Json.JsonSerializer.Deserialize<T>(json.RootElement.GetProperty(key), settings)!;
        var options = Read<PrintOptions>("Options");
        var pixels = new byte[options.Page.Width * options.Page.Height * 3];
        var raw = File.ReadAllBytes(Path.Combine(folder, "route-owners.bin"));
        var owners = new int[raw.Length / 4];
        Buffer.BlockCopy(raw, 0, owners, 0, raw.Length);
        var scene = new RenderScene(pixels, owners, Read<LabelPart[]>("Parts"), Read<ViewRegion[]>("Views"), options: options)
            .WithSpecs(Read<SpecBlock[]>("Specs")).WithCaptions(Read<ViewCaptionMark[]>("Captions"), false).WithPerformance(Read<PerformanceData>("Performance"));
        var block = scene.Specs[0];
        var part = scene.Parts.Single(p => p.NodeId == block.EnginePart);
        var visual = scene.Visible[(block.ViewId, part.Id)];
        Console.WriteLine($"Target {part.Name}, view {block.ViewId}, bounds {visual.Bounds}, edges {visual.Edges.Length}, contacts {visual.ContactEdges.Length}; block {block.Bounds}");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var route = SpecLeaderRouting.Route(scene, block, []);
        watch.Stop();
        Console.WriteLine($"Route {route.Length} points, {watch.ElapsedMilliseconds} ms: {string.Join(';', route)}");
        if (route.Length > 0 && (route.Length > 4 || route[0].Y != route[1].Y || !Enumerable.Range(1, route.Length - 1).All(i =>
            LeaderRouting.ClearStroke(scene, route[i - 1], route[i], i == route.Length - 1 ? part.Id : -1, i == route.Length - 1 ? block.Tip : LeaderTip.None, 10))))
            throw new Exception("Unsafe native route");
        var colors = SheetLayout.Colors(options.Theme);
        var canvas = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
        for (int q = 0; q < owners.Length; q++)
            if (owners[q] >= 0)
                canvas.Ink(q % scene.Width, q / scene.Width, owners[q] == part.Id ? .9f : .2f);
        PerformancePainting.Paint(canvas, scene, []);
        NativeText.Apply(canvas.Pixels, scene.Width, scene.Height, canvas.Runs);
        Png.Write(Path.Combine(folder, "route-mask-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"), scene.Width, scene.Height, canvas.Pixels);
        if (expectOccluded ? route.Length != 0 : route.Length < 2)
            throw new Exception("Native route did not match the expected visibility");
    }

    public static void Run(Action<bool, string> check, string output)
    {
        var mainGimbal = NozzleGimbal.Primary([new(5000, 70, 0), new(950000, 2, 2)]);
        check(mainGimbal.Y == 2 && mainGimbal.Z == 2, "Main nozzle keeps both 2-degree axes instead of mixing auxiliary 70-degree movement");
        check(NozzleGimbal.Primary([new(950000, 2, 2), new(5000, 70, 0)]) == mainGimbal, "Main nozzle choice is independent of nozzle enumeration order");
        var fixedMain = NozzleGimbal.Primary([new(5000, 70, 10), new(950000, 0, 0)]);
        check(fixedMain.Y == 0 && fixedMain.Z == 0, "A fixed main nozzle does not inherit a moving auxiliary gimbal");
        check(NozzleGimbal.Primary([]) == default && NozzleGimbal.Primary([new(double.NaN, 70, 70), new(0, 10, 10)]) == default, "Missing or invalid nozzle ratings do not invent a gimbal range");
        var e = new EngineRating("a", 1, "Test engine", "engine", 0, new(100000, 0, 0), new(80000, 0, 0), 30, 32, .2, "Fuel + Oxidizer", 5, 8, 100);
        var data = new PerformanceData([e, e with { Id = "b", Part = 2 }, e with { Id = "c", Part = 3, Template = "other", Vacuum = new(50000, 0, 0), SeaLevel = new(40000, 0, 0), FlowVac = 10, FlowAsl = 12 }], 5000, 1000, 4000, 12, new(3, 4, 20), [new("Fuel", 4000)], [new(0, "VAC", 5000, 1000, 4000, 2400, 120, ["a", "b"], 10, [new("Fuel", 4000)])]);
        var engine = new SpecBlock(1, new(140, 300, 780, 1600), EnginePart: 1, Fields: (SpecField)511);
        var vehicle = new SpecBlock(2, new(1050, 300, 1050, 1800), Vehicle: true, Fields: (SpecField)131071);
        SpecMetric Row(SpecBlock b, string key) => SpecMetrics.Rows(data, b).Single(r => r.Key == key);
        check(SpecMetrics.Engines(data, engine).Length == 2 && Row(engine, "thrust").Vacuum == 100, "Identical grouping shows quantity while per-engine ratings stay per engine");
        check(Row(engine with
        {
            Combined = true
        }, "thrust").Vacuum == 200, "Combined engine block sums the selected identical group");
        check(SpecMetrics.Engines(data, engine with
        {
            Identical = false
        }).Length == 1, "Ungrouping isolates the selected engine");
        check(Math.Abs(Row(vehicle, "isp").Vacuum!.Value - 250000 / (70 * 9.80665)) < .001, "Effective Isp uses combined thrust and flow, not average Isp");
        check(Math.Abs(Row(vehicle, "twr").Vacuum!.Value - 250000 / (5000 * 9.80665)) < .001, "Vehicle TWR uses captured mass and identified Earth g0");
        check(Row(vehicle with
        {
            Stage = 0
        }, "thrust").Vacuum == 200 && Row(vehicle with
        {
            Stage = 0
        }, "dv").Vacuum == 2400, "Stage scope uses its engine set and native delta-v estimate");
        check(Row(vehicle with
        {
            SelectedEngines = ["c"]
        }, "thrust").Vacuum == 50 && Row(vehicle with
        {
            SelectedEngines = ["c"]
        }, "burn").Text!.StartsWith("Unavailable"), "Custom group isolates ratings and does not fabricate a fuel-feed burn estimate");
        check(SpecMetrics.Rows(data with
        {
            EstimateUnavailable = "Updating"
        }, vehicle).Single(r => r.Key == "dv").Text == "Updating", "Unavailable native estimates remain explicitly unavailable");
        check(PerformancePainting.Environments(Row(engine, "thrust"), engine with
        {
            Environment = 1
        }).Single().Label == "VAC" && PerformancePainting.Environments(Row(engine, "thrust"), engine with
        {
            Environment = 2
        }).Single().Value == 80, "VAC/ASL visibility selects the correct rating");
        check(SpecMetrics.Rows(data, engine with
        {
            Fields = SpecField.Gimbal
        }).Single().Angle, "Field toggles omit other metrics and retain gimbal angle data");
        var multi = data with
        {
            Engines = [e, e with { Id = "second controller" }]
        };
        check(SpecMetrics.Rows(multi, engine with
        {
            Identical = false
        }).Single(r => r.Key == "thrust").Vacuum == 200, "A multi-controller hardware assembly includes all its controllers");
        check(PerformancePainting.Maximum(data, engine with
        {
            Styles = [new("thrust", 0, 350)]
        }, Row(engine, "thrust"), 100) == 350, "Custom bar range is honored in printed units");
        var options = new PrintOptions(DrawingSize: 100);
        var colors = SheetLayout.Colors(0);
        var paper = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
        var owners = Enumerable.Repeat(-1, options.Page.Width * options.Page.Height).ToArray();
        var scene = new RenderScene(paper.Pixels, owners, [], [], options: options).WithPerformance(data);
        var namedData = data with { CraftName = "NewRocket_1" };
        var namedOptions = options with { TitleBlock = new(Title: "Moon Lander") };
        var namedBlock = vehicle with { Fields = SpecField.Name | SpecField.Thrust };
        var namedScene = scene.WithPerformance(namedData).WithOptions(namedOptions).WithSpecs([namedBlock]);
        check(SpecMetrics.Title(namedData, namedBlock, "Moon Lander") == "VEHICLE PERFORMANCE / MOON LANDER"
            && SpecMetrics.Title(namedData, namedBlock, " ") == "VEHICLE PERFORMANCE / NEWROCKET_1",
            "Vehicle heading follows the drawing title and falls back to captured craft name only for a blank title");
        var customBlock = namedBlock with { VehicleName = "Lander / ascent unit" };
        check(SpecMetrics.Title(namedData, customBlock, "Different title") == "VEHICLE PERFORMANCE / LANDER / ASCENT UNIT"
            && SpecMetrics.Title(namedData, customBlock with { VehicleName = null }, "Different title") == "VEHICLE PERFORMANCE / DIFFERENT TITLE",
            "Per-block vehicle-name override stays independent and can return to following the title");
        check(SpecMetrics.Title(namedData, engine, "Moon Lander") == SpecMetrics.Title(data, engine)
            && SpecMetrics.Rows(namedData, customBlock).SequenceEqual(SpecMetrics.Rows(namedData, namedBlock)),
            "Vehicle display names do not rename engines or alter performance calculations");
        Canvas PaintNamed(RenderScene s)
        {
            var c = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, s.Options);
            PerformancePainting.Paint(c, s, []);
            return c;
        }
        check(PaintNamed(namedScene).Runs.Any(r => r.Text == "VEHICLE PERFORMANCE / MOON LANDER")
            && PaintNamed(namedScene.WithOptions(namedOptions with { TitleBlock = new(Title: "Lander Two") }))
                .Runs.Any(r => r.Text == "VEHICLE PERFORMANCE / LANDER TWO"),
            "Printed vehicle heading resolves the current scene title rather than the captured file name");
        var customScene = namedScene.WithSpecs([customBlock]);
        var nameHistory = new AnnotationDocument(namedScene);
        nameHistory.Rebind(customScene, [], []);
        nameHistory.Undo();
        check(nameHistory.Scene.Specs.Single().VehicleName == null, "Undo restores title-following vehicle-name mode");
        nameHistory.Redo();
        check(nameHistory.Scene.Specs.Single().VehicleName == customBlock.VehicleName, "Redo restores the per-block vehicle name");
        var nameSheet = new Sheet("Moon Lander", namedOptions, paper.Pixels, Sheet.MakePreview(paper.Pixels), []) { Scene = namedScene };
        var nameRegenerated = CompositionRegeneration.Rebuild(nameSheet, nameSheet with { Scene = customScene }, default);
        check(nameRegenerated.Scene!.Specs.Single().VehicleName == customBlock.VehicleName
            && PaintNamed(nameRegenerated.Scene).Runs.Any(r => r.Text == "VEHICLE PERFORMANCE / LANDER / ASCENT UNIT"),
            "Regeneration preserves and prints the custom performance-block name");
        var narrowName = namedBlock with { Bounds = new(200, 300, 330, 1000) };
        string longName = string.Join(" ", Enumerable.Repeat("Long vehicle title", 8));
        check(PerformancePainting.Height(namedData, narrowName, 24, longName) > PerformancePainting.Height(namedData, narrowName, 24, "Short"),
            "Performance block sizing accounts for wrapping the resolved drawing title");
        var routeOwners = Enumerable.Repeat(-1, owners.Length).ToArray();
        void Rect(int id, int x, int y, int w, int h)
        {
            for (int yy = y; yy < y + h; yy++)
                for (int xx = x; xx < x + w; xx++)
                    routeOwners[yy * options.Page.Width + xx] = id;
        }
        Rect(0, 400, 800, 60, 60);
        Rect(1, 560, 700, 180, 300);
        var routingScene = new RenderScene(paper.Pixels, routeOwners, [new(0, "Engine", "engine", [], NodeId: 1), new(1, "Gear", "gear", [], NodeId: 2)],
            [new(100, "SIDE", new(300, 600, 600, 600), null)], options: options).WithPerformance(data);
        var routedBlock = engine with
        {
            Bounds = new(1000, 720, 650, 650),
            ViewId = 100,
            Tip = LeaderTip.Dot,
            Leader = true
        };
        routingScene = routingScene.WithSpecs([routedBlock]);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var path = SpecLeaderRouting.Route(routingScene, routedBlock, []);
        timer.Stop();
        check(path.Length is >= 2 and <= 4 && Math.Abs(path[0].Y - path[1].Y) < .1f, "Engine leader uses at most two bends and enters its block horizontally");
        check(LeaderRouting.FirstHit(routingScene, new(1000, 830), new(459, 830), out _, out int blocked) && blocked == 1, "Routing regression reproduces the old straight leader crossing landing gear");
        check(path.Length > 2 && Enumerable.Range(1, path.Length - 1).All(i => LeaderRouting.ClearStroke(routingScene, path[i - 1], path[i], i == path.Length - 1 ? 0 : -1, i == path.Length - 1 ? LeaderTip.Dot : LeaderTip.None)), "Bent engine leader clears intervening parts and lands on the visible engine edge");
        check(timer.Elapsed.TotalSeconds < 5, "Engine detour search remains bounded on the obstructed fixture");
        // Block connection points remain valid when a spec block touches the printed frame.
        var borderOwners = Enumerable.Repeat(-1, owners.Length).ToArray();
        for (int y = 80; y < 110; y++)
            for (int x = 200; x < 240; x++)
                borderOwners[y * options.Page.Width + x] = 0;
        var borderBlock = routedBlock with
        {
            Bounds = new(400, 60, 150, 50)
        };
        var borderScene = new RenderScene(paper.Pixels, borderOwners,
            [new(0, "Engine", "engine", [], NodeId: 1)],
            [new(100, "SIDE", new(60, 60, 600, 200), null)], options: options)
            .WithSpecs([borderBlock]);
        var borderPath = SpecLeaderRouting.Route(borderScene, borderBlock, []);
        check(borderPath.Length is >= 2 and <= 4 && borderPath.All(PageLayout.Placement(options).Contains)
            && borderPath[0].Y < 130 && borderPath[0].Y == borderPath[1].Y,
            "Spec leaders use the current printed border, including the top margin reclaimed for views");
        var routeCanvas = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
        for (int q = 0; q < routeOwners.Length; q++)
            if (routeOwners[q] >= 0)
                routeCanvas.Ink(q % options.Page.Width, q / options.Page.Width, .18f);
        var untouched = (byte[])routeCanvas.Pixels.Clone();
        var expectedLeader = new Canvas((byte[])untouched.Clone(), options.Page.Width, options.Page.Height, colors.Ink);
        LeaderPainting.Paint(expectedLeader, path, routedBlock.Line, routedBlock.Tip);
        PerformancePainting.Paint(routeCanvas, routingScene, []);
        NativeText.Apply(routeCanvas.Pixels, options.Page.Width, options.Page.Height, routeCanvas.Runs);
        int leaderPixels = 0;
        bool sameInk = true;
        for (int q = 0; q < routeOwners.Length; q++)
        {
            if (routedBlock.Bounds.Contains(new(q % options.Page.Width, q / options.Page.Width)))
                continue;
            int p = q * 3;
            if (expectedLeader.Pixels[p] == untouched[p] && expectedLeader.Pixels[p + 1] == untouched[p + 1] && expectedLeader.Pixels[p + 2] == untouched[p + 2])
                continue;
            leaderPixels++;
            for (int c = 0; c < 3; c++)
                sameInk &= expectedLeader.Pixels[p + c] == routeCanvas.Pixels[p + c];
        }
        check(leaderPixels > 100 && sameInk, "Performance leader paints every dot and endpoint with the same visible ink as annotation leaders");
        Canvas Strip() => new(new byte[220 * 160 * 3], 220, 160, new(255));
        var straightInk = Strip();
        var splitInk = Strip();
        LeaderPainting.Paint(straightInk, [new(20, 50), new(180, 50)], LeaderLine.Dotted, LeaderTip.Dot);
        LeaderPainting.Paint(splitInk, [new(20, 50), new(70, 50), new(180, 50)], LeaderLine.Dotted, LeaderTip.Dot);
        check(straightInk.Pixels.SequenceEqual(splitInk.Pixels), "Dotted spacing and endpoint weight do not restart when a leader path is split");
        var cornerInk = Strip();
        LeaderPainting.Paint(cornerInk, [new(20, 50), new(60, 50), new(60, 140)], LeaderLine.Dotted, LeaderTip.None);
        check(cornerInk.Pixels[(62 * 220 + 60) * 3] > 0 && cornerInk.Pixels[(65 * 220 + 60) * 3] == 0 && cornerInk.Pixels[(62 * 220 + 61) * 3] > 0, "Dots retain their spacing and multi-pixel width around a leader bend");
        var styleLabel = new PartLabel(10, 100, 0, "Engine", 200, 500, ManualPlacement: true);
        var styleHistory = new AnnotationDocument(routingScene, [styleLabel]);
        var styledScene = routingScene.WithSpecs([routedBlock with { Line = LeaderLine.Solid, Tip = LeaderTip.OutlineTriangle }]);
        styleHistory.Rebind(styledScene, [styleLabel with { Line = LeaderLine.Solid, Tip = LeaderTip.OutlineTriangle }], []);
        styleHistory.Undo();
        bool restoredStyle = styleHistory.Scene.Specs.Single() == routedBlock && styleHistory.Labels.Single() == styleLabel;
        styleHistory.Redo();
        check(restoredStyle && styleHistory.Scene.Specs.Single().Line == LeaderLine.Solid && styleHistory.Labels.Single().Tip == LeaderTip.OutlineTriangle, "Print-wide label and performance leader styles undo/redo together as one change");
        Directory.CreateDirectory(output);
        Png.Write(Path.Combine(output, "routed-engine-leader-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"), options.Page.Width, options.Page.Height, routeCanvas.Pixels);
        var doc = new AnnotationDocument(scene);
        var compact = scene.WithSpecs([engine]);
        doc.Rebind(compact, [], []);
        doc.Undo();
        check(doc.Scene.Specs.Length == 0, "Undo removes a newly placed performance block");
        doc.Redo();
        check(doc.Scene.Specs.Single() == engine, "Redo restores all performance settings");
        var stacked = engine with
        {
            Id = 2,
            Bounds = new(1050, 300, 1050, 2100),
            Format = 1,
            BarMode = 0
        };
        var paired = scene.WithSpecs([engine, stacked]);
        PerformancePainting.Validate(paired, []);
        PerformancePainting.Paint(paper, paired, []);
        check(paper.Runs.All(r => r.Y + r.Height <= paired.Specs.Max(b => b.Bounds.Bottom)), "All compact and stacked text stays inside the allocated vertical space");
        check(paper.Runs.Any(r => r.Text.Contains("Y +/-5")) && paper.Runs.Any(r => r.Text.Contains("Z +/-8")), "Asymmetric gimbal axes have separate labeled diagrams");
        var gimbalOnly = stacked with
        {
            Fields = SpecField.Gimbal,
            Reference = false
        };
        check(PerformancePainting.Height(data, gimbalOnly, 20) < 150, "Stacked gimbal has a compact text-height section instead of a fixed 190px diagram allowance");
        var controls = gimbalOnly with
        {
            Fields = SpecField.Throttle | SpecField.Gimbal
        };
        check(PerformancePainting.Height(data, controls, 20) < 200, "Gimbal diagram shares the throttle/gimbal text band");
        foreach (double angle in new[] { 0d, 10, 90, 180 })
            foreach (int width in new[] { 280, 500, 780 })
            {
                var model = data with
                {
                    Engines = [e with { GimbalY = angle, GimbalZ = angle }]
                };
                var block = controls with
                {
                    Bounds = new(140, 300, width, 600)
                };
                var canvas = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
                PerformancePainting.Paint(canvas, scene.WithPerformance(model).WithSpecs([block]), []);
                check(canvas.Runs.All(r => r.X >= block.Bounds.X && r.X + r.Text.Length * NativeText.MonoWidth(r.Height) <= block.Bounds.Right + 2 && r.Y + r.Height <= block.Bounds.Y + PerformancePainting.Height(model, block, PerformancePainting.Font(model, block))), $"Compact gimbal text fits at {angle} degrees, width {width}");
            }
        NativeText.Apply(paper.Pixels, options.Page.Width, options.Page.Height, paper.Runs);
        Directory.CreateDirectory(output);
        Png.Write(Path.Combine(output, "formats-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png"), options.Page.Width, options.Page.Height, paper.Pixels);
        var sheet = new Sheet("Test", options, scene.BaseRgb, Sheet.MakePreview(scene.BaseRgb), []) { Scene = scene };
        var regen = CompositionRegeneration.Rebuild(sheet, sheet with
        {
            Scene = compact
        }, default);
        check(regen.Scene!.Specs.Single() == engine, "Generate preserves performance block fields and placement");
        try
        {
            PerformancePainting.Validate(scene.WithSpecs([engine, engine with { Id = 2 }]), []);
            throw new Exception("overlap accepted");
        }
        catch (InvalidOperationException) { check(true, "Overlapping performance blocks are rejected transactionally"); }
        try
        {
            PerformancePainting.Validate(scene.WithSpecs([vehicle with { Stage = 99 }]), []);
            throw new Exception("missing stage accepted");
        }
        catch (InvalidOperationException) { check(true, "Missing stage rejects regeneration instead of silently switching scope"); }
        foreach (int format in new[] { 0, 1 })
            foreach (int env in new[] { 1, 2, 3 })
                foreach (int style in new[] { 0, 1, 2 })
                {
                    var block = engine with
                    {
                        Format = format,
                        Environment = env,
                        BarMode = style,
                        Bounds = new(140, 300, 500, 2100)
                    };
                    var c = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
                    PerformancePainting.Paint(c, scene.WithSpecs([block]), []);
                    check(c.Runs.All(r => r.X >= block.Bounds.X && r.X + r.Text.Length * NativeText.MonoWidth(r.Height) <= block.Bounds.Right + 2 && r.Y + r.Height <= block.Bounds.Bottom), $"Format {format}, environment {env}, bar style {style}: narrow block text fits");
                }
    }
}
