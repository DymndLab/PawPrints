using System.Text.Json;

namespace Blueprint.Rendering;

// Immutable rules captured once per generation. File edits never change an active render.
sealed class PartSupport
{
    public const int MaxBytes = 1024 * 1024;
    const string FileName = "part-support.json";
    const string ResourceName = "PawPrints.PartSupport.json";
    readonly Rule[] rules;
    readonly HashSet<string> categories;

    sealed record Rule(string? TemplateId, string? TemplatePrefix, string? TemplateContains,
        string? Surface, string? FallbackLabel)
    {
        public bool Matches(string template) => TemplateId != null
            ? template == TemplateId
            : TemplatePrefix != null
                ? template.StartsWith(TemplatePrefix, StringComparison.Ordinal)
                : template.Contains(TemplateContains!, StringComparison.Ordinal);
    }

    PartSupport(IEnumerable<Rule> rules, IEnumerable<string> categories)
    {
        // Stable ordering: exact IDs win; patterns keep their order in the file.
        this.rules = rules.OrderBy(rule => rule.TemplateId == null ? 1 : 0).ToArray();
        this.categories = new(categories, StringComparer.Ordinal);
    }

    public static PartSupport Bundled { get; } = LoadBundled();

    public string Surface(string template) => rules.FirstOrDefault(rule => rule.Surface != null && rule.Matches(template))?.Surface ?? "structure";
    public string? FallbackLabel(string template) => rules.FirstOrDefault(rule => rule.FallbackLabel != null && rule.Matches(template))?.FallbackLabel;
    public bool IncludesCategory(string category) => categories.Contains(category);

    static PartSupport LoadBundled()
    {
        using var stream = typeof(PartSupport).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Bundled part support definitions are missing.");
        return Parse(ReadBounded(stream));
    }

    public static PartSupport Load(string directory)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(directory, FileName), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Parse(ReadBounded(stream));
        }
        catch (FileNotFoundException) { return Bundled; }
        catch (DirectoryNotFoundException) { return Bundled; }
        catch (JsonException)
        {
            throw Invalid("Invalid JSON. Check the file before generating again.");
        }
        catch (InvalidDataException) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw Invalid("The file could not be read.");
        }
    }

    static byte[] ReadBounded(Stream stream)
    {
        var bytes = new byte[MaxBytes + 1];
        int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        if (count > MaxBytes)
            throw Invalid("The file exceeds the 1 MB limit.");
        return bytes[..count];
    }

    public static PartSupport Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaxBytes)
            throw Invalid("The file exceeds the 1 MB limit.");
        if (bytes.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
            bytes = bytes[3..];
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            MaxDepth = 8
        });
        var root = document.RootElement;
        CheckProperties(root, ["version", "labelCategories", "rules"]);
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int number) || number != 1)
            throw Invalid("Expected version 1.");
        if (!root.TryGetProperty("rules", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > 2048)
            throw Invalid("Expected a rules array with at most 2048 entries.");
        var categories = new HashSet<string>(StringComparer.Ordinal);
        if (root.TryGetProperty("labelCategories", out var categoryList))
        {
            if (categoryList.ValueKind != JsonValueKind.Array || categoryList.GetArrayLength() > 64)
                throw Invalid("Expected at most 64 label categories.");
            foreach (var item in categoryList.EnumerateArray())
                if (!categories.Add(Text(item, 160)))
                    throw Invalid("Repeated label category.");
        }
        var rules = new List<Rule>();
        var selectors = new HashSet<(string, string)>();
        foreach (var item in entries.EnumerateArray())
        {
            CheckProperties(item, ["templateId", "templatePrefix", "templateContains", "surface", "fallbackLabel"]);
            string? id = Optional(item, "templateId", 512);
            string? prefix = Optional(item, "templatePrefix", 512);
            string? contains = Optional(item, "templateContains", 512);
            if ((id != null ? 1 : 0) + (prefix != null ? 1 : 0) + (contains != null ? 1 : 0) != 1)
                throw Invalid("Each rule needs exactly one templateId, templatePrefix or templateContains.");
            var selector = id != null ? ("id", id) : prefix != null ? ("prefix", prefix) : ("contains", contains!);
            if (!selectors.Add(selector))
                throw Invalid("Repeated rule selector. Combine its settings into one rule.");
            string? surface = Optional(item, "surface", 16);
            string? label = Optional(item, "fallbackLabel", 160);
            if (surface != null && surface is not ("structure" or "solar" or "foil"))
                throw Invalid("Surface must be structure, solar or foil.");
            if (surface == null && label == null)
                throw Invalid("Each rule needs a surface or fallbackLabel.");
            rules.Add(new(id, prefix, contains, surface, label));
        }
        return new(rules, categories);
    }

    static void CheckProperties(JsonElement value, string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Invalid("Expected an object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name))
                throw Invalid("Unrecognized or repeated property.");
    }

    static string? Optional(JsonElement item, string name, int limit) => item.TryGetProperty(name, out var value) ? Text(value, limit) : null;

    static string Text(JsonElement item, int limit)
    {
        if (item.ValueKind != JsonValueKind.String)
            throw Invalid("Rule names and labels must be strings.");
        string text = item.GetString()!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > limit || text.Any(char.IsControl))
            throw Invalid("A rule name or label is empty, too long or contains control characters.");
        return text;
    }

    static InvalidDataException Invalid(string reason) => new(FileName + ": " + reason);
}
