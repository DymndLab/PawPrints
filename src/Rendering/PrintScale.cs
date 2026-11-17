using System.Globalization;
namespace Blueprint.Rendering;

static class PrintScale
{
    public const int Dpi = 300;
    // PNG pHYs expresses integer pixels per metre. KSA assembly coordinates are metres.
    public const int PixelsPerMetre = 11811;

    public static int Density(PrintOptions options)
    {
        // Fit the PNG uniformly inside 8 x 11 inch paper (rotated for landscape).
        bool landscape = options.Page.Width > options.Page.Height;
        double width = landscape ? 11 : 8, height = landscape ? 8 : 11;
        return (int)Math.Round(Math.Max(options.Page.Width / width, options.Page.Height / height) / .0254);
    }

    public static string Paper(PrintOptions options) => options.Page.Width > options.Page.Height ? "11 x 8 in" : "8 x 11 in";

    public static float FitScale(float maximum, PrintOptions options)
    {
        if (!float.IsFinite(maximum) || maximum <= 0)
            return maximum;
        double density = Density(options), ratio = density / maximum;
        // Choose the next clean denominator that fits, then change projection,
        // not just its caption. Smaller objects retain useful fine steps.
        double step = ratio >= 100 ? 10 : ratio >= 5 ? 5 : Math.Pow(10, Math.Floor(Math.Log10(ratio)));
        double rounded = Math.Ceiling(ratio / step - 1e-6) * step;
        return (float)(density / rounded);
    }

    public static string Ratio(ProjectionFrame? frame, PrintOptions? options = null)
    {
        if (frame == null || !float.IsFinite(frame.Scale) || frame.Scale <= 0)
            return "NOT TO SCALE";
        double denominator = (options == null ? PixelsPerMetre : Density(options)) / (double)frame.Scale;
        string Number(double n) => n.ToString("0.##", CultureInfo.InvariantCulture);
        return denominator >= 1 ? "1:" + Number(denominator) : Number(1 / denominator) + ":1";
    }

    public static string Summary(IEnumerable<ViewRegion> views, PrintOptions? options = null)
    {
        var main = views.Where(v => v.Id < 1000).ToArray();
        if (main.Length == 0 || main.Any(v => v.Frame == null))
            return "NOT TO SCALE";
        var groups = main.GroupBy(v => Ratio(v.Frame, options)).ToArray();
        return groups.Length == 1 ? groups[0].Key : "";
    }

    public static string Title(string craft, TitleBlockData? data) => string.IsNullOrWhiteSpace(data?.Title) ? craft : AnnotationText.Clean(data.Title, 100);

    public static string Text(TitleBlockData data, string? calculated) => string.IsNullOrWhiteSpace(data.Scale) ? calculated ?? "NOT TO SCALE" : AnnotationText.Clean(data.Scale, 100);
}
