using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Blueprint.Rendering;

sealed record ProjectPartAddress(uint Part, string Template, string Key);
sealed record ProjectLabelAddress(int LabelPart, uint Part, bool DetailOnly);
sealed record ProjectVehicleIdentity(string Fingerprint, ProjectPartAddress[] Parts, ProjectLabelAddress[] Labels);

// Restart binding uses the assembly's structure and transforms. Native runtime
// and save traversal IDs are deliberately excluded from the fingerprint.
static class ProjectCraftIdentity
{
    sealed record AssemblyAddress(string Fingerprint, Dictionary<uint, string> Keys);

    static AssemblyAddress Address(AssemblyCapture assembly)
    {
        var topology = assembly.Topology ?? throw new InvalidDataException("Vehicle attachment identity is unavailable.");
        var nodes = assembly.Nodes.ToDictionary(n => n.Id);
        var keys = new Dictionary<uint, string>(nodes.Count);
        var chain = new Stack<AssemblyNode>();
        foreach (var node in assembly.Nodes)
        {
            var cursor = node;
            string parentKey = "";
            while (!keys.TryGetValue(cursor.Id, out parentKey!))
            {
                chain.Push(cursor);
                if (chain.Count > nodes.Count)
                    throw new InvalidDataException("Cyclic vehicle assembly.");
                if (cursor.Parent == null)
                {
                    parentKey = "";
                    break;
                }
                if (!nodes.TryGetValue(cursor.Parent.Value, out cursor))
                    throw new InvalidDataException("Vehicle assembly is missing a parent.");
            }
            while (chain.TryPop(out var part))
            {
                string parent = parentKey;
                parentKey = Hash(writer =>
                {
                    writer.Write(parent);
                    writer.Write(part.TemplateId);
                    writer.Write(part.Subpart);
                    // Quantize at 0.1 mm and 0.0001 in axis/scale components.
                    // Coincident addresses remain ambiguous when referenced.
                    writer.Write(part.Pose != null);
                    if (part.Pose is { } pose)
                    {
                        WriteVector(writer, pose.Origin);
                        WriteVector(writer, pose.X);
                        WriteVector(writer, pose.Y);
                        WriteVector(writer, pose.Z);
                    }
                    WriteVector(writer, part.Scale);
                });
                keys.Add(part.Id, parentKey);
            }
        }

        string Endpoint(JointEndpoint endpoint) => keys.TryGetValue(endpoint.Part, out var key)
            ? key + endpoint.Connector : throw new InvalidDataException("Vehicle attachment has no part address.");
        var joints = topology.Links.Select(link =>
        {
            string a = Endpoint(link.Key.A), b = Endpoint(link.Key.B);
            if (string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
            return (link.Kind, link.Internal, link.EndJoint, A: a, B: b);
        }).OrderBy(j => j.Kind).ThenBy(j => j.Internal).ThenBy(j => j.EndJoint)
            .ThenBy(j => j.A, StringComparer.Ordinal).ThenBy(j => j.B, StringComparer.Ordinal);
        var fingerprint = Hash(writer =>
        {
            writer.Write("pawprints-assembly-1");
            writer.Write(keys.Count);
            foreach (string key in keys.Values.Order(StringComparer.Ordinal)) writer.Write(key);
            writer.Write(topology.Links.Length);
            foreach (var joint in joints)
            {
                writer.Write((int)joint.Kind);
                writer.Write(joint.Internal);
                writer.Write(joint.EndJoint);
                writer.Write(joint.A);
                writer.Write(joint.B);
            }
        });
        return new(fingerprint, keys);
    }

    static string Hash(Action<BinaryWriter> write)
    {
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true);
        write(writer);
        return Convert.ToHexString(SHA256.HashData(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length))));
    }

    static void WriteVector(BinaryWriter writer, Vector3 value)
    {
        static long Quantize(float f) => float.IsFinite(f)
            ? checked((long)Math.Round((double)f * 10000))
            : throw new InvalidDataException("Vehicle contains a non-finite transform.");
        writer.Write(Quantize(value.X));
        writer.Write(Quantize(value.Y));
        writer.Write(Quantize(value.Z));
    }

    public static ProjectVehicleIdentity Capture(AssemblyCapture assembly, RenderScene scene, DrawingContent content)
    {
        var addresses = Address(assembly);
        var byId = assembly.Nodes.ToDictionary(n => n.Id);
        var labelParts = scene.Parts.ToDictionary(p => p.Id);
        var labels = content.Labels.Select(l => l.PartId).Distinct().Select(id =>
        {
            var part = labelParts[id];
            return new ProjectLabelAddress(id, part.NodeId ?? throw new InvalidDataException("A label has no vehicle part identity."), part.DetailOnly);
        }).ToArray();
        var parts = References(content, labels).Distinct().Order().Select(id =>
            new ProjectPartAddress(id, byId[id].TemplateId, addresses.Keys[id])).ToArray();
        return new(addresses.Fingerprint, parts, labels);
    }

    internal static IEnumerable<uint> References(DrawingContent content, ProjectLabelAddress[] labels)
    {
        foreach (var label in labels) yield return label.Part;
        foreach (var pose in content.Poses) yield return pose.Part;
        foreach (var spec in content.Specs)
        {
            if (spec.EnginePart is { } part) yield return part;
            foreach (string engine in spec.SelectedEngines ?? []) yield return EnginePart(engine);
        }
        foreach (var detail in content.Details)
        {
            yield return detail.Scope;
            if (detail.Seed is { } seed) yield return seed;
            foreach (uint node in detail.Expanded ?? []) yield return node;
            foreach (var group in detail.Groups ?? []) yield return group.Node;
            if (detail.Selection is not { } scope) continue;
            if (scope.Root is { } root) yield return root;
            foreach (var cut in scope.Cuts) { yield return cut.A.Part; yield return cut.B.Part; }
            foreach (uint id in scope.Removed) yield return id;
            foreach (uint id in scope.VisibleParts ?? []) yield return id;
            foreach (uint id in scope.RemovedBranches ?? []) yield return id;
        }
    }

    static uint EnginePart(string engine)
    {
        int slash = engine?.IndexOf('/') ?? -1;
        return slash > 0 && uint.TryParse(engine.AsSpan(0, slash), NumberStyles.None, CultureInfo.InvariantCulture, out uint part)
            ? part : throw new InvalidDataException("Invalid saved engine identity.");
    }

    public static (DrawingContent Content, ProjectLabelAddress[] Labels) Rebind(DrawingContent content,
        ProjectVehicleIdentity identity, SourceBinding saved, PoseSource source)
    {
        var assembly = source.Assembly;
        var topology = assembly.Topology ?? throw new InvalidDataException("Vehicle attachment identity is unavailable.");
        var mapped = new Dictionary<uint, uint>();
        if (saved.Session == topology.Binding.Session)
        {
            var current = assembly.Nodes.ToDictionary(n => n.Id);
            foreach (var part in identity.Parts)
            {
                if (!current.TryGetValue(part.Part, out var node) || node.TemplateId != part.Template)
                    throw new InvalidOperationException("The edited vehicle is missing a part referenced by this drawing.");
                mapped.Add(part.Part, node.Id);
            }
        }
        else
        {
            var addresses = Address(assembly);
            if (addresses.Fingerprint != identity.Fingerprint)
                throw new InvalidOperationException("The loaded vehicle differs from the saved assembly. Load the original vehicle to open this drawing.");
            var byAddress = addresses.Keys.ToLookup(p => p.Value, p => p.Key, StringComparer.Ordinal);
            var templates = assembly.Nodes.ToDictionary(n => n.Id, n => n.TemplateId);
            var claimed = new HashSet<uint>();
            foreach (var part in identity.Parts)
            {
                var matches = byAddress[part.Key];
                if (!matches.Any() || matches.Skip(1).Any())
                    throw new InvalidOperationException("A saved part has no unique match in the loaded vehicle.");
                uint match = matches.Single();
                if (!claimed.Add(match) || templates[match] != part.Template)
                    throw new InvalidOperationException("A saved part has no unique match in the loaded vehicle.");
                mapped.Add(part.Part, match);
            }
        }
        uint Map(uint id) => mapped.TryGetValue(id, out var next) ? next
            : throw new InvalidDataException("A saved part reference is incomplete.");
        uint? MapOptional(uint? id) => id is { } value ? Map(value) : null;
        JointKey MapCut(JointKey key)
        {
            var joint = JointKey.Between(key.A with { Part = Map(key.A.Part) }, key.B with { Part = Map(key.B.Part) });
            if (topology.ResolveJoint(topology.Binding, joint) != BindingStatus.Exact)
                throw new InvalidOperationException("An attachment cut no longer has a unique match on the loaded vehicle.");
            return joint;
        }
        string MapEngine(string id) => Map(EnginePart(id)).ToString(CultureInfo.InvariantCulture) + id[id.IndexOf('/')..];
        var details = content.Details.Select(d => d with
        {
            Scope = Map(d.Scope), Seed = MapOptional(d.Seed),
            Expanded = d.Expanded?.Select(Map).ToArray(),
            Groups = d.Groups?.Select(g => g with { Node = Map(g.Node) }).ToArray(),
            Selection = d.Selection is { } s ? s with
            {
                Root = MapOptional(s.Root), Cuts = [.. s.Cuts.Select(MapCut)], Removed = [.. s.Removed.Select(Map)],
                VisibleParts = s.VisibleParts is { } visible ? [.. visible.Select(Map)] : null,
                RemovedBranches = s.RemovedBranches is { } branches ? [.. branches.Select(Map)] : null
            } : null
        }).ToArray();
        var specs = content.Specs.Select(s => s with
        {
            EnginePart = MapOptional(s.EnginePart), SelectedEngines = s.SelectedEngines?.Select(MapEngine).ToArray()
        }).ToArray();
        var engines = source.Performance?.Engines.Select(e => e.Id).ToHashSet() ?? [];
        if (specs.Any(s => s.SelectedEngines?.Any(id => !engines.Contains(id)) == true))
            throw new InvalidOperationException("The loaded vehicle is missing a referenced engine.");
        return (content with { Details = details, Specs = specs, Poses = content.Poses.Select(p => p with { Part = Map(p.Part) }).ToArray() },
            identity.Labels.Select(l => l with { Part = Map(l.Part) }).ToArray());
    }
}
