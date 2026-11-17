using Blueprint.Rendering;
using Brutal.Numerics;
using Brutal.Render.Mesh;
using HarmonyLib;
using KSA;
using RenderCore;

namespace Blueprint;

sealed record PartIdentity(uint InstanceId, string TemplateId, string Name, uint AssemblyInstanceId, string AssemblyTemplateId, string AssemblyName);
sealed record CapturedCraft(string Name, Capture Geometry, Dictionary<string, RawTexture> Textures, PartIdentity[] Parts, string[] Warnings)
{
    public PerformanceData? Performance
    {
        get; init;
    }
    public AssemblyCapture? Assembly
    {
        get; init;
    }
    public PoseControl[] Poses { get; init; } = [];
}

static class CraftCapture
{


    // This entire method runs on the editor thread. No game-owned object escapes it.
    public static CapturedCraft Take(VehicleEditor editor)
    {
        var tree = editor.EditingSpace.Parts ?? throw new InvalidOperationException("Load a craft in the editor first.");
        var support = PartSupport.Load(Path.GetDirectoryName(typeof(CraftCapture).Assembly.Location)!);
        var meshes = new List<Rendering.Mesh>();
        var identities = new List<PartIdentity>();
        var nodes = new Dictionary<uint, AssemblyNode>();
        void Remember(Part part, Part? modelOwner = null)
        {
            if (nodes.TryGetValue(part.InstanceId, out var known))
            {
                if (modelOwner != null)
                    nodes[part.InstanceId] = known with
                    {
                        Parent = modelOwner.InstanceId,
                        Subpart = true
                    };
                return;
            }
            var parent = modelOwner ?? part.PartParent ?? part.TreeParent;
            var pose = part.ComputeMatrixAsmb2VehicleAsmb();
            System.Numerics.Vector3 Point(double x, double y, double z)
            {
                var p = new double3(x, y, z).Transform(pose);
                return new((float)p.Y, (float)p.Z, (float)p.X);
            }
            var origin = Point(0, 0, 0);
            var axes = new AssemblyPose(origin, System.Numerics.Vector3.Normalize(Point(1, 0, 0) - origin), System.Numerics.Vector3.Normalize(Point(0, 1, 0) - origin), System.Numerics.Vector3.Normalize(Point(0, 0, 1) - origin));
            // KSA's AttachedInternal is a separate tree Part (e.g. capsule IVA
            // owning its glass), but belongs to the same structural part.
            nodes.Add(part.InstanceId, new(part.InstanceId, parent?.InstanceId, PartNames.Display(part.Template.Id, part.Template.DisplayName, support), part.Template.Id, modelOwner != null || part.PartParent != null || part.Template.IsSubPart || part.IsAttachedInternal, axes));
            if (parent != null)
                Remember(parent);
            foreach (var child in part.SubParts)
                Remember(child, part);
        }
        foreach (var part in tree.Parts)
            Remember(part);
        var textures = new Dictionary<string, RawTexture>();
        var warnings = new HashSet<string>();
        int vertexCount = 0;
        long decodedBytes = 0;
        TextureInfo? Map(TextureReference? reference)
        {
            if (reference == null)
                return null;
            var asset = reference.TextureAsset;
            string key = reference.Id;
            if (textures.TryGetValue(key, out var prior))
                return prior.Info;
            if (asset == null || !asset.HasHostData)
            {
                warnings.Add($"Material unavailable: {key}");
                return null;
            }
            int w = asset.Width, h = asset.Height;
            if (w < 1 || h < 1 || w > 8192 || h > 8192)
                throw new InvalidOperationException("Material dimensions exceed the export budget.");
            int format = (int)asset.SampleFormat;
            if (!RawTexture.Supported(format))
            {
                warnings.Add($"Material {key}: unsupported texture format {asset.SampleFormat}; geometry retained.");
                return null;
            }
            decodedBytes += checked((long)w * h * 4);
            if (decodedBytes > 384L * 1024 * 1024)
                throw new InvalidOperationException("Craft materials exceed the 384 MB export budget.");
            var info = new TextureInfo(key, w, h);
            textures.Add(key, new(info, format, asset.FaceData(0).ToArray()));
            return info;
        }
        void Add(Part part, MeshReference? mesh, PbrMaterialReference? material, double4x4 pose)
        {
            Remember(part);
            if (mesh?.HostPrimitives == null)
            {
                warnings.Add($"Missing mesh: {part.DisplayName}");
                return;
            }
            string template = part.Template.Id;
            string surface = support.Surface(template);
            var normal = surface == "structure" ? null : Map(material?.NormalReference?.Value);
            var diffuse = surface == "structure" ? null : Map(material?.DiffuseReference?.Value);
            if (surface != "structure" && normal == null)
                warnings.Add($"{part.DisplayName}: authored normal map unavailable.");
            if (surface != "structure" && diffuse == null)
                warnings.Add($"{part.DisplayName}: authored color map unavailable.");
            var assembly = part.StructuralPart;
            foreach (var primitive in mesh.HostPrimitives)
            {
                var positions = primitive.GetVertexSpan<float3>(MeshAttribute.Position);
                var normals = primitive.GetVertexSpan<float3>(MeshAttribute.Normal);
                var uvs = primitive.GetVertexSpan<float2>(MeshAttribute.Uv0);
                vertexCount += positions.Length;
                if (vertexCount > 1000000)
                    throw new InvalidOperationException("Craft exceeds the one-million-vertex export budget.");
                if (positions.Length == 0)
                    continue;
                var local = new double[positions.Length][];
                var world = new double[positions.Length][];
                var n = new double[positions.Length][];
                var uv = new double[positions.Length][];
                if (normals.Length != positions.Length)
                    throw new InvalidDataException($"Missing mesh normals: {part.DisplayName}");
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i];
                    local[i] = [p.X, p.Y, p.Z];
                    var v = new double3(p.X, p.Y, p.Z).Transform(pose);
                    if (!double.IsFinite(v.X) || !double.IsFinite(v.Y) || !double.IsFinite(v.Z))
                        throw new InvalidDataException("Nonfinite craft geometry.");
                    world[i] = [v.X, v.Y, v.Z];
                    n[i] = [normals[i].X, normals[i].Y, normals[i].Z];
                    uv[i] = uvs.Length == positions.Length ? [uvs[i].X, uvs[i].Y] : [0d, 0d];
                }
                if (primitive.IndexBuffer == null)
                    throw new InvalidDataException("Mesh index data unavailable.");
                var indices = primitive.IndexBuffer.ToSpan<int>().ToArray();
                if (indices.Length % 3 != 0 || indices.Any(i => i < 0 || i >= world.Length))
                    throw new InvalidDataException("Invalid mesh indices.");
                meshes.Add(new(part.DisplayName, world, indices, local, n, uv, surface, normal, diffuse));
                identities.Add(new(part.InstanceId, template, part.DisplayName, assembly.InstanceId, assembly.Template.Id, PartNames.Display(assembly.Template.Id, assembly.Template.DisplayName, support)));
            }
        }
        foreach (var m in tree.Modules.Get<PartModelModule>())
            if (!m.PartModel.Template.Internal && m.PartModel.Template.RayTracing != PartModelModule.RaytracingMode.ShadowProxy)
                Add(m.Parent, m.PartModel.Template.Mesh?.Value, m.PartModel.Template.Material?.Value, m.Parent.ComputeMatrixAsmb2VehicleAsmb());
        var gimbal = AccessTools.Method(typeof(PartTreeRenderData), "ApplyGimbal") ?? throw new MissingMethodException("Part gimbal transform unavailable.");
        foreach (var m in tree.Modules.Get<PartModelDynamicModule>())
            Add(m.Parent, m.PartModelDynamic.Template.Mesh?.Value, m.PartModelDynamic.Template.Material?.Value, (double4x4)gimbal.Invoke(null, [m, m.Parent.ComputeMatrixAsmb2VehicleAsmb()])!);
        foreach (var m in tree.Modules.Get<PartModelGlassModule>())
            Add(m.Parent, m.PartModelGlass.Template.Mesh?.Value, m.PartModelGlass.Template.Material?.Value, m.Parent.ComputeMatrixAsmb2VehicleAsmb());
        if (meshes.Count == 0)
            throw new InvalidOperationException("The current craft has no exportable meshes.");
        int exteriorCount = meshes.Count;
        var exterior = new Capture(meshes.ToArray());
        var exteriorIds = identities.ToArray();
        var exteriorTextures = textures.Keys.ToHashSet();
        try
        {
            foreach (var m in tree.Modules.Get<PartModelModule>())
                if (m.PartModel.Template.Internal && m.PartModel.Template.RayTracing != PartModelModule.RaytracingMode.ShadowProxy)
                    Add(m.Parent, m.PartModel.Template.Mesh?.Value, m.PartModel.Template.Material?.Value, m.Parent.ComputeMatrixAsmb2VehicleAsmb());
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
        {
            meshes.RemoveRange(exteriorCount, meshes.Count - exteriorCount);
            identities.RemoveRange(exteriorCount, identities.Count - exteriorCount);
            foreach (var key in textures.Keys.Where(k => !exteriorTextures.Contains(k)).ToArray())
                textures.Remove(key);
            warnings.Add("Internal detail geometry omitted: " + e.Message);
        }
        var assembly = new AssemblyCapture(new(meshes.ToArray()), nodes.Values.ToArray(), identities.Select(p => p.InstanceId).ToArray(), Enumerable.Range(0, meshes.Count).Select(i => i >= exteriorCount).ToArray());
        foreach (var node in assembly.Nodes)
            AssemblyGraph.Ancestors(assembly, node.Id);
        var poses = PoseCapture.Take(tree, assembly, support);
        return new(editor.EditingSpace.Id, exterior, textures, exteriorIds, warnings.ToArray())
        {
            Assembly = assembly,
            Poses = poses,
            Performance = PerformanceCapture.Take(tree, assembly, support) with
            {
                CraftName = editor.EditingSpace.Id
            }
        };
    }


    public static LabelPart[] LabelParts(CapturedCraft craft) => craft.Parts.Select((part, index) => (part, index)).GroupBy(p => p.part.AssemblyInstanceId)
        .Select((group, id) => new LabelPart(id, PartNames.Display(group.First().part.AssemblyTemplateId, group.First().part.AssemblyName), group.First().part.AssemblyTemplateId, group.Select(p => p.index).ToArray(), NodeId: group.Key)).ToArray();
}
