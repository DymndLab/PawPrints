using Blueprint.Rendering;
using System.Text.Json;

namespace Blueprint.Storage;

sealed record TitlePreferences(TitleBlockData Data, string LogoFile, bool LivePreviewUpdates = false);

sealed class TitlePreferenceStore(ProfileStorage profile)
{
    public const string DefaultApprover = "Dymnd H. K.";
    public const string DefaultApproverInitials = "DHK";

    // Approvals belong to the current session. Personal author/company defaults
    // and their formatting persist, but a previous approver never becomes one.
    static TitleBlockData Defaults(TitleBlockData data) => data with
    {
        Approver = DefaultApprover,
        ApproverInitials = DefaultApproverInitials,
        Logo = null,
        Title = "",
        Revision = "0",
        DrawingDate = "",
        ApprovalDate = ""
    };

    public TitlePreferences? Load()
    {
        if (!File.Exists(profile.Preferences))
            return null;
        if (new FileInfo(profile.Preferences).Length > 65536)
            throw new InvalidDataException("Title settings are too large to read.");
        var saved = JsonSerializer.Deserialize<TitlePreferences>(File.ReadAllText(profile.Preferences));
        if (saved?.Data == null)
            throw new InvalidDataException("Title settings do not contain drawing defaults.");
        return new(Defaults(saved.Data), FileName(saved.LogoFile), saved.LivePreviewUpdates);
    }

    public void Save(TitleBlockData data, string logoFile, bool livePreviewUpdates = false)
    {
        Directory.CreateDirectory(profile.Logos);
        string temp = profile.Preferences + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new TitlePreferences(Defaults(data), FileName(logoFile), livePreviewUpdates)));
            File.Move(temp, profile.Preferences, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    // A logo selection is a filename in the profile's library, never a path.
    static string FileName(string? value) => string.IsNullOrEmpty(value) || value != Path.GetFileName(value) ? "" : value;
}
