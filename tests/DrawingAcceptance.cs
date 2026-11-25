using System.Reflection;
using System.Text.Json;
using System.Numerics;
using KSA;
static class DrawingAcceptance
{
    static int phase, frames, index;
    static bool finished, startedBlank;
    static float ratio;
    static object? before;
    static string native = "";
    static readonly string[] crafts = ["Lander_1", "Gemini7", "Rocket"];
    static readonly List<object> evidence = [];
    static Type Mod => AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "PawPrints").GetType("Blueprint.BlueprintMod")!;

    static Type T(string n) => Mod.Assembly.GetType("Blueprint.Rendering." + n)!;

    static object? Field(string n) => (Mod.GetField(n, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingFieldException(Mod.FullName, n)).GetValue(null);

    static void Set(string n, object? value) => (Mod.GetField(n, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingFieldException(Mod.FullName, n)).SetValue(null, value);

    static object Get(object o, string n) => o.GetType().GetProperty(n)?.GetValue(o) ?? o.GetType().GetField(n)?.GetValue(o) ?? throw new Exception("Missing " + n);

    static object? Call(string n, params object?[] args) => (Mod.GetMethod(n, BindingFlags.NonPublic | BindingFlags.Static) ?? throw new MissingMethodException(Mod.FullName, n)).Invoke(null, args);

    static object Copy(object o, params (string Name, object Value)[] values)
    {
        var c = o.GetType().GetMethod("<Clone>$")!.Invoke(o, null)!;
        foreach (var v in values)
            c.GetType().GetProperty(v.Name)!.SetValue(c, v.Value);
        return c;
    }
    static object Sheet => Field("sheet") ?? throw new Exception("No sheet: " + Field("status"));
    static object Scene => Get(Sheet, "Scene");
    static object Drawing => Get(Scene, "Drawing");
    static object[] Views => ((Array)Get(Drawing, "Views")).Cast<object>().ToArray();

    static string State() => string.Join(";", KSA.Program.Editor!.EditingSpace.Parts!.Parts.ToArray().SelectMany(p => p.SubParts.ToArray().Prepend(p)).Distinct().OrderBy(p => p.InstanceId).Select(p => $"{p.InstanceId}:{p.PositionParentAsmb}:{p.Asmb2ParentAsmb}:{p.Scale}"));

    static void Begin(int? parent, int camera)
    {
        Call("BeginView", parent);
        Set("viewDraft", Copy(Field("viewDraft")!, ("Camera", camera), ("Axes", null!), ("RollDirection", -1)));
        Set("draftRatio", ratio);
        Call("PrepareView");
    }

    // The fixture supplies its own canvas coordinates. A pointer hovering the
    // background game window must not choose a second projection direction.
    static void IgnorePhysicalHover(ref bool hovered) => hovered = false;

    static void BeginRoll(int parent, int direction)
    {
        Set("projectionStyle", 0);
        Call("BeginView", parent);
        var source = Views.Single(v => (int)Get(v, "Id") == parent);
        var draft = T("DrawingPlacements").GetMethod("Roll")!.Invoke(null, [Field("viewDraft"), source, direction, 3])!;
        Set("viewDraft", draft);
        Set("draftRatio", ratio);
        Call("PrepareView");
    }

    static bool Commit(float x, float y, bool view = true)
    {
        if (Field("ghostTask") != null)
            return false;
        var pixels = Field("ghostPixels") ?? throw new Exception("No placement preview: " + Field("status"));
        float w = Convert.ToSingle(Get(pixels, "Width")), h = Convert.ToSingle(Get(pixels, "Height"));
        float caption = view ? 98 : 0;
        var box = Activator.CreateInstance(T("Box"), x * (int)Get(Sheet, "PixelWidth") - w / 2, y * (int)Get(Sheet, "PixelHeight") - (h - caption) / 2, w, h)!;
        if (Field("ghostPosition") is Delegate position)
            box = position.DynamicInvoke(box)!;
        if (Field("ghostValid") is Delegate valid && valid.DynamicInvoke(box) is false)
            throw new Exception("Fixture placement rejected at " + x + "," + y + ": " + JsonSerializer.Serialize(box, new JsonSerializerOptions { IncludeFields = true }));
        var commit = (Delegate)Field("ghostCommit")!;
        Call("CancelGhost");
        commit.DynamicInvoke(box);
        return true;
    }

    static void Count(int count)
    {
        if (Views.Length != count)
            throw new Exception($"Expected {count} views, found {Views.Length}: {Field("status")}");
    }

    public static void Tick(string instance)
    {
        if (finished)
            return;
        try
        {
            if (++frames > 18000)
                throw new Exception("Timed out in phase " + phase + ": " + Field("status"));
            if (phase == 0)
            {
                if (index == 0)
                {
                    var ghost = Mod.GetMethod("DrawGhost", BindingFlags.NonPublic | BindingFlags.Static)!;
                    var prefix = typeof(DrawingAcceptance).GetMethod(nameof(IgnorePhysicalHover), BindingFlags.NonPublic | BindingFlags.Static)!;
                    new HarmonyLib.Harmony("blueprint.acceptance").Patch(ghost, prefix: new HarmonyLib.HarmonyMethod(prefix));
                }
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
                Set("partsTable", false);
                Set("flagTable", false);
                Set("zones", true);
                Set("pageOrientation", 0);
                Set("pageAspect", 0);
                startedBlank = false;
                phase = 1;
                frames = 0;
            }
            else if (phase == 1 && frames > 90 && Field("renderTask") == null)
            {
                if (!startedBlank)
                {
                    // LoadVehicle schedules a native history commit. Capture the craft
                    // only once that commit has reached the regular editor frame.
                    if (Acceptance.LoadPending(KSA.Program.Editor!))
                        return;
                    Call("OpenExport");
                    Call("ChooseLayout", null, false, Field("setupStage"), false, false);
                    Call("StartDrawing", KSA.Program.Editor);
                    startedBlank = true;
                    return;
                }
                Count(0);
                native = State();
                ratio = 0;
                Call("BeginViewDraft", null, 2);
                phase = 2;
            }
            else if (phase == 2)
            {
                if (Field("renderTask") != null)
                    return;
                if (ratio == 0)
                {
                    if (Field("viewDraft") == null)
                        throw new Exception("Initial capture did not start placement: " + Field("status"));
                    ratio = (float)Field("draftRatio")! * 3;
                    Set("draftRatio", ratio);
                    Call("PrepareView");
                    return;
                }
                if (Commit(.28f, .48f))
                    phase = 3;
            }
            else if (Field("renderTask") == null && phase >= 3)
            {
                if (State() != native)
                    throw new Exception("Placement changed native craft geometry");
                if (phase == 3)
                {
                    Count(1);
                    BeginRoll(100, 1);
                    phase = 4;
                }
                else if (phase == 4)
                {
                    if (Commit(.28f, .2f))
                        phase = 5;
                }
                else if (phase == 5)
                {
                    Count(2);
                    BeginRoll(101, 0);
                    phase = 6;
                }
                else if (phase == 6)
                {
                    if (Commit(.7f, .2f))
                        phase = 7;
                }
                else if (phase == 7)
                {
                    Count(3);
                    Begin(null, 4);
                    phase = 8;
                }
                else if (phase == 8)
                {
                    if (Commit(.7f, .58f))
                        phase = 9;
                }
                else if (phase == 9)
                {
                    Count(4);
                    before = Drawing;
                    if ((int)Get(Views[2], "Parent") != 101)
                        throw new Exception("Projection chain lost");
                    if ((int)Get(Views[1], "Camera") != 3 || (Vector3)Get(Get(Views[1], "Axes"), "Right") != Vector3.UnitX
                        || (Vector3)Get(Get(Views[2], "Axes"), "Up") != Vector3.UnitY)
                        throw new Exception("Rolled projection orientation lost");
                    var v = Views[0];
                    var moved = T("DrawingPlacements").GetMethod("Move")!.Invoke(null, [Drawing, 100, new Vector2((float)Get(v, "X") * (int)Get(Sheet, "PixelWidth") + 40, (float)Get(v, "Y") * (int)Get(Sheet, "PixelHeight") + 30), Get(Sheet, "Options"), false])!;
                    Call("QueueDrawing", moved, false, -1, null);
                    phase = 10;
                }
                else if (phase == 10)
                {
                    if (Drawing.Equals(before))
                        throw new Exception("Move failed: " + Field("status"));
                    Call("UndoAnnotations", false);
                    phase = 11;
                }
                else if (phase == 11)
                {
                    if (!Drawing.Equals(before))
                        throw new Exception("View Undo failed: " + Field("status"));
                    Call("UndoAnnotations", true);
                    phase = 12;
                }
                else if (phase == 12)
                {
                    if (Drawing.Equals(before))
                        throw new Exception("View Redo failed");
                    before = Drawing;
                    Call("Generate", KSA.Program.Editor!, false, null, false);
                    phase = 13;
                }
                else if (phase == 13)
                {
                    if (!Drawing.Equals(before))
                        throw new Exception("Generate moved views");
                    Call("ShowTable", 0);
                    phase = 15;
                }
                else if (phase == 15)
                {
                    if (Get(Get(Sheet, "Options"), "PartsTable") is not true)
                        throw new Exception("Table not placed");
                    Call("UndoAnnotations", false);
                    phase = 16;
                }
                else if (phase == 16)
                {
                    if (Get(Get(Sheet, "Options"), "PartsTable") is not false)
                        throw new Exception("Table Undo failed");
                    Call("UndoAnnotations", true);
                    phase = 17;
                }
                else if (phase == 17)
                {
                    if (Get(Get(Sheet, "Options"), "PartsTable") is not true)
                        throw new Exception("Table Redo failed");
                    string image = Path.Combine(instance, "drawing-" + crafts[index] + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                    T("Png").GetMethod("Write")!.Invoke(null, [image, Get(Sheet, "PixelWidth"), Get(Sheet, "PixelHeight"), Get(Sheet, "Rgb"), null]);
                    evidence.Add(new
                    {
                        craft = crafts[index],
                        blankPaper = true,
                        renderedPreviews = true,
                        primary = true,
                        projectionChain = true,
                        independentIsometric = true,
                        sharedRatio = ratio,
                        moveUndoRedo = true,
                        generatePreserved = true,
                        tablePlacementUndoRedo = true,
                        nativeUnchanged = true,
                        image
                    });
                    Set("pageOrientation", 3);
                    Set("desktopPreset", 0);
                    Set("customWidth", 1920);
                    Set("customHeight", 1080);
                    Call("Generate", KSA.Program.Editor!, false, null, false);
                    phase = 18;
                }
                else if (phase == 18)
                {
                    if ((int)Get(Get(Sheet, "Options"), "Orientation") != 3)
                        throw new Exception("Desktop repage failed: " + Field("status"));
                    Count(4);
                    Call("Save");
                    phase = 19;
                }
                else if (phase == 19 && Field("saveTask") == null)
                {
                    string status = Field("status")?.ToString() ?? "";
                    if (!status.StartsWith("Saved: "))
                        throw new Exception("Desktop save: " + status);
                    var bytes = File.ReadAllBytes(status[7..]);
                    if (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)) != 1920 || System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20)) != 1080)
                        throw new Exception("Desktop output pixels");
                    evidence.Add(new
                    {
                        craft = crafts[index],
                        desktopRepage = true,
                        desktopPixels = "1920x1080",
                        path = status[7..]
                    });
                    frames = 0;
                    phase = 24;
                }
                else if (phase == 24)
                {
                    // A freshly loaded native editor can still be committing its initial
                    // history snapshot. Begin the gesture on the accepted craft revision.
                    if (frames < 180 || !Equals(Field("acceptedCraft"), Field("observedCraft")))
                        return;
                    Count(4);
                    Call("DeleteDrawingView", 100);
                    if (!Equals(Field("pendingPrimaryDelete"), 100))
                        throw new Exception("Deleting the primary did not offer a choice for its projections");
                    Set("pendingPrimaryDelete", -1);
                    Call("CommitViewDeletion", 100, false);
                    phase = 20;
                }
                else if (phase == 20)
                {
                    Count(3);
                    if (Views.Any(v => v.GetType().GetProperty("Parent")!.GetValue(v) != null))
                        throw new Exception("Kept projections did not become independent primaries");
                    Call("UndoAnnotations", false);
                    phase = 21;
                }
                else if (phase == 21)
                {
                    Count(4);
                    Call("UndoAnnotations", true);
                    phase = 22;
                }
                else if (phase == 22)
                {
                    Count(3);
                    Call("UndoAnnotations", false);
                    phase = 23;
                }
                else if (phase == 23)
                {
                    Count(4);
                    Call("DeleteDrawingView", 100);
                    if (!Equals(Field("pendingPrimaryDelete"), 100))
                        throw new Exception("Restored primary lost its projections");
                    Set("pendingPrimaryDelete", -1);
                    Call("CommitViewDeletion", 100, true);
                    phase = 25;
                }
                else if (phase == 25)
                {
                    Count(1);
                    if ((int)Get(Views[0], "Id") != 103)
                        throw new Exception("Cascade deletion removed the independent view");
                    Call("UndoAnnotations", false);
                    phase = 26;
                }
                else if (phase == 26)
                {
                    Count(4);
                    evidence.Add(new
                    {
                        craft = crafts[index],
                        deleteViewUndoRedo = true,
                        keepAndCascadeProjectedViews = true
                    });
                    index++;
                    phase = 0;
                    frames = 0;
                    if (index == crafts.Length)
                    {
                        finished = true;
                        File.WriteAllText(Path.Combine(instance, "drawing-acceptance.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
                        if (Environment.GetEnvironmentVariable("BLUEPRINT_ACCEPTANCE_HOLD") != "1")
                            KSA.Program.Instance.RequestExit();
                    }
                }
            }
        }
        catch (Exception e) { finished = true; File.WriteAllText(Path.Combine(instance, "drawing-acceptance-error-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt"), "Phase " + phase + "\n" + e); Console.WriteLine("[Drawing QA] " + e); KSA.Program.Instance.RequestExit(); }
    }
}
