namespace Blueprint.Storage;

/// <summary>Writable print data belongs to the active profile, not a managed mod folder.</summary>
sealed record ProfileStorage(string Root)
{
    public string Logos => Path.Combine(Root, "PawPrints", "Logos");
    public string Preferences => Path.Combine(Logos, "title-settings.json");
    public string Exports => Path.Combine(Root, "Exports", "PawPrints");

    public static ProfileStorage FromResolvedProfile(string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        return new(Path.GetFullPath(profile));
    }

    /// <summary>
    /// Copy earlier mod-local data without overwriting profile data or modifying
    /// the installed mod. Existing copies and the legacy originals stay intact.
    /// </summary>
    public IReadOnlyList<string> PrepareLogoLibrary(string modDirectory)
    {
        Directory.CreateDirectory(Logos);
        string legacy = Path.Combine(modDirectory, "Logos");
        if (!Directory.Exists(legacy) || string.Equals(Path.GetFullPath(legacy), Logos, StringComparison.OrdinalIgnoreCase))
            return [];

        var warnings = new List<string>();
        foreach (string source in Directory.EnumerateFiles(legacy))
        {
            string name = Path.GetFileName(source);
            // Preserve legacy originals, including formats no longer offered by the picker.
            string extension = Path.GetExtension(name);
            bool settings = name.Equals("title-settings.json", StringComparison.OrdinalIgnoreCase);
            if (!settings && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".webp", StringComparison.OrdinalIgnoreCase))
                continue;

            string destination = Path.Combine(Logos, name);
            if (File.Exists(destination))
                continue;

            try
            {
                var file = new FileInfo(source);
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked files are not imported automatically.");
                long limit = settings ? 65536 : 16 * 1024 * 1024;
                if (file.Length > limit)
                    throw new IOException("File exceeds the supported import size.");
                File.Copy(source, destination, overwrite: false);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Could not import legacy {name}: {error.Message}");
            }
        }
        return warnings;
    }
}
