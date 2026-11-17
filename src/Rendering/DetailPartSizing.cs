using System.Numerics;
namespace Blueprint.Rendering;

static class DetailPartSizing
{
    const string TooSmall = "These parts are too big for the detail box. Enlarge the box or reduce the part size and try again.";


    // Only group centres spread. All vertices within a whole part retain their
    // authored dimensions, so Frame.Scale remains a meaningful physical scale.
    public static (DetailGeometry Geometry, ProjectionFrame Frame, DetailSizing Sizing) Place(
        DetailGeometry geometry, DetailSizing sizing, int width, int height, int view, PrintOptions options)
    {
        if (!float.IsFinite(sizing.Ratio) || sizing.Ratio < .01f || sizing.Ratio > 1000000)
            throw new InvalidOperationException("Choose a positive part scale between 1:0.01 and 1:1000000.");
        float scale = PrintScale.Density(options) / sizing.Ratio;
        var axes = SheetLayout.Basis(view);
        var points = geometry.Capture.Meshes.SelectMany(m => m.Vertices).Select(ExplodedGeometry.World).ToArray();
        var center = sizing.Center ?? (points.Aggregate(new Vector3(float.PositiveInfinity), Vector3.Min) + points.Aggregate(new Vector3(float.NegativeInfinity), Vector3.Max)) * .5f;
        float low = 0, high = float.PositiveInfinity;
        void Limit(float location, float extent, float half)
        {
            if (Math.Abs(location) < .000001f)
            {
                if (Math.Abs(extent) > half + .001f)
                    throw new InvalidOperationException(TooSmall);
                return;
            }
            float a = (-half - extent) / location, b = (half - extent) / location;
            low = Math.Max(low, Math.Min(a, b));
            high = Math.Min(high, Math.Max(a, b));
        }
        for (int i = 0; i < geometry.Capture.Meshes.Length; i++)
        {
            var partCenter = geometry.MovedCenters[i];
            foreach (var vertex in geometry.Capture.Meshes[i].Vertices)
            {
                var offset = ExplodedGeometry.World(vertex) - partCenter;
                Limit(Vector3.Dot(partCenter - center, axes.Right), Vector3.Dot(offset, axes.Right) * scale, (width - 32) * .5f);
                Limit(Vector3.Dot(partCenter - center, axes.Up), Vector3.Dot(offset, axes.Up) * scale, (height - 32) * .5f);
            }
        }
        bool refit = sizing.Spread == null || sizing.Width != width || sizing.Height != height;
        float spread = refit ? (float.IsPositiveInfinity(high) ? 1 : Math.Max(0, high)) : sizing.Spread!.Value;
        if (!float.IsFinite(spread) || spread < 0 || spread < low - .001f || spread > high + .001f)
            throw new InvalidOperationException(TooSmall);
        Vector3 MoveCenter(Vector3 p) => center + (p - center) * (spread / scale);
        var meshes = geometry.Capture.Meshes.Select((m, i) =>
        {
            var delta = MoveCenter(geometry.MovedCenters[i]) - geometry.MovedCenters[i];
            return m with
            {
                Vertices = m.Vertices.Select(p => new[] { p[0] + delta.Z, p[1] + delta.X, p[2] + delta.Y }).ToArray()
            };
        }).ToArray();
        return (geometry with
        {
            Capture = new(meshes),
            OriginalCenters = geometry.OriginalCenters.Select(MoveCenter).ToArray(),
            MovedCenters = geometry.MovedCenters.Select(MoveCenter).ToArray()
        },
            new(width, height, scale, center, axes.Right, axes.Up, axes.Toward), new(sizing.Ratio, spread, center, width, height));
    }
}
