using System.Numerics;
namespace Blueprint.Rendering;

// Small, authored rocket model projected with the same orthographic camera
// bases as the print. Cache linework once; no raster textures or per-frame mesh work.
static class TemplateRocket
{
    public static readonly (Vector2 A, Vector2 B)[][] Views = Enumerable.Range(0, 5).Select(Build).ToArray();

    static (Vector2 A, Vector2 B)[] Build(int camera)
    {
        var basis = SheetLayout.Basis(camera);
        var vertices = new List<Vector3>();
        var ids = new Dictionary<Vector3, int>();
        int Id(Vector3 p)
        {
            if (!ids.TryGetValue(p, out int i))
            {
                i = vertices.Count;
                vertices.Add(p);
                ids[p] = i;
            }
            return i;
        }
        var edges = new Dictionary<(int, int), List<(bool Visible, int Surface)>>();
        void Edge(Vector3 a, Vector3 b, bool visible, int surface)
        {
            int i = Id(a), j = Id(b);
            if (i == j)
                return;
            var key = i < j ? (i, j) : (j, i);
            if (!edges.TryGetValue(key, out var faces))
                edges[key] = faces = [];
            faces.Add((visible, surface));
        }
        (float R, float Z)[] profile = [(.19f, -1), (.11f, -.72f), (.26f, -.64f), (.26f, .5f), (0, 1.08f)];
        const int segments = 32;
        Vector3 Ring(int ring, int i)
        {
            float a = (i % segments) * MathF.Tau / segments;
            var p = profile[ring];
            return new(p.R * MathF.Cos(a), p.R * MathF.Sin(a), p.Z);
        }
        for (int ring = 0; ring < profile.Length - 1; ring++)
            for (int i = 0; i < segments; i++)
            {
                float angle = (i + .5f) * MathF.Tau / segments;
                float dz = profile[ring + 1].Z - profile[ring].Z, dr = profile[ring + 1].R - profile[ring].R;
                bool visible = Vector3.Dot(new(dz * MathF.Cos(angle), dz * MathF.Sin(angle), -dr), basis.Toward) > 0;
                var a = Ring(ring, i);
                var b = Ring(ring, i + 1);
                var c = Ring(ring + 1, i + 1);
                var d = Ring(ring + 1, i);
                Edge(a, b, visible, ring);
                Edge(b, c, visible, ring);
                Edge(c, d, visible, ring);
                Edge(d, a, visible, ring);
            }
        var lines = edges.Where(e => e.Value.Any(f => f.Visible) && (e.Value.Count == 1 || e.Value.Any(f => !f.Visible) || e.Value.Select(f => f.Surface).Distinct().Count() > 1))
            .Select(e => (A: vertices[e.Key.Item1], B: vertices[e.Key.Item2])).ToList();
        // Only near-side fins; rear fins must not show through the fuselage.
        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.PI / 2;
            var radial = new Vector3(MathF.Cos(angle), MathF.Sin(angle), 0);
            if (Vector3.Dot(radial, basis.Toward) < -.001f)
                continue;
            var a = radial * .26f + new Vector3(0, 0, -.15f);
            var b = radial * .57f + new Vector3(0, 0, -.83f);
            var c = radial * .26f + new Vector3(0, 0, -.64f);
            lines.Add((a, b));
            lines.Add((b, c));
        }
        Vector2 Project(Vector3 p) => new(Vector3.Dot(p, basis.Right), -Vector3.Dot(p, basis.Up));
        var result = lines.Select(l => (A: Project(l.A), B: Project(l.B))).ToArray();
        var points = result.SelectMany(l => new[] { l.A, l.B }).ToArray();
        var min = points.Aggregate(new Vector2(float.PositiveInfinity), Vector2.Min);
        var max = points.Aggregate(new Vector2(float.NegativeInfinity), Vector2.Max);
        var center = (min + max) * .5f;
        float extent = Math.Max(max.X - min.X, max.Y - min.Y) * .5f;
        return result.Select(l => ((l.A - center) / extent, (l.B - center) / extent)).ToArray();
    }
}
