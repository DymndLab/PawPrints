using Blueprint.Rendering;
using System.Text.Json;

namespace Blueprint.Storage;

sealed class LayoutTemplateStore(ProfileStorage profile)
{
    const int Limit = 64;
    string Folder => Path.Combine(profile.Root, "PawPrints", "Templates");

    public IReadOnlyList<(string Name, LayoutTemplate Template)> ReadAll()
    {
        var entries = new List<(string, LayoutTemplate)>();
        if (!Directory.Exists(Folder)) return entries;

        foreach (string path in Directory.EnumerateFiles(Folder, "*.json").Take(Limit))
        {
            using var stream = File.OpenRead(path);
            if (stream.Length > DrawingSchemas.MaxBytes)
                throw new InvalidDataException("A saved template exceeds the size limit.");
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
            string name = ValidName(json.RootElement.GetProperty("Name").GetString() ?? "");
            var template = DrawingSchemas.ReadTemplate(System.Text.Encoding.UTF8.GetBytes(
                json.RootElement.GetProperty("Template").GetRawText()));
            RequireSupported(template);
            entries.Add((name, template));
        }
        return entries.OrderBy(e => e.Item1, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void Save(string name, LayoutTemplate template)
    {
        name = ValidName(name.Trim());
        RequireSupported(template);
        var entries = ReadAll();
        if (entries.Count >= Limit)
            throw new InvalidOperationException("The template library is full (64 templates).");
        if (entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A template already has that name. Choose another name.");

        // DrawingSchemas uses camel-case names and its own enum converters.
        using var layout = JsonDocument.Parse(DrawingSchemas.WriteTemplate(template));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Name = name, Template = layout.RootElement });
        if (bytes.Length > DrawingSchemas.MaxBytes) throw new InvalidDataException("Template is too large.");

        Directory.CreateDirectory(Folder);
        var key = Guid.NewGuid().ToString("N");
        string temporary = Path.Combine(Folder, key + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                stream.Write(bytes);
            File.Move(temporary, Path.Combine(Folder, key + ".json"));
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    static string ValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(char.IsControl))
            throw new InvalidDataException("Enter a template name up to 64 characters.");
        return name;
    }

    static void RequireSupported(LayoutTemplate template)
    {
        var views = template.Layout.Views;
        if (views.IsDefault || views.Count(v => !v.Exploded) > DrawingPlacements.Limit ||
            views.Count(v => v.Exploded) > ExplodedPainting.Limit ||
            views.Any(v => v == null || v.Ratio is < .01f or > 1000000 ||
                v.Alignment is < 0 or > 2 || v.RollDirection is < -1 or > 7 || v.ProjectionAngle is not (1 or 3) ||
                (v.Exploded ? v.Id <= 1000 || v.Parent != null : v.Id is < 0 or >= 1000) ||
                Math.Abs(v.Bounds.X) > 32 || Math.Abs(v.Bounds.Y) > 32 || v.Bounds.Width > 32 || v.Bounds.Height > 32))
            throw new InvalidDataException("Template layout exceeds supported drawing limits.");
    }
}
