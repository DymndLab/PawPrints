namespace Blueprint.Rendering;

// Position is independent of the image boundary; size limits protect CPU loops
// and allocation arithmetic. Clipping is advice, never a placement veto.
static class CanvasBounds
{
    public static bool Safe(Box b) => float.IsFinite(b.X + b.Y + b.Width + b.Height)
        && Math.Abs(b.X) <= 1000000 && Math.Abs(b.Y) <= 1000000
        && b.Width >= 0 && b.Height >= 0 && b.Width <= 8192 && b.Height <= 8192;
    public static void Require(Box b)
    {
        if (!Safe(b))
            throw new InvalidOperationException("Drawing bounds exceed the coordinate or size safety budget.");
    }
    public static bool Clipped(Box b, PrintOptions options)
    {
        double visible = Math.Max(0, Math.Min(b.Right, options.Page.Width) - Math.Max(b.X, 0))
            * Math.Max(0, Math.Min(b.Bottom, options.Page.Height) - Math.Max(b.Y, 0));
        return b.Width > 0 && b.Height > 0 && visible < (double)b.Width * b.Height * .9;
    }
}
