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
    ("MenuPatch", "KSA.Program", "DrawMenuBar", new[] { "KSA.IGameViewport", "System.Int32" }),
    ("EditorPatch", "KSA.VehicleEditor", "OnDrawUi", new[] { "KSA.IGameViewport" }),
    ("EditorResourcesPatch", "KSA.ResourcesPanel", "DrawEditor", new[] { "KSA.PartTree", "Brutal.Numerics.float2" }),
    ("EditorStagingPatch", "KSA.VehicleEditingSpace", "DrawStageWindow", new[] { "KSA.IViewport" })
})
{
    var methods = host.GetType(type, true)!.GetMethods(flags).Where(m => m.Name == name).ToArray();
    Require(methods.Length == 1, type + "." + name + " is unambiguous");
    var target = methods.Single();
    Require(target.IsStatic == (patch == "EditorResourcesPatch") && target.ReturnType == typeof(void), name + " static/instance/return contract");
    Require(target.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(parameters), name + " parameter types: " + string.Join(", ", target.GetParameters().Select(p => p.ParameterType.FullName)));
    var patchType = modType.GetNestedType(patch, BindingFlags.NonPublic)!;
    var patchMethod = patchType.GetMethods(flags).Single(m => m.Name is "Prefix" or "Postfix" or "Transpiler");
    if (patchMethod.Name != "Transpiler")
        foreach (var p in patchMethod.GetParameters())
        {
            var native = p.Name == "__instance" ? target.DeclaringType : target.GetParameters().SingleOrDefault(n => n.Name == p.Name)?.ParameterType;
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
