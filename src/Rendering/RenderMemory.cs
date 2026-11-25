using System.Runtime.CompilerServices;

namespace Blueprint.Rendering;

static class RenderMemory
{
    const long MiB = 1024 * 1024;
    const int PixelLimit = 16_777_216;
    const long PeakLimit = 2_048 * MiB;
    internal const long GeometryLimit = 512 * MiB;
    static readonly SemaphoreSlim Slot = new(1, 1);
    [ThreadStatic] static long acceptedBytes;

    public static T InSlot<T>(Func<T> work, CancellationToken token, long retained = 0)
    {
        Slot.Wait(token);
        acceptedBytes = retained;
        try { return work(); }
        finally
        {
            acceptedBytes = 0;
            Slot.Release();
        }
    }

    // Positions and normals are retained for every instance. Allow for UV or
    // legacy transforms, plus projected vertices for the largest primitive.
    // Large renderers reserve the exact triangle capacity up front.
    internal static long LargeRasterGeometry(long vertices, long triangles, long maxVertices) =>
        checked(48 * vertices + Unsafe.SizeOf<Triangle>() * triangles + 12 * maxVertices);

    internal static long LargeRasterGeometry(Capture scope, long triangles) =>
        LargeRasterGeometry(scope.Meshes.Sum(mesh => (long)mesh.VertexCount), triangles,
            scope.Meshes.Max(mesh => (long)mesh.VertexCount));

    internal static long CurvatureScratch(long vertices, long triangles) =>
        checked(96 * vertices + 384 * triangles);

    internal static void RequireGeometry(long geometry, int rasterPixels = 0)
    {
        if (geometry > GeometryLimit)
            throw new InvalidOperationException($"Drawing geometry requires an estimated {geometry:N0} bytes (limit: {GeometryLimit:N0}). Reduce the assembly scope.");
        if (PeakOverBudget(checked(geometry + 96L * rasterPixels), acceptedBytes) is { } budget)
            throw new InvalidOperationException($"Drawing exceeds the memory budget ({budget.Used:N0} bytes, limit: {budget.Limit:N0}). Reduce view size or assembly scope.");
    }

    public static int RasterPixels(int width, int height)
    {
        long pixels = (long)width * height;
        if (width <= 0 || height <= 0 || pixels > PixelLimit)
            throw new InvalidOperationException($"Drawing raster requires {pixels:N0} pixels (limit: {PixelLimit:N0}). Reduce view size.");
        return (int)pixels;
    }

    // 60 bytes of full-resolution raster arrays, optional 4-byte solar ink,
    // plus allowances for page composition, picking, preview and output buffers.
    public static long CandidatePixels(PageSize page) => checked(96L * RasterPixels(page.Width, page.Height));

    public static long Retained(params Sheet?[] sheets)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        long bytes = 0;
        void Count<T>(T[]? buffer) where T : unmanaged
        {
            if (buffer != null && visited.Add(buffer))
                bytes = checked(bytes + buffer.LongLength * Unsafe.SizeOf<T>());
        }

        var remaining = new Stack<Sheet>(sheets.OfType<Sheet>());
        while (remaining.TryPop(out var sheet))
        {
            if (!visited.Add(sheet)) continue;
            Count(sheet.Scene?.PixelViews);
            Count(sheet.Rgb);
            Count(sheet.PreviewRgba);
            if (sheet.MainCurvature is { } curvature && visited.Add(curvature))
                bytes = checked(bytes + curvature.EstimatedRetainedBytes);
            if (sheet.PoseSource is { } source && visited.Add(source))
            {
                bytes = checked(bytes + source.DetachedGeometryBytes);
                foreach (var texture in source.Maps.Values) Count(texture.Pixels);
            }
            if (sheet.MainInk != null)
                foreach (var tile in sheet.MainInk)
                {
                    Count(tile.Pixels);
                    Count(tile.MeshOwners);
                    Count(tile.MeshParts);
                }
            if (sheet.Scene is { } scene && visited.Add(scene))
            {
                Count(scene.BaseRgb);
                Count(scene.Owners);
                Count(scene.MeshOwners);
                Count(scene.MeshNodes);
                foreach (var detail in scene.Details)
                {
                    Count(detail.Pixels.Ink);
                    Count(detail.Pixels.MeshOwners);
                }
                foreach (var visual in scene.Visible.Values)
                {
                    Count(visual.Edges);
                    Count(visual.ContactEdges);
                }
            }
            if (sheet.PoseMain != null) remaining.Push(sheet.PoseMain);
            if (sheet.RegeneratedBase != null) remaining.Push(sheet.RegeneratedBase);
        }
        return bytes;
    }

    public static (long Used, long Limit)? PeakOverBudget(long additional, long retained)
    {
        long peak = checked(additional + retained);
        if (peak > PeakLimit) return (peak, PeakLimit);
        var memory = GC.GetGCMemoryInfo();
        // The GC's last observation is advisory. Keep a separate hard cap even
        // when this information is unavailable or the system reports ample RAM.
        if (memory.MemoryLoadBytes > 0 && memory.HighMemoryLoadThresholdBytes > 0)
        {
            long available = Math.Max(0, memory.HighMemoryLoadThresholdBytes - memory.MemoryLoadBytes - 128 * MiB);
            if (additional > available)
                return (additional, available);
        }
        return null;
    }
}
