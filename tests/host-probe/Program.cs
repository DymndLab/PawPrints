using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using HarmonyLib;

if (args.Length != 3)
    throw new ArgumentException("Usage: HostProbe <game directory> <loader directory> <PawPrints.dll>");
string game = Path.GetFullPath(args[0]), loader = Path.GetFullPath(args[1]), modFile = Path.GetFullPath(args[2]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string root in new[] { game, loader, Path.GetDirectoryName(modFile)! })
    {
        string file = Path.Combine(root, name.Name + ".dll");
        if (File.Exists(file))
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
    }
    return null;
};
// No entrypoint invocation, PatchAll, game startup, profile writes or GPU calls.
var host = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "KSA.dll"));
var mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(modFile);
var modType = mod.GetType("Blueprint.BlueprintMod", true)!;
string expected = (string)modType.GetField("SupportedHash")!.GetRawConstantValue()!;
Require(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(host.Location))) == expected, "Mod hash guard matches the actual inspected host");
Console.WriteLine($"Host MVID {host.ManifestModule.ModuleVersionId}; mod SHA256 {Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modFile)))}");
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
var targets = new Dictionary<string, MethodInfo>();
foreach (var (patch, type, name, parameters) in new[]
{
    ("ShutdownPatch", "KSA.Program", "Dispose", Array.Empty<string>()),
    ("EditorClosedPatch", "KSA.VehicleEditor", "Dispose", Array.Empty<string>()),
    ("ShortcutPatch", "KSA.Program", "OnKey", new[] { "Brutal.GlfwApi.GlfwWindow", "Brutal.GlfwApi.GlfwKey", "System.Int32", "Brutal.GlfwApi.GlfwKeyAction", "Brutal.GlfwApi.GlfwModifier" }),
    ("VehicleSwitchPatch", "KSA.Program", "DispatchKeyEvent", new[] { "RenderCore.Input.GlfwKeyEvent&" }),
    ("MenuPatch", "KSA.Program", "DrawMenuBar", new[] { "KSA.IGameViewport", "System.Int32" }),
    ("EditorPatch", "KSA.VehicleEditor", "OnDrawUi", new[] { "KSA.IGameViewport" }),
    ("EditorToolPromptPatch", "KSA.VehicleEditor", "OnDrawUi", new[] { "KSA.IGameViewport" }),
    ("EditorResourcesPatch", "KSA.ResourcesPanel", "DrawEditor", new[] { "KSA.PartTree", "Brutal.Numerics.float2" }),
    ("EditorStagingPatch", "KSA.VehicleEditingSpace", "DrawStageWindow", new[] { "KSA.IViewport" })
})
{
    var methods = host.GetType(type, true)!.GetMethods(flags).Where(m => m.Name == name).ToArray();
    Require(methods.Length == 1, type + "." + name + " is unambiguous");
    var target = methods.Single();
    Require(target.IsStatic == (patch == "EditorResourcesPatch") && target.ReturnType == (patch == "VehicleSwitchPatch" ? typeof(bool) : typeof(void)), name + " static/instance/return contract");
    Require(target.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(parameters), name + " parameter types: " + string.Join(", ", target.GetParameters().Select(p => p.ParameterType.FullName)));
    var patchType = modType.GetNestedType(patch, BindingFlags.NonPublic)!;
    var patchMethod = patchType.GetMethods(flags).Single(m => m.Name is "Prefix" or "Postfix" or "Transpiler");
    if (patchMethod.Name != "Transpiler")
        foreach (var p in patchMethod.GetParameters())
        {
            var native = p.Name == "__result" ? target.ReturnType.MakeByRefType() : p.Name == "__instance" ? target.DeclaringType : target.GetParameters().SingleOrDefault(n => n.Name == p.Name)?.ParameterType;
            Require(native == p.ParameterType, patch + " binds " + p.Name + " with its exact host type");
        }
    targets.Add(patch, target);
}
// Exercise the actual production prefixes without applying Harmony patches or
// invoking the host draw methods. Busy capture must not alter panel ownership.
var enabledField = modType.GetField("enabled", flags)!;
var openField = modType.GetField("open", flags)!;
var busyField = modType.GetField("renderTask", flags)!;
var completionType = typeof(TaskCompletionSource<>).MakeGenericType(busyField.FieldType.GenericTypeArguments);
var pendingRender = completionType.GetProperty("Task")!.GetValue(Activator.CreateInstance(completionType));
var savedState = new[] { enabledField, openField, busyField }.Select(f => f.GetValue(null)).ToArray();
try
{
    foreach (bool active in new[] { false, true })
    foreach (bool visible in new[] { false, true })
    foreach (bool capturing in new[] { false, true })
    {
        enabledField.SetValue(null, active);
        openField.SetValue(null, visible);
        busyField.SetValue(null, capturing ? pendingRender : null);
        foreach (string name in new[] { "EditorResourcesPatch", "EditorStagingPatch" })
        {
            var prefix = modType.GetNestedType(name, BindingFlags.NonPublic)!.GetMethod("Prefix", flags)!;
            Require((bool)prefix.Invoke(null, null)! == !(active && visible),
                $"{name}: enabled={active}, open={visible}, busy={capturing}");
        }
    }
}
finally
{
    enabledField.SetValue(null, savedState[0]);
    openField.SetValue(null, savedState[1]);
    busyField.SetValue(null, savedState[2]);
}
// Exercise the production guard against the host's real binding matcher. This
// process never starts the game or writes settings; the editor is an inert
// uninitialized sentinel and key bindings are temporary in-memory settings.
var programType = host.GetType("KSA.Program", true)!;
var editorField = programType.GetField("Editor", flags)!;
var settingsType = host.GetType("KSA.GameSettings", true)!;
var currentSettings = settingsType.GetProperty("Current", flags)!;
var originalSettings = currentSettings.GetValue(null);
var originalEditor = editorField.GetValue(null);
var keyEventType = targets["VehicleSwitchPatch"].GetParameters()[0].ParameterType.GetElementType()!;
var eventConstructor = keyEventType.GetConstructors().Single(c => c.GetParameters().Length == 4
    && c.GetParameters()[0].ParameterType.FullName == "Brutal.GlfwApi.GlfwWindow"
    && c.GetParameters()[1].ParameterType.FullName == "Brutal.GlfwApi.GlfwKeyAction");
var eventParameters = eventConstructor.GetParameters();
var guard = modType.GetNestedType("VehicleSwitchPatch", BindingFlags.NonPublic)!.GetMethod("Prefix", flags)!;
var inputType = host.GetType("KSA.Input", true)!;
object FreshSettings()
{
    var settings = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(settingsType);
    settingsType.GetField("KeyBindings")!.SetValue(settings, inputType.GetMethod("GenerateDefault")!.Invoke(null, null));
    return settings;
}
void CheckSwitch(string key, string action, int modifiers, bool blocked)
{
    object keyEvent = eventConstructor.Invoke(new[] {
        eventParameters[0].ParameterType.IsValueType ? Activator.CreateInstance(eventParameters[0].ParameterType) : null,
        Enum.Parse(eventParameters[1].ParameterType, action),
        Enum.Parse(eventParameters[2].ParameterType, key),
        Enum.ToObject(eventParameters[3].ParameterType, modifiers) });
    object?[] call = { keyEvent, false };
    bool runNative = (bool)guard.Invoke(null, call)!;
    Require(runNative == !blocked && (bool)call[1]! == blocked,
        $"Vehicle switch {key}/{action}/mods={modifiers}: blocked={blocked}");
}
try
{
    currentSettings.SetValue(null, FreshSettings());
    var sentinelEditor = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(editorField.FieldType);
    foreach (bool active in new[] { false, true })
    foreach (bool visible in new[] { false, true })
    foreach (bool editing in new[] { false, true })
    {
        enabledField.SetValue(null, active);
        openField.SetValue(null, visible);
        editorField.SetValue(null, editing ? sentinelEditor : null);
        foreach (string key in new[] { "LeftBracket", "RightBracket" })
        foreach (string action in new[] { "Press", "Release", "Repeat" })
            CheckSwitch(key, action, 0, active && visible && editing && action == "Press");
    }
    CheckSwitch("RightBracket", "Press", 48, true); // lock keys
    CheckSwitch("RightBracket", "Press", 2, false);
    CheckSwitch("Escape", "Press", 0, false);
    CheckSwitch("P", "Press", 6, false);
    var rebound = FreshSettings();
    var bindings = (System.Collections.IDictionary)settingsType.GetField("KeyBindings")!.GetValue(rebound)!;
    var keyBindingType = host.GetType("KSA.KeyBindingValue", true)!;
    var keyBinding = Activator.CreateInstance(keyBindingType, Enum.Parse(eventParameters[2].ParameterType, "F8"), Enum.ToObject(eventParameters[3].ParameterType, 2));
    var bindingType = host.GetType("KSA.BindingValue", true)!;
    var convert = bindingType.GetMethods().Single(m => m.Name == "op_Implicit" && m.GetParameters()[0].ParameterType == keyBindingType);
    bindings[Enum.Parse(host.GetType("KSA.InputAction", true)!, "VesselNext")] = convert.Invoke(null, new[] { keyBinding });
    currentSettings.SetValue(null, rebound);
    CheckSwitch("RightBracket", "Press", 0, false);
    CheckSwitch("F8", "Press", 0, false);
    CheckSwitch("F8", "Press", 2, true);
    CheckSwitch("F8", "Release", 2, false);
}
finally
{
    currentSettings.SetValue(null, originalSettings);
    editorField.SetValue(null, originalEditor);
    enabledField.SetValue(null, savedState[0]);
    openField.SetValue(null, savedState[1]);
}
var gimbal = host.GetType("KSA.PartTreeRenderData", true)!.GetMethods(flags).Where(m => m.Name == "ApplyGimbal").ToArray();
Require(gimbal.Length == 1 && gimbal[0].IsStatic && gimbal[0].ReturnType.FullName == "Brutal.Numerics.double4x4"
    && gimbal[0].GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "KSA.PartModelDynamicModule", "Brutal.Numerics.double4x4" }), "Private ApplyGimbal reflection contract");

var menu = targets["MenuPatch"];
var instructions = ReadIl(menu);
var imgui = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game, "Brutal.ImGui.dll")).GetType("Brutal.ImGuiApi.ImGui", true)!;
var original = imgui.GetMethods(flags).Single(m => m.Name == "MenuItem" && m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(new[] { "Brutal.ImGuiApi.ImString", "Brutal.ImGuiApi.ImString", "System.Boolean", "System.Boolean" }));
int callCount = instructions.Count(i => i.Calls(original));
Require(callCount > 0, "Real menu IL calls the intercepted value overload");
using var pe = new PEReader(File.OpenRead(host.Location));
var metadata = pe.GetMetadataReader();
int exitIndex = instructions.FindIndex(i => i.opcode == OpCodes.Ldsflda && i.operand is FieldInfo field && field.Module == host.ManifestModule && IsExitLabel(field));
Require(exitIndex >= 0, "Real menu IL contains the UTF-8 Exit Editor label");
var exitCall = instructions.Skip(exitIndex + 1).First(i => i.operand is MethodInfo m && m.DeclaringType == imgui && m.Name == "MenuItem");
Require(exitCall.Calls(original), "Exit Editor uses the intercepted overload");
var menuPatch = modType.GetNestedType("MenuPatch", BindingFlags.NonPublic)!;
var transpile = menuPatch.GetMethod("Transpiler", flags)!;
var replacement = menuPatch.GetMethod("DrawMenuItem", flags)!;
var before = instructions.Select(i => (i.opcode, i.operand)).ToArray();
var after = ((IEnumerable<CodeInstruction>)transpile.Invoke(null, [instructions])!).ToArray();
Require(after.Length == before.Length, "Production menu transpiler preserves instruction count");
for (int i = 0; i < after.Length; i++)
{
    object? operand = Equals(before[i].operand, original) && before[i].opcode == OpCodes.Call ? replacement : before[i].operand;
    if (after[i].opcode != before[i].opcode || !Equals(after[i].operand, operand))
        throw new InvalidDataException("Unexpected menu rewrite at instruction " + i);
}
Require(after.Count(i => i.Calls(replacement)) == callCount, "Production menu transpiler rewrites only matching calls");
var editorUi = ReadIl(targets["EditorToolPromptPatch"]);
int prompt = editorUi.FindIndex(i => i.opcode == OpCodes.Ldstr && Equals(i.operand, "Select a part to "));
int foreground = editorUi.FindIndex(prompt + 1, i => i.operand is MethodInfo m &&
    m.DeclaringType == imgui && m.Name == "GetForegroundDrawList");
int drawPrompt = editorUi.FindIndex(foreground + 1, i => i.operand is MethodInfo m &&
    m.DeclaringType?.FullName == "Brutal.ImGuiApi.ImDrawListExtensions" && m.Name == "AddText");
Require(prompt >= 0 && foreground > prompt && drawPrompt > foreground,
    "The native transform hint is drawn in the foreground after building its text");
var nativePrompt = (MethodInfo)editorUi[drawPrompt].operand;
var promptPatch = modType.GetNestedType("EditorToolPromptPatch", BindingFlags.NonPublic)!;
var promptReplacement = promptPatch.GetMethod("DrawPrompt", flags)!;
Require(promptReplacement.IsStatic && promptReplacement.ReturnType == nativePrompt.ReturnType &&
    promptReplacement.GetParameters().Select(p => p.ParameterType).SequenceEqual(nativePrompt.GetParameters().Select(p => p.ParameterType)),
    "The transform hint wrapper matches the native draw signature");
var editorBefore = editorUi.Select(i => (i.opcode, i.operand)).ToArray();
var editorAfter = ((IEnumerable<CodeInstruction>)promptPatch.GetMethod("Transpiler", flags)!.Invoke(null, [editorUi])!).ToArray();
Require(editorAfter.Length == editorBefore.Length && editorAfter.Select((i, index) =>
    i.opcode == editorBefore[index].opcode && Equals(i.operand, index == drawPrompt ? promptReplacement : editorBefore[index].operand)).All(v => v),
    "The tool prompt hook replaces only the native foreground text draw");
PrintRecoveryChecks.Run(modType, mod, Require);
SelectionChecks.Run(modType, Require);
WorkspaceLifecycleChecks.Run(modType, editorField.FieldType, Require);
FuelHoseCaptureChecks.Run(host, mod, Require);
CraftIdentityChecks.Run(host, mod, Require);
RenderBoundaryChecks.Run(modType, mod, Require);
Console.WriteLine("PASS host contracts and real-IL menu rewrite. No patches installed; native game acceptance remains separate.");

bool IsExitLabel(FieldInfo field)
{
    var definition = metadata.GetFieldDefinition((FieldDefinitionHandle)MetadataTokens.EntityHandle(field.MetadataToken));
    int rva = definition.GetRelativeVirtualAddress();
    return rva != 0 && pe.GetSectionData(rva).GetContent(0, 12).AsSpan().SequenceEqual("Exit Editor\0"u8);
}

static void Require(bool condition, string message)
{
    if (!condition)
        throw new InvalidDataException(message);
    Console.WriteLine("PASS " + message);
}

static List<CodeInstruction> ReadIl(MethodInfo method)
{
    var ops = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
    var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
    var result = new List<CodeInstruction>();
    for (int index = 0; index < bytes.Length;)
    {
        ushort value = bytes[index++];
        if (value == 0xfe)
            value = (ushort)(0xfe00 | bytes[index++]);
        var op = ops[value];
        int count = op.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, index),
            _ => 4
        };
        object? operand = op.OperandType switch
        {
            OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok => method.Module.ResolveMember(BitConverter.ToInt32(bytes, index)),
            OperandType.InlineString => method.Module.ResolveString(BitConverter.ToInt32(bytes, index)),
            _ => count == 0 ? null : Convert.ToHexString(bytes.AsSpan(index, count))
        };
        result.Add(new CodeInstruction(op, operand));
        index += count;
    }
    return result;
}
