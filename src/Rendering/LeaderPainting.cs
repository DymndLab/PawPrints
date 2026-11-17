using System.Numerics;
namespace Blueprint.Rendering;

// One print-pixel style for labels, flagnotes, table keys and performance leaders.
static class LeaderPainting
{

    public static void Paint(Canvas canvas, IReadOnlyList<Vector2> path, LeaderLine line, LeaderTip tip, float textGap = 0)
    {
        if (path.Count < 2)
            return;
        float total = 0;
        for (int i = 1; i < path.Count; i++)
            total += Vector2.Distance(path[i - 1], path[i]);
        if (total < .001f)
            return;
        Vector2 At(float distance)
        {
            for (int i = 1; i < path.Count; i++)
            {
                float length = Vector2.Distance(path[i - 1], path[i]);
                if (length < .001f)
                    continue;
                if (distance <= length)
                    return path[i - 1] + (path[i] - path[i - 1]) / length * distance;
                distance -= length;
            }
            return path[^1];
        }
        void Stroke(float from, float to)
        {
            float offset = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                float length = Vector2.Distance(a, b);
                float lo = Math.Max(from, offset), hi = Math.Min(to, offset + length);
                if (length > .001f && hi > lo)
                {
                    var direction = (b - a) / length;
                    var start = a + direction * (lo - offset);
                    var end = a + direction * (hi - offset);
                    canvas.Line((int)start.X, (int)start.Y, (int)end.X, (int)end.Y, .85f);
                }
                offset += length;
            }
        }
        float stop = Math.Max(0, total - (tip == LeaderTip.Dot ? 5 : tip == LeaderTip.None ? 0 : 16));
        if (line == LeaderLine.Solid && stop > textGap)
            Stroke(textGap, stop);
        else if (line != LeaderLine.Solid)
            for (float t = Math.Max(4, textGap + 2); t < stop; t += line == LeaderLine.Dotted ? 12 : 18)
            {
                if (line == LeaderLine.Dotted)
                    Disk(canvas, At(t), 1.6f, .85f);
                else
                    Stroke(t, Math.Min(t + 8, stop));
            }
        var edge = path[^1];
        var u = Vector2.UnitX;
        for (int i = path.Count - 1; i > 0; i--)
        {
            var delta = path[i] - path[i - 1];
            if (delta.LengthSquared() > .000001f)
            {
                u = Vector2.Normalize(delta);
                break;
            }
        }
        if (tip == LeaderTip.Dot)
            Disk(canvas, edge, 5, 1);
        else if (tip != LeaderTip.None)
        {
            var v = new Vector2(-u.Y, u.X);
            var p = edge - u * 16 + v * 6;
            var q = edge - u * 16 - v * 6;
            if (tip == LeaderTip.FilledTriangle)
                for (float t = 0; t <= 16; t += .5f)
                {
                    var center = edge - u * t;
                    var left = center + v * (t * 6 / 16);
                    var right = center - v * (t * 6 / 16);
                    canvas.Line((int)left.X, (int)left.Y, (int)right.X, (int)right.Y, 1);
                }
            canvas.Line((int)edge.X, (int)edge.Y, (int)p.X, (int)p.Y, 1);
            canvas.Line((int)edge.X, (int)edge.Y, (int)q.X, (int)q.Y, 1);
            canvas.Line((int)p.X, (int)p.Y, (int)q.X, (int)q.Y, 1);
        }
    }

    static void Disk(Canvas canvas, Vector2 p, float radius, float alpha)
    {
        for (int y = -(int)Math.Ceiling(radius); y <= radius; y++)
            for (int x = -(int)Math.Ceiling(radius); x <= radius; x++)
                if (x * x + y * y <= radius * radius)
                    canvas.Ink((int)p.X + x, (int)p.Y + y, alpha);
    }
}
