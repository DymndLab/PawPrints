using System.Numerics;
namespace Blueprint.Rendering;

sealed record ScaleFit(float Maximum, string Warning);
static class ScaleAdjustment
{

    public static ScaleFit FindMaximum(Func<float, string?> issue)
    {
        string? warning = issue(400);
        if (warning == null)
            return new(400, "");
        // The opening preview is already accepted. Find the first obstruction
        // as its footprints grow about their fixed centres.
        float low = 100, high = 400;
        for (int i = 0; i < 24; i++)
        {
            float mid = (low + high) * .5f;
            var error = issue(mid);
            if (error == null)
                low = mid;
            else
            {
                high = mid;
                warning = error;
            }
        }
        return new(Math.Max(100, MathF.Floor(low * 10) / 10), warning);
    }

    public static ScaleFit ForMain(PoseSource source, RenderScene scene, DrawingPlacement drawing, Box[] obstacles)
    {
        var options = scene.Options;
        var unposed = PartPoses.Exterior(source, source.Assembly);
        var fit = scene.Poses.Length == 0 ? unposed : new Capture(drawing.Views.SelectMany(v =>
            PartPoses.Exterior(source, PartPoses.Apply(source, scene.Poses, v.Id)).Meshes).ToArray());
        var preliminary = DrawingPlacements.PreparePlan(unposed, options, drawing);
        var planned = DrawingPlacements.PreparePlan(fit, options, drawing);
        return FindMaximum(percent =>
        {
            try
            {
                float ratio = drawing.Ratio * 100 / percent;
                preliminary(ratio);
                var views = planned(ratio);
                DrawingPlacements.Validate(views, options);
                var changed = views.Where(v => scene.Views.FirstOrDefault(p => p.Id == v.Id)?.Bounds != v.Bounds).ToArray();
                DrawingPlacements.Validate(changed, options, obstacles);
                return null;
            }
            catch (InvalidOperationException e) { return e.Message; }
        });
    }

    public static ScaleFit Detail(DetailGeometry geometry, DetailSizing sizing, int view, PrintOptions options)
    {
        if (sizing.Spread is not { } spread || sizing.Center is not { } center)
            throw new InvalidOperationException("Generate the detail before adjusting its scale.");
        float scale = PrintScale.Density(options) / sizing.Ratio, maximum = 4;
        var axes = SheetLayout.Basis(view);
        void Limit(float location, float extent, float half)
        {
            if (extent > 0)
                maximum = Math.Min(maximum, (half - location) / extent);
            else if (extent < 0)
                maximum = Math.Min(maximum, (-half - location) / extent);
        }
        for (int i = 0; i < geometry.Capture.Meshes.Length; i++)
        {
            var part = geometry.MovedCenters[i];
            foreach (var vertex in geometry.Capture.Meshes[i].Vertices)
            {
                var offset = ExplodedGeometry.World(vertex) - part;
                Limit(Vector3.Dot(part - center, axes.Right) * spread, Vector3.Dot(offset, axes.Right) * scale, (sizing.Width - 32) * .5f);
                Limit(Vector3.Dot(part - center, axes.Up) * spread, Vector3.Dot(offset, axes.Up) * scale, (sizing.Height - 32) * .5f);
            }
        }
        return new(Math.Clamp(MathF.Floor(maximum * 1000) / 10, 100, 400), "These parts are too big for the detail box.");
    }
}
