using System.Reflection;
using System.Text.Json;
using KSA;
using Brutal.Numerics;

// Isolated native fixture only. Not shipped with Paw Prints.
static class PoseAcceptance
{
    static int phase, frame, craftIndex;
    static bool finished;
    static string[] crafts = ["Lander_1", "Gemini7"];
    static Type Mod => AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "PawPrints").GetType("Blueprint.BlueprintMod")!;

    static object? Field(string name) => (Mod.GetField(name, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingFieldException(Mod.FullName, name)).GetValue(null);

    static void Set(string name, object value) => (Mod.GetField(name, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingFieldException(Mod.FullName, name)).SetValue(null, value);

    static object Get(object obj, string name) => obj.GetType().GetProperty(name)?.GetValue(obj) ?? obj.GetType().GetField(name)?.GetValue(obj) ?? throw new Exception($"Missing/null {obj.GetType().FullName}.{name}");

    static void Call(string name, params object[] args) => (Mod.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException(Mod.FullName, name)).Invoke(null, args);
    static Array poses = Array.Empty<object>();
    static string originalState = "";
    static object? source;
    static readonly List<object> evidence = [];

    static string State() => string.Join(";", KSA.Program.Editor!.EditingSpace.Parts!.Parts.ToArray().SelectMany(p => p.SubParts.ToArray().Prepend(p)).Distinct().OrderBy(p => p.InstanceId).Select(p => $"{p.InstanceId}:{p.PositionParentAsmb}:{p.Asmb2ParentAsmb}:{p.Scale}"));

    static Array PoseArray(object control, int view, int percent)
    {
        var t = Mod.Assembly.GetType("Blueprint.Rendering.PartPose")!;
        var a = Array.CreateInstance(t, 1);
        a.SetValue(Activator.CreateInstance(t, Get(control, "Part"), Get(control, "Key"), view, percent), 0);
        return a;
    }

    public static void Tick(string instance)
    {
        if (finished)
            return;
        try
        {
            if (phase == 0)
            {
                using var reader = File.OpenText(Path.Combine(instance, "Vehicles", crafts[craftIndex], "vehicle.xml"));
                var data = (VehicleSaveData)VehicleSaves.VehicleSerializer.Deserialize(reader)!;
                data.OnDataLoad(KSA.Mod.Empty);
                var parts = PartTree.Deserialize(new PartTreeData { Root = data.RootPartInstance!, SequenceEnvironments = data.SequenceEnvironments, FuelLinks = data.FuelLinks });
                if (KSA.Program.Editor != null)
                    KSA.Program.Editor.LaunchNewVehicle = false;
                KSA.Program.Editor?.Dispose();
                KSA.Program.Editor = new VehicleEditor(null, true);
                KSA.Program.EditorFlag = true;
                Universe.SetSimulationSpeed(0);
                KSA.Program.Editor.EditingSpace.Id = crafts[craftIndex];
                KSA.Program.Editor.LoadVehicle(parts);
                phase = 1;
                frame = 0;
            }
            else if (phase == 1 && ++frame > 90)
            {
                Set("drawingPlacement", null!);
                Set("partsTable", false);
                Set("flagTable", false);
                Call("OpenExport");
                Call("Generate", KSA.Program.Editor!);
                phase = 2;
            }
            else if (phase == 2 && Field("renderTask") == null)
            {
                var sheet = Field("sheet") ?? throw new Exception("No preview: " + Field("status"));
                source = Get(sheet, "PoseSource");
                var controls = (Array)Get(source, "Controls");
                if (controls.Length == 0)
                    throw new Exception("No animation controls captured");
                originalState = State();
                var control = controls.GetValue(0)!;
                var module = KSA.Program.Editor!.EditingSpace.Parts!.Modules.Get<KeyframeAnimationModule>().ToArray().Single(m => m.Parent.InstanceId == (uint)Get(control, "Part") && m.TemplateId == (string)Get(control, "Key"));
                var tree = module.Parent.Tree!;
                tree.States.TryGetTypeList(out ModuleStateful<KeyframeAnimationModule, KeyframeAnimationModule.State, EmptyStruct, EmptyStruct>.StateList states);
                float oldTime = states.GetModuleAndState(module).State.TimeCurrent;
                double maxError = 0;
                try
                {
                    foreach (int percent in new[] { 0, 50, 100 })
                    {
                        var settings = PoseArray(control, -1, percent);
                        var applied = Mod.Assembly.GetType("Blueprint.Rendering.PartPoses")!.GetMethod("Apply")!.Invoke(null, [source, settings, 1])!;
                        module.ApplyAnimationTransforms(module.Shared.Duration * percent / 100f);
                        var capture = Mod.Assembly.GetType("Blueprint.CraftCapture")!.GetMethod("Take")!.Invoke(null, [KSA.Program.Editor])!;
                        var expected = Get(Get(Get(capture, "Assembly"), "Geometry"), "Meshes") as Array;
                        var actual = (Array)Get(Get(applied, "Geometry"), "Meshes");
                        for (int i = 0; i < actual.Length; i++)
                        {
                            var av = (double[][])Get(actual.GetValue(i)!, "Vertices");
                            var ev = (double[][])Get(expected!.GetValue(i)!, "Vertices");
                            for (int j = 0; j < av.Length; j += Math.Max(1, av.Length / 40))
                                for (int k = 0; k < 3; k++)
                                    maxError = Math.Max(maxError, Math.Abs(av[j][k] - ev[j][k]));
                        }
                    }
                }
                finally { module.ApplyAnimationTransforms(oldTime); }
                if (State() != originalState)
                    throw new Exception("Native comparison did not restore its isolated fixture pose");
                if (maxError > .001)
                    throw new Exception("Detached/native animation mismatch: " + maxError);
                var pp = Mod.Assembly.GetType("Blueprint.Rendering.PartPoses")!;
                var linked = (Array)pp.GetMethod("Linked")!.Invoke(null, [source, control, true])!;
                var empty = Array.CreateInstance(Mod.Assembly.GetType("Blueprint.Rendering.PartPose")!, 0);
                var sym = (Array)pp.GetMethod("SetLinked")!.Invoke(null, [source, empty, control, -1, 100, true])!;
                var individual = (Array)pp.GetMethod("SetLinked")!.Invoke(null, [source, sym, control, -1, 25, false])!;
                if (sym.Length != linked.Length || individual.Cast<object>().Count(p => (int)Get(p, "Percent") == 25) != 1 || individual.Cast<object>().Count(p => (int)Get(p, "Percent") == 100) != linked.Length - 1)
                    throw new Exception("Symmetry mapping/individual override failed");
                evidence.Add(new
                {
                    craft = crafts[craftIndex],
                    control = Get(control, "Name"),
                    controls = controls.Length,
                    symmetryMembers = linked.Length,
                    individualOverride = true,
                    maxVertexErrorMetres = maxError,
                    nativeSamples = new[] { 0, 50, 100 }
                });
                poses = PoseArray(control, -1, 100);
                Call("QueuePose", poses);
                phase = 3;
            }
            else if (phase >= 3 && Field("renderTask") == null && Field("saveTask") == null)
            {
                var sheet = Field("sheet") ?? throw new Exception("No sheet");
                var scene = Get(sheet, "Scene");
                var current = (Array)Get(scene, "Poses");
                if (State() != originalState)
                    throw new Exception("Print posing changed native part transforms");
                if (phase == 3)
                {
                    if (current.Length != 1)
                        throw new Exception("Shared pose failed: " + Field("status"));
                    var control = ((Array)Get(source!, "Controls")).GetValue(0)!;
                    var view = ((Array)Get(scene, "Views")).GetValue(0)!;
                    var t = current.GetType().GetElementType()!;
                    poses = Array.CreateInstance(t, 2);
                    poses.SetValue(current.GetValue(0), 0);
                    poses.SetValue(PoseArray(control, (int)Get(view, "Id"), 0).GetValue(0), 1);
                    Call("QueuePose", poses);
                    phase = 4;
                }
                else if (phase == 4)
                {
                    if (current.Length != 2)
                        throw new Exception("Single-view pose failed: " + Field("status"));
                    Call("UndoAnnotations", false);
                    phase = 5;
                }
                else if (phase == 5)
                {
                    if (current.Length != 1)
                        throw new Exception("Pose undo failed: " + Field("status"));
                    Call("UndoAnnotations", true);
                    phase = 6;
                }
                else if (phase == 6)
                {
                    if (current.Length != 2)
                        throw new Exception("Pose redo failed: " + Field("status"));
                    Call("Generate", KSA.Program.Editor!);
                    phase = 7;
                }
                else if (phase == 7)
                {
                    if (current.Length != 2)
                        throw new Exception("Generate lost pose: " + Field("status"));
                    var control = ((Array)Get(source!, "Controls")).GetValue(0)!;
                    var view = ((Array)Get(scene, "Views")).GetValue(0)!;
                    Call("BeginPose", Get(control, "Part"), 0, Get(view, "Id"));
                    phase = 9;
                    frame = 0;
                }
                else if (phase == 9)
                {
                    if (++frame < 5)
                        return;
                    if (Mod.GetProperty("PosePopupDrawn", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) is not true || Field("poseKeepSymmetry") is not true)
                        throw new Exception("Pose modal/default symmetry not presented");
                    Call("CancelPlacement");
                    Call("Save");
                    phase = 8;
                }
                else
                {
                    if (!Field("status")!.ToString()!.StartsWith("Saved:"))
                        throw new Exception("Save failed: " + Field("status"));
                    evidence.Add(new
                    {
                        craft = crafts[craftIndex],
                        shared = true,
                        perView = true,
                        undoRedo = true,
                        generateRetained = true,
                        gameUnchanged = true,
                        poseDialog = true,
                        keepSymmetryDefault = true,
                        export = Field("status")
                    });
                    craftIndex++;
                    phase = 0;
                    if (craftIndex == crafts.Length)
                    {
                        finished = true;
                        File.WriteAllText(Path.Combine(instance, "pose-acceptance.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                        KSA.Program.Instance.RequestExit();
                    }
                }
            }
        }
        catch (Exception e) { finished = true; File.WriteAllText(Path.Combine(instance, "pose-acceptance-error.txt"), e.ToString()); Console.WriteLine("[Pose QA] " + e); KSA.Program.Instance.RequestExit(); }
    }
}
