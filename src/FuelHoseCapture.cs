using Blueprint.Rendering;
using KSA;
using System.Numerics;

namespace Blueprint;

static class FuelHoseCapture
{
    internal sealed record SampledRoute(Part First, Part Second, HoseSample[] Samples);

    // Editor-thread capture only. Compute a private route so the live route's
    // waypoint list/sample cache is never rebuilt or shared with render workers.
    public static IEnumerable<SampledRoute> Sample(PartTree tree, CancellationToken token)
    {
        // Snapshot link references here because the native list uses a span.
        foreach (var link in tree.FuelLinks.Links.ToArray())
        {
            token.ThrowIfCancellationRequested();
            var samples = CopySamples(link);
            if (FuelHoseGeometry.Measure(samples) != 0)
                yield return new(link.PartA, link.PartB, samples);
        }
    }

    static HoseSample[] CopySamples(FuelLink link)
    {
        var route = new FuelLinkRoute();
        route.Compute(link);
        var native = route.EnsureSamples(0);
        if (native.Length > FuelHoseGeometry.MaximumSamples)
            throw new InvalidOperationException($"Fuel hose requires {native.Length:N0} route samples (limit: {FuelHoseGeometry.MaximumSamples:N0}).");
        var samples = new HoseSample[native.Length];
        for (int i = 0; i < native.Length; i++)
        {
            var p = native[i].Position;
            var q = native[i].Rotation;
            samples[i] = new(new Vector3(p.X, p.Y, p.Z), new Quaternion(q.X, q.Y, q.Z, q.W));
        }
        return samples;
    }

    public static Rendering.Mesh Build(SampledRoute route, int vertexBudget = int.MaxValue)
    {
        // Disabled flow still has a physical hose, just as in the game.
        var mesh = FuelHoseGeometry.Build(route.Samples, route.First.InstanceId, route.Second.InstanceId, vertexBudget)!;
        var anchors = new HashSet<uint>(mesh.ConnectedParts);
        foreach (var endpoint in new[] { route.First, route.Second })
            if (FuelPort.GetForPart(endpoint) is { } port && port.TryGetConnectorTip(out var connector, out _))
                anchors.Add(connector.InstanceId);
        return mesh with { ConnectedParts = anchors.ToArray() };
    }

    public static (Part Owner, Rendering.Mesh Mesh)[] Take(PartTree tree, int vertexBudget)
    {
        var result = new List<(Part, Rendering.Mesh)>();
        foreach (var route in Sample(tree, default))
        {
            var mesh = Build(route, vertexBudget);
            vertexBudget -= mesh.VertexCount;
            result.Add((route.First, mesh));
        }
        return result.ToArray();
    }
}
