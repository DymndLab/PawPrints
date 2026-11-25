using System.Text.Json;
using Blueprint.Rendering;

namespace Blueprint.Storage;

sealed record ProfileLogo(string Name, byte[] Png);
sealed record ProfileTemplate(string Name, JsonElement Layout);
sealed record ProfileBundle(string Kind, int Version, TitlePreferences Settings, ProfileLogo[] Logos, ProfileTemplate[] Templates);

sealed class ProfileTransfer(ProfileStorage profile)
{
    const string Kind = "pawprints.profile";
    const int MaxBytes = 64 * 1024 * 1024;
    const int MaxLogos = 200;
    static readonly JsonSerializerOptions Json = new() { MaxDepth = 40 };

    public void Export(string path, TitlePreferences settings, bool extraLogos, bool templates)
    {
        string[] names = settings.LogoFile.Length == 0 ? [] : [settings.LogoFile];
        if (extraLogos && Directory.Exists(profile.Logos))
            names = [.. names.Concat(Directory.EnumerateFiles(profile.Logos).Where(CompanyLogo.Supported)
                .Select(Path.GetFileName).OfType<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxLogos + 1)];
        var logos = new List<ProfileLogo>();
        long total = 0;
        foreach (var name in names)
        {
            ValidLogoName(name);
            var png = CompanyLogo.Read(Path.Combine(profile.Logos, name));
            total += png.Length;
            if (total > MaxBytes * 3L / 4) throw new InvalidDataException("The logo library exceeds the profile size limit.");
            logos.Add(new(name, png));
        }
        var layouts = templates ? new LayoutTemplateStore(profile).ReadAll()
            .Select(t => new ProfileTemplate(t.Name, JsonSerializer.Deserialize<JsonElement>(DrawingSchemas.WriteTemplate(t.Template)))).ToArray() : [];
        var bundle = new ProfileBundle(Kind, 1, TitlePreferenceStore.Normalize(settings), [.. logos], layouts);
        Validate(bundle);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(bundle, Json);
        if (bytes.Length > MaxBytes) throw new InvalidDataException("The profile exceeds 64 MB. Export fewer library items.");
        WriteNew(path, bytes);
    }

    public static ProfileBundle Read(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length is <= 0 or > MaxBytes) throw new InvalidDataException("The profile must be 64 MB or smaller.");
        var bytes = new byte[(int)file.Length];
        file.ReadExactly(bytes);
        var bundle = JsonSerializer.Deserialize<ProfileBundle>(bytes, Json) ?? throw new InvalidDataException("The profile is empty.");
        Validate(bundle);
        return bundle with { Settings = TitlePreferenceStore.Normalize(bundle.Settings) };
    }

    static void Validate(ProfileBundle bundle)
    {
        if (bundle.Kind != Kind || bundle.Version != 1 || bundle.Settings == null ||
            bundle.Logos == null || bundle.Templates == null || bundle.Logos.Length > MaxLogos ||
            bundle.Templates.Length > LayoutTemplateStore.Limit)
            throw new InvalidDataException("Unsupported PawPrints profile or library size.");
        var settings = bundle.Settings;
        if (settings.LogoFile == null || settings.ProjectionAngle is not (1 or 3))
            throw new InvalidDataException("The profile has invalid logo or projection settings.");
        var title = TitlePreferenceStore.Normalize(settings).Data;
        if (!Fits(title.Company, 255) || !Fits(title.Creator, 255) || !Fits(title.CreatorInitials, 47) ||
            !Fits(title.Subtitle, 255) || !Fits(title.Scale, 255) ||
            title.InitialsStyle == null || title.ApproverInitialsStyle == null)
            throw new InvalidDataException("The profile has invalid title defaults.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var logo in bundle.Logos)
        {
            if (logo == null || logo.Png == null || !names.Add(ValidLogoName(logo.Name)))
                throw new InvalidDataException("The profile contains a missing or duplicate logo.");
            CompanyLogo.Decode(logo.Png);
        }
        if (settings.LogoFile.Length > 0 && !names.Contains(ValidLogoName(settings.LogoFile)))
            throw new InvalidDataException("The default logo is missing from the profile.");

        names.Clear();
        foreach (var template in bundle.Templates)
        {
            if (template?.Name == null || !names.Add(LayoutTemplateStore.ValidName(template.Name.Trim())))
                throw new InvalidDataException("The profile contains a missing or duplicate template name.");
            ReadTemplate(template);
        }
    }

    static bool Fits(string? text, int bytes) =>
        text != null && !text.Any(char.IsControl) && System.Text.Encoding.UTF8.GetByteCount(text) <= bytes;

    static string ValidLogoName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 160 || !CompanyLogo.Supported(name) ||
            name.Any(c => char.IsControl(c) || "\\/:*?\"<>|".Contains(c)) ||
            name.StartsWith('.') || name.EndsWith(' ') ||
            IsDeviceName(name.Split('.')[0].TrimEnd()))
            throw new InvalidDataException("The profile has an invalid logo filename.");
        return name;
    }

    static bool IsDeviceName(string stem)
    {
        string name = stem.ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" ||
            name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal))
                && "0123456789¹²³".Contains(name[3]);
    }

    static LayoutTemplate ReadTemplate(ProfileTemplate template)
    {
        var layout = DrawingSchemas.ReadTemplate(System.Text.Encoding.UTF8.GetBytes(template.Layout.GetRawText()));
        LayoutTemplateStore.RequireSupported(layout);
        return layout;
    }

    public TitlePreferences Apply(ProfileBundle bundle)
    {
        // Read validates all payloads before the UI offers Apply. Files added here
        // are owned by this import. The preferences replacement is the last write.
        var templateStore = new LayoutTemplateStore(profile);
        var existingTemplates = bundle.Templates.Length == 0 ? [] : templateStore.ReadAll();
        var names = existingTemplates.ToDictionary(t => t.Name, t => t.Template, StringComparer.OrdinalIgnoreCase);
        var layouts = new List<(string Name, LayoutTemplate Layout)>();
        foreach (var template in bundle.Templates)
        {
            string name = template.Name.Trim();
            var layout = ReadTemplate(template);
            var bytes = DrawingSchemas.WriteTemplate(layout);
            var copy = name;
            for (int i = 2; names.TryGetValue(copy, out var existing); i++)
            {
                if (bytes.AsSpan().SequenceEqual(DrawingSchemas.WriteTemplate(existing))) break;
                copy = CopyName(name, 64, i);
            }
            name = copy;
            if (names.ContainsKey(name)) continue;
            names.Add(name, layout);
            layouts.Add((name, layout));
        }
        if (names.Count > LayoutTemplateStore.Limit) throw new InvalidOperationException("The destination template library is full.");

        var added = new List<string>();
        var settings = bundle.Settings;
        try
        {
            Directory.CreateDirectory(profile.Logos);
            var files = Directory.EnumerateFileSystemEntries(profile.Logos).Select(Path.GetFileName).OfType<string>()
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            string selected = "";
            foreach (var logo in bundle.Logos)
            {
                string name = logo.Name;
                for (int i = 2; files.TryGetValue(name, out string? existing); i++)
                {
                    string candidate = Path.Combine(profile.Logos, existing);
                    if (File.Exists(candidate) && new FileInfo(candidate).Length == logo.Png.Length &&
                        File.ReadAllBytes(candidate).AsSpan().SequenceEqual(logo.Png))
                    {
                        name = existing;
                        break;
                    }
                    name = CopyName(Path.GetFileNameWithoutExtension(logo.Name), 156, i) + ".png";
                }
                if (!files.ContainsKey(name))
                {
                    string path = Path.Combine(profile.Logos, name);
                    WriteNew(path, logo.Png);
                    added.Add(path);
                    files.Add(name, name);
                }
                if (logo.Name.Equals(settings.LogoFile, StringComparison.OrdinalIgnoreCase)) selected = name;
            }
            foreach (var template in layouts) added.Add(templateStore.Save(template.Name, template.Layout));
            var applied = settings with { LogoFile = selected };
            new TitlePreferenceStore(profile).Save(applied);
            return applied;
        }
        catch
        {
            foreach (var path in added)
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    static string CopyName(string name, int max, int index)
    {
        string suffix = $" ({index})";
        return name[..Math.Min(name.Length, max - suffix.Length)] + suffix;
    }

    static void WriteNew(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes);
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
