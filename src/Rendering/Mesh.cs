using System.Numerics;
using System.Text.Json.Serialization;

namespace Blueprint.Rendering;

readonly record struct MeshPoint(double X, double Y, double Z)
{
    public bool Finite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);
    public Vector3 Native => new((float)X, (float)Y, (float)Z);
    public Vector3 World => new((float)Y, (float)Z, (float)X);

    public static MeshPoint FromWorld(Vector3 p) => new(p.Z, p.X, p.Y);
    public static MeshPoint operator +(MeshPoint a, MeshPoint b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
}

readonly record struct MeshTransform(MeshPoint X, MeshPoint Y, MeshPoint Z, MeshPoint Origin)
{
    public bool Finite => X.Finite && Y.Finite && Z.Finite && Origin.Finite;

    public MeshPoint Apply(Vector3 p) => new(
        p.X * X.X + p.Y * Y.X + p.Z * Z.X + Origin.X,
        p.X * X.Y + p.Y * Y.Y + p.Z * Z.Y + Origin.Y,
        p.X * X.Z + p.Y * Y.Z + p.Z * Z.Z + Origin.Z);

    public Matrix4x4 WorldBasis => new(
        (float)X.Y, (float)X.Z, (float)X.X, 0,
        (float)Y.Y, (float)Y.Z, (float)Y.X, 0,
        (float)Z.Y, (float)Z.Z, (float)Z.X, 0,
        0, 0, 0, 1);
}

// Capture owns these buffers. Instances of a native primitive share them for the
// lifetime of the detached snapshot, including in print poses and exploded views.
sealed record MeshSource(Vector3[] Positions, Vector3[] Normals, Vector2[] UV, int[] Indices);

sealed record MeshInstance(MeshSource Source, MeshTransform Transform, Matrix4x4? Pose = null, MeshPoint Offset = default)
{
    public MeshPoint PointAt(int index)
    {
        var p = Transform.Apply(Source.Positions[index]);
        if (Pose is { } pose)
            p = MeshPoint.FromWorld(Vector3.Transform(p.World, pose));
        return p + Offset;
    }

    public Matrix4x4 NormalMatrix()
    {
        var basis = Transform.WorldBasis;
        if (Pose is { } pose)
        {
            pose.Translation = Vector3.Zero;
            basis *= pose;
        }
        if (!Matrix4x4.Invert(basis, out var inverse))
            throw new InvalidDataException("Invalid source mesh transform.");
        return Matrix4x4.Transpose(inverse);
    }
}

// The positional arrays keep the captured-asset JSON and procedural mesh format
// compatible. Native model meshes leave these arrays empty and use Instance.
record Mesh(string Name, double[][] Vertices, int[] Indices, double[][] SourceVertices,
    double[][] SourceNormals, double[][] UV, string Surface, TextureInfo? NormalTexture, TextureInfo? DiffuseTexture)
{
    public Vector3? SolarDirection { get; init; }
    public uint[] ConnectedParts { get; init; } = [];
    [JsonIgnore] public uint? LineworkNode { get; init; }
    [JsonIgnore] public bool FeatureInk { get; init; }
    [JsonIgnore] public MeshInstance? Instance { get; init; }
    [JsonIgnore] public int VertexCount => Instance?.Source.Positions.Length ?? Vertices.Length;

    public MeshPoint PointAt(int index)
    {
        if (Instance is { } instance)
            return instance.PointAt(index);
        var p = Vertices[index];
        return new(p[0], p[1], p[2]);
    }

    public IEnumerable<Vector3> WorldVertices()
    {
        for (int i = 0; i < VertexCount; i++)
            yield return PointAt(i).World;
    }

    public Vector3 LocalPosition(int index) => Instance is { } instance
        ? instance.Source.Positions[index] : LegacyVector(SourceVertices[index]);

    public Vector3 LocalNormal(int index) => Instance is { } instance
        ? instance.Source.Normals[index] : LegacyVector(SourceNormals[index]);

    static Vector3 LegacyVector(double[] v) => new((float)v[0], (float)v[1], (float)v[2]);

    public Mesh Translate(Vector3 delta)
    {
        if (delta == Vector3.Zero)
            return this;
        if (Instance is { } instance)
            return this with { Instance = instance with { Offset = instance.Offset + MeshPoint.FromWorld(delta) } };
        return this with { Vertices = Vertices.Select(p => new[] { p[0] + delta.Z, p[1] + delta.X, p[2] + delta.Y }).ToArray() };
    }

    public Mesh ApplyPose(Matrix4x4 pose)
    {
        if (Instance is { } instance)
            return this with { Instance = instance with { Pose = pose } };
        return this with
        {
            Vertices = WorldVertices().Select(v =>
            {
                var p = Vector3.Transform(v, pose);
                return new double[] { p.Z, p.X, p.Y };
            }).ToArray()
        };
    }
}
