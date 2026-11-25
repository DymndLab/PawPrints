using Blueprint.Rendering;
using KSA;
using System.Runtime.CompilerServices;
using Brutal.Numerics;
using Vec = System.Numerics.Vector3;

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
        var mounts = new Dictionary<uint, ExplodedMount>();
        var symmetry = new Dictionary<uint, uint[]>();
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
            if (part.SymmetryLink is { } linked)
            {
                foreach (var layer in linked.Symmetries)
                {
                    var members = layer.Parts.Distinct().ToArray();
                    if (members.Any(p => ReferenceEquals(p, part)))
                    {
                        if (members.Length >= 2)
                            symmetry[part.InstanceId] = members.Select(p => p.InstanceId).ToArray();
                        break;
                    }
                }
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
                static bool Axial(Part.Connection.IConnector endpoint) => endpoint is Part.Connector connector &&
                    (connector.Flags & (Part.Connector.Flag.Internal | Part.Connector.Flag.FromSurface |
                        Part.Connector.Flag.ToSurface | Part.Connector.Flag.Edge)) == 0;
                static System.Numerics.Vector3? Position(Part.Connection.IConnector endpoint)
                {
                    if (endpoint is not Part.Connector connector) return null;
                    var p = connector.PositionVehicleAsmb;
                    return new((float)p.Y, (float)p.Z, (float)p.X);
                }
                RecordMount(a, b);
                RecordMount(b, a);
                var pa = Position(a);
                var pb = Position(b);
                links.Add(new(SourceLinkKind.Structural, JointKey.Between(Endpoint(a), Endpoint(b)),
                    Internal(a) || Internal(b), pa.HasValue && pb.HasValue ? (pa.Value + pb.Value) * .5f : pa ?? pb,
                    Axial(a) && Axial(b)));
            }
        }
        foreach (var link in fuel)
            links.Add(new(SourceLinkKind.Fuel, JointKey.Between(new(link.PartA.InstanceId, ""), new(link.PartB.InstanceId, ""))));
        return new(binding, nodes, links) { Mounts = mounts, RadialSymmetry = symmetry };

        static Vec World(double3 point) => new((float)point.Y, (float)point.Z, (float)point.X);

        void RecordMount(Part.Connection.IConnector endpoint, Part.Connection.IConnector other)
        {
            if (endpoint is not Part.Connector connector) return;
            bool surface = (connector.Flags & Part.Connector.Flag.ToSurface) != 0;
            bool interior = (connector.Flags & Part.Connector.Flag.Internal) != 0;
            if (!surface && !interior) return;
            var mounted = surface ? connector.Parent : other.ConnectionPart;
            var host = surface ? other.ConnectionPart : connector.Parent;
            if (mounted.TreeParent != host) return;
            var normal = World(double3.UnitX.Transform(connector.Asmb2VehicleAsmb)) * (surface ? -1 : 1);
            var longitudinal = World(double3.UnitX.Transform(host.Asmb2VehicleAsmb));
            if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < .5f) return;
            normal = Vec.Normalize(normal);
            // Internal axial joints include the main engine mount.
            if (interior && Math.Abs(Vec.Dot(longitudinal, normal)) > .5f) return;
            var slide = longitudinal - normal * Vec.Dot(longitudinal, normal);
            if (slide.LengthSquared() > 1e-6f) slide = Vec.Normalize(slide);
            else slide = Vec.Zero;
            mounts.TryAdd(mounted.InstanceId, new(host.InstanceId, World(connector.PositionVehicleAsmb), normal, slide, interior));
        }
    }
}
