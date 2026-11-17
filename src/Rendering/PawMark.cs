using System.Numerics;

namespace Blueprint.Rendering;

// Small authored footprint geometry. Rasterizes through the ordinary print ink
// path, without emoji, external images, or font-dependent pictograms.
static class PawMark
{

    public static void Draw(Canvas canvas, Vector2 center, float scale, float degrees, bool outline)
    {
        float angle = degrees * MathF.PI / 180, cos = MathF.Cos(angle), sin = MathF.Sin(angle);
        Vector2 World(Vector2 p) => center + scale * new Vector2(p.X * cos - p.Y * sin, p.X * sin + p.Y * cos);
        void Shape(List<Vector2> local)
        {
            var polygon = local.Select(World).ToArray();
            float stroke = 1.5f;
            int left = (int)MathF.Floor(polygon.Min(p => p.X) - stroke), right = (int)MathF.Ceiling(polygon.Max(p => p.X) + stroke);
            int top = (int)MathF.Floor(polygon.Min(p => p.Y) - stroke), bottom = (int)MathF.Ceiling(polygon.Max(p => p.Y) + stroke);
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 3; sy++)
                        for (int sx = 0; sx < 3; sx++)
                        {
                            var q = new Vector2(x + (sx + .5f) / 3, y + (sy + .5f) / 3);
                            bool inside = false;
                            float distance = float.MaxValue;
                            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
                            {
                                var a = polygon[j];
                                var b = polygon[i];
                                var edge = b - a;
                                var nearest = a + edge * Math.Clamp(Vector2.Dot(q - a, edge) / Math.Max(edge.LengthSquared(), .0001f), 0, 1);
                                distance = Math.Min(distance, Vector2.DistanceSquared(q, nearest));
                                if ((a.Y > q.Y) != (b.Y > q.Y) && q.X < (b.X - a.X) * (q.Y - a.Y) / (b.Y - a.Y) + a.X)
                                    inside = !inside;
                            }
                            if (outline ? distance <= stroke * stroke * .25f : inside)
                                hits++;
                        }
                    if (hits > 0)
                        canvas.Ink(x, y, hits / 9f);
                }
        }
        foreach (var contour in Contours)
            Shape(contour);
    }

    // Append the authored contours at the requested size and orientation.
    public static readonly IReadOnlyList<List<Vector2>> Contours = CreateContours();

    static IReadOnlyList<List<Vector2>> CreateContours()
    {
        var contours = new List<List<Vector2>>();
        foreach (var toe in new[] { (X: -18f, Y: -11f, Rx: 5f, Ry: 7.5f, Tilt: -.5f), (X: -7f, Y: -22f, Rx: 5.5f, Ry: 8f, Tilt: -.15f), (X: 7f, Y: -22f, Rx: 5.5f, Ry: 8f, Tilt: .15f), (X: 18f, Y: -11f, Rx: 5f, Ry: 7.5f, Tilt: .5f) })
        {
            var points = new List<Vector2>();
            for (int i = 0; i < 48; i++)
            {
                float t = i * MathF.Tau / 48, x = toe.Rx * MathF.Cos(t), y = toe.Ry * MathF.Sin(t);
                points.Add(new(toe.X + x * MathF.Cos(toe.Tilt) - y * MathF.Sin(toe.Tilt), toe.Y + x * MathF.Sin(toe.Tilt) + y * MathF.Cos(toe.Tilt)));
            }
            contours.Add(points);
        }
        var pad = new List<Vector2> { new(-16, 7) };
        void Curve(Vector2 b, Vector2 c, Vector2 d)
        {
            var a = pad[^1];
            for (int i = 1; i <= 20; i++)
            {
                float t = i / 20f, u = 1 - t;
                pad.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d);
            }
        }
        Curve(new(-16, 0), new(-9, -1), new(-6, -7));
        Curve(new(-3, -13), new(3, -13), new(6, -7));
        Curve(new(9, -1), new(16, 0), new(16, 7));
        Curve(new(16, 16), new(6, 18), new(0, 13));
        Curve(new(-6, 18), new(-16, 16), new(-16, 7));
        contours.Add(pad);
        return contours;
    }
}
