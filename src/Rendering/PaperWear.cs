using System.Numerics;

namespace Blueprint.Rendering;

// A repeatable paper texture in normalized sheet coordinates. Build the wear
// pattern once per surface so rendering a 4K page needs only interpolation.
sealed class PaperWear
{
    const int Columns = 256, Rows = 192;

    readonly float[] values = new float[Columns * Rows];
    readonly int[] column;
    readonly float[] across;
    readonly float yScale;
    readonly Vector3 tint;

    public PaperWear(int width, int height, float strength, Vector3 paper, string? title)
    {
        column = new int[width];
        across = new float[width];
        float xScale = (Columns - 1.001f) / Math.Max(1, width - 1);
        yScale = (Rows - 1.001f) / Math.Max(1, height - 1);
        for (int x = 0; x < width; x++)
        {
            float position = x * xScale;
            column[x] = (int)position;
            across[x] = position - column[x];
        }

        tint = paper.X < 100 ? new(55, 52, 44) : new(-37, -38, -43);
        uint seed = 2166136261u;
        foreach (char c in title ?? "")
            seed = unchecked((seed ^ c) * 16777619u);
        float stainX = .16f + .68f * (seed & 0xffff) / 65535f;
        float stainY = .14f + .48f * (seed >> 16) / 65535f;

        for (int y = 0; y < Rows; y++)
            for (int x = 0; x < Columns; x++)
                values[y * Columns + x] = Pattern(x / (Columns - 1f), y / (Rows - 1f), stainX, stainY) * strength;
    }

    public Row AtRow(int y)
    {
        float position = y * yScale;
        int row = (int)position;
        return new(values, column, across, row * Columns, position - row, tint);
    }

    public readonly struct Row(float[] values, int[] columns, float[] across, int start, float down, Vector3 tint)
    {
        public Vector3 At(int x)
        {
            int i = start + columns[x];
            float top = Lerp(values[i], values[i + 1], across[x]);
            float bottom = Lerp(values[i + Columns], values[i + Columns + 1], across[x]);
            return tint * Lerp(top, bottom, down);
        }
    }

    static float Pattern(float x, float y, float stainX, float stainY)
    {
        float edgeX = MathF.Exp(-Math.Min(x, 1 - x) / .065f);
        float edgeY = MathF.Exp(-Math.Min(y, 1 - y) / .065f);
        float edge = Math.Max(edgeX, edgeY) * .32f + edgeX * edgeY * .12f;

        float dx = (x - stainX) / .080f;
        float dy = (y - stainY) / .065f;
        float mottle = Noise(x * 34, y * 28);
        float stain = Math.Clamp(1 - dx * dx - dy * dy + (mottle - .5f) * .38f, 0, 1);
        stain *= stain * (.24f + .12f * mottle);
        return edge + stain;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    static float Noise(float x, float y)
    {
        int i = (int)x, j = (int)y;
        float u = x - i, v = y - j;
        u *= u * (3 - 2 * u);
        v *= v * (3 - 2 * v);
        return Lerp(Lerp(Hash(i, j), Hash(i + 1, j), u), Lerp(Hash(i, j + 1), Hash(i + 1, j + 1), u), v);
    }

    static float Hash(int x, int y)
    {
        uint n = unchecked((uint)x * 374761393u + (uint)y * 668265263u + 97571u);
        n = (n ^ (n >> 13)) * 1274126177u;
        return ((n ^ (n >> 16)) & 65535) / 65535f;
    }
}
