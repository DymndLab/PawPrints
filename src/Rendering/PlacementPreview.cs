using System.Numerics;
namespace Blueprint.Rendering;

sealed record PlacementPreview(int Width, int Height, byte[] Rgba, Box Bounds, Vector2? CaptionAnchor = null);
static class PlacementPreviews
{

    public static PlacementPreview View(PoseSource source, DrawingView view, float ratio, PrintOptions options, PartPose[] poses, CancellationToken token)
    {
        var capture = PartPoses.Exterior(source, PartPoses.Apply(source, poses, view.Id));
        var region = DrawingPlacements.Plan(capture, options, new(ratio, [view with { Parent = null }]))[0];
        var frame = region.Frame!;
        var tile = new BlueprintRenderer(capture, source.Maps, "Placement", view.Camera, token, frame).RenderView(_ => { });
        int h = tile.Height + (options.Captions ? ViewCaption.Band : 0), w = tile.Width;
        var canvas = new Canvas(new byte[w * h * 3], w, h, new(255));
        for (int y = 0; y < tile.Height; y++)
            for (int x = 0; x < w; x++)
                canvas.Ink(x, y, tile.Ink[y * w + x] / 255f);
        Vector2? captionAnchor = null;
        if (options.Captions)
        {
            int left = w, top = tile.Height, right = -1, bottom = -1;
            for (int y = 0; y < tile.Height; y++)
                for (int x = 0; x < w; x++)
                    if (tile.MeshOwners[y * w + x] >= 0)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
            if (right >= left)
            {
                var mark = ViewCaption.Place(region with
                {
                    Bounds = new(0, 0, w, tile.Height)
                }, new(left, top, right - left + 1, bottom - top + 1));
                canvas.Text(region.Name, (int)mark.Bounds.X, (int)mark.Bounds.Y, mark.FontSize, 1);
                captionAnchor = ViewAlignment.Anchor(mark.Bounds, mark.FontSize);
            }
        }
        NativeText.Apply(canvas.Pixels, w, h, canvas.Runs);
        var ink = SheetLayout.Colors(options.Theme).Ink;
        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            rgba[i * 4] = (byte)ink.X;
            rgba[i * 4 + 1] = (byte)ink.Y;
            rgba[i * 4 + 2] = (byte)ink.Z;
            rgba[i * 4 + 3] = canvas.Pixels[i * 3];
        }
        return new(w, h, rgba, region.Bounds with
        {
            Height = h
        }, captionAnchor);
    }

    public static PlacementPreview Panel(RenderScene scene, Box bounds, Action<Canvas> paint)
    {
        var colors = SheetLayout.Colors(scene.Options.Theme);
        var canvas = new Canvas(scene.Width, scene.Height, colors.Paper, colors.Ink, scene.Options);
        paint(canvas);
        NativeText.Apply(canvas.Pixels, scene.Width, scene.Height, canvas.Runs);
        int w = (int)MathF.Ceiling(bounds.Width), h = (int)MathF.Ceiling(bounds.Height);
        var rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int xx = (int)bounds.X + x, yy = (int)bounds.Y + y;
                if (xx < 0 || yy < 0 || xx >= scene.Width || yy >= scene.Height)
                    continue;
                int q = (yy * scene.Width + xx) * 3, d = (y * w + x) * 4;
                rgba[d] = canvas.Pixels[q];
                rgba[d + 1] = canvas.Pixels[q + 1];
                rgba[d + 2] = canvas.Pixels[q + 2];
                rgba[d + 3] = 255;
            }
        return new(w, h, rgba, bounds);
    }
}
