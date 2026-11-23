using System.Numerics;

namespace Blueprint.Rendering;

readonly record struct HoseSample(Vector3 Position, Quaternion Rotation);

static class FuelHoseGeometry
{
    // KSA 5901 FuelLinkList submits this tube size to SplineRenderer. Samples
    // are captured from its native route; no KSA assets or shader code are bundled.
    public const float Radius = .05f;
    public const int RadialSegments = 16;
    public const int MaximumSamples = 256;

    public static Mesh? Build(HoseSample[] samples, uint endpointA, uint endpointB, int vertexBudget)
    {
        if (samples.Length < 2)
            return null;
        if (samples.Length > MaximumSamples || checked(samples.Length * RadialSegments) > vertexBudget)
            throw new InvalidOperationException("Fuel hoses exceed the craft export geometry budget.");
        float length = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var p = samples[i].Position;
            var q = samples[i].Rotation;
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)
                || !float.IsFinite(q.LengthSquared()) || q.LengthSquared() < 1e-12f)
                throw new InvalidDataException("Invalid fuel hose route geometry.");
            if (i > 0) length += Vector3.Distance(samples[i - 1].Position, p);
        }
        if (!float.IsFinite(length))
            throw new InvalidDataException("Invalid fuel hose route length.");
        if (length < .0001f)
            return null;
        int count = samples.Length * RadialSegments;
        var positions = new double[count][];
        var normals = new double[count][];
        var uv = new double[count][];
        var indices = new int[(samples.Length - 1) * RadialSegments * 6];
        for (int ring = 0; ring < samples.Length; ring++)
        {
            var sample = samples[ring];
            var rotation = Quaternion.Normalize(sample.Rotation);
            for (int radial = 0; radial < RadialSegments; radial++)
            {
                float angle = 2 * MathF.PI * radial / RadialSegments;
                var normal = Vector3.Transform(new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0), rotation);
                var p = sample.Position + normal * Radius;
                int index = ring * RadialSegments + radial;
                positions[index] = [p.X, p.Y, p.Z];
                normals[index] = [normal.X, normal.Y, normal.Z];
                uv[index] = [(double)radial / RadialSegments, (double)ring / (samples.Length - 1)];
                if (ring == samples.Length - 1) continue;
                int next = (radial + 1) % RadialSegments;
                int a = index, b = ring * RadialSegments + next,
                    c = (ring + 1) * RadialSegments + next, d = index + RadialSegments;
                int offset = index * 6;
                indices[offset] = a; indices[offset + 1] = b; indices[offset + 2] = c;
                indices[offset + 3] = a; indices[offset + 4] = c; indices[offset + 5] = d;
            }
        }
        return new("Fuel hose", positions, indices, positions, normals, uv, "structure", null, null)
        {
            ConnectedParts = [endpointA, endpointB]
        };
    }
}
