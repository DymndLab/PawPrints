using Blueprint.Storage;
using Blueprint.Rendering;
using Brutal.ImGuiApi;
using System.Diagnostics;
using System.Globalization;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static readonly ImInputString companyText = new(256), creatorText = new(256), creatorInitials = new(48), drawDate = new(32), approverText = new(256), approverInitials = new(48), approvalDate = new(32);
    static readonly ImInputString drawingTitle = new(256), drawingSubtitle = new(256), drawingRevision = new(48), drawingScale = new(256);
    static bool calculateScale = true;
    static KSA.VehicleEditor? titleEditor;
    static string? titleCraft;
    static string logoFolder = "", logoMessage = "", logoName = "";
    static string[] logoFiles = [];
    static LogoImage? selectedLogo;
    static int initialsChoice = Array.IndexOf(InitialsFont.Names, InitialsFont.Face), approverStyleChoice = Array.IndexOf(InitialsFont.Names, InitialsFont.ApproverFace);
    static Task<(string? Path, LogoImage? Image)>? logoTask;

    // History owns the logo lifetime; filename lookup must not retain discarded images.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<LogoImage, string> knownLogoNames = new();
    static TitlePreferenceStore? titlePreferences;
    static long? titleSaveDue;
    static int creatorInitialsSize = 100, approverInitialsSize = 92;
    static TitleBlockData TitleData => new(companyText.ToString(), creatorText.ToString(), creatorInitials.ToString(), drawDate.ToString(), approverText.ToString(), approverInitials.ToString(), approvalDate.ToString(), 4, selectedLogo, InitialsFont.Names[initialsChoice], InitialsFont.Names[approverStyleChoice], string.IsNullOrWhiteSpace(drawingTitle.ToString()) ? titleCraft ?? "" : drawingTitle.ToString(), drawingSubtitle.ToString(), drawingRevision.ToString(), calculateScale ? "" : string.IsNullOrWhiteSpace(drawingScale.ToString()) ? "NOT TO SCALE" : drawingScale.ToString(), creatorInitialsSize, approverInitialsSize);

    static void EnsureDrawingTitle(KSA.VehicleEditor editor)
    {
        if (ReferenceEquals(titleEditor, editor) && titleCraft == editor.EditingSpace.Id)
            return;
        titleEditor = editor;
        titleCraft = editor.EditingSpace.Id;
        drawingTitle.SetValue(titleCraft.AsSpan());
        drawingRevision.SetValue("0".AsSpan());
    }

    static void InitializeTitleBlock()
    {
        var storage = ProfileStorage.FromResolvedProfile(KSA.Constants.DocumentsFolderPath);
        logoFolder = storage.Logos;
        titlePreferences = new(storage);
        drawDate.SetValue(CompanyTitleBlock.LocalDate().AsSpan());
        approvalDate.SetValue(CompanyTitleBlock.LocalDate().AsSpan());
        approverText.SetValue(TitlePreferenceStore.DefaultApprover.AsSpan());
        approverInitials.SetValue(TitlePreferenceStore.DefaultApproverInitials.AsSpan());
        drawingSubtitle.SetValue("GENERAL ARRANGEMENT".AsSpan());
        drawingRevision.SetValue("0".AsSpan());
        drawingScale.SetValue("NOT TO SCALE".AsSpan());
        try
        {
            var migrationWarnings = storage.PrepareLogoLibrary(Path.GetDirectoryName(typeof(BlueprintMod).Assembly.Location)!);
            foreach (string warning in migrationWarnings)
                Console.WriteLine("[Paw Prints] " + warning);
            if (migrationWarnings.Count > 0)
                logoMessage = migrationWarnings[0];
            RefreshLogos();
            var pref = titlePreferences.Load();
            if (pref == null)
                return;
            livePreviewUpdates = pref.LivePreviewUpdates;
            var d = pref.Data;
            companyText.SetValue(d.Company.AsSpan());
            creatorText.SetValue(d.Creator.AsSpan());
            creatorInitials.SetValue(d.CreatorInitials.AsSpan());
            approverText.SetValue(d.Approver.AsSpan());
            approverInitials.SetValue(d.ApproverInitials.AsSpan());
            initialsChoice = Array.IndexOf(InitialsFont.Names, InitialsFont.Normalize(d.InitialsStyle));
            approverStyleChoice = Array.IndexOf(InitialsFont.Names, InitialsFont.Normalize(d.ApproverInitialsStyle, InitialsFont.ApproverFace));
            titleBlockVisible = pref.TitleBlockVisible;
            creatorInitialsSize = Math.Clamp(d.CreatorInitialsSize, InitialsFont.MinPercent, InitialsFont.MaxPercent);
            approverInitialsSize = Math.Clamp(d.ApproverInitialsSize, InitialsFont.MinPercent, InitialsFont.MaxPercent);
            drawingSubtitle.SetValue((d.Subtitle ?? "GENERAL ARRANGEMENT").AsSpan());
            calculateScale = string.IsNullOrWhiteSpace(d.Scale);
            if (!calculateScale)
                drawingScale.SetValue(d.Scale.AsSpan());
            // A new session starts with today's date; these aren't stored approval events.
            logoName = pref.LogoFile;
            if (logoFiles.Contains(logoName))
                LoadLogo(Path.Combine(logoFolder, logoName), false);
        }
        catch (Exception e) { logoMessage = "Logo settings: " + e.Message; }
    }

    static void SaveTitlePreferences()
    {
        titleSaveDue = null;
        try { titlePreferences?.Save(TitleData, logoName, livePreviewUpdates, titleBlockVisible); }
        catch (Exception e) { logoMessage = "Could not save title defaults: " + e.Message; }
    }

    static void PollTitlePreferences()
    {
        if (titleSaveDue is { } due && Environment.TickCount64 >= due)
            SaveTitlePreferences();
    }

    static void FlushTitlePreferences()
    {
        if (titleSaveDue != null)
            SaveTitlePreferences();
    }

    static void RefreshLogos()
    {
        try
        {
            logoFiles = Directory.EnumerateFiles(logoFolder).Where(CompanyLogo.Supported).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
        }
        catch (Exception e) { logoMessage = "Cannot read logo folder: " + e.Message; }
    }

    static void LoadLogo(string path, bool import)
    {
        logoMessage = "Loading logo...";
        logoTask = Task.Run<(string? Path, LogoImage? Image)>(() => import ? CompanyLogo.Import(path, logoFolder) : (path, CompanyLogo.Load(path)));
    }

    static void PollLogo()
    {
        if (logoTask?.IsCompleted != true)
            return;
        var task = logoTask;
        logoTask = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            if (result.Path == null)
            {
                logoMessage = "";
                return;
            }
            selectedLogo = result.Image;
            logoName = Path.GetFileName(result.Path);
            if (selectedLogo != null)
                knownLogoNames.AddOrUpdate(selectedLogo, logoName);
            RefreshLogos();
            SaveTitlePreferences();
            logoMessage = "Logo ready.";
        }
        catch (Exception e) { logoMessage = "Logo not loaded: " + e.Message; }
    }

    static bool ValidTitleDates()
    {
        foreach (var value in new[] { drawDate.ToString(), approvalDate.ToString() })
            if (value.Length > 0 && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                ReportFailure("Use YYYY-MM-DD for drawing and approval dates, or leave them blank.");
                return false;
            }
        return true;
    }

    static void Field(string label, ImInputString value)
    {
        ImGui.Text(label);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##Title " + label, value))
            titleSaveDue = Environment.TickCount64 + 600;
        if (ImGui.IsItemActivated())
            PrintDiagnostics.Record("title.field.focus " + label);
        bool finishedEdit = ImGui.IsItemDeactivatedAfterEdit();
        value.EvaluateLength();
        if (finishedEdit)
        {
            PrintDiagnostics.Record("title.field.edited " + label);
            FlushTitlePreferences();
        }
    }

    static void DrawTitleBlockControls()
    {
        if (!ImGui.BeginTabBar("Title settings"u8))
            return;
        try
        {
            if (ImGui.BeginTabItem("Title"u8))
            {
                PrintDiagnostics.State("title.tab", "Title");
                try
                {
                    ImGui.BeginDisabled(ControlsLocked);
                    try
                    {
                        Field("Title", drawingTitle);
                        Field("Subtitle", drawingSubtitle);
                        Field("Revision", drawingRevision);
                        ImGui.Separator();
                        ImGui.Checkbox("Calculate scale", ref calculateScale);
                        PrintUiStyle.Help("All main views share one scale, rounded in steps of 5 (10 above 1:100), based on 8 x 11 inch paper rotated for landscape. Print at 100% without stretching. Exploded details have their own scale. Generate PawPrint recalculates it after settings change.");
                        if (calculateScale)
                        {
                            if (sheet?.Scene != null)
                                PrintUiText.Wrapped(PrintScale.Summary(sheet.Scene.Views, sheet.Options));
                        }
                        else
                            Field("Scale text", drawingScale);
                    }
                    finally { ImGui.EndDisabled(); }
                }
                finally { ImGui.EndTabItem(); }
            }
            if (ImGui.BeginTabItem("Signoffs"u8))
            {
                PrintDiagnostics.State("title.tab", "Signoffs");
                try
                {
                    ImGui.BeginDisabled(ControlsLocked);
                    try
                    {
                        DrawSignerControls("DRAWN BY", creatorText, creatorInitials, ref initialsChoice, drawDate, ref creatorInitialsSize);
                        ImGui.Separator();
                        DrawSignerControls("APPROVED BY", approverText, approverInitials, ref approverStyleChoice, approvalDate, ref approverInitialsSize);
                    }
                    finally { ImGui.EndDisabled(); }
                }
                finally { ImGui.EndTabItem(); }
            }
            if (ImGui.BeginTabItem("Company"u8))
            {
                PrintDiagnostics.State("title.tab", "Company");
                try
                {
                    ImGui.BeginDisabled(ControlsLocked);
                    try
                    {
                        DrawCompanyControls();
                    }
                    finally { ImGui.EndDisabled(); }
                }
                finally { ImGui.EndTabItem(); }
            }

        }
        finally { ImGui.EndTabBar(); }
    }

    static void DrawSignerControls(string heading, ImInputString name, ImInputString initials, ref int handwriting, ImInputString date, ref int size)
    {
        ImGui.PushID(heading);
        try
        {
            ImGui.Text(heading);
            Field("Name", name);
            Field("Initials", initials);
            PrintUiStyle.Help("Printed in the selected handwriting font.");
            ImGui.Text("Handwriting");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##Handwriting"u8, ref handwriting, InitialsFont.Names, InitialsFont.Names.Length))
                SaveTitlePreferences();
            ImGui.Text("Initials size");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("##Initials size"u8, ref size, InitialsFont.MinPercent, InitialsFont.MaxPercent, "%d%%"u8))
                SaveTitlePreferences();
            Field("Date", date);
            PrintUiStyle.Help("Use YYYY-MM-DD. The drawing export date is added automatically.");
        }
        finally { ImGui.PopID(); }
    }

    static readonly ImInputString logoImportPath = new(4096);

    static void DrawCompanyControls()
    {
        Field("Company name", companyText);
        ImGui.Text("Company logo");
        int logoWidth = Math.Max(1, PageLayout.Right(Options) - CompanyTitleBlock.Layout(Options).Split - 247);
        int logoHeight = CompanyTitleBlock.BrandingHeight(Options) - 48;
        float logoFactor = Math.Min(2, 1024f / Math.Max(logoWidth, logoHeight));
        int suggestedWidth = (int)Math.Round(logoWidth * logoFactor / 8) * 8, suggestedHeight = (int)Math.Round(logoHeight * logoFactor / 8) * 8;
        ImGui.Text($"Suggested: {suggestedWidth} x {suggestedHeight} px");
        PrintUiStyle.Help("Use transparent PNG. Square logos also work; proportions are preserved. Single black or white ink follows the theme text color. Two or more visible RGB colors retain their original colors.");
        var entries = new[] { "None" }.Concat(logoFiles).ToArray();
        int selected = Array.IndexOf(logoFiles, logoName) + 1;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Company logo"u8, ref selected, entries, entries.Length))
        {
            if (selected == 0)
            {
                selectedLogo = null;
                logoName = "";
                logoMessage = "";
                SaveTitlePreferences();
            }
            else
                LoadLogo(Path.Combine(logoFolder, logoFiles[selected - 1]), false);
        }
        if (OperatingSystem.IsLinux())
        {
            ImGui.InputText("PNG path"u8, logoImportPath);
            var path = logoImportPath.ToString();
            ImGui.BeginDisabled(!Path.IsPathFullyQualified(path));
            if (ImGui.Button("Import PNG"u8))
                LoadLogo(path, true);
            ImGui.EndDisabled();
        }
        else if (ImGui.Button("Choose PNG..."u8))
        {
            var picker = LogoPicker.Choose(Process.GetCurrentProcess().MainWindowHandle, logoFolder);
            logoMessage = "Choose a logo file...";
            logoTask = Task.Run<(string? Path, LogoImage? Image)>(async () => { var path = await picker; return path == null ? (null, null) : CompanyLogo.Import(path, logoFolder); });
        }
        if (ImGui.Button("Scan logo folder for new files"))
            RefreshLogos();
        if (ImGui.IsItemHovered())
            PrintUiText.Tooltip("Use this after adding PNG files to the logo folder.");
        if (ImGui.Button("Open logo folder"u8))
        {
            try
            {
                Directory.CreateDirectory(logoFolder);
                OpenDesktopFolder(logoFolder);
            }
            catch (Exception e) { logoMessage = e.Message; }
        }
        if (logoMessage.Length > 0)
            PrintUiText.Wrapped(logoMessage);
    }
}
