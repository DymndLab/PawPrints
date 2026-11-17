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
        return current with
        {
            Labels = current.Labels.Select(l => l with { Line = (LeaderLine)defaultLine, Tip = (LeaderTip)defaultTip, ManualPlacement = true }).ToArray(),
            Scene = current.Scene.WithSpecs(current.Scene.Specs.Select(s => s with { Line = (LeaderLine)defaultLine, Tip = (LeaderTip)defaultTip }).ToArray())
        };
    }

    static void PollLivePreview()
    {
        if (!livePreviewUpdates || !open || sheet?.PoseSource == null || document == null || ShowingTemplates)
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
        var current = PendingStyle(sheet with { Scene = document.Scene, Labels = document.Labels, Details = document.Details });
        var main = sheet.PoseMain ?? basePrint ?? sheet;
        cancellation?.Dispose();
        cancellation = new();
        var token = cancellation.Token;
        composingLabels = true;
        renderKeepsDocument = true;
        renderAdoptsHistory = false;
        renderAppliesSettings = true;
        progress = "Updating preview";
        PrintDiagnostics.Record("preview.live.begin");
        renderTask = Task.Run(() => PreviewUpdates.Render(current, main, request.Options, token), token);
    }

    static void AdoptPrintSettings(PrintOptions o)
    {
        view = o.View; theme = o.Theme; grid = o.Grid; archivalWear = o.Archive ? o.WearLevel : 0;
        emphasis = o.Emphasis >= 10 ? 2 : o.Emphasis >= 5 ? 1 : 0;
        layout = o.Layout; visibleViews = o.VisibleViews; captions = o.Captions;
        partsTable = o.PartsTable; flagTable = o.FlagTable; drawingSize = o.DrawingSize;
        pageOrientation = o.Orientation; pageAspect = o.Aspect; brand = o.Brand; arrangement = o.Arrangement;
        boxLayout = o.Boxes ?? boxLayout; drawingPlacement = o.Drawing; zones = o.Zones;
        customWidth = customWidthEdit = o.CustomWidth; customHeight = customHeightEdit = o.CustomHeight;
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
