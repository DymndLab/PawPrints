namespace Blueprint;

// Counts include the primitive that crossed a limit, so a failed capture still
// explains the rejection. Memory is a conservative x64 estimate, not a peak.
sealed class CaptureStats
{
    readonly Dictionary<string, long> templates = new();
    readonly HashSet<uint> exteriorParts = new(), interiorParts = new();
    long exteriorVertices, exteriorTriangles, exteriorPrimitives;
    long interiorVertices, interiorTriangles, interiorPrimitives;
    public string Failure = "none";

    public void Add(string template, uint part, bool interior, int vertices, int triangles)
    {
        if (interior)
        {
            interiorVertices += vertices; interiorTriangles += triangles; interiorPrimitives++;
            interiorParts.Add(part);
        }
        else
        {
            exteriorVertices += vertices; exteriorTriangles += triangles; exteriorPrimitives++;
            exteriorParts.Add(part);
        }
        templates[template] = templates.GetValueOrDefault(template) + vertices;
        long estimate = EstimatedBytes;
        const long limit = 512L * 1024 * 1024;
        if (estimate > limit)
        {
            Failure = "geometry-estimated-bytes";
            throw new InvalidOperationException($"Craft geometry estimated at {estimate:N0} bytes (limit: {limit:N0}). See PawPrints diagnostics.");
        }
    }

    long EstimatedBytes => (exteriorVertices + interiorVertices) * 264 + (exteriorTriangles + interiorTriangles) * 60;

    public void Log(TimeSpan elapsed)
    {
        // Jagged arrays, references and indices before source sharing, plus the
        // renderer's converted vectors and triangle records. Pixel buffers,
        // textures, document history and runtime overhead are additional.
        long estimate = EstimatedBytes;
        PrintDiagnostics.Record($"capture.geometry exterior_vertices={exteriorVertices} exterior_triangles={exteriorTriangles} exterior_primitives={exteriorPrimitives} exterior_parts={exteriorParts.Count} interior_vertices={interiorVertices} interior_triangles={interiorTriangles} interior_primitives={interiorPrimitives} interior_parts={interiorParts.Count} estimated_geometry_bytes={estimate} elapsed_ms={(long)elapsed.TotalMilliseconds} budget_failure={Failure}");
        foreach (var entry in templates.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).Take(10))
        {
            // Template IDs are useful for repeated-part diagnosis, but never log
            // craft or display names. Bound and sanitize mod-provided IDs.
            string id = new string(entry.Key.Take(100).Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray());
            PrintDiagnostics.Record($"capture.template id={id} vertices={entry.Value}");
        }
    }
}
