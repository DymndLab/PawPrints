using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Brutal.Numerics;
using Brutal.Render.Mesh;
using HarmonyLib;
using KSA;
using StarMap.API;
using RenderCore.Mesh;
using RenderCore;
using Core;
using Brutal.GltfApi;
using Brutal.VulkanApi.Abstractions;

namespace BlueprintProof;

// Development capture driver for detached craft geometry and material fixtures.
// Reads only the named craft in the isolated editor; never commands flight or saves a vehicle.
[StarMapMod]
public sealed class CaptureProbe
{
    const string Owner = "blueprint.proof.capture.5801";
    static string? output;
    static readonly HashSet<string> captured = new(StringComparer.Ordinal);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> completed = new(StringComparer.Ordinal);
    static string[] allowedCraft = ["Lander_1"];
    static string? observedCraft;
    static int frames;
    Harmony? harmony;
    static bool batchEnabled, batchFailed;
    static int batchIndex;
    static string? batchSource;

    [StarMapBeforeMain]
    public void Load()
    {
        output = Environment.GetEnvironmentVariable("BLUEPRINT_CAPTURE_DIR");
        allowedCraft = (Environment.GetEnvironmentVariable("BLUEPRINT_CAPTURE_CRAFTS") ?? "Lander_1").Split(',');
        batchSource = Environment.GetEnvironmentVariable("BLUEPRINT_BATCH_SOURCE");
        batchEnabled = batchSource != null && Environment.GetEnvironmentVariable("STARMAP_INSTANCE_PATH")?.EndsWith("blueprint-stock\\instance", StringComparison.OrdinalIgnoreCase) == true;
        if (string.IsNullOrEmpty(output))
            return;
        Directory.CreateDirectory(output);
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(VehicleEditor).Assembly.Location)));
            if (hash != "3A9B34894446A419193A1D9810A0902AA8EAA8B8AC910F04F687DC647818E586")
                throw new NotSupportedException("This capture probe requires inspected KSA 5801.");
            harmony = new Harmony(Owner);
            harmony.PatchAll(typeof(CaptureProbe).Assembly);
            File.WriteAllText(Path.Combine(output, "probe-status.txt"), "Loaded; waiting for named editor craft: " + string.Join(", ", allowedCraft));
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output, "probe-error.txt"), e.ToString()); harmony?.UnpatchAll(Owner); }
    }

    [StarMapUnload]
    public void Unload()
    {
        harmony?.UnpatchAll(Owner);
    }

    public sealed record Mesh(string PartId, string Name, string Kind, double[][] Vertices, int[] Indices);

    // Opt-in only in the disposable stock profile. Calls the same deserializer
    // and editor loading APIs as KSA's load dialog; never saves or launches craft.
    [HarmonyPatch(typeof(KSA.Program), "OnFrameEditor")]
    static class BatchPatch
    {

        static void Postfix()
        {
            if (!batchEnabled || batchFailed || output == null || batchSource == null)
                return;
            try
            {
                if (batchIndex >= allowedCraft.Length)
                    return;
                string craft = allowedCraft[batchIndex];
                if (KSA.Program.Editor?.EditingSpace.Id == craft)
                {
                    CapturePatch.Postfix(KSA.Program.Editor);
                    if (!completed.ContainsKey(craft))
                        return;
                    batchIndex++;
                    if (batchIndex >= allowedCraft.Length)
                    {
                        File.WriteAllText(Path.Combine(output, "batch-complete.txt"), "All requested stock editor captures completed.");
                        return;
                    }
                    craft = allowedCraft[batchIndex];
                }
                string file = Path.Combine(batchSource, craft, "vehicle.xml");
                using var reader = File.OpenText(file);
                var data = (VehicleSaveData)VehicleSaves.VehicleSerializer.Deserialize(reader)!;
                if (data.Id != craft)
                    throw new InvalidDataException("Stock craft identity mismatch");
                data.OnDataLoad(Mod.Empty);
                if (data.RootPartInstance == null)
                    throw new InvalidDataException("Missing stock root part");
                var parts = PartTree.Deserialize(new PartTreeData { Root = data.RootPartInstance, SequenceEnvironments = data.SequenceEnvironments, FuelLinks = data.FuelLinks });
                if (KSA.Program.Editor != null)
                    KSA.Program.Editor.LaunchNewVehicle = false;
                KSA.Program.Editor?.Dispose();
                KSA.Program.Editor = new VehicleEditor(null, true);
                KSA.Program.EditorFlag = true;
                Universe.SetSimulationSpeed(0);
                KSA.Program.Editor.EditingSpace.Id = craft;
                KSA.Program.Editor.LoadVehicle(parts);
                File.WriteAllText(Path.Combine(output, "batch-status.txt"), "Loaded stock save into native editor: " + craft);
            }
            catch (Exception e) { batchFailed = true; File.WriteAllText(Path.Combine(output, "batch-error.txt"), e.ToString()); }
        }
    }

    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.OnDrawUi))]
    static class CapturePatch
    {

        public static void Postfix(VehicleEditor __instance)
        {
            string craft = __instance.EditingSpace.Id;
            if (captured.Contains(craft) || output == null || !allowedCraft.Contains(craft))
                return;
            if (observedCraft != craft)
            {
                observedCraft = craft;
                frames = 0;
            }
            var tree = __instance.EditingSpace.Parts;
            if (tree == null || ++frames < 120)
                return;
            try
            {
                var legs = new List<object>();
                foreach (var a in tree.Modules.Get<KeyframeAnimationModule>())
                {
                    if (!a.Parent.Template.Id.Contains("LandingLeg", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!tree.States.TryGetTypeList(out ModuleStateful<KeyframeAnimationModule, KeyframeAnimationModule.State, EmptyStruct, EmptyStruct>.StateList states))
                        return;
                    var state = states.GetModuleAndState(a).State;
                    if (craft == "Lander_1" && (Math.Abs(state.TimeCurrent - a.Shared.Duration) > .002f || state.DeploymentState != KeyframeAnimationModule.DeploymentState.Deployed))
                        return;
                    legs.Add(new
                    {
                        part = a.Parent.Id,
                        current = state.TimeCurrent,
                        duration = a.Shared.Duration,
                        state = state.DeploymentState.ToString()
                    });
                }
                if (craft == "Lander_1" && legs.Count != 4)
                    return;
                var meshes = new List<Mesh>();
                var warnings = new List<string>();
                int totalVertices = 0;
                void Add(Part p, MeshReference? mesh, string kind, double4x4 transform)
                {
                    if (mesh?.HostPrimitives == null)
                    {
                        warnings.Add($"Missing mesh: {p.Id} ({kind})");
                        return;
                    }
                    foreach (var primitive in mesh.HostPrimitives)
                    {
                        var positions = primitive.GetVertexSpan<float3>(MeshAttribute.Position);
                        totalVertices += positions.Length;
                        if (totalVertices > 3000000)
                            throw new InvalidOperationException("Proof capture vertex budget exceeded.");
                        var vertices = new double[positions.Length][];
                        for (int i = 0; i < positions.Length; ++i)
                        {
                            var v = new double3(positions[i].X, positions[i].Y, positions[i].Z).Transform(transform);
                            if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))
                                throw new InvalidDataException("Nonfinite vertex");
                            vertices[i] = [v.X, v.Y, v.Z];
                        }
                        if (primitive.IndexBuffer == null)
                            throw new InvalidDataException("Missing index buffer");
                        var indices = primitive.IndexBuffer.ToSpan<int>().ToArray();
                        if (indices.Length % 3 != 0 || indices.Any(i => i < 0 || i >= vertices.Length))
                            throw new InvalidDataException("Invalid triangle indices");
                        meshes.Add(new Mesh(p.Id, p.DisplayName, kind, vertices, indices));
                    }
                }
                foreach (var m in tree.Modules.Get<PartModelModule>())
                    if (!m.PartModel.Template.Internal && m.PartModel.Template.RayTracing != PartModelModule.RaytracingMode.ShadowProxy)
                        Add(m.Parent, m.PartModel.Template.Mesh?.Value, "solid", m.Parent.ComputeMatrixAsmb2VehicleAsmb());
                var gimbal = AccessTools.Method(typeof(PartTreeRenderData), "ApplyGimbal");
                foreach (var m in tree.Modules.Get<PartModelDynamicModule>())
                    Add(m.Parent, m.PartModelDynamic.Template.Mesh?.Value, "dynamic", (double4x4)gimbal.Invoke(null, [m, m.Parent.ComputeMatrixAsmb2VehicleAsmb()])!);
                foreach (var m in tree.Modules.Get<PartModelGlassModule>())
                    Add(m.Parent, m.PartModelGlass.Template.Mesh?.Value, "glass", m.Parent.ComputeMatrixAsmb2VehicleAsmb());
                if (meshes.Count == 0)
                    throw new InvalidOperationException("No render meshes captured");
                captured.Add(craft);
                var snapshot = new
                {
                    craft = __instance.EditingSpace.Id,
                    build = "2026.11.10.5801",
                    capturedUtc = DateTime.UtcNow,
                    legs,
                    warnings,
                    meshes,
                    totalVertices,
                    triangles = meshes.Sum(m => m.Indices.Length / 3),
                    limitations = new[] { "Static/dynamic/glass render meshes; excludes shader dents, material texture details and non-mesh effects.", "Local proof only; not a production exporter." }
                };
                File.WriteAllText(Path.Combine(output, "probe-status.txt"), "Geometry copied from live editor; writing snapshot.");
                // The worker receives only detached managed arrays, no game objects.
                _ = Task.Run(() =>
                {
                    try
                    {
                        string slug = craft == "Lander_1" ? "lander-1" : craft.ToLowerInvariant();
                        File.WriteAllText(Path.Combine(output, slug + ".geometry.json"), JsonSerializer.Serialize(snapshot));
                        completed[craft] = true;
                        File.WriteAllText(Path.Combine(output, "probe-status.txt"), $"Captured {craft}: {meshes.Count} meshes / {totalVertices} vertices; {legs.Count} landing leg states recorded.");
                    }
                    catch (Exception e) { File.WriteAllText(Path.Combine(output, "probe-error.txt"), e.ToString()); }
                });
            }
            catch (Exception e) { captured.Add(craft); File.WriteAllText(Path.Combine(output, "probe-error.txt"), e.ToString()); }
        }
    }
}
