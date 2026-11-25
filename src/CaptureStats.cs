using Blueprint.Rendering;

﻿namespace Blueprint;

// Measured geometry is kept independently of admission. A refused craft still
// gets a complete census when the bounded traversal reaches the last primitive.
sealed class CaptureStats
{
    internal const int PrimitiveLimit = 100_000;
    const long MiB = 1024 * 1024;
    const long SourceLimit = 192 * MiB;
    const long DecodedMaterialLimit = 384 * MiB;
    const long WorkingMaterialLimit = 512 * MiB;

    sealed class Layer
    {
        public readonly HashSet<uint> Parts = [];
        public long Vertices, Triangles, Primitives;
        public long UniqueVertices, UniqueTriangles, UniquePrimitives;
        public long Materials, RawBytes, DecodedBytes, DecodeScratch;
        public long MaxPrimitiveScratch, MaxPrimitiveVertices, ProceduralBytes;
        public string? MaterialProblem;
    }

    readonly Layer exterior = new(), interior = new();
    readonly Dictionary<string, (long Exterior, long Interior)> templates = new(StringComparer.Ordinal);
    public string Failure { get; set; } = "none";
    public bool Complete { get; set; }
    public long CensusMilliseconds { get; set; }
    public long RetainedBytes { get; set; }
    public long CandidatePixelBytes { get; set; }
    public long EstimatedPeakBytes { get; private set; }
    public long CopyMilliseconds { get; set; }
    public int CapturedParts { get; set; }
    public string InteriorFailure { get; set; } = "none";
    public bool InteriorOmitted { get; set; }

    public void Add(string template, uint part, bool internalPart, int vertices, int triangles, bool unique, bool procedural = false)
    {
        var layer = internalPart ? interior : exterior;
        layer.Primitives++;
        layer.Vertices = checked(layer.Vertices + vertices);
        layer.Triangles = checked(layer.Triangles + triangles);
        layer.Parts.Add(part);
        if (unique)
        {
            layer.UniquePrimitives++;
            layer.UniqueVertices = checked(layer.UniqueVertices + vertices);
            layer.UniqueTriangles = checked(layer.UniqueTriangles + triangles);
        }
        if (procedural)
            layer.ProceduralBytes = checked(layer.ProceduralBytes + 136L * vertices);
        // Per-primitive scratch is short-lived; use the largest individual mesh,
        // while retained curvature and view buffers scale with all instances.
        layer.MaxPrimitiveScratch = Math.Max(layer.MaxPrimitiveScratch, RenderMemory.CurvatureScratch(vertices, triangles));
        layer.MaxPrimitiveVertices = Math.Max(layer.MaxPrimitiveVertices, vertices);
        var count = templates.GetValueOrDefault(template);
        if (internalPart) count.Interior += vertices;
        else count.Exterior += vertices;
        templates[template] = count;
        if (exterior.Primitives + interior.Primitives > PrimitiveLimit)
            Reject("geometry-census-primitives", exterior.Primitives + interior.Primitives, PrimitiveLimit, "mesh primitives in the bounded census");
    }

    public void Material(bool internalPart, int width, int height, int rawBytes, bool compressed)
    {
        var layer = internalPart ? interior : exterior;
        layer.Materials++;
        if (width is < 1 or > 8192 || height is < 1 or > 8192)
        {
            layer.MaterialProblem ??= $"Material dimensions {width:N0} x {height:N0} (supported range: 1 to 8,192 pixels per side).";
            return;
        }
        long decoded = checked((long)width * height * 4);
        layer.RawBytes = checked(layer.RawBytes + rawBytes);
        layer.DecodedBytes = checked(layer.DecodedBytes + decoded);
        if (compressed)
            layer.DecodeScratch = Math.Max(layer.DecodeScratch, decoded);
    }

    internal long SourceBytes(bool includeInterior)
    {
        long Size(Layer layer) => checked(32 * layer.UniqueVertices + 12 * layer.UniqueTriangles + 512 * layer.Primitives + layer.ProceduralBytes);
        return checked(Size(exterior) + (includeInterior ? Size(interior) : 0));
    }

    internal long WorkingGeometryBytes(bool includeInterior)
    {
        long triangles = exterior.Triangles + (includeInterior ? interior.Triangles : 0);
        long scratch = Math.Max(exterior.MaxPrimitiveScratch, includeInterior ? interior.MaxPrimitiveScratch : 0);
        if (triangles >= NormalLinework.LargeScopeTriangles)
        {
            long vertices = exterior.Vertices + (includeInterior ? interior.Vertices : 0);
            long maxVertices = Math.Max(exterior.MaxPrimitiveVertices, includeInterior ? interior.MaxPrimitiveVertices : 0);
            return Math.Max(scratch, RenderMemory.LargeRasterGeometry(vertices, triangles, maxVertices));
        }
        long Size(Layer layer) => checked(48 * layer.Vertices + 192 * layer.Triangles);
        return checked(Size(exterior) + (includeInterior ? Size(interior) : 0) + scratch);
    }

    public void RequireBudget(bool includeInterior)
    {
        long source = SourceBytes(includeInterior);
        long working = WorkingGeometryBytes(includeInterior);
        long decoded = exterior.DecodedBytes + (includeInterior ? interior.DecodedBytes : 0);
        long raw = exterior.RawBytes + (includeInterior ? interior.RawBytes : 0);
        long work = checked(raw + decoded +
            Math.Max(exterior.DecodeScratch, includeInterior ? interior.DecodeScratch : 0));
        // Reserve the full classified geometry allowance for a large candidate.
        // The worker checks its actual selected meshes and cache before a view.
        bool large = exterior.Triangles + (includeInterior ? interior.Triangles : 0) >= NormalLinework.LargeScopeTriangles;
        long geometryReserve = large ? RenderMemory.GeometryLimit : working;
        long additional = checked(source + CandidatePixelBytes + Math.Max(geometryReserve + raw + decoded, work));
        EstimatedPeakBytes = checked(additional + RetainedBytes);
        if (source > SourceLimit)
            Reject("geometry-source-bytes", source, SourceLimit, "estimated bytes for detached geometry");
        if (working > RenderMemory.GeometryLimit)
            Reject("geometry-render-bytes", working, RenderMemory.GeometryLimit, "estimated bytes for geometry processing");
        var materialProblem = exterior.MaterialProblem ?? (includeInterior ? interior.MaterialProblem : null);
        if (materialProblem != null)
        {
            Failure = "material-dimensions";
            throw new InvalidOperationException(materialProblem + " See PawPrints diagnostics.");
        }
        if (decoded > DecodedMaterialLimit)
            Reject("material-decoded-bytes", decoded, DecodedMaterialLimit, "decoded material bytes");
        if (work > WorkingMaterialLimit)
            Reject("material-working-bytes", work, WorkingMaterialLimit, "estimated bytes for material decoding");
        if (CandidatePixelBytes > 0 && RenderMemory.PeakOverBudget(additional, RetainedBytes) is { } exceeded)
            Reject("combined-memory", exceeded.Used, exceeded.Limit, "estimated bytes to generate the drawing");
    }

    void Reject(string code, long used, long limit, string unit)
    {
        Failure = code;
        throw new InvalidOperationException($"Craft exceeds export budget: {used:N0} {unit} (limit: {limit:N0}). See PawPrints diagnostics.");
    }

    public void Log(TimeSpan elapsed)
    {
        PrintDiagnostics.Record($"capture.geometry census={(Complete ? "complete" : "partial")} exterior_vertices={exterior.Vertices} exterior_triangles={exterior.Triangles} exterior_primitives={exterior.Primitives} exterior_parts={exterior.Parts.Count} interior_vertices={interior.Vertices} interior_triangles={interior.Triangles} interior_primitives={interior.Primitives} interior_parts={interior.Parts.Count} unique_primitives={exterior.UniquePrimitives + interior.UniquePrimitives} unique_vertices={exterior.UniqueVertices + interior.UniqueVertices} unique_triangles={exterior.UniqueTriangles + interior.UniqueTriangles} census_mesh_parts={exterior.Parts.Count + interior.Parts.Count(id => !exterior.Parts.Contains(id))} captured_parts={CapturedParts} interior_omitted={InteriorOmitted} interior_failure={InteriorFailure} estimated_source_bytes={SourceBytes(true)} estimated_geometry_working_bytes={WorkingGeometryBytes(true)} material_count={exterior.Materials + interior.Materials} raw_material_bytes={exterior.RawBytes + interior.RawBytes} decoded_material_bytes={exterior.DecodedBytes + interior.DecodedBytes} retained_estimate_bytes={RetainedBytes} pixel_working_estimate_bytes={CandidatePixelBytes} peak_estimate_bytes={EstimatedPeakBytes} census_ms={CensusMilliseconds} elapsed_ms={(long)elapsed.TotalMilliseconds} copy_ms={CopyMilliseconds} budget_failure={Failure}");
        foreach (var entry in templates.OrderByDescending(x => x.Value.Exterior + x.Value.Interior).ThenBy(x => x.Key, StringComparer.Ordinal).Take(10))
        {
            string id = new(entry.Key.Take(100).Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray());
            PrintDiagnostics.Record($"capture.template id={id} vertices={entry.Value.Exterior + entry.Value.Interior} exterior_vertices={entry.Value.Exterior} interior_vertices={entry.Value.Interior}");
        }
    }
}
