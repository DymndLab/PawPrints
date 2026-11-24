using System.Collections.Immutable;

namespace Blueprint.Rendering;

enum SourceLinkKind { Structural, Ownership, Fuel }
enum BindingStatus { Exact, Missing, Ambiguous, RebindRequired }
readonly record struct SourceBinding(Guid Session, long Revision);
sealed record SourcePart(uint Id, string Template, uint? Owner, bool Internal);
readonly record struct JointEndpoint(uint Part, string Connector);
readonly record struct JointKey(JointEndpoint A, JointEndpoint B)
{
    public static JointKey Between(JointEndpoint a, JointEndpoint b) =>
        a.Part < b.Part || a.Part == b.Part && string.CompareOrdinal(a.Connector, b.Connector) <= 0
            ? new(a, b) : new(b, a);
}
sealed record SourceLink(SourceLinkKind Kind, JointKey Key, bool Internal = false, System.Numerics.Vector3? Position = null, bool EndJoint = false);

// IDs are authoritative only within Session. Native serialized LocalInstanceId
// values are traversal numbers, not restart-safe identity. Never match by name.
sealed class SourceTopology
{
    public SourceBinding Binding { get; }
    public ImmutableArray<SourcePart> Parts { get; }
    public ImmutableArray<SourceLink> Links { get; }
    readonly Dictionary<uint, SourcePart> parts;
    readonly Dictionary<JointKey, int> joints;

    public SourceTopology(SourceBinding binding, IEnumerable<SourcePart> sourceParts, IEnumerable<SourceLink> links)
    {
        if (binding.Session == Guid.Empty || binding.Revision < 1)
            throw new ArgumentException("Source identity requires a session and positive revision.");
        Binding = binding;
        Parts = sourceParts.ToImmutableArray();
        Links = links.ToImmutableArray();
        parts = new();
        foreach (var part in Parts)
            if (string.IsNullOrWhiteSpace(part.Template) || !parts.TryAdd(part.Id, part))
                throw new InvalidDataException("Source contains missing templates or duplicate part identities.");
        joints = new();
        foreach (var link in Links)
        {
            if (!parts.ContainsKey(link.Key.A.Part) || !parts.ContainsKey(link.Key.B.Part))
                throw new InvalidDataException("Source connection refers to a missing part.");
            if (link.Kind == SourceLinkKind.Structural)
            {
                var key = JointKey.Between(link.Key.A, link.Key.B);
                joints[key] = checked(joints.GetValueOrDefault(key) + 1);
            }
        }
    }

    public BindingStatus ResolvePart(SourceBinding saved, uint id, string template)
    {
        if (saved.Session != Binding.Session) return BindingStatus.RebindRequired;
        return parts.TryGetValue(id, out var part) && part.Template == template ? BindingStatus.Exact : BindingStatus.Missing;
    }

    public BindingStatus ResolveJoint(SourceBinding saved, JointKey key)
    {
        if (saved.Session != Binding.Session) return BindingStatus.RebindRequired;
        return joints.GetValueOrDefault(JointKey.Between(key.A, key.B)) switch
        {
            0 => BindingStatus.Missing,
            1 => BindingStatus.Exact,
            _ => BindingStatus.Ambiguous
        };
    }
}
