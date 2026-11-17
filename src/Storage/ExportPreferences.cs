using System.Text.Json;

namespace Blueprint.Storage;

/// <summary>The export destination is an explicit, profile-local user choice.</summary>
sealed class ExportPreferences(ProfileStorage profile)
{
    public string FilePath => Path.Combine(profile.Root, "PawPrints", "export-settings.json");
    sealed record Settings(string Folder);

    public string? Load()
    {
        if (!File.Exists(FilePath))
            return null;
        if (new FileInfo(FilePath).Length > 65536)
            throw new InvalidDataException("Export settings are too large to read.");
        var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath));
        if (settings == null || string.IsNullOrWhiteSpace(settings.Folder)
            || !Path.IsPathFullyQualified(settings.Folder))
            throw new InvalidDataException("Export settings do not contain a valid folder path.");
        // Retain a missing destination so Save can report it instead of silently
        // changing the user's choice or recreating a disconnected location.
        return Path.GetFullPath(settings.Folder);
    }

    public string Choose(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            throw new ArgumentException("Choose an existing folder.", nameof(folder));
        string fullPath = Path.GetFullPath(folder);
        RequireExistingFolder(fullPath);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new Settings(fullPath)));
            File.Move(temp, FilePath, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
        return fullPath;
    }

    public static void RequireExistingFolder(string folder)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("The export folder no longer exists or is unavailable:\n"
                + folder + "\nChoose another export folder.");
    }
}
