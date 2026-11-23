using Blueprint.Rendering;
using KSA;
using System.Numerics;

namespace Blueprint;

static class FuelHoseCapture
{
    // Editor-thread capture only. Compute a private route so the live route's
    // waypoint list/sample cache is never rebuilt or shared with render workers.
    public static (Part Owner, Rendering.Mesh Mesh)[] Take(PartTree tree, int vertexBudget)
    {
        var result = new List<(Part, Rendering.Mesh)>();
        foreach (var link in tree.FuelLinks.Links)
        {
            var route = new FuelLinkRoute();
            route.Compute(link);
            var native = route.EnsureSamples(0);
            var samples = new HoseSample[native.Length];
            for (int i = 0; i < native.Length; i++)
            {
                var p = native[i].Position;
                var q = native[i].Rotation;
                samples[i] = new(new Vector3(p.X, p.Y, p.Z), new Quaternion(q.X, q.Y, q.Z, q.W));
            }
            // Disabled flow still has a physical hose, just as in the game.
            var mesh = FuelHoseGeometry.Build(samples, link.PartA.InstanceId, link.PartB.InstanceId, vertexBudget);
            if (mesh == null) continue;
            var anchors = new HashSet<uint>(mesh.ConnectedParts);
            foreach (var endpoint in new[] { link.PartA, link.PartB })
                if (FuelPort.GetForPart(endpoint) is { } port && port.TryGetConnectorTip(out var connector, out _))
                    anchors.Add(connector.InstanceId);
            mesh = mesh with { ConnectedParts = anchors.ToArray() };
            vertexBudget -= mesh.Vertices.Length;
            result.Add((link.PartA, mesh));
        }
        return result.ToArray();
    }
}
