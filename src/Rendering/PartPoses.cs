using System.Numerics;
namespace Blueprint.Rendering;

sealed record PoseTrack(uint Node, int Depth, uint[] Descendants, Matrix4x4[] Deltas);
sealed record PoseControl(uint Part, string Key, string Name, bool DeployRetract, int CapturedPercent, PoseTrack[] Tracks, uint[]? SymmetryParts = null);
// View -1 is shared by all main views and details; a view ID overrides it.
sealed record PartPose(uint Part, string Key, int View, int Percent);
sealed record PoseSource(AssemblyCapture Assembly, PoseControl[] Controls, int ExteriorCount, LabelPart[] Parts, IReadOnlyDictionary<string, Texture> Maps, PerformanceData? Performance = null);

static class PartPoses
{

    public static int? Value(PartPose[] poses, PoseControl control, int view) =>
        (poses.LastOrDefault(p => p.Part == control.Part && p.Key == control.Key && p.View == view)
        ?? poses.LastOrDefault(p => p.Part == control.Part && p.Key == control.Key && p.View == -1))?.Percent;

    public static PartPose[] Set(PartPose[] poses, PoseControl control, int view, int? percent)
    {
        var kept = poses.Where(p => p.Part != control.Part || p.Key != control.Key || (view != -1 && p.View != view)).ToArray();
        return percent is { } value ? [.. kept, new(control.Part, control.Key, view, Math.Clamp(value, -1, 100))] : kept;
    }

    public static PoseControl[] Linked(PoseSource source, PoseControl control, bool symmetry) => symmetry
        ? source.Controls.Where(c => c.Key == control.Key && (c.Part == control.Part || (control.SymmetryParts ?? []).Contains(c.Part))).ToArray() : [control];

    public static PartPose[] SetLinked(PoseSource source, PartPose[] poses, PoseControl control, int view, int? percent, bool symmetry)
    {
        foreach (var c in Linked(source, control, symmetry))
            poses = Set(poses, c, view, percent);
        return poses;
    }

    public static void Validate(PoseSource source, PartPose[] poses, IEnumerable<int> views)
    {
        var available = views.ToHashSet();
        foreach (var p in poses)
            if (p.Percent is < -1 or > 100 || p.View != -1 && !available.Contains(p.View) || !source.Controls.Any(c => c.Part == p.Part && c.Key == p.Key))
                throw new InvalidOperationException("A posed part or view is no longer available. Restore it or reset its pose before generating. The previous print is unchanged.");
    }

    public static AssemblyCapture Apply(PoseSource source, PartPose[] poses, int view)
    {
        var transforms = new Dictionary<uint, Matrix4x4>();
        // Children first: each captured world-space delta is then carried by
        // the parent's delta. No live KSA transforms or states are written.
        foreach (var item in source.Controls.Select(c => (Control: c, Percent: Value(poses, c, view))).Where(x => x.Percent is >= 0)
            .SelectMany(x => x.Control.Tracks.Select(t => (Track: t, Percent: x.Percent!.Value))).OrderByDescending(x => x.Track.Depth))
            foreach (uint node in item.Track.Descendants)
                transforms[node] = (transforms.TryGetValue(node, out var previous) ? previous : Matrix4x4.Identity) * item.Track.Deltas[item.Percent];
        if (transforms.Count == 0)
            return source.Assembly;
        var a = source.Assembly;
        foreach (var mesh in a.Geometry.Meshes.Where(m => m.ConnectedParts.Length > 1))
        {
            var movements = mesh.ConnectedParts.Select(id => transforms.GetValueOrDefault(id, Matrix4x4.Identity)).ToArray();
            if (movements.Skip(1).Any(m => m != movements[0]))
                throw new InvalidOperationException("A fuel hose connects parts with different print poses. Reset those poses, or pose the craft in the editor and generate again. The previous print is unchanged.");
        }
        var meshes = a.Geometry.Meshes.Select((m, i) =>
        {
            if (!transforms.TryGetValue(a.MeshNodes[i], out var matrix))
                return m;
            return m with
            {
                Vertices = m.Vertices.Select(v => { var p = Vector3.Transform(ExplodedGeometry.World(v), matrix); return new double[] { p.Z, p.X, p.Y }; }).ToArray()
            };
        }).ToArray();
        var nodes = a.Nodes.Select(n =>
        {
            if (n.Pose is not { } p || !transforms.TryGetValue(n.Id, out var matrix))
                return n;
            Vector3 Axis(Vector3 v) => Vector3.Normalize(Vector3.TransformNormal(v, matrix));
            return n with
            {
                Pose = new(Vector3.Transform(p.Origin, matrix), Axis(p.X), Axis(p.Y), Axis(p.Z))
            };
        }).ToArray();
        return a with
        {
            Geometry = new(meshes),
            Nodes = nodes
        };
    }

    public static Capture Exterior(PoseSource source, AssemblyCapture assembly) => new(assembly.Geometry.Meshes.Take(source.ExteriorCount).ToArray());
}

static class PosePainting
{

    public static Sheet RenderMain(PoseSource source, string title, PrintOptions options, string[] warnings, PartPose[] poses, CancellationToken token, bool composeAnnotations = true, Sheet? previous = null)
    {
        var unposed = PartPoses.Exterior(source, source.Assembly);
        Dictionary<int, Capture>? captures = null;
        Capture fit = unposed;
        if (poses.Length != 0)
        {
            var ids = options.Drawing?.Views.Select(v => v.Id) ?? SheetLayout.Plan(unposed, options).Select(v => v.Id);
            captures = ids.ToDictionary(id => id, id => PartPoses.Exterior(source, PartPoses.Apply(source, poses, id)));
            // Fit to all selected poses; render only the assigned pose in each view.
            if (captures.Count != 0)
                fit = new(captures.Values.SelectMany(c => c.Meshes).ToArray());
        }
        // Scope reuse to the accepted snapshot. New captures and pose changes
        // go through the ordinary render path.
        var reusable = options.FreeLayout && previous is { Options.FreeLayout: true, Scene: { } old } &&
            ReferenceEquals(previous.PoseSource, source) && old.Poses.SequenceEqual(poses) ? previous : null;
        var result = SheetLayout.Render(unposed, source.Maps, title, source.Parts, options, warnings, token, _ => { }, captures, fit, composeAnnotations, reusable);
        result.Scene!.MeshNodes = source.Assembly.MeshNodes;
        return result with
        {
            PoseSource = source,
            DetailSource = new(source.Assembly, source.Maps),
            Scene = result.Scene.WithPoses(poses).WithPerformance(source.Performance)
        };
    }

    public static Sheet Rebuild(Sheet previous, PartPose[] poses, CancellationToken token, bool history = false)
    {
        var source = previous.PoseSource ?? throw new InvalidOperationException("Generate PawPrint to capture part animations first.");
        PartPoses.Validate(source, poses, (previous.Scene!.Drawing?.Views.Select(v => v.Id) ?? previous.Scene.Views.Where(v => v.Id < 1000).Select(v => v.Id)).Concat(previous.Details.Select(d => d.ViewId)));
        var main = RenderMain(source, previous.Title, previous.Options with
        {
            Drawing = previous.Scene.Drawing
        }, previous.Warnings, poses, token, composeAnnotations: false);
        var result = CompositionRegeneration.Rebuild(main, previous with
        {
            Scene = previous.Scene.WithoutDetailCache()
        }, token, history);
        return result with
        {
            RegeneratedBase = null,
            PoseMain = main,
            Notice = "Print updated. Ctrl+Z undoes the change; the craft in KSA is unchanged."
        };
    }
}
