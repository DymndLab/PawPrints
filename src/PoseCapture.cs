using Blueprint.Rendering;
using Brutal.Numerics;
using KSA;
using M = System.Numerics.Matrix4x4;
using V = System.Numerics.Vector3;
namespace Blueprint;

static class PoseCapture
{


    static M Matrix(double4x4 native)
    {
        V Point(double x, double y, double z)
        {
            var p = new double3(x, y, z).Transform(native);
            return new((float)p.Y, (float)p.Z, (float)p.X);
        }
        var o = Point(0, 0, 0);
        var x = Point(0, 1, 0) - o;
        var y = Point(0, 0, 1) - o;
        var z = Point(1, 0, 0) - o;
        return new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, o.X, o.Y, o.Z, 1);
    }


    public static PoseControl[] Take(PartTree tree, AssemblyCapture assembly, PartSupport support)
    {
        var result = new List<PoseControl>();
        Dictionary<uint, AssemblyNode>? nodes = null;
        ILookup<uint, AssemblyNode>? children = null;
        int trackCount = 0;
        foreach (var module in tree.Modules.Get<KeyframeAnimationModule>())
        {
            if (module.Shared.Duration <= 0)
                continue;
            var tracks = new List<PoseTrack>();
            var parentWorld = module.Parent.ComputeMatrixAsmb2VehicleAsmb();
            foreach (var child in module.Parent.SubParts)
            {
                if (!module.Shared.PartLookup.TryGetValue(child.Id, out var animation))
                    continue;
                if (++trackCount > 2048)
                    throw new InvalidOperationException("Craft animations exceed the pose capture budget.");
                if (!M.Invert(Matrix(child.ComputeMatrixAsmb2VehicleAsmb()), out var inverse))
                    throw new InvalidDataException("Part animation has a singular transform.");
                var deltas = new M[101];
                for (int i = 0; i <= 100; i++)
                {
                    var local = module.Shared.EvaluateWorldMatrix(animation, module.Shared.Duration * i / 100d);
                    float4x4.Decompose(local, out var scale, out var rotation, out var translation);
                    var nativeLocal = double4x4.CreateScale(double3.Unpack(in scale)) * double4x4.CreateFromQuaternion(doubleQuat.Unpack(in rotation)) * double4x4.CreateTranslation(double3.Unpack(in translation));
                    deltas[i] = inverse * Matrix(nativeLocal * parentWorld);
                    if (!float.IsFinite(deltas[i].GetDeterminant()))
                        throw new InvalidDataException("Invalid animation transform.");
                }
                nodes ??= assembly.Nodes.ToDictionary(n => n.Id);
                children ??= assembly.Nodes.Where(n => n.Parent.HasValue).ToLookup(n => n.Parent!.Value);
                tracks.Add(new(child.InstanceId, AssemblyGraph.Ancestors(nodes, child.InstanceId).Length,
                    AssemblyGraph.Subtree(children, child.InstanceId).ToArray(), deltas));
            }
            if (tracks.Count == 0 || !tracks.Any(t => t.Deltas.Any(d => d != t.Deltas[0])))
                continue;
            tree.States.TryGetTypeList(out ModuleStateful<KeyframeAnimationModule, KeyframeAnimationModule.State, EmptyStruct, EmptyStruct>.StateList states);
            int percent = (int)MathF.Round(states.GetModuleAndState(module).State.TimeCurrent / module.Shared.Duration * 100);
            var symmetry = new HashSet<uint> { module.Parent.InstanceId };
            var links = module.Parent.SymmetryLink ?? module.Parent.StructuralPart.SymmetryLink;
            if (links is { } linked)
                foreach (var group in linked.Symmetries)
                    foreach (var member in group.Parts)
                        foreach (var match in member.SubtreeModules.Get<KeyframeAnimationModule>())
                            if (match.TemplateId == module.TemplateId)
                                symmetry.Add(match.Parent.InstanceId);
            result.Add(new(module.Parent.InstanceId, module.TemplateId, PartNames.Display(module.Parent.Template.Id, module.Parent.Template.DisplayName, support), module.ShowDeployRetract, Math.Clamp(percent, 0, 100), tracks.ToArray(), symmetry.ToArray()));
        }
        return result.ToArray();
    }
}
