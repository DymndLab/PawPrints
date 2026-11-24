using Blueprint.Rendering;
using KSA;
using System.Runtime.CompilerServices;

namespace Blueprint;

static class CraftIdentityCapture
{
    sealed class Session
    {
        public readonly Guid Id = Guid.NewGuid();
        public long Revision;
    }
    static readonly ConditionalWeakTable<Part, Session> sessions = new();

    // Native owner-thread only. No Part, Connector, FuelLink or tree escapes.
    public static SourceTopology Take(PartTree tree)
    {
        var parts = tree.Parts.ToArray();
        return TakeParts(parts, tree.FuelLinks.Links.ToArray(), BindingFor(parts));
    }

    internal static SourceBinding BindingFor(Part[] parts)
    {
        // Editing can replace the PartTree while retaining its Part objects.
        // Keep that session; merging previously independent captures is explicit
        // ambiguity, and a reload with new Part objects always gets a new session.
        Session? session = null;
        bool merged = false;
        foreach (var part in parts)
            if (sessions.TryGetValue(part, out var known))
            {
                if (session != null && !ReferenceEquals(session, known)) merged = true;
                session ??= known;
            }
        if (merged || session == null) session = new();
        foreach (var part in parts)
        {
            if (sessions.TryGetValue(part, out var known) && ReferenceEquals(known, session)) continue;
            sessions.Remove(part);
            sessions.Add(part, session);
        }
        return new(session.Id, checked(++session.Revision));
    }

    internal static SourceTopology TakeParts(Part[] roots, FuelLink[] fuel, SourceBinding binding)
    {
        var seen = new HashSet<Part>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<Part>(roots);
        var nodes = new List<SourcePart>();
        var links = new List<SourceLink>();
        var connections = new HashSet<Part.Connection>(ReferenceEqualityComparer.Instance);
        while (pending.TryPop(out var part))
        {
            if (!seen.Add(part)) continue;
            bool interior = part.IsAttachedInternal;
            var owner = part.PartParent ?? (interior ? part.TreeParent : null);
            nodes.Add(new(part.InstanceId, part.Template.Id, owner?.InstanceId, interior));
            if (owner != null)
            {
                pending.Push(owner);
                links.Add(new(SourceLinkKind.Ownership, JointKey.Between(
                    new(owner.InstanceId, ""), new(part.InstanceId, "")), interior));
            }
            foreach (var child in part.SubParts) pending.Push(child);
            foreach (var connection in part.Connections)
            {
                if (!connections.Add(connection)) continue;
                var a = connection.Connectors[0];
                var b = connection.Connectors[1];
                pending.Push(a.ConnectionPart);
                pending.Push(b.ConnectionPart);
                static JointEndpoint Endpoint(Part.Connection.IConnector endpoint) => new(
                    endpoint.ConnectionPart.InstanceId, endpoint is Part.Connector connector ? connector.Id : "");
                static bool Internal(Part.Connection.IConnector endpoint) => endpoint is Part.Connector connector &&
                    (connector.Flags & Part.Connector.Flag.Internal) != 0;
                links.Add(new(SourceLinkKind.Structural, JointKey.Between(Endpoint(a), Endpoint(b)), Internal(a) || Internal(b)));
            }
        }
        foreach (var link in fuel)
            links.Add(new(SourceLinkKind.Fuel, JointKey.Between(new(link.PartA.InstanceId, ""), new(link.PartB.InstanceId, ""))));
        return new(binding, nodes, links);
    }
}
