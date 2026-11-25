using Blueprint.Rendering;

namespace Blueprint.Storage;

sealed class DrawingProjectStore(ProfileStorage profile)
{
    const string Extension = ".pawprint";
    string Folder => Path.Combine(profile.Root, "PawPrints", "Drawings");

    public string[] List() => Directory.Exists(Folder)
        ? Directory.EnumerateFiles(Folder, "*" + Extension).Select(Path.GetFileName).OfType<string>()
            .Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];

    string PathFor(string name)
    {
        if (string.IsNullOrEmpty(name) || name != Path.GetFileName(name) || name.IndexOfAny(['/','\\',':']) >= 0 ||
            !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid drawing filename.");
        return Path.Combine(Folder, name);
    }

    public DrawingProject Read(string name)
    {
        using var file = File.OpenRead(PathFor(name));
        DrawingProjectCodec.RequireSize(file.Length);
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        return DrawingProjectCodec.Read(bytes);
    }

    public string Save(DrawingProject project, string? existing)
    {
        var bytes = DrawingProjectCodec.Write(project);
        string name = existing ?? $"{PrintFiles.SafeStem(project.Title)}-{Guid.NewGuid():N}{Extension}";
        string target = PathFor(name);
        Directory.CreateDirectory(Folder);
        string temporary = Path.Combine(Folder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: existing != null);
            return name;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
