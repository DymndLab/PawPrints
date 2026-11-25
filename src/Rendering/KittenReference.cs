using System.Numerics;
using System.Text.Json;
namespace Blueprint.Rendering;

sealed record KittenModel(Vector3[] Vertices, int[] Indices, Capture LineCapture)
{
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<(int View, float Scale, ViewAxes Axes), KittenProjection> Projections = new();
    internal readonly System.Collections.Concurrent.ConcurrentDictionary<(int View, float Scale, ViewAxes Axes), byte[]> Outlines = new();

    // Source body POSITION values are in metres in the inspected 5801 asset's
    // neutral pose. Native Y-up becomes our Z-up; helmet/visor are separate assets.
    public static KittenModel Load(string path)
    {
        if (new FileInfo(path).Length > 2_000_000)
            throw new InvalidDataException("Kitten model metadata exceeds budget.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        var buffers = root.GetProperty("buffers");
        if (buffers.GetArrayLength() != 1)
            throw new InvalidDataException("Expected one kitten model buffer.");
        string name = buffers[0].GetProperty("uri").GetString()!;
        if (name != Path.GetFileName(name) || name.Contains(':'))
            throw new InvalidDataException("Invalid kitten model buffer path.");
        string bufferPath = Path.Combine(Path.GetDirectoryName(path)!, name);
        if (new FileInfo(bufferPath).Length > 16_000_000)
            throw new InvalidDataException("Kitten geometry exceeds budget.");
        byte[] bytes = File.ReadAllBytes(bufferPath);
        var accessors = root.GetProperty("accessors");
        var views = root.GetProperty("bufferViews");
        int Value(JsonElement e, string key, int fallback = 0) => e.TryGetProperty(key, out var p) ? p.GetInt32() : fallback;
        (int Count, int Offset, int Stride, int Type) Access(int id, int components)
        {
            var a = accessors[id];
            if (a.TryGetProperty("sparse", out _))
                throw new InvalidDataException("Sparse kitten geometry is unsupported.");
            var v = views[a.GetProperty("bufferView").GetInt32()];
            int type = Value(a, "componentType"), size = type == 5121 ? 1 : type == 5123 ? 2 : type is 5125 or 5126 ? 4 : throw new InvalidDataException("Unexpected kitten vertex format.");
            int count = Value(a, "count"), offset = Value(v, "byteOffset") + Value(a, "byteOffset"), stride = Value(v, "byteStride", size * components);
            if (count < 1 || count > 500000 || stride < size * components || offset < 0 || (long)offset + (count - 1L) * stride + size * components > bytes.Length)
                throw new InvalidDataException("Invalid kitten geometry bounds.");
            return (count, offset, stride, type);
        }
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var meshes = new List<Mesh>();
        foreach (var mesh in root.GetProperty("meshes").EnumerateArray())
            foreach (var primitive in mesh.GetProperty("primitives").EnumerateArray())
            {
                if (Value(primitive, "mode", 4) != 4)
                    throw new InvalidDataException("Kitten model needs triangle geometry.");
                var a = Access(primitive.GetProperty("attributes").GetProperty("POSITION").GetInt32(), 3);
                if (a.Type != 5126)
                    throw new InvalidDataException("Expected floating point kitten positions.");
                int start = vertices.Count;
                for (int i = 0; i < a.Count; i++)
                {
                    int q = a.Offset + i * a.Stride;
                    float x = BitConverter.ToSingle(bytes, q), y = BitConverter.ToSingle(bytes, q + 4), z = BitConverter.ToSingle(bytes, q + 8);
                    if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                        throw new InvalidDataException("Invalid kitten vertex.");
                    vertices.Add(new(z, x, y));
                }
                int indexStart = indices.Count;
                var ix = Access(primitive.GetProperty("indices").GetInt32(), 1);
                if (ix.Count % 3 != 0)
                    throw new InvalidDataException("Incomplete kitten triangles.");
                for (int i = 0; i < ix.Count; i++)
                {
                    int q = ix.Offset + i * ix.Stride;
                    uint n = ix.Type switch
                    {
                        5121 => bytes[q],
                        5123 => BitConverter.ToUInt16(bytes, q),
                        5125 => BitConverter.ToUInt32(bytes, q),
                        _ => throw new InvalidDataException("Invalid kitten indices.")
                    };
                    if (n >= a.Count)
                        throw new InvalidDataException("Kitten index outside mesh.");
                    indices.Add(start + (int)n);
                }
                // Feed the actual craft line renderer its native capture coordinate
                // convention. Keep mesh ownership so eyes/mouth remain visible details.
                var positions = vertices.Skip(start).Select(v => new double[] { v.Z, v.X, v.Y }).ToArray();
                var normals = new double[a.Count][];
                var na = Access(primitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32(), 3);
                if (na.Type != 5126 || na.Count != a.Count)
                    throw new InvalidDataException("Invalid kitten normals.");
                for (int i = 0; i < na.Count; i++)
                {
                    int q = na.Offset + i * na.Stride;
                    normals[i] = [BitConverter.ToSingle(bytes, q + 4), BitConverter.ToSingle(bytes, q + 8), BitConverter.ToSingle(bytes, q)];
                }
                meshes.Add(new("Kitten body", positions, indices.Skip(indexStart).Select(i => i - start).ToArray(), positions, normals, positions.Select(_ => new double[] { 0, 0 }).ToArray(), "structure", null, null));
            }
        if (vertices.Count == 0 || vertices.Count > 200000 || indices.Count > 600000)
            throw new InvalidDataException("Kitten geometry budget exceeded.");
        return new(vertices.ToArray(), indices.ToArray(), new(meshes.ToArray()));
    }
}
enum ScaleReferenceKind
{
    Kitten, Banana
}
sealed record KittenReference(int Id, int View, float X, float Y, bool Outline = false, ScaleReferenceKind Kind = ScaleReferenceKind.Kitten, int? SourceView = null, ViewAxes? Axes = null, bool FlipHorizontal = false, bool FlipVertical = false);
sealed record KittenProjection(Vector2[] Points, float Width, float Height);
static class KittenReferences
{

    public static float Scale(RenderScene scene) => scene.Views.FirstOrDefault(v => v.Id < 1000 && v.Frame != null)?.Frame?.Scale ?? (scene.Drawing is { } d ? PrintScale.Density(scene.Options) / d.Ratio : throw new InvalidOperationException("Place a primary view first."));

    public static KittenProjection Project(KittenModel model, int view, float scale, ViewAxes? axes = null)
    {
        if (view < 0 || view >= DrawingPlacements.Names.Length)
            throw new ArgumentOutOfRangeException(nameof(view));
        if (model.Projections.Count > 8)
            model.Projections.Clear();
        return model.Projections.GetOrAdd((view, scale, axes ?? DrawingPlacements.Axes(new(0, view, 0, 0))), key => Build(model, key.Axes, key.Scale));
    }

    static KittenProjection Build(KittenModel model, ViewAxes basis, float scale)
    {
        var p = model.Vertices.Select(v => new Vector2(Vector3.Dot(v, basis.Right) * scale, -Vector3.Dot(v, basis.Up) * scale)).ToArray();
        var lo = p.Aggregate(new Vector2(float.PositiveInfinity), Vector2.Min);
        var hi = p.Aggregate(new Vector2(float.NegativeInfinity), Vector2.Max);
        return new(p.Select(v => v - lo + Vector2.One).ToArray(), MathF.Ceiling(hi.X - lo.X) + 2, MathF.Ceiling(hi.Y - lo.Y) + 2);
    }

    public static KittenModel Model(RenderScene scene, KittenReference reference) => reference.Kind == ScaleReferenceKind.Banana
        ? BananaReference.Model : scene.KittenModel ?? throw new InvalidOperationException("Kitten reference model is missing.");

    public static KittenReference FromView(RenderScene scene, int id, int viewId, bool outline = true,
        ScaleReferenceKind kind = ScaleReferenceKind.Kitten)
    {
        var view = scene.Views.Single(v => v.Id == viewId);
        var frame = view.Frame ?? throw new InvalidOperationException("The view has no reference projection.");
        int camera = view.Projection ?? scene.Drawing?.Views.FirstOrDefault(v => v.Id == viewId)?.Camera ??
            scene.Details.FirstOrDefault(v => v.Definition.ViewId == viewId)?.Definition.View ?? 2;
        return new(id, camera, view.Bounds.Right + 24, view.Bounds.Bottom, outline, kind,
            SourceView: viewId, Axes: new(frame.Right, frame.Up, frame.Toward));
    }

    static ProjectionFrame? SourceFrame(RenderScene scene, KittenReference reference) =>
        reference.SourceView is { } id ? scene.Views.FirstOrDefault(v => v.Id == id)?.Frame : null;

    public static float Scale(RenderScene scene, KittenReference reference) => SourceFrame(scene, reference)?.Scale ?? Scale(scene);

    static ViewAxes Axes(RenderScene scene, KittenReference reference) => SourceFrame(scene, reference) is { } frame
        ? new(frame.Right, frame.Up, frame.Toward)
        : reference.Axes ?? DrawingPlacements.Axes(new(0, reference.View, 0, 0));

    public static Box Bounds(RenderScene scene, KittenReference reference)
    {
        var p = Project(Model(scene, reference), reference.View, Scale(scene, reference), Axes(scene, reference));
        return new(reference.X, reference.Y, p.Width, p.Height);
    }

    // Placement previews must start on usable paper before the pointer positions them.
    // A fixed bottom offset can put a full-size kitten through the title block.
    public static RenderScene PlacementScene(RenderScene scene, KittenReference reference, KittenModel? model)
    {
        var prepared = scene.WithKittens([], model);
        return prepared.WithKittens([Clamp(prepared, reference)]);
    }

    public static KittenReference Clamp(RenderScene scene, KittenReference reference)
    {
        var b = Bounds(scene, reference);
        if (scene.Options.FreeLayout) { CanvasBounds.Require(b); return reference; }
        var a = FrameLayout.Area(scene.Options);
        if (b.Width > a.Width || b.Height > a.Height)
            throw new InvalidOperationException("The scale reference does not fit at this drawing scale. Reduce View scale first.");
        var desired = b with
        {
            X = Math.Clamp(reference.X, a.X, a.Right - b.Width),
            Y = Math.Clamp(reference.Y, a.Y, a.Bottom - b.Height)
        };
        var clear = FrameLayout.Clear(desired, [PageLayout.TitleBounds(scene.Options)], a, false, b.Width, b.Height)
            ?? throw new InvalidOperationException("The scale reference does not fit clear of the title block at this scale.");
        return reference with
        {
            X = clear.X,
            Y = clear.Y
        };
    }

    public static void Paint(Canvas canvas, RenderScene scene, CancellationToken token)
    {
        if (scene.Kittens.Length == 0)
            return;
        foreach (var kitten in scene.Kittens)
        {
            canvas.HitTarget = new(DrawingObjectKind.ScaleReference, kitten.Id);
            var model = Model(scene, kitten);
            if (!(scene.Options.FreeLayout ? CanvasBounds.Safe(Bounds(scene, kitten)) : PageLayout.Fits(Bounds(scene, kitten), scene.Options)))
                throw new InvalidOperationException("Keep the scale reference inside the sheet and clear of the title block.");
            var p = Project(model, kitten.View, Scale(scene, kitten), Axes(scene, kitten));
            int pixelWidth = (int)p.Width, pixelHeight = (int)p.Height;
            // Reflect the finished ink, not the camera or mesh: hidden surfaces and
            // lighting stay identical, and view matching retains its physical scale.
            void PaintPixel(int x, int y, float alpha)
            {
                int outputX = kitten.FlipHorizontal ? pixelWidth - 1 - x : x;
                int outputY = kitten.FlipVertical ? pixelHeight - 1 - y : y;
                canvas.Ink((int)kitten.X + outputX, (int)kitten.Y + outputY, alpha);
            }
            int w = pixelWidth * 2, h = pixelHeight * 2;
            if ((long)w * h > 16_000_000)
                throw new InvalidOperationException("Kitten silhouette exceeds render budget.");
            if (kitten.Outline)
            {
                if (model.Outlines.Count > 8)
                    model.Outlines.Clear();
                var ink = model.Outlines.GetOrAdd((kitten.View, Scale(scene, kitten), Axes(scene, kitten)), key =>
                {
                    var basis = key.Axes;
                    float Mid(Vector3 axis)
                    {
                        var values = model.Vertices.Select(v => Vector3.Dot(v, axis)).ToArray();
                        return (values.Min() + values.Max()) * .5f;
                    }
                    var center = basis.Right * Mid(basis.Right) + basis.Up * Mid(basis.Up);
                    var frame = new ProjectionFrame((int)p.Width, (int)p.Height, key.Scale, center, basis.Right, basis.Up, basis.Toward);
                    return new BlueprintRenderer(model.LineCapture, new Dictionary<string, Texture>(), "Kitten", key.View, token, frame).RenderView(_ => { }).Ink;
                });
                for (int y = 0; y < (int)p.Height; y++)
                    for (int x = 0; x < (int)p.Width; x++)
                        PaintPixel(x, y, ink[y * pixelWidth + x] / 255f);
                continue;
            }
            var mask = new byte[w * h];
            float Edge(Vector2 a, Vector2 b, Vector2 c) => (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
            for (int i = 0; i < model.Indices.Length; i += 3)
            {
                if (i % 3000 == 0)
                    token.ThrowIfCancellationRequested();
                var a = p.Points[model.Indices[i]] * 2;
                var b = p.Points[model.Indices[i + 1]] * 2;
                var c = p.Points[model.Indices[i + 2]] * 2;
                float area = Edge(a, b, c);
                if (Math.Abs(area) < .00001f)
                    continue;
                float sign = MathF.Sign(area);
                int x0 = Math.Max(0, (int)MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), y0 = Math.Max(0, (int)MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
                int x1 = Math.Min(w - 1, (int)MathF.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X)))), y1 = Math.Min(h - 1, (int)MathF.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var point = new Vector2(x + .5f, y + .5f);
                        if (Edge(a, b, point) * sign >= 0 && Edge(b, c, point) * sign >= 0 && Edge(c, a, point) * sign >= 0)
                            mask[y * w + x] = 1;
                    }
            }
            for (int y = 0; y < h; y += 2)
                for (int x = 0; x < w; x += 2)
                    PaintPixel(x / 2, y / 2, (mask[y * w + x] + mask[y * w + x + 1] + mask[(y + 1) * w + x] + mask[(y + 1) * w + x + 1]) * .25f);
        }
    }
}
