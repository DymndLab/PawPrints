using System.Numerics;
namespace Blueprint.Rendering;

static class DetailPartSizing
{
    // Free layout preserves physical part scale and separation; the viewport
    // follows their projected extent instead of stretching whitespace around them.
    public static (DetailGeometry Geometry, ProjectionFrame Frame, DetailSizing Sizing) Free(
        DetailGeometry geometry, DetailSizing sizing, int view, PrintOptions options)
    {
        if (!float.IsFinite(sizing.Ratio) || sizing.Ratio < .01f || sizing.Ratio > 1000000)
            throw new InvalidOperationException("Choose a positive part scale between 1:0.01 and 1:1000000.");
        float scale = PrintScale.Density(options) / sizing.Ratio;
        float spread = sizing.Spread ?? scale;
        var center = sizing.Center ?? Vector3.Zero;
        if (!float.IsFinite(spread) || spread <= 0 || !float.IsFinite(center.LengthSquared()))
            throw new InvalidOperationException("Invalid exploded view sizing.");
        var axes = SheetLayout.Basis(view);
        Vector3 MoveCenter(Vector3 p) => center + (p - center) * (spread / scale);
        var meshes = geometry.Capture.Meshes.Select((mesh, i) =>
        {
            var delta = MoveCenter(geometry.MovedCenters[i]) - geometry.MovedCenters[i];
            return mesh.Translate(delta);
        }).ToArray();
        float minX = float.PositiveInfinity, minY = minX, maxX = float.NegativeInfinity, maxY = maxX;
        foreach (var mesh in meshes)
            foreach (var p in mesh.WorldVertices())
            {
                float x = Vector3.Dot(p - center, axes.Right), y = Vector3.Dot(p - center, axes.Up);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        var extent = new Vector2((maxX - minX) * scale, (maxY - minY) * scale);
        var size = DetailFrames.TileSize(extent, options);
        var cameraCenter = center + axes.Right * ((minX + maxX) / 2) + axes.Up * ((minY + maxY) / 2);
        var frame = new ProjectionFrame(size.Width, size.Height, scale, cameraCenter, axes.Right, axes.Up, axes.Toward) { ContentExtent = extent };
        return (geometry with
        {
            Capture = new(meshes),
            OriginalCenters = geometry.OriginalCenters.Select(MoveCenter).ToArray(),
            MovedCenters = geometry.MovedCenters.Select(MoveCenter).ToArray(),
            MountGuides = geometry.MountGuides.ToDictionary(g => g.Key, g => (MoveCenter(g.Value.Start), MoveCenter(g.Value.End)))
        }, frame,
            new(sizing.Ratio, spread, center, size.Width, size.Height));
    }

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
        var points = geometry.Capture.Meshes.SelectMany(m => m.WorldVertices()).ToArray();
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
            foreach (var vertex in geometry.Capture.Meshes[i].WorldVertices())
            {
                var offset = vertex - partCenter;
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
            return m.Translate(delta);
        }).ToArray();
        return (geometry with
        {
            Capture = new(meshes),
            OriginalCenters = geometry.OriginalCenters.Select(MoveCenter).ToArray(),
            MovedCenters = geometry.MovedCenters.Select(MoveCenter).ToArray(),
            MountGuides = geometry.MountGuides.ToDictionary(g => g.Key, g => (MoveCenter(g.Value.Start), MoveCenter(g.Value.End)))
        },
            new(width, height, scale, center, axes.Right, axes.Up, axes.Toward), new(sizing.Ratio, spread, center, width, height));
    }
}
