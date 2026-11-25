using System.Reflection;
using System.Text.Json;
using KSA;
// Disposable native fixture. Never shipped in the release package.
static class PerformanceAcceptance
{
    static int phase, frames, index;
    static bool finished;
    static readonly string[] crafts = ["Lander_1", "Gemini7", "Rocket"];
    static readonly List<object> evidence = [];
    static string original = "";
    static readonly string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
    static Type Mod => AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "PawPrints").GetType("Blueprint.BlueprintMod")!;

    static object? Field(string n) => (Mod.GetField(n, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingFieldException(Mod.FullName, n)).GetValue(null);

    static void Set(string n, object value) => (Mod.GetField(n, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingFieldException(Mod.FullName, n)).SetValue(null, value);

    static object Get(object o, string n) => o.GetType().GetProperty(n)?.GetValue(o) ?? o.GetType().GetField(n)?.GetValue(o) ?? throw new Exception("Missing " + n);

    static object? Call(string n, params object[] args) => (Mod.GetMethod(n, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException(Mod.FullName, n)).Invoke(null, args);

    static object Copy(object o, params (string Name, object Value)[] values)
    {
        var c = o.GetType().GetMethod("<Clone>$")!.Invoke(o, null)!;
        foreach (var v in values)
            o.GetType().GetProperty(v.Name)!.SetValue(c, v.Value);
        return c;
    }

    static Array Specs(params object[] entries)
    {
        var a = Array.CreateInstance(Mod.Assembly.GetType("Blueprint.Rendering.SpecBlock")!, entries.Length);
        for (int i = 0; i < entries.Length; i++)
            a.SetValue(entries[i], i);
        return a;
    }

    static string State() => string.Join(";", KSA.Program.Editor!.EditingSpace.Parts!.Parts.ToArray().SelectMany(p => p.SubParts.ToArray().Prepend(p)).Distinct().OrderBy(p => p.InstanceId).Select(p => $"{p.InstanceId}:{p.PositionParentAsmb}:{p.Asmb2ParentAsmb}:{p.Scale}"));

    static void SaveEvidenceImage(string instance, object sheet, string suffix)
    {
        string path = Path.Combine(instance, "performance-" + crafts[index] + suffix + "-" + stamp + ".png");
        Mod.Assembly.GetType("Blueprint.Rendering.Png")!.GetMethod("Write")!.Invoke(null, [path, Get(sheet, "PixelWidth"), Get(sheet, "PixelHeight"), Get(sheet, "Rgb"), null]);
    }

    static void Near(double actual, double expected, double tolerance, string label)
    {
        if (!double.IsFinite(actual) || !double.IsFinite(expected) || Math.Abs(actual - expected) > tolerance)
            throw new Exception($"{label}: captured={actual}, native={expected}");
    }

    // Real KSA objects, independently enumerated: the portable renderer tests
    // cannot catch dropping a rocket/core or using another nozzle's gimbal.
    static object CheckEngine(EngineController engine, object captured)
    {
        RocketControllerData.ComputeFromRockets(engine.Rockets.AsSpan(), 0, 101325, 1, out var vac, out var asl);
        Near((double)captured.GetType().GetMethod("Thrust")!.Invoke(captured, [false])!, vac.ThrustMax.Length(), .5, "Vacuum thrust");
        Near((double)captured.GetType().GetMethod("Thrust")!.Invoke(captured, [true])!, asl.ThrustMax.Length(), .5, "Sea-level thrust");
        Near((double)Get(captured, "FlowVac"), vac.MassFlowRateMax, .001, "Vacuum flow");
        Near((double)Get(captured, "FlowAsl"), asl.MassFlowRateMax, .001, "Sea-level flow");
        Near((double)Get(captured, "MinimumThrottle"), engine.MinimumThrottle, .000001, "Minimum throttle");
        var candidates = new List<(RocketNozzle Nozzle, float Thrust)>();
        var propellants = new HashSet<string>(StringComparer.Ordinal);
        int cores = 0;
        foreach (var rocket in engine.Rockets)
        {
            var rocketCores = rocket.Cores;
            var conditions = new RocketCoreConditions[rocketCores.Length];
            rocket.ComputeCoreConditions(1f, conditions);
            for (int i = 0; i < rocketCores.Length; i++)
            {
                var core = rocketCores[i];
                cores++;
                foreach (var phase in core.Reaction.ReactantPhases)
                    propellants.Add(phase.Name);
                foreach (var nozzle in core.Nozzles)
                {
                    float thrust = nozzle.ComputePerformance(in conditions[i], 0).GetTotalThrust();
                    if (float.IsFinite(thrust) && thrust > 0)
                        candidates.Add((nozzle, thrust));
                }
            }
        }
        var main = candidates.OrderByDescending(n => n.Thrust).FirstOrDefault().Nozzle;
        var gimbal = main?.Parent.GimbalAsmb;
        Near((double)Get(captured, "GimbalY"), gimbal == null ? 0 : (double)gimbal.AxisY.MaxAngle * 180 / Math.PI, .000001, "Main nozzle Y");
        Near((double)Get(captured, "GimbalZ"), gimbal == null ? 0 : (double)gimbal.AxisZ.MaxAngle * 180 / Math.PI, .000001, "Main nozzle Z");
        string[] labels = ((string)Get(captured, "Propellants")).Split(" + ", StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length != propellants.Count || !propellants.SetEquals(labels))
            throw new Exception("Missing, duplicate or unexpected propellant labels");
        return new
        {
            id = engine.Parent.InstanceId + "/" + engine.TemplateId,
            rockets = engine.Rockets.Length,
            cores,
            positiveNozzles = candidates.Count,
            solidRockets = engine.Rockets.Count(r => r is SolidRocket),
            mainNozzle = main?.TemplateId,
            gimbalVerified = true,
            propellantsVerified = true,
            flowsVerified = true
        };
    }

    public static void Tick(string instance)
    {
        if (finished)
            return;
        try
        {
            if (phase == 0)
            {
                using var reader = File.OpenText(Path.Combine(instance, "Vehicles", crafts[index], "vehicle.xml"));
                var data = (VehicleSaveData)VehicleSaves.VehicleSerializer.Deserialize(reader)!;
                data.OnDataLoad(KSA.Mod.Empty);
                var parts = PartTree.Deserialize(new PartTreeData { Root = data.RootPartInstance!, SequenceEnvironments = data.SequenceEnvironments, FuelLinks = data.FuelLinks });
                if (KSA.Program.Editor != null)
                    KSA.Program.Editor.LaunchNewVehicle = false;
                KSA.Program.Editor?.Dispose();
                KSA.Program.Editor = new VehicleEditor(null, true);
                KSA.Program.EditorFlag = true;
                Universe.SetSimulationSpeed(0);
                KSA.Program.Editor.EditingSpace.Id = crafts[index];
                KSA.Program.Editor.LoadVehicle(parts);
                phase = 1;
                frames = 0;
            }
            else if (phase == 1 && ++frames > 120)
            {
                if (Acceptance.LoadPending(KSA.Program.Editor!))
                    return;
                Set("drawingPlacement", null!);
                Set("partsTable", false);
                Set("flagTable", false);
                Call("OpenExport");
                Call("Generate", KSA.Program.Editor!, true, null!, false);
                Set("creatingBlank", true);
                phase = 2;
            }
            else if (phase == 2 && Field("renderTask") == null)
            {
                var sheet = Field("sheet") ?? throw new Exception("Generate: " + Field("status"));
                var scene = Get(sheet, "Scene");
                var data = Get(scene, "Performance");
                var captured = ((Array)Get(data, "Engines")).Cast<object>().ToArray();
                var tree = KSA.Program.Editor!.EditingSpace.Parts!;
                if (captured.Length != tree.Modules.Get<EngineController>().Length || captured.Length == 0)
                    throw new Exception("Engine capture count");
                double maxError = 0;
                var engineContracts = new List<object>();
                foreach (var e in tree.Modules.Get<EngineController>())
                {
                    var c = captured.Single(c => (string)Get(c, "Id") == e.Parent.InstanceId + "/" + e.TemplateId);
                    engineContracts.Add(CheckEngine(e, c));
                    RocketControllerData.ComputeFromRockets(e.Rockets.AsSpan(), 0, 101325, 1, out var vac, out var asl);
                    maxError = Math.Max(maxError, Math.Abs((double)c.GetType().GetMethod("Thrust")!.Invoke(c, [false])! - vac.ThrustMax.Length()));
                    maxError = Math.Max(maxError, Math.Abs((double)c.GetType().GetMethod("Thrust")!.Invoke(c, [true])! - asl.ThrustMax.Length()));
                    if (Math.Abs((double)Get(c, "FlowVac") - vac.MassFlowRateMax) > .001)
                        throw new Exception("Flow mismatch");
                }
                double nativeMass = (double)tree.ComputeInertMassPropertiesAsmb().Props.Mass + tree.ComputePropellantMassPropertiesAsmb().Props.Mass;
                Near((double)Get(data, "WetMass"), nativeMass, .001, "Wet mass");
                if (Math.Abs((double)Get(data, "WetMass") - nativeMass) > .001 || maxError > .5)
                    throw new Exception($"Rating/mass mismatch: captured={(double)Get(data, "WetMass")}, now={nativeMass}, thrust error={maxError}");
                var stages = (Array)Get(data, "Stages");
                if (stages.Length == 0)
                    throw new Exception("No native stage estimates captured");
                evidence.Add(new
                {
                    craft = crafts[index],
                    engines = captured.Length,
                    stages = stages.Length,
                    nativeRatingMaxErrorN = maxError,
                    massKg = nativeMass,
                    engineContracts,
                    ratings = captured.Select(c => new { name = Get(c, "Name"), vacN = c.GetType().GetMethod("Thrust")!.Invoke(c, [false]), aslN = c.GetType().GetMethod("Thrust")!.Invoke(c, [true]), flow = Get(c, "FlowVac"), gimbalY = Get(c, "GimbalY"), gimbalZ = Get(c, "GimbalZ") })
                });
                original = State();
                var visible = (System.Collections.IDictionary)Get(scene, "Visible");
                var parts = ((Array)Get(scene, "Parts")).Cast<object>().ToArray();
                var pick = (from c in captured
                            from v in ((Array)Get(scene, "Views")).Cast<object>()
                            from p in parts
                            where (uint)Get(p, "NodeId") == (uint)Get(c, "Part") && visible.Contains(((int)Get(v, "Id"), (int)Get(p, "Id")))
                                && ((Array)Get(visible[((int)Get(v, "Id"), (int)Get(p, "Id"))]!, "Edges")).Length >= 24
                            orderby ((Array)Get(visible[((int)Get(v, "Id"), (int)Get(p, "Id"))]!, "Edges")).Length descending,
                                (double)c.GetType().GetMethod("Thrust")!.Invoke(c, [false])! descending
                            select (View: v, Part: p)).First();
                var view = pick.View;
                var part = pick.Part;
                Call("BeginSpec", Get(part, "Id"), Get(view, "Id"), false);
                phase = 3;
                frames = 0;
            }
            else if (phase >= 3 && Field("renderTask") == null)
            {
                var sheet = Field("sheet")!;
                var scene = Get(sheet, "Scene");
                var specs = (Array)Get(scene, "Specs");
                if (State() != original)
                    throw new Exception("Performance editing altered native transforms");
                if (phase == 3)
                {
                    if (++frames < 8)
                        return;
                    if (Mod.GetProperty("SpecPopupDrawn", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) is not true)
                        throw new Exception("Specification dialog not presented");
                    var draft = Copy(Field("specDraft")!, ("Leader", true));
                    var fitted = Call("FitSpec", draft) ?? throw new Exception("Default engine block cannot fit");
                    Call("CancelPlacement");
                    Call("QueueSpecs", Specs(fitted));
                    phase = 4;
                }
                else if (phase == 4)
                {
                    if (specs.Length != 1)
                        throw new Exception("Engine block: " + Field("status"));
                    var settingsOnly = Copy(specs.GetValue(0)!, ("Environment", 1));
                    var kept = Call("FitSpec", settingsOnly) ?? throw new Exception("Settings edit failed");
                    if (!Get(kept, "Bounds").Equals(Get(settingsOnly, "Bounds")))
                        throw new Exception("Settings edit relocated an existing block");
                    SaveEvidenceImage(instance, sheet, "-compact");
                    var stacked = Copy(specs.GetValue(0)!, ("Format", 1), ("BarMode", 0));
                    var fitted = Call("FitSpec", stacked) ?? throw new Exception("Stacked engine block cannot fit");
                    Call("QueueSpecs", Specs(fitted));
                    phase = 5;
                }
                else if (phase == 5)
                {
                    if ((int)Get(specs.GetValue(0)!, "Format") != 1)
                        throw new Exception("Stacked block: " + Field("status"));
                    Call("UndoAnnotations", false);
                    phase = 6;
                }
                else if (phase == 6)
                {
                    if ((int)Get(specs.GetValue(0)!, "Format") != 0)
                        throw new Exception("Undo failed");
                    Call("UndoAnnotations", true);
                    phase = 7;
                }
                else if (phase == 7)
                {
                    if ((int)Get(specs.GetValue(0)!, "Format") != 1)
                        throw new Exception("Redo failed");
                    Call("BeginSpec", -1, 0, true);
                    phase = 8;
                    frames = 0;
                }
                else if (phase == 8)
                {
                    if (++frames < 5)
                        return;
                    var draft = Field("specDraft")!;
                    var fields = Enum.ToObject(draft.GetType().GetProperty("Fields")!.PropertyType, 1L | 2 | 4 | 8 | 256 | 512 | 1024 | 2048 | 4096 | 8192 | 16384 | 65536);
                    // Free placement can overlap blocks. Keep the vehicle block clear of
                    // the engine block and its leader for this routing scenario.
                    var bounds = Get(draft, "Bounds");
                    var placed = Activator.CreateInstance(bounds.GetType(), 60f, Get(bounds, "Y"), Get(bounds, "Width"), Get(bounds, "Height"))!;
                    var fitted = Call("FitSpec", Copy(draft, ("Fields", fields), ("Bounds", placed))) ?? throw new Exception("Vehicle block cannot fit");
                    Call("CancelPlacement");
                    Call("QueueSpecs", Specs(specs.GetValue(0)!, fitted));
                    phase = 9;
                }
                else if (phase == 9)
                {
                    if (specs.Length != 2)
                        throw new Exception("Vehicle block: " + Field("status"));
                    Call("Generate", KSA.Program.Editor!, false, null!, false);
                    phase = 10;
                }
                else if (phase == 10)
                {
                    if (specs.Length != 2)
                        throw new Exception("Generate lost performance blocks: " + Field("status"));
                    var path = (Array)Mod.Assembly.GetType("Blueprint.Rendering.SpecLeaderRouting")!.GetMethod("Route")!.Invoke(null, [scene, specs.GetValue(0), Get(sheet, "Labels")])!;
                    if (path.Length < 2 || path.Length > 4)
                    {
                        SaveEvidenceImage(instance, sheet, "-route-diagnostic");
                        var owners = (int[])Get(scene, "Owners");
                        var bytes = new byte[owners.Length * 4];
                        Buffer.BlockCopy(owners, 0, bytes, 0, bytes.Length);
                        File.WriteAllBytes(Path.Combine(instance, "route-owners.bin"), bytes);
                        File.WriteAllText(Path.Combine(instance, "route-scene.json"), JsonSerializer.Serialize(new
                        {
                            Options = Get(scene, "Options"),
                            Parts = Get(scene, "Parts"),
                            Views = Get(scene, "Views"),
                            Specs = specs,
                            Captions = Get(scene, "Captions"),
                            Performance = Get(scene, "Performance")
                        }, new JsonSerializerOptions { IncludeFields = true }));
                        throw new Exception("Engine leader failed to find a bounded clear route");
                    }
                    evidence.Add(new
                    {
                        craft = crafts[index],
                        routedLeader = true,
                        bends = path.Length - 2
                    });
                    SaveEvidenceImage(instance, sheet, "-both");
                    evidence.Add(new
                    {
                        craft = crafts[index],
                        dialog = true,
                        engine = true,
                        vehicle = true,
                        stacked = true,
                        undoRedo = true,
                        generatePreserved = true,
                        settingsKeepPlacement = true,
                        nativeUnchanged = true,
                        png = true
                    });
                    index++;
                    phase = 0;
                    if (index == crafts.Length)
                    {
                        finished = true;
                        File.WriteAllText(Path.Combine(instance, "performance-acceptance.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                        if (Environment.GetEnvironmentVariable("BLUEPRINT_ACCEPTANCE_HOLD") != "1")
                            KSA.Program.Instance.RequestExit();
                    }
                }
            }
        }
        catch (Exception e) { finished = true; File.WriteAllText(Path.Combine(instance, "performance-acceptance-error.txt"), e.ToString()); Console.WriteLine("[Performance QA] " + e); KSA.Program.Instance.RequestExit(); }
    }
}
