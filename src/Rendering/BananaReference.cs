using System.Numerics;

namespace Blueprint.Rendering;

/// <summary>A procedural 40 cm reference, not a measurement of a particular fruit.</summary>
static class BananaReference
{
    public const float LengthMetres = .40f;
    static readonly Lazy<KittenModel> model = new(() => Create(LengthMetres));
    public static KittenModel Model => model.Value;

    public static KittenModel Create(float lengthMetres)
    {
        if (!float.IsFinite(lengthMetres) || lengthMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(lengthMetres));
        const int segments = 24, sides = 6;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        // A short stalk makes the silhouette recognizable without texture.
        var stalkUp = Vector3.Normalize(new Vector3(1.1f, 0, 1));
        foreach (var center in new[] { new Vector3(-.014f, 0, .069f), new Vector3(-.010f, 0, .061f) })
            for (int j = 0; j < sides; j++)
            {
                float angle = j * MathF.Tau / sides;
                var normal = MathF.Cos(angle) * stalkUp + MathF.Sin(angle) * Vector3.UnitY;
                vertices.Add(center + normal * .003f);
                normals.Add(normal);
            }
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            var center = new Vector3(t * .20f, 0, .055f * MathF.Pow(2 * t - 1, 2));
            var up = Vector3.Normalize(new Vector3(-1.1f * (2 * t - 1), 0, 1));
            float radius = .001f + .004f * MathF.Pow(1 - t, 6) + .017f * MathF.Pow(Math.Max(0, MathF.Sin(MathF.PI * t)), .7f);
            for (int j = 0; j < sides; j++)
            {
                float angle = j * MathF.Tau / sides;
                var normal = MathF.Cos(angle) * up + MathF.Sin(angle) * Vector3.UnitY;
                vertices.Add(center + normal * radius);
                normals.Add(normal);
            }
        }
        int ringCount = vertices.Count / sides;
        for (int i = 0; i < ringCount - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = i * sides + j, b = i * sides + (j + 1) % sides;
                indices.AddRange([a, b, b + sides, a, b + sides, a + sides]);
            }
        for (int j = 1; j < sides - 1; j++)
        {
            indices.AddRange([0, j + 1, j]);
            int end = (ringCount - 1) * sides;
            indices.AddRange([end, end + j, end + j + 1]);
        }
        float min = vertices.Min(v => v.X), max = vertices.Max(v => v.X);
        float factor = lengthMetres / (max - min);
        // Stand on the tip with the stalk at the top. Rotate positions and
        // normals together so side and isometric views share upright geometry.
        var points = vertices.Select(v => new Vector3(v.Z * factor, v.Y * factor, (max - v.X) * factor)).ToArray();
        double[][] positions = points.Select(v => new double[] { v.Z, v.X, v.Y }).ToArray();
        double[][] ns = normals.Select(v => new double[] { -v.X, v.Z, v.Y }).ToArray();
        var mesh = new Mesh($"Banana (nominal {lengthMetres * 100:0} cm)", positions, indices.ToArray(), positions, ns,
            positions.Select(_ => new double[] { 0, 0 }).ToArray(), "structure", null, null);
        return new(points, indices.ToArray(), new([mesh]));
    }
}
