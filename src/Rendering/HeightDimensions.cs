using System.Globalization;
using System.Numerics;
namespace Blueprint.Rendering;

sealed record CraftHeight(float Bottom, float Top);
sealed record HeightDimension(int ViewId, bool Left = true, int Gap = 90, float? X = null);
sealed record HeightMark(float X, float Top, float Bottom, string Text, Box TextBounds, Box Bounds);
static class HeightDimensions
{
    public const int Font = 36;

    public static CraftHeight Measure(Capture capture)
    {
        var z = capture.Meshes.SelectMany(m => m.Vertices).Select(v => (float)v[0]).ToArray();
        return new(z.Min(), z.Max());
    }

    public static bool Eligible(ViewRegion view) => view.Id < 1000 && view.Frame is { } f && Math.Abs(f.Up.Z) > .1f && Math.Abs(f.Right.Z) < .001f;

    public static HeightMark Place(RenderScene scene, HeightDimension dimension, bool validate = true)
    {
        var view = scene.Views.SingleOrDefault(v => v.Id == dimension.ViewId);
        if (view == null || !Eligible(view) || scene.CraftHeight == null)
            throw new InvalidOperationException("Craft height needs a front, side, isometric or three-quarter view. Top looks down the height axis.");
        var frame = view.Frame!;
        var height = scene.ViewHeights.GetValueOrDefault(view.Id) ?? scene.CraftHeight;
        var occupied = scene.Visible.Where(p => p.Key.View == view.Id).Select(p => p.Value.Bounds).ToArray();
        if (occupied.Length == 0) occupied = [view.Bounds];
        float Project(float z) => view.Bounds.Y + frame.Height * .5f - (z - frame.Center.Z) * frame.Up.Z * frame.Scale;
        float top = Math.Min(Project(height.Top), Project(height.Bottom)), bottom = Math.Max(Project(height.Top), Project(height.Bottom));
        string text = (height.Top - height.Bottom).ToString("0.##", CultureInfo.InvariantCulture) + " m";
        float width = text.Length * NativeText.MonoWidth(Font), x = dimension.X ?? (dimension.Left ? occupied.Min(b => b.X) - dimension.Gap : occupied.Max(b => b.Right) + dimension.Gap);
        var label = new Box(x - width / 2, (top + bottom - Font) / 2, width, Font);
        var bounds = new Box(Math.Min(x - 24, label.X) - 8, top - 2, Math.Max(48, width) + 16, bottom - top + 4);
        var area = FrameLayout.Area(scene.Options);
        if (validate && !scene.Options.FreeLayout && (bottom - top < Font + 32 || !PageLayout.Fits(bounds, scene.Options)))
            throw new InvalidOperationException("Height dimension needs more room. Try the other side, reduce its gap, or reduce View scale.");
        return new(x, top, bottom, text, label, bounds);
    }

    public static HeightDimension Move(RenderScene scene, HeightDimension dimension, float x)
    {
        var mark = Place(scene, dimension, false);
        var area = FrameLayout.Area(scene.Options);
        if (!scene.Options.FreeLayout) x = Math.Clamp(x, area.X + mark.Bounds.Width / 2, area.Right - mark.Bounds.Width / 2);
        var occupied = scene.Visible.Where(p => p.Key.View == dimension.ViewId).Select(p => p.Value.Bounds).ToArray();
        if (occupied.Length == 0) occupied = [scene.Views.Single(v => v.Id == dimension.ViewId).Bounds];
        float left = occupied.Min(b => b.X), right = occupied.Max(b => b.Right);
        bool onLeft = x < (left + right) / 2;
        return dimension with
        {
            X = x,
            Left = onLeft,
            Gap = (int)MathF.Round(onLeft ? left - x : x - right)
        };
    }

    public static bool Hit(HeightMark mark, Vector2 point, float tolerance) => mark.TextBounds.Contains(point)
        || Math.Abs(point.X - mark.X) <= tolerance && point.Y >= mark.Top && point.Y <= mark.Bottom
        || Math.Abs(point.X - mark.X) <= 22 + tolerance && (Math.Abs(point.Y - mark.Top) <= tolerance || Math.Abs(point.Y - mark.Bottom) <= tolerance);

    public static void Paint(Canvas canvas, RenderScene scene, PartLabel[] labels)
    {
        foreach (var dimension in scene.Dimensions)
        {
            var mark = Place(scene, dimension);
            if (!scene.Options.FreeLayout && (scene.Specs.Any(b => b.Bounds.Overlaps(mark.Bounds)) || scene.Details.Any(d => d.Definition.Bounds.Overlaps(mark.Bounds)) || PrintTables.Panels(scene, labels).Any(t => t.Bounds.Overlaps(mark.Bounds))))
                throw new InvalidOperationException("Move the height dimension, table or detail so they do not overlap.");
            int x = (int)mark.X, top = (int)mark.Top, bottom = (int)mark.Bottom;
            canvas.Line(x, top, x, (int)mark.TextBounds.Y - 10, 1);
            canvas.Line(x, (int)mark.TextBounds.Bottom + 10, x, bottom, 1);
            canvas.Line(x - 22, top, x + 22, top, 1);
            canvas.Line(x - 22, bottom, x + 22, bottom, 1);
            canvas.Text(mark.Text, (int)mark.TextBounds.X, (int)mark.TextBounds.Y, Font, 1);
        }
    }
}
