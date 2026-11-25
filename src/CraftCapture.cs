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
    public long DetachedGeometryBytes { get; init; }
    public PoseControl[] Poses { get; init; } = [];
}

static class CraftCapture
{


    // This entire method runs on the editor thread. No game-owned object escapes it.
    public static CapturedCraft Take(VehicleEditor editor) => TakeCancellable(editor, default);

    public static CapturedCraft TakeForDrawing(VehicleEditor editor, PageSize page, params Sheet?[] retained) =>
        TakeCancellable(editor, default, page, RenderMemory.Retained(retained));

    internal static CapturedCraft TakeCancellable(VehicleEditor editor, CancellationToken token, PageSize? page = null, long retained = 0)
    {
        var stats = new CaptureStats();
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            stats.RetainedBytes = retained;
            stats.CandidatePixelBytes = page.HasValue ? RenderMemory.CandidatePixels(page.Value) : 0;
            return TakeCore(editor, stats, token);
        }
        catch (OperationCanceledException) { stats.Failure = "cancelled"; throw; }
        catch (OverflowException) { stats.Failure = "arithmetic-overflow"; throw; }
        catch
        {
            if (stats.Failure == "none") stats.Failure = "invalid-source";
            throw;
        }
        finally { stats.Log(timer.Elapsed); }
    }

    static CapturedCraft TakeCore(VehicleEditor editor, CaptureStats stats, CancellationToken token)
    {
        var tree = editor.EditingSpace.Parts ?? throw new InvalidOperationException("Load a craft in the editor first.");
        var support = PartSupport.Load(Path.GetDirectoryName(typeof(CraftCapture).Assembly.Location)!);
        var census = System.Diagnostics.Stopwatch.StartNew();
        FuelHoseCapture.SampledRoute[] routes;
        try { routes = CraftCensus.Count(tree, support, stats, token); }
        finally { stats.CensusMilliseconds = census.ElapsedMilliseconds; }
        stats.RequireBudget(includeInterior: false);
        string? omittedInterior = null;
        try { stats.RequireBudget(includeInterior: true); }
        catch (InvalidOperationException e)
        {
            omittedInterior = e.Message;
            stats.InteriorFailure = stats.Failure;
            stats.Failure = "none";
        }

        var copy = System.Diagnostics.Stopwatch.StartNew();
        try { return CopyCraft(editor, tree, support, routes, stats, token, omittedInterior); }
        finally { stats.CopyMilliseconds = copy.ElapsedMilliseconds; }
    }

    static CapturedCraft CopyCraft(VehicleEditor editor, PartTree tree, PartSupport support,
        FuelHoseCapture.SampledRoute[] routes, CaptureStats stats, CancellationToken token, string? omittedInterior)
    {
        var meshes = new List<Rendering.Mesh>();
        var identities = new List<PartIdentity>();
        var nodes = new Dictionary<uint, AssemblyNode>();
        var pending = new Stack<(Part Part, Part? Owner)>();
        void Remember(Part root)
        {
            pending.Push((root, null));
            while (pending.TryPop(out var entry))
            {
                token.ThrowIfCancellationRequested();
                var (part, modelOwner) = entry;
                if (nodes.TryGetValue(part.InstanceId, out var known))
                {
                    if (modelOwner != null)
                        nodes[part.InstanceId] = known with
                        {
                            Parent = modelOwner.InstanceId,
                            Subpart = true
                        };
                    continue;
                }
                var parent = modelOwner ?? part.PartParent ?? part.TreeParent;
                var pose = part.ComputeMatrixAsmb2VehicleAsmb();
                System.Numerics.Vector3 Point(double x, double y, double z)
                {
                    var p = new double3(x, y, z).Transform(pose);
                    return new((float)p.Y, (float)p.Z, (float)p.X);
                }
                var origin = Point(0, 0, 0);
                var x = Point(1, 0, 0) - origin;
                var y = Point(0, 1, 0) - origin;
                var z = Point(0, 0, 1) - origin;
                var axes = new AssemblyPose(origin, System.Numerics.Vector3.Normalize(x), System.Numerics.Vector3.Normalize(y), System.Numerics.Vector3.Normalize(z));
                // KSA's AttachedInternal is a separate tree Part (e.g. capsule IVA
                // owning its glass), but belongs to the same structural part.
                nodes.Add(part.InstanceId, new(part.InstanceId, parent?.InstanceId, PartNames.Display(part.Template.Id, part.Template.DisplayName, support), part.Template.Id, modelOwner != null || part.PartParent != null || part.Template.IsSubPart || part.IsAttachedInternal, axes)
                {
                    Scale = new(x.Length(), y.Length(), z.Length())
                });
                var children = part.SubParts;
                for (int i = children.Length - 1; i >= 0; i--)
                    pending.Push((children[i], part));
                if (parent != null)
                    pending.Push((parent, null));
            }
        }
        foreach (var part in tree.Parts)
            Remember(part);
        stats.CapturedParts = nodes.Count;
        var textures = new Dictionary<string, RawTexture>();
        var warnings = new HashSet<string>();
        var sourceCache = new Dictionary<(MeshReference Mesh, int Primitive), MeshSource>();
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
            {
                stats.Failure = "material-dimensions";
                throw new InvalidOperationException($"Material dimensions {w:N0} x {h:N0} exceed the 8,192 x 8,192 export limit. See PawPrints diagnostics.");
            }
            int format = (int)asset.SampleFormat;
            if (!RawTexture.Supported(format))
            {
                warnings.Add($"Material {key}: unsupported texture format {asset.SampleFormat}; geometry retained.");
                return null;
            }
            decodedBytes += checked((long)w * h * 4);
            if (decodedBytes > 384L * 1024 * 1024)
            {
                stats.Failure = "material-bytes";
                throw new InvalidOperationException($"Craft materials require at least {decodedBytes:N0} decoded bytes (limit: {384L * 1024 * 1024:N0}). See PawPrints diagnostics.");
            }
            var info = new TextureInfo(key, w, h);
            textures.Add(key, new(info, format, asset.FaceData(0).ToArray()));
            return info;
        }
        var gimbal = AccessTools.Method(typeof(PartTreeRenderData), "ApplyGimbal") ?? throw new MissingMethodException("Part gimbal transform unavailable.");
        void Add(Part part, MeshReference? mesh, PbrMaterialReference? material, PartModelDynamicModule? dynamic)
        {
            token.ThrowIfCancellationRequested();
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
            System.Numerics.Vector3? solarDirection = null;
            if (surface == "solar")
            {
                // KSA's incidence calculation uses module-local +X. Resolve it into
                // the cell mesh's source frame once, before any detached print poses.
                for (Part? owner = part; owner != null; owner = owner.PartParent)
                {
                    var panels = owner.Modules.Get<SolarPanel>();
                    if (panels.Length == 0)
                        continue;
                    if (panels.Length == 1)
                    {
                        var axis = double3.UnitX.Transform(panels[0].Asmb2ParentAsmb)
                            .Transform(owner.Asmb2VehicleAsmb).Transform(part.Asmb2VehicleAsmb.Inverse());
                        solarDirection = new((float)axis.X, (float)axis.Y, (float)axis.Z);
                    }
                    break;
                }
            }
            var pose = part.ComputeMatrixAsmb2VehicleAsmb();
            if (dynamic != null)
                pose = (double4x4)gimbal.Invoke(null, [dynamic, pose])!;
            var transform = new MeshTransform(new(pose.M11, pose.M12, pose.M13), new(pose.M21, pose.M22, pose.M23),
                new(pose.M31, pose.M32, pose.M33), new(pose.M41, pose.M42, pose.M43));
            if (!transform.Finite)
                throw new InvalidDataException("Nonfinite craft transform.");
            var assembly = part.StructuralPart;
            int primitiveNumber = 0;
            foreach (var primitive in mesh.HostPrimitives)
            {
                var key = (mesh, primitiveNumber++);
                if (!sourceCache.TryGetValue(key, out var source))
                {
                    var positions = primitive.GetVertexSpan<float3>(MeshAttribute.Position);
                    if (positions.Length == 0) continue;
                    var normals = primitive.GetVertexSpan<float3>(MeshAttribute.Normal);
                    var uvs = primitive.GetVertexSpan<float2>(MeshAttribute.Uv0);
                    if (normals.Length != positions.Length)
                        throw new InvalidDataException($"Missing mesh normals: {part.DisplayName}");
                    if (primitive.IndexBuffer == null)
                        throw new InvalidDataException("Mesh index data unavailable.");
                    var local = new System.Numerics.Vector3[positions.Length];
                    var n = new System.Numerics.Vector3[positions.Length];
                    var uv = new System.Numerics.Vector2[positions.Length];
                    for (int i = 0; i < positions.Length; i++)
                    {
                        if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                        var p = positions[i];
                        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                            throw new InvalidDataException("Nonfinite craft geometry.");
                        local[i] = new(p.X, p.Y, p.Z);
                        n[i] = new(normals[i].X, normals[i].Y, normals[i].Z);
                        if (uvs.Length == positions.Length)
                            uv[i] = new(uvs[i].X, uvs[i].Y);
                    }
                    var input = primitive.IndexBuffer.ToSpan<int>();
                    if (input.Length % 3 != 0)
                        throw new InvalidDataException("Invalid mesh indices.");
                    var indices = new int[input.Length];
                    for (int i = 0; i < indices.Length; i++)
                    {
                        if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                        int value = input[i];
                        if ((uint)value >= (uint)local.Length)
                            throw new InvalidDataException("Invalid mesh indices.");
                        indices[i] = value;
                    }
                    source = new(local, n, uv, indices);
                    sourceCache.Add(key, source);
                }
                meshes.Add(new(part.DisplayName, [], source.Indices, [], [], [], surface, normal, diffuse)
                {
                    LineworkNode = part.InstanceId,
                    Instance = new(source, transform),
                    SolarDirection = solarDirection
                });
                identities.Add(new(part.InstanceId, template, part.DisplayName, assembly.InstanceId, assembly.Template.Id, PartNames.Display(assembly.Template.Id, assembly.Template.DisplayName, support)));
            }
        }
        CraftCensus.VisitModels(tree, false, Add);
        foreach (var route in routes)
        {
            token.ThrowIfCancellationRequested();
            var part = route.First;
            var hose = FuelHoseCapture.Build(route);
            Remember(part);
            meshes.Add(hose);
            // A hose belongs to its first fuel port for picking and exploded
            // disassembly. It is not an additional stock part in the parts table.
            var owner = part.StructuralPart;
            identities.Add(new(part.InstanceId, part.Template.Id, part.DisplayName,
                owner.InstanceId, owner.Template.Id, PartNames.Display(owner.Template.Id, owner.Template.DisplayName, support)));
        }
        if (meshes.Count == 0)
            throw new InvalidOperationException("The current craft has no exportable meshes.");
        int exteriorCount = meshes.Count;
        var exterior = new Capture(meshes.ToArray());
        var exteriorIds = identities.ToArray();
        var exteriorTextures = textures.Keys.ToHashSet();
        if (omittedInterior == null)
        {
            try { CraftCensus.VisitModels(tree, true, Add); }
            catch (Exception e) when (e is InvalidOperationException or InvalidDataException)
            {
                stats.InteriorFailure = stats.Failure == "none" ? "invalid-interior" : stats.Failure;
                stats.Failure = "none";
                omittedInterior = e.Message;
                meshes.RemoveRange(exteriorCount, meshes.Count - exteriorCount);
                identities.RemoveRange(exteriorCount, identities.Count - exteriorCount);
                foreach (var key in textures.Keys.Where(k => !exteriorTextures.Contains(k)).ToArray())
                    textures.Remove(key);
            }
        }
        if (omittedInterior != null)
        {
            stats.InteriorOmitted = true;
            warnings.Add("Internal detail geometry omitted: " + omittedInterior);
        }
        token.ThrowIfCancellationRequested();
        var assembly = new AssemblyCapture(new(meshes.ToArray()), nodes.Values.ToArray(), identities.Select(p => p.InstanceId).ToArray(), Enumerable.Range(0, meshes.Count).Select(i => i >= exteriorCount).ToArray())
        {
            Topology = CraftIdentityCapture.Take(tree)
        };
        AssemblyGraph.ValidateHierarchy(assembly.Nodes, token);
        var poses = PoseCapture.Take(tree, assembly, support);
        return new(editor.EditingSpace.Id, exterior, textures, exteriorIds, warnings.ToArray())
        {
            Assembly = assembly,
            Poses = poses,
            DetachedGeometryBytes = stats.SourceBytes(!stats.InteriorOmitted),
            Performance = PerformanceCapture.Take(tree, assembly, support) with
            {
                CraftName = editor.EditingSpace.Id
            }
        };
    }


    public static LabelPart[] LabelParts(CapturedCraft craft) => craft.Parts.Select((part, index) => (part, index)).GroupBy(p => p.part.AssemblyInstanceId)
        .Select((group, id) => new LabelPart(id, PartNames.Display(group.First().part.AssemblyTemplateId, group.First().part.AssemblyName), group.First().part.AssemblyTemplateId, group.Select(p => p.index).ToArray(), NodeId: group.Key)).ToArray();
}
