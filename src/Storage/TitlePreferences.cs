using Blueprint.Rendering;
using System.Text.Json;

namespace Blueprint.Storage;

sealed record TitlePreferences(TitleBlockData Data, string LogoFile, bool LivePreviewUpdates = false,
    bool TitleBlockVisible = true, int InitialsSizeVersion = 0, int ProjectionAngle = 3, bool DetailedExport = false,
    NewDrawingDefaults? Drawing = null, bool SnapPanels = true, LeaderLine Leader = LeaderLine.Dotted, LeaderTip Endpoint = LeaderTip.Dot);

sealed record NewDrawingDefaults(DrawingOutput Output, int Width, int Height, int Theme = 0,
    bool Grid = true, bool Zones = true, int Wear = 0)
{
    public static readonly NewDrawingDefaults Initial = new(DrawingOutput.Image, ImagePresets.Default.Width, ImagePresets.Default.Height);
}

sealed class TitlePreferenceStore(ProfileStorage profile)
{

    // Approvals belong to the current session. Personal author/company defaults
    // and their formatting persist, but a previous approver never becomes one.
    static TitleBlockData Defaults(TitleBlockData data) => data with
    {
        Approver = TitleBlockData.DefaultApprover,
        ApproverInitials = TitleBlockData.DefaultApproverInitials,
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
        return Normalize(saved);
    }

    internal static TitlePreferences Normalize(TitlePreferences saved)
    {
        if (saved.Data == null) throw new InvalidDataException("Title defaults are missing.");
        if (saved.Drawing is { } drawing)
        {
            PageSize.Custom(drawing.Width, drawing.Height);
            if (!Enum.IsDefined(drawing.Output) || drawing.Theme is < 0 or > 3 || drawing.Wear is < 0 or > 5)
                throw new InvalidDataException("Drawing defaults are out of range.");
        }
        if (!Enum.IsDefined(saved.Leader) || !Enum.IsDefined(saved.Endpoint))
            throw new InvalidDataException("Leader defaults are out of range.");
        int max = saved.InitialsSizeVersion == 0 ? 100 : InitialsFont.MaxPercent;
        var data = Defaults(saved.Data) with
        {
            CreatorInitialsSize = Math.Clamp(saved.Data.CreatorInitialsSize, InitialsFont.MinPercent, max),
            ApproverInitialsSize = Math.Clamp(saved.Data.ApproverInitialsSize, InitialsFont.MinPercent, max)
        };
        return saved with { Data = data, LogoFile = FileName(saved.LogoFile), InitialsSizeVersion = 1,
            ProjectionAngle = saved.ProjectionAngle == 1 ? 1 : 3 };
    }

    public void Save(TitleBlockData data, string logoFile, bool livePreviewUpdates = false,
        bool titleBlockVisible = true, int projectionAngle = 3, bool detailedExport = false)
    {
        Save(new(data, logoFile, livePreviewUpdates, titleBlockVisible, InitialsSizeVersion: 1,
            ProjectionAngle: projectionAngle, DetailedExport: detailedExport));
    }

    public void Save(TitlePreferences settings)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(Normalize(settings));
        if (bytes.Length > 65536) throw new InvalidDataException("Title settings are too large to save.");
        Directory.CreateDirectory(profile.Logos);
        string temp = profile.Preferences + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write)) file.Write(bytes);
            File.Move(temp, profile.Preferences, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    // A logo selection is a filename in the profile's library, never a path.
    static string FileName(string? value) => string.IsNullOrEmpty(value) || value.Contains('\\') || value != Path.GetFileName(value) ? "" : value;
}
