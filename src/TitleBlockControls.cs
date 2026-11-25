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
    static TitlePreferences profileDefaults = new(new(), "", InitialsSizeVersion: 1);
    static NewDrawingDraft? logoDraftOwner;
    static AnnotationDocument? logoDocumentOwner;
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
        approverText.SetValue(TitleBlockData.DefaultApprover.AsSpan());
        approverInitials.SetValue(TitleBlockData.DefaultApproverInitials.AsSpan());
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
            profileDefaults = titlePreferences.Load() ?? new(new(), "", InitialsSizeVersion: 1);
            ApplySavedEditorPreferences();
        }
        catch (Exception e) { logoMessage = "Logo settings: " + e.Message; }
    }

    static void ApplySavedEditorPreferences()
    {
        livePreviewUpdates = profileDefaults.LivePreviewUpdates;
        detailedExport = profileDefaults.DetailedExport;
        snapPanels = profileDefaults.SnapPanels;
        defaultLine = (int)profileDefaults.Leader;
        defaultTip = (int)profileDefaults.Endpoint;
        livePreviewGate.Reset();
    }

    static bool SaveProfileSettings(TitlePreferences settings)
    {
        try
        {
            var saved = TitlePreferenceStore.Normalize(settings);
            (titlePreferences ?? throw new InvalidOperationException("The PawPrints profile is unavailable.")).Save(saved);
            profileDefaults = saved;
            return true;
        }
        catch (Exception error)
        {
            profileMessage = "Could not save defaults: " + error.Message;
            return false;
        }
    }

    static void RefreshLogos()
    {
        try
        {
            logoFiles = Directory.EnumerateFiles(logoFolder).Where(CompanyLogo.Supported).Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).Take(200).ToArray();
        }
        catch (Exception e) { logoMessage = "Cannot read logo folder: " + e.Message; }
    }

    static void LoadLogo(string path, bool import, NewDrawingDraft? draft = null)
    {
        logoDraftOwner = draft;
        logoDocumentOwner = document;
        logoMessage = "Loading logo...";
        logoTask = Task.Run<(string? Path, LogoImage? Image)>(() => import ? CompanyLogo.Import(path, logoFolder) : (path, CompanyLogo.Load(path)));
    }

    static void PollLogo()
    {
        if (logoTask?.IsCompleted != true)
            return;
        var task = logoTask;
        logoTask = null;
        var draft = logoDraftOwner;
        bool current = draft != null ? ReferenceEquals(draft, newDrawing) : ReferenceEquals(logoDocumentOwner, document);
        logoDraftOwner = null;
        logoDocumentOwner = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            if (!current)
            {
                RefreshLogos();
                return;
            }
            if (result.Path == null)
            {
                logoMessage = "";
                return;
            }
            var name = Path.GetFileName(result.Path);
            if (result.Image != null) knownLogoNames.AddOrUpdate(result.Image, name);
            if (draft != null) draft.SetLogo(result.Image, name);
            else
            {
                selectedLogo = result.Image;
                logoName = name;
            }
            RefreshLogos();
            logoMessage = "Logo ready.";
        }
        catch (Exception error)
        {
            if (!current) return;
            if (draft is { Logo: null }) draft.SetLogo(null, "");
            logoMessage = "Logo not loaded: " + error.Message;
        }
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
        ImGui.InputText("##Title " + label, value);
        if (ImGui.IsItemActivated())
            PrintDiagnostics.Record("title.field.focus " + label);
        bool finishedEdit = ImGui.IsItemDeactivatedAfterEdit();
        value.EvaluateLength();
        if (finishedEdit)
        {
            PrintDiagnostics.Record("title.field.edited " + label);
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
            ImGui.Combo("##Handwriting"u8, ref handwriting, InitialsFont.Names, InitialsFont.Names.Length);
            ImGui.Text("Initials size");
            ImGui.SetNextItemWidth(-1);
            ImGui.SliderInt("##Initials size"u8, ref size, InitialsFont.MinPercent, InitialsFont.MaxPercent, "%d%%"u8);
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
        DrawLogoSelection();
        if (ImGui.Button("Scan logo folder for new files"))
            RefreshLogos();
        if (ImGui.Button("Open logo folder"u8))
        {
            try
            {
                Directory.CreateDirectory(logoFolder);
                OpenDesktopPath(logoFolder);
            }
            catch (Exception e) { logoMessage = e.Message; }
        }
    }

    static void DrawLogoSelection(NewDrawingDraft? draft = null)
    {
        var entries = new[] { "None" }.Concat(logoFiles).ToArray();
        int selected = Array.FindIndex(logoFiles, name => name.Equals(draft?.LogoName ?? logoName, StringComparison.OrdinalIgnoreCase)) + 1;
        if (ImGui.Combo("Company logo"u8, ref selected, entries, entries.Length))
        {
            if (selected == 0)
            {
                if (draft != null) draft.SetLogo(null, "");
                else
                {
                    selectedLogo = null;
                    logoName = "";
                }
                logoMessage = "";
            }
            else
                LoadLogo(Path.Combine(logoFolder, logoFiles[selected - 1]), false, draft);
        }
        if (OperatingSystem.IsLinux())
        {
            ImGui.InputText("PNG path"u8, logoImportPath);
            var path = logoImportPath.ToString();
            ImGui.BeginDisabled(!Path.IsPathFullyQualified(path));
            if (ImGui.Button("Import PNG"u8))
                LoadLogo(path, true, draft);
            ImGui.EndDisabled();
        }
        else if (ImGui.Button("Choose PNG..."u8))
        {
            var picker = LogoPicker.Choose(Process.GetCurrentProcess().MainWindowHandle, logoFolder);
            logoDraftOwner = draft;
            logoDocumentOwner = document;
            logoMessage = "Choose a logo file...";
            logoTask = Task.Run<(string? Path, LogoImage? Image)>(async () => { var path = await picker; return path == null ? (null, null) : CompanyLogo.Import(path, logoFolder); });
        }
        if (logoMessage.Length > 0)
            PrintUiText.Wrapped(logoMessage);
    }
}
