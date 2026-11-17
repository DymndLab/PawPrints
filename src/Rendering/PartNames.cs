using System.Text.RegularExpressions;
namespace Blueprint.Rendering;

static class PartNames
{

    // Native display names take priority over configured fallback labels.
    // Otherwise format the source ID while preserving variant codes.
    public static string Display(string template, string? display, PartSupport? support = null)
    {
        if (!string.IsNullOrWhiteSpace(display) && display != template)
            return display;
        support ??= PartSupport.Bundled;
        if (support.FallbackLabel(template) is { } fallback)
            return fallback;
        int split = template.IndexOf("_Prefab_", StringComparison.Ordinal);
        string stem = (split < 0 ? template : template[(split + 8)..]).Replace('_', ' ');
        foreach (string pattern in new[] { "(?<=[a-z])(?=[A-Z])", "(?<=[0-9])(?=[A-Z][a-z])", "(?<=[A-Z])(?=[A-Z][a-z])", "(?<=[a-z])(?=[0-9])" })
            stem = Regex.Replace(stem, pattern, " ");
        string category = template.Split('_')[0];
        if (category.StartsWith("Core", StringComparison.Ordinal))
            category = category[4..];
        category = Regex.Replace(category, "[A-Z]$", "");
        category = Regex.Replace(category, "(?<=[a-z])(?=[A-Z])", " ");
        return support.IncludesCategory(category) ? category + " / " + stem : stem;
    }
}
