using Blueprint.Rendering;
using Brutal.ImGuiApi;
using System.Globalization;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool livePreviewUpdates, printStyleDirty, renderAppliesSettings;
    static readonly PreviewUpdateGate livePreviewGate = new();
    static bool PrintIsCurrent => sheet != null && sheet.Options == Options && !printStyleDirty;

    static Sheet PendingStyle(Sheet current)
    {
        if (!printStyleDirty || current.Scene == null)
            return current;
        return PrintStyles.Apply(current, (LeaderLine)defaultLine, (LeaderTip)defaultTip);
    }

    static void RefreshTitleBlock()
    {
        if (sheet == null || document == null) return;
        var current = sheet with { Scene = document.Scene, Labels = document.Labels, Details = document.Details };
        var main = sheet.PoseMain ?? basePrint ?? sheet;
        var desired = sheet.Options with { TitleBlockVisible = titleBlockVisible, ProjectionAngle = CurrentProjectionAngle };
        renderKeepsDocument = true;
        renderAdoptsHistory = false;
        renderAppliesSettings = true;
        composingLabels = true;
        StartRender(token => current.PoseSource == null
            ? PreviewUpdates.RenderBlank(current, desired, token)
            : PreviewUpdates.Render(current, main, desired, token));
    }

    static void PollLivePreview()
    {
        if (!livePreviewUpdates || !open || sheet == null || document == null || ShowingTemplates || setupStage != SetupStage.Editing)
            return;
        if (PrintIsCurrent)
        {
            livePreviewGate.Reset();
            return;
        }
        // Incomplete dates are ordinary typing states, not popup errors.
        bool datesReady = new[] { drawDate.ToString(), approvalDate.ToString() }
            .All(s => s.Length == 0 || DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        var request = new PreviewRequest(Options, defaultLine, defaultTip, printStyleDirty);
        if (!livePreviewGate.Ready(request, Environment.TickCount64, livePreviewUpdates, busy,
            ControlsLocked || partMenuOpen || ImGui.IsMouseDown(ImGuiMouseButton.Left) || !datesReady))
            return;
        if (sheet.PoseSource == null && (sheet.Options.Page != request.Options.Page || request.Options.PartsTable || request.Options.FlagTable))
            return;
        var current = PendingStyle(sheet with { Scene = document.Scene, Labels = document.Labels, Details = document.Details });
        var main = sheet.PoseMain ?? basePrint ?? sheet;
        composingLabels = true;
        renderKeepsDocument = true;
        renderAdoptsHistory = false;
        renderAppliesSettings = true;
        progress = "Updating preview";
        PrintDiagnostics.Record("preview.live.begin");
        StartRender(token => current.PoseSource == null
            ? PreviewUpdates.RenderBlank(current, request.Options, token)
            : PreviewUpdates.Render(current, main, request.Options, token));
    }

    static void AdoptPrintSettings(PrintOptions o)
    {
        projectionStyle = o.DrawingAngle == 1 ? 1 : 0;
        titleBlockVisible = o.TitleBlockVisible;
        outputTarget = o.OutputTarget == DrawingOutput.DesktopBackground ? 0 : 1;
        view = o.View; theme = o.Theme; grid = o.Grid; archivalWear = o.Archive ? o.WearLevel : 0;
        emphasis = o.Emphasis >= 10 ? 2 : o.Emphasis >= 5 ? 1 : 0;
        layout = o.Layout; visibleViews = o.VisibleViews; captions = o.Captions;
        partsTable = o.PartsTable; flagTable = o.FlagTable; drawingSize = o.DrawingSize;
        pageOrientation = o.Orientation; pageAspect = o.Aspect; brand = o.Brand; arrangement = o.Arrangement;
        boxLayout = o.Boxes ?? boxLayout; drawingPlacement = o.Drawing; zones = o.Zones;
        customWidth = customWidthEdit = o.CustomWidth; customHeight = customHeightEdit = o.CustomHeight;
        if (o.OutputTarget == DrawingOutput.DesktopBackground && o.Orientation == 3)
        {
            int preset = Array.FindIndex(DesktopSizes, size => size.W == o.CustomWidth && size.H == o.CustomHeight);
            desktopPreset = preset >= 0 ? preset : DesktopSizes.Length;
            if (preset < 0) lastCustomDesktop = new(o.CustomWidth, o.CustomHeight);
        }
        if (o.TitleBlock is { } t)
        {
            companyText.SetValue(t.Company.AsSpan()); creatorText.SetValue(t.Creator.AsSpan());
            creatorInitials.SetValue(t.CreatorInitials.AsSpan()); drawDate.SetValue(t.DrawingDate.AsSpan());
            approverText.SetValue(t.Approver.AsSpan()); approverInitials.SetValue(t.ApproverInitials.AsSpan());
            approvalDate.SetValue(t.ApprovalDate.AsSpan()); drawingTitle.SetValue(t.Title.AsSpan());
            drawingSubtitle.SetValue(t.Subtitle.AsSpan()); drawingRevision.SetValue(t.Revision.AsSpan());
            calculateScale = string.IsNullOrWhiteSpace(t.Scale); drawingScale.SetValue(t.Scale.AsSpan());
            initialsChoice = Array.IndexOf(InitialsFont.Names, InitialsFont.Normalize(t.InitialsStyle));
            approverStyleChoice = Array.IndexOf(InitialsFont.Names, InitialsFont.Normalize(t.ApproverInitialsStyle, InitialsFont.ApproverFace));
            creatorInitialsSize = t.CreatorInitialsSize; approverInitialsSize = t.ApproverInitialsSize;
            selectedLogo = t.Logo;
            logoName = selectedLogo == null ? "" : knownLogoNames.TryGetValue(selectedLogo, out var name) ? name : logoName;
        }
    }
}
