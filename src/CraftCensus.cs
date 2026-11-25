using Blueprint.Rendering;
using Brutal.Numerics;
using Brutal.Render.Mesh;
using KSA;
using RenderCore;

namespace Blueprint;

static class CraftCensus
{
    // This visitor is always called on the KSA editor thread. The count pass
    // reads buffer lengths without expanding meshes or computing world poses.
    public static void VisitModels(PartTree tree, bool interior,
        Action<Part, MeshReference?, PbrMaterialReference?, PartModelDynamicModule?> visit)
    {
        foreach (var m in tree.Modules.Get<PartModelModule>())
        {
            var template = m.PartModel.Template;
            if (template.Internal == interior && template.RayTracing != PartModelModule.RaytracingMode.ShadowProxy)
                visit(m.Parent, template.Mesh?.Value, template.Material?.Value, null);
        }
        if (interior) return;
        foreach (var m in tree.Modules.Get<PartModelDynamicModule>())
            visit(m.Parent, m.PartModelDynamic.Template.Mesh?.Value, m.PartModelDynamic.Template.Material?.Value, m);
        foreach (var m in tree.Modules.Get<PartModelGlassModule>())
            visit(m.Parent, m.PartModelGlass.Template.Mesh?.Value, m.PartModelGlass.Template.Material?.Value, null);
    }

    public static FuelHoseCapture.SampledRoute[] Count(PartTree tree, PartSupport support, CaptureStats stats, CancellationToken token)
    {
        var sources = new HashSet<(MeshReference Mesh, int Primitive)>();
        var materials = new HashSet<string>(StringComparer.Ordinal);
        void CountModels(bool interior)
        {
            VisitModels(tree, interior, (part, mesh, material, _) =>
            {
                token.ThrowIfCancellationRequested();
                if (mesh?.HostPrimitives == null) return;
                int index = 0;
                foreach (var primitive in mesh.HostPrimitives)
                {
                    int vertices = primitive.GetVertexSpan<float3>(MeshAttribute.Position).Length;
                    if (vertices == 0) { index++; continue; }
                    int triangles = (primitive.IndexBuffer == null ? 0 : primitive.IndexBuffer.ToSpan<int>().Length) / 3;
                    stats.Add(part.Template.Id, part.InstanceId, interior, vertices, triangles, sources.Add((mesh, index++)));
                }
                if (support.Surface(part.Template.Id) == "structure") return;
                CountMaterial(material?.NormalReference?.Value, interior);
                CountMaterial(material?.DiffuseReference?.Value, interior);
            });
        }

        void CountMaterial(TextureReference? reference, bool interior)
        {
            if (reference?.TextureAsset is not { HasHostData: true } asset ||
                !RawTexture.Supported((int)asset.SampleFormat) || !materials.Add(reference.Id))
                return;
            stats.Material(interior, asset.Width, asset.Height, asset.Width is >= 1 and <= 8192 && asset.Height is >= 1 and <= 8192 ? asset.FaceData(0).Length : 0, (int)asset.SampleFormat >= 131);
        }

        CountModels(false);
        var hoses = new List<FuelHoseCapture.SampledRoute>();
        foreach (var route in FuelHoseCapture.Sample(tree, token))
        {
            int triangles = (route.Samples.Length - 1) * FuelHoseGeometry.RadialSegments * 2;
            stats.Add(route.First.Template.Id, route.First.InstanceId, false, route.Samples.Length * FuelHoseGeometry.RadialSegments, triangles, true, procedural: true);
            hoses.Add(route);
        }
        CountModels(true);
        stats.Complete = true;
        return hoses.ToArray();
    }
}
