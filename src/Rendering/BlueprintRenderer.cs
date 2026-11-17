using System.Numerics;
namespace Blueprint.Rendering;

record Capture(Mesh[] Meshes);
record Mesh(string Name, double[][] Vertices, int[] Indices, double[][] SourceVertices, double[][] SourceNormals, double[][] UV, string Surface, TextureInfo? NormalTexture, TextureInfo? DiffuseTexture);
record TextureInfo(string Path, int Width, int Height);
record Texture(TextureInfo Info, byte[] Pixels)
{


    public Vector3 Sample(Vector2 uv)
    {
        float x = (uv.X - MathF.Floor(uv.X)) * Info.Width - .5f, y = (uv.Y - MathF.Floor(uv.Y)) * Info.Height - .5f;
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        Vector3 P(int a, int b)
        {
            a = (a % Info.Width + Info.Width) % Info.Width;
            b = (b % Info.Height + Info.Height) % Info.Height;
            int q = (b * Info.Width + a) * 4;
            return new(Pixels[q + 2] / 255f, Pixels[q + 1] / 255f, Pixels[q] / 255f);
        }
        return Vector3.Lerp(Vector3.Lerp(P(ix, iy), P(ix + 1, iy), fx), Vector3.Lerp(P(ix, iy + 1), P(ix + 1, iy + 1), fx), fy);
    }
}
record Triangle(int Mesh, Vector3 Face);
record HatchEdgeVariant(string Label, float ShadowShift = 0, float Feather = 0, bool CrossOnly = false, int Clearance = 6, int EdgeFade = 0, int EndTaper = 0, int SmoothRadius = 0, float TaperPower = 1, bool TaperCrossOnly = false, bool TaperBoundaryOnly = false);
sealed partial class BlueprintRenderer
{
    readonly int W, H;
    readonly int sampleRate;
    readonly Vector2 inkOrigin, referenceSize;
    readonly Capture capture;
    readonly string title;
    readonly CancellationToken cancellation;
    readonly float[] depth, tone, luma;
    readonly int[] face;
    readonly Vector3[] surfaceNormal, normalDeviation;
    readonly float[] structuralLines;
    readonly int[] edgeDistance;
    readonly float[] contourCoordinate, hatchShade;
    readonly int[] hatchClearance;

    readonly Vector3 right, up, toward;
    readonly Dictionary<string, Texture> textures = new();
    readonly List<(Vector3[] Pos, Vector3[] Norm, Vector2[] UV)> geometry = new();
    public readonly List<Triangle> Triangles = new();
    public float MaxFitError;
    float scale, offsetX, offsetY, creaseDegrees = 10;
    bool protectPanelCorners = true;
    HatchEdgeVariant? hatchVariant = new("08 / PANEL ENDS", EndTaper: 9, TaperBoundaryOnly: true);
    readonly float[]? variantShade = null;


    static Vector3 V(double[] a) => new((float)a[0], (float)a[1], (float)a[2]);


    static Vector3 World(Vector3 a) => new(a.Y, a.Z, a.X);


    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;


    static Vector3 Unit(Vector3 a) => a.LengthSquared() > 1e-15f ? Vector3.Normalize(a) : Vector3.UnitZ;


    public BlueprintRenderer(Capture c, IReadOnlyDictionary<string, Texture> maps, string craftTitle, int view, CancellationToken token, ProjectionFrame? frame = null, int sampleRate = 1, Vector2 inkOrigin = default, Vector2? referenceSize = null)
    {
        capture = c;
        title = craftTitle;
        cancellation = token;
        W = frame?.Width ?? 2400;
        H = frame?.Height ?? 3000;
        this.sampleRate = sampleRate;
        this.inkOrigin = inkOrigin;
        this.referenceSize = referenceSize ?? new(W / (float)sampleRate, H / (float)sampleRate);
        depth = new float[W * H];
        tone = new float[W * H];
        luma = new float[W * H];
        face = new int[W * H];
        surfaceNormal = new Vector3[W * H];
        normalDeviation = new Vector3[W * H];
        structuralLines = new float[W * H];
        edgeDistance = new int[W * H];
        contourCoordinate = new float[W * H];
        hatchShade = new float[W * H];
        hatchClearance = new int[W * H];
        token.ThrowIfCancellationRequested();
        if (c.Meshes.Length == 0)
            throw new InvalidDataException("No craft geometry.");
        toward = Vector3.Normalize(view switch
        {
            1 => Vector3.UnitX,
            2 => -Vector3.UnitY,
            3 => Vector3.UnitZ,
            4 => new Vector3(1, -1, 1),
            _ => new Vector3(9, -17, 7)
        });
        right = Vector3.Normalize(Vector3.Cross(view == 3 ? Vector3.UnitY : Vector3.UnitZ, toward));
        up = Vector3.Cross(toward, right);
        if (frame != null)
        {
            right = frame.Right;
            up = frame.Up;
            toward = frame.Toward;
        }
        float minX = float.PositiveInfinity, maxX = float.NegativeInfinity, minY = minX, maxY = maxX;
        foreach (var m in c.Meshes)
        {
            token.ThrowIfCancellationRequested();
            var src = m.SourceVertices.Select(V).ToArray();
            var dst = m.Vertices.Select(V).ToArray();
            // Recover the already-captured affine pose from exact matching vertex order.
            int a = 0, b = 0, d = 0;
            float area = 0;
            for (int j = 0; j < m.Indices.Length; j += 3)
            {
                int i0 = m.Indices[j], i1 = m.Indices[j + 1], i2 = m.Indices[j + 2];
                float test = Vector3.Cross(src[i1] - src[i0], src[i2] - src[i0]).LengthSquared();
                if (test > area)
                {
                    area = test;
                    a = i0;
                    b = i1;
                    d = i2;
                }
            }
            if (area < 1e-15)
                throw new InvalidDataException("Degenerate source mesh");
            static Matrix4x4 Basis(Vector3 x, Vector3 y, Vector3 z) => new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
            var e1 = src[b] - src[a];
            var e2 = src[d] - src[a];
            var f1 = dst[b] - dst[a];
            var f2 = dst[d] - dst[a];
            Matrix4x4.Invert(Basis(e1, e2, Unit(Vector3.Cross(e1, e2))), out var inv);
            float k = MathF.Sqrt(f1.Length() * f2.Length() / (e1.Length() * e2.Length()));
            var transform = inv * Basis(f1, f2, Unit(Vector3.Cross(f1, f2)) * k);
            var reflected = inv * Basis(f1, f2, -Unit(Vector3.Cross(f1, f2)) * k);
            float Error(Matrix4x4 t) => src.Select((v, i) => Vector3.Distance(Vector3.TransformNormal(v - src[a], t) + dst[a], dst[i])).Max();
            if (Error(reflected) < Error(transform))
                transform = reflected;
            float error = Error(transform);
            MaxFitError = Math.Max(MaxFitError, error);
            if (error > .001f)
                throw new InvalidDataException($"Source pose mismatch {m.Name}: {error}");
            Matrix4x4.Invert(transform, out var normInv);
            var normalMatrix = Matrix4x4.Transpose(normInv);
            var norms = m.SourceNormals.Select(v => World(Unit(Vector3.TransformNormal(V(v), normalMatrix)))).ToArray();
            var pos = dst.Select(World).ToArray();
            var uv = m.UV.Select(v => new Vector2((float)v[0], (float)v[1])).ToArray();
            geometry.Add((pos, norms, uv));
            foreach (var p in pos)
            {
                float x = Vector3.Dot(p, right), y = Vector3.Dot(p, up);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
            foreach (var info in new[] { m.NormalTexture, m.DiffuseTexture })
                if (info != null && !textures.ContainsKey(info.Path))
                {
                    textures[info.Path] = maps[info.Path];
                }
        }
        scale = Math.Min(W * .79f / Math.Max(maxX - minX, .001f), H * .68f / Math.Max(maxY - minY, .001f));
        offsetX = W * .5f - (minX + maxX) * .5f * scale;
        offsetY = H * .455f + (minY + maxY) * .5f * scale;
        if (frame != null)
        {
            scale = frame.Scale;
            offsetX = W * .5f - Vector3.Dot(frame.Center, right) * scale;
            offsetY = H * .5f + Vector3.Dot(frame.Center, up) * scale;
        }
        Array.Fill(depth, float.NegativeInfinity);
        Array.Fill(face, -1);
    }


    Vector3 Project(Vector3 p) => new(Vector3.Dot(p, right) * scale + offsetX, offsetY - Vector3.Dot(p, up) * scale, Vector3.Dot(p, toward));


    public void Rasterize()
    {
        for (int mi = 0; mi < capture.Meshes.Length; mi++)
        {
            cancellation.ThrowIfCancellationRequested();
            var m = capture.Meshes[mi];
            var g = geometry[mi];
            var pp = g.Pos.Select(Project).ToArray();
            for (int ti = 0; ti < m.Indices.Length; ti += 3)
            {
                if ((ti & 1023) == 0)
                    cancellation.ThrowIfCancellationRequested();
                int a = m.Indices[ti], b = m.Indices[ti + 1], c = m.Indices[ti + 2];
                var p0 = pp[a];
                var p1 = pp[b];
                var p2 = pp[c];
                Vector2 e1 = new(p1.X - p0.X, p1.Y - p0.Y), e2 = new(p2.X - p0.X, p2.Y - p0.Y);
                float det = Cross(e1, e2);
                if (Math.Abs(det) < .00001)
                    continue;
                var fn = Unit(Vector3.Cross(g.Pos[b] - g.Pos[a], g.Pos[c] - g.Pos[a]));
                int id = Triangles.Count;
                Triangles.Add(new(mi, fn));
                var dpdx = ((g.Pos[b] - g.Pos[a]) * e2.Y - (g.Pos[c] - g.Pos[a]) * e1.Y) / det;
                var dpdy = (-(g.Pos[b] - g.Pos[a]) * e2.X + (g.Pos[c] - g.Pos[a]) * e1.X) / det;
                var duvdx = ((g.UV[b] - g.UV[a]) * e2.Y - (g.UV[c] - g.UV[a]) * e1.Y) / det;
                var duvdy = (-(g.UV[b] - g.UV[a]) * e2.X + (g.UV[c] - g.UV[a]) * e1.X) / det;
                int x0 = Math.Clamp((int)MathF.Floor(Math.Min(p0.X, Math.Min(p1.X, p2.X))), 0, W - 1), x1 = Math.Clamp((int)MathF.Ceiling(Math.Max(p0.X, Math.Max(p1.X, p2.X))), 0, W - 1);
                int y0 = Math.Clamp((int)MathF.Floor(Math.Min(p0.Y, Math.Min(p1.Y, p2.Y))), 0, H - 1), y1 = Math.Clamp((int)MathF.Ceiling(Math.Max(p0.Y, Math.Max(p1.Y, p2.Y))), 0, H - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        Vector2 point = new(x + .5f - p0.X, y + .5f - p0.Y);
                        float v = Cross(point, e2) / det, w = Cross(e1, point) / det, u = 1 - v - w;
                        if (u < -.00001 || v < -.00001 || w < -.00001)
                            continue;
                        float z = u * p0.Z + v * p1.Z + w * p2.Z;
                        int q = y * W + x;
                        if (z <= depth[q])
                            continue;
                        depth[q] = z;
                        face[q] = id;
                        var n = Unit(g.Norm[a] * u + g.Norm[b] * v + g.Norm[c] * w);
                        var uv = g.UV[a] * u + g.UV[b] * v + g.UV[c] * w;
                        surfaceNormal[q] = n;
                        normalDeviation[q] = default;
                        tone[q] = 0;
                        luma[q] = 0;
                        contourCoordinate[q] = (g.Pos[a].Z * u + g.Pos[b].Z * v + g.Pos[c].Z * w) * scale;
                        if (m.Surface == "structure")
                            continue;
                        var tex = m.NormalTexture is { } normalInfo ? textures[normalInfo.Path].Sample(uv) : new Vector3(.5f, .5f, 1);
                        float nx = -(tex.X * 2 - 1), ny = tex.Y * 2 - 1, nz = MathF.Sqrt(Math.Max(0, 1 - nx * nx - ny * ny));
                        // Same cotangent-frame construction and BC5 X flip as KSA SharedFrag.glsl.
                        var dp2p = Vector3.Cross(dpdy, n);
                        var dp1p = Vector3.Cross(n, dpdx);
                        var tangent = dp2p * duvdx.X + dp1p * duvdy.X;
                        var bitangent = dp2p * duvdx.Y + dp1p * duvdy.Y;
                        float invmax = 1 / MathF.Sqrt(Math.Max(1e-15f, Math.Max(tangent.LengthSquared(), bitangent.LengthSquared())));
                        var normal = Unit(tangent * (nx * invmax) + bitangent * (ny * invmax) + n * nz);
                        surfaceNormal[q] = normal;
                        normalDeviation[q] = normal - n;
                        var light = Unit(new Vector3(-.8f, -1, .8f));
                        tone[q] = m.Surface == "solar" ? (m.NormalTexture == null ? 0 : Math.Clamp(.09f + Vector3.Dot(normal - n, light) * 6f, 0, .28f)) : Math.Clamp(.5f - Vector3.Dot(normal, light) * .5f, 0, 1);
                        var col = m.DiffuseTexture is { } diffuseInfo ? textures[diffuseInfo.Path].Sample(uv) : new Vector3(.5f);
                        luma[q] = Vector3.Dot(col, new Vector3(.2126f, .7152f, .0722f));
                    }
            }
        }
        Console.WriteLine($"Rasterized {Triangles.Count} triangles with native material normals.");
    }


    bool Foil(int q) => q >= 0 && q < face.Length && face[q] >= 0 && capture.Meshes[Triangles[face[q]].Mesh].Surface == "foil";


    bool SameCloth(int a, int b) => Foil(a) && Foil(b) && Triangles[face[a]].Mesh == Triangles[face[b]].Mesh;


    public void PrepareStructure(float degrees = 10)
    {
        // Restore the original High study's 10-degree geometry threshold on ALL
        // materials. Material detail is additive and never substitutes for edges.
        creaseDegrees = degrees;
        Array.Clear(structuralLines);
        float cos = MathF.Cos(degrees * MathF.PI / 180);
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                if (x == 1 || x == 5)
                    cancellation.ThrowIfCancellationRequested();
                int q = y * W + x, id = face[q];
                if (id < 0)
                    continue;
                var t = Triangles[id];
                float line = 0;
                foreach (int j in new[] { q - 1, q + 1, q - W, q + W })
                {
                    if (face[j] < 0)
                    {
                        line = 1;
                        continue;
                    }
                    var other = Triangles[face[j]];
                    if (t.Mesh != other.Mesh || Vector3.Dot(t.Face, other.Face) < cos)
                        line = Math.Max(line, .8f);
                }
                structuralLines[q] = line;
            }
        // Chebyshev distance to structure or non-cloth pixels. Five clear pixels
        // protect panel edges, hardware boundaries and silhouettes from wrinkles.
        for (int q = 0; q < edgeDistance.Length; q++)
            edgeDistance[q] = !Foil(q) || structuralLines[q] > 0 ? 0 : 10000;
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                if (x == 1 || x == 5)
                    cancellation.ThrowIfCancellationRequested();
                int q = y * W + x;
                edgeDistance[q] = Math.Min(edgeDistance[q], 1 + Math.Min(Math.Min(edgeDistance[q - 1], edgeDistance[q - W]), Math.Min(edgeDistance[q - W - 1], edgeDistance[q - W + 1])));
            }
        for (int y = H - 2; y > 0; y--)
            for (int x = W - 2; x > 0; x--)
            {
                if (x == 1 || x == 5)
                    cancellation.ThrowIfCancellationRequested();
                int q = y * W + x;
                edgeDistance[q] = Math.Min(edgeDistance[q], 1 + Math.Min(Math.Min(edgeDistance[q + 1], edgeDistance[q + W]), Math.Min(edgeDistance[q + W - 1], edgeDistance[q + W + 1])));
            }
    }


    int PreservePanelCorners()
    {
        // Shallow, near-vertical folds in the captured wrap geometry can fall
        // below the general detail threshold. Preserve long runs independently.
        var candidate = new bool[W * H];
        float cos = MathF.Cos(7 * MathF.PI / 180);
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                if (x == 1 || x == 5)
                    cancellation.ThrowIfCancellationRequested();
                int q = y * W + x;
                if (!Foil(q))
                    continue;
                var a = Triangles[face[q]];
                foreach (int j in new[] { q - 1, q + 1, q - W, q + W })
                {
                    if (!SameCloth(q, j))
                        continue;
                    var b = Triangles[face[j]];
                    if (Vector3.Dot(a.Face, b.Face) >= cos)
                        continue;
                    var axis = Vector3.Cross(a.Face, b.Face);
                    if (Math.Abs(Vector3.Dot(axis, up)) > 3 * Math.Abs(Vector3.Dot(axis, right)))
                        candidate[q] = true;
                }
            }
        int count = 0;
        var visited = new bool[W * H];
        int[] offsets = [-W - 1, -W, -W + 1, -1, 1, W - 1, W, W + 1];
        for (int q = 0; q < candidate.Length; q++)
        {
            if (!candidate[q] || visited[q])
                continue;
            var component = new List<int> { q };
            visited[q] = true;
            for (int i = 0; i < component.Count; i++)
                foreach (int step in offsets)
                {
                    int j = component[i] + step;
                    if (j >= 0 && j < candidate.Length && candidate[j] && !visited[j])
                    {
                        visited[j] = true;
                        component.Add(j);
                    }
                }
            if (component.Max(p => p / W) - component.Min(p => p / W) < 150 * Math.Min(referenceSize.X / 2400f, referenceSize.Y / 3000f) * sampleRate)
                continue;
            foreach (int p in component)
                structuralLines[p] = 1;
            count++;
        }
        Console.WriteLine($"Protected {count} long shallow panel-fold runs from captured geometry.");
        return count;
    }


    void PrepareHatching()
    {
        var horizontal = new Vector3[W * H];
        var light = Unit(-right * .8f + up * .9f + toward * .35f);
        int radius = 5 * sampleRate;
        bool Same(int a, int b) => face[b] >= 0 && Triangles[face[a]].Mesh == Triangles[face[b]].Mesh;
        for (int y = radius; y < H - radius; y++)
            for (int x = radius; x < W - radius; x++)
            {
                if (x == radius)
                    cancellation.ThrowIfCancellationRequested();
                int q = y * W + x;
                if (face[q] < 0)
                    continue;
                Vector3 sum = default;
                float weights = 0;
                for (int k = -radius; k <= radius; k++)
                    if (Same(q, q + k))
                    {
                        float w = radius + 1 - Math.Abs(k);
                        sum += surfaceNormal[q + k] * w;
                        weights += w;
                    }
                horizontal[q] = sum / Math.Max(weights, 1);
            }
        for (int y = radius; y < H - radius; y++)
            for (int x = radius; x < W - radius; x++)
            {
                if (x == radius)
                    cancellation.ThrowIfCancellationRequested();
                int q = y * W + x;
                if (face[q] < 0)
                    continue;
                Vector3 sum = default;
                float weights = 0;
                for (int k = -radius; k <= radius; k++)
                    if (Same(q, q + k * W))
                    {
                        float w = radius + 1 - Math.Abs(k);
                        sum += horizontal[q + k * W] * w;
                        weights += w;
                    }
                hatchShade[q] = Math.Clamp((.72f - Vector3.Dot(Unit(sum / Math.Max(weights, 1)), light)) / 1.1f, 0, 1);
            }
        for (int q = 0; q < face.Length; q++)
            hatchClearance[q] = face[q] < 0 || structuralLines[q] > 0 || capture.Meshes[Triangles[face[q]].Mesh].Surface == "solar" ? 0 : 10000;
        for (int y = 1; y < H - 1; y++)
            for (int x = 1; x < W - 1; x++)
            {
                int q = y * W + x;
                hatchClearance[q] = Math.Min(hatchClearance[q], 1 + Math.Min(Math.Min(hatchClearance[q - 1], hatchClearance[q - W]), Math.Min(hatchClearance[q - W - 1], hatchClearance[q - W + 1])));
            }
        for (int y = H - 2; y > 0; y--)
            for (int x = W - 2; x > 0; x--)
            {
                int q = y * W + x;
                hatchClearance[q] = Math.Min(hatchClearance[q], 1 + Math.Min(Math.Min(hatchClearance[q + 1], hatchClearance[q + W]), Math.Min(hatchClearance[q + W - 1], hatchClearance[q + W + 1])));
            }
    }


    float HatchInk(int q, int x, int y, int mode)
    {
        int clearance = (hatchVariant?.Clearance ?? (protectPanelCorners ? 6 : 3)) * sampleRate;
        if (hatchClearance[q] <= clearance)
            return 0;
        float s = (variantShade ?? hatchShade)[q];
        static float Stroke(float coordinate, float spacing, float radius = .65f)
        {
            float phase = coordinate - spacing * MathF.Floor(coordinate / spacing);
            return Math.Clamp(radius + .5f - Math.Min(phase, spacing - phase), 0, 1);
        }
        float px = x / (float)sampleRate + inkOrigin.X, py = y / (float)sampleRate + inkOrigin.Y;
        float diag = (px + py) * .7071068f, cross = (px - py) * .7071068f, ink = 0;
        if (mode == 1 && s > .30f)
            ink = Stroke(diag, 15) * (.22f + .18f * s);
        if (mode == 2)
        {
            if (s > .28f)
                ink = Stroke(diag, 15) * .32f;
            if (s > .60f)
                ink = Math.Max(ink, Stroke(cross, 17) * .36f);
        }
        if (mode == 3)
        {
            if (s > .28f)
                ink = Stroke(contourCoordinate[q] / sampleRate, 12) * (.24f + .16f * s);
            if (s > .70f)
                ink = Math.Max(ink, Stroke(diag, 22) * .26f);
        }
        if (mode == 4 && s > .30f)
        {
            float row = MathF.Floor(diag / 14);
            float along = cross + row * 9;
            float phase = along - 45 * MathF.Floor(along / 45);
            float taper = Math.Clamp(Math.Min(phase, 29 - phase) / 5, 0, 1);
            float wobble = .55f * MathF.Sin(cross * .06f + row * 1.7f);
            ink = Stroke(diag + wobble, 14) * taper * (.28f + .15f * s);
            if (s > .72f)
                ink = Math.Max(ink, Stroke(cross, 24) * .22f);
        }
        if (mode == 5)
        {
            float Gate(float threshold, bool crossing, int direction)
            {
                threshold += hatchVariant?.ShadowShift ?? 0;
                float feather = hatchVariant != null && (!hatchVariant.CrossOnly || crossing) ? hatchVariant.Feather : 0;
                float gate = feather > 0 ? Math.Clamp((s - threshold) / feather, 0, 1) : (s > threshold ? 1 : 0);
                if (feather > 0)
                    gate = gate * gate * (3 - 2 * gate);
                int taper = (hatchVariant?.EndTaper ?? 0) * sampleRate;
                if (gate > 0 && taper > 0 && (!(hatchVariant?.TaperCrossOnly ?? false) || crossing))
                {
                    int distance = taper;
                    for (int step = 1; step <= taper; step++)
                    {
                        bool ends = false;
                        foreach (int sign in new[] { -1, 1 })
                        {
                            int xx = x + step * sign, yy = y + step * sign * direction;
                            if (xx <= 0 || xx >= W - 1 || yy <= 0 || yy >= H - 1)
                            {
                                ends = true;
                                break;
                            }
                            int at = yy * W + xx;
                            bool shadeEnd = !(hatchVariant?.TaperBoundaryOnly ?? false) && (variantShade ?? hatchShade)[at] <= threshold;
                            if (face[at] < 0 || Triangles[face[at]].Mesh != Triangles[face[q]].Mesh || hatchClearance[at] <= clearance || shadeEnd)
                            {
                                ends = true;
                                break;
                            }
                        }
                        if (ends)
                        {
                            distance = step;
                            break;
                        }
                    }
                    gate *= MathF.Pow(distance / (float)taper, hatchVariant?.TaperPower ?? 1);
                }
                return gate;
            }
            ink = Stroke(diag, 13) * .38f * Gate(.55f, false, -1);
            ink = Math.Max(ink, Stroke(cross, 14) * .40f * Gate(.72f, true, 1));
            ink = Math.Max(ink, Stroke(diag + 6.5f, 13) * .28f * Gate(.88f, true, -1));
            if (hatchVariant?.EdgeFade > 0)
                ink *= Math.Clamp((hatchClearance[q] - clearance) / (float)(hatchVariant.EdgeFade * sampleRate), 0, 1);
        }
        return ink;
    }
}
