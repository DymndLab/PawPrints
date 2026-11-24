using System.Numerics;
namespace Blueprint.Rendering;

static class DetailFrames
{
    public static ExplodedDetail PrepareEdit(ExplodedDetail? previous, ExplodedDetail edited, PrintOptions options)
    {
        // Free-layout frames grow around moved parts. Retain the physical scale,
        // custom spread and coordinate origin so other parts cannot jump.
        if (options.FreeLayout || previous == null || edited.Sizing is not { } sizing)
            return edited;
        return previous with
        {
            Bounds = edited.Bounds,
            Caption = edited.Caption,
            Camera = edited.Camera,
            Sizing = edited.Sizing,
            Guides = edited.Guides,
            GuideLine = edited.GuideLine
        } == edited ? edited : edited with
        {
            Sizing = sizing with
            {
                Spread = null,
                Center = null
            }
        };
    }

    public static (int Width, int Height) TileSize(Vector2 extent, PrintOptions options)
    {
        double width = Math.Max(40, Math.Ceiling(extent.X) + 32), height = Math.Max(36, Math.Ceiling(extent.Y) + 32);
        if (!double.IsFinite(width + height) || width + 24 > 8192 || height + ExplodedPainting.CaptionSpace > 8192
            || width * height > (long)options.Page.Width * options.Page.Height)
            throw new InvalidOperationException("Exploded view exceeds the render tile resource budget. Reduce its scale.");
        return ((int)width, (int)height);
    }

    public static ExplodedDetail Resize(RenderedDetail detail, float factor, PrintOptions options, bool fixedTopLeft = false)
    {
        if (!float.IsFinite(factor) || factor <= 0 || detail.Sizing is not { } sizing)
            throw new InvalidOperationException("Invalid exploded view scale.");
        var extent = detail.Frame.ContentExtent ?? new Vector2(detail.Frame.Width - 32, detail.Frame.Height - 32);
        var size = TileSize(extent * factor, options);
        float ratio = sizing.Ratio / factor;
        if (!float.IsFinite(ratio) || ratio < .01f || ratio > 1000000)
            throw new InvalidOperationException("Choose a positive part scale between 1:0.01 and 1:1000000.");
        var box = detail.Definition.Bounds;
        var bounds = new Box(fixedTopLeft ? box.X : box.X + (detail.Frame.Width - size.Width) / 2f,
            fixedTopLeft ? box.Y : box.Y + (detail.Frame.Height - size.Height) / 2f,
            size.Width + 24, size.Height + ExplodedPainting.CaptionSpace);
        CanvasBounds.Require(bounds);
        return detail.Definition with
        {
            Bounds = bounds,
            Camera = detail.Frame,
            Sizing = sizing with
            {
                Ratio = ratio,
                Spread = (sizing.Spread ?? detail.Frame.Scale) * factor,
                Width = size.Width,
                Height = size.Height
            }
        };
    }
}
