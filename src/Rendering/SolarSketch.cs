using System.Numerics;
namespace Blueprint.Rendering;

// UV-aligned illustrative cells: selected study 05. These do not count physical cells.
static class SolarSketch
{
    // Some decorative cell meshes have no power module. A single-sided sheet
    // has a clear authored front. A closed slab has two plausible faces.
    internal static Vector3? SingleSidedAxis(Mesh mesh)
    {
        var sum = Vector3.Zero;
        float total = 0;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            int a = mesh.Indices[i], b = mesh.Indices[i + 1], c = mesh.Indices[i + 2];
            var origin = mesh.LocalPosition(a);
            var area = Vector3.Cross(mesh.LocalPosition(b) - origin, mesh.LocalPosition(c) - origin);
            var normal = mesh.LocalNormal(a) + mesh.LocalNormal(b) + mesh.LocalNormal(c);
            sum += Vector3.Dot(area, normal) < 0 ? -area : area;
            total += area.Length();
        }
        return sum.Length() > total * .9f ? Vector3.Normalize(sum) : null;
    }

    static float Hash(int x, int y, int salt)
    {
        uint h = unchecked((uint)(x * 374761393 + y * 668265263 + salt * 1274126177));
        h = unchecked((h ^ (h >> 13)) * 1274126177);
        return (h & 65535) / 65535f;
    }

    public static float Ink(Vector2 uv, Vector2 dx, Vector2 dy)
    {
        float nx = 48, ny = 27;
        float pitch = 1 / Math.Max(Math.Max(dx.Length(), dy.Length()), .00001f);
        while (Math.Max(nx, ny) > pitch / 3.2f && nx > 3)
        {
            nx *= .5f;
            ny *= .5f;
        }
        var a = new Vector2(uv.X * nx, uv.Y * ny);
        var footprint = new Vector2(
            Math.Max(.0001f, MathF.Sqrt(dx.X * dx.X + dy.X * dy.X) * nx),
            Math.Max(.0001f, MathF.Sqrt(dx.Y * dx.Y + dy.Y * dy.Y) * ny));
        return Math.Max(Lines(a.X, a.Y, footprint.X, 17), Lines(a.Y, a.X, footprint.Y, 29));
    }

    static float Lines(float across, float along, float footprint, int salt)
    {
        int line = (int)MathF.Round(across), segment = (int)MathF.Floor(along);
        float phase = along - segment;
        float start = .08f * Hash(line, segment, salt + 2);
        float end = 1 - .08f * Hash(line, segment, salt + 3);
        if (phase < start || phase > end)
            return 0;
        float distance = Math.Abs(across - line) / footprint;
        float alpha = Math.Clamp(.40f + .5f - distance, 0, 1);
        return alpha * (.42f + .35f * Hash(line, segment, salt));
    }
}
