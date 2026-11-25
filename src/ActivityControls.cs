using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static DrawingNotice? activeNotice;
    static long noticeUntil;
    static bool scrollToActivity;
    static int missingDetailDelete = -1, replacementDetail = -1;
    static readonly Queue<int> missingRoots = new();
    static readonly ImInputString rootSearch = new(128);

    static string? ExportRefreshBlock => CraftNeedsRefresh ? observedCraft is not { PartCount: > 0 }
        ? "No vehicle geometry is available to refresh. Your drawing is still open."
        : "Update the drawing from the current vehicle before exporting."
        : document?.Details.Any(d => d.SourceIssue != DetailSourceIssue.None) == true
            ? "Resolve the affected exploded views in Preferences > Activity before exporting." : null;

    static void AddActivity(string code, string message, bool notify = false)
    {
        var notice = new DrawingNotice(code, message);
        drawingNotices.Add(notice);
        if (!notify) return;
        activeNotice = notice;
        noticeUntil = Environment.TickCount64 + 8000;
    }

    static void ReportFailure(string message)
    {
        string code = message switch
        {
            _ when message.StartsWith("Save failed:", StringComparison.Ordinal) => "export.failed",
            _ when message.StartsWith("Export unavailable:", StringComparison.Ordinal) => "capture.failed",
            _ when message.StartsWith("Preview failed:", StringComparison.Ordinal) => "render.failed",
            _ when message.StartsWith("Invalid page size:", StringComparison.Ordinal) => "layout.invalid",
            _ => "operation.failed"
        };
        if (code is "render.failed" or "layout.invalid" or "capture.failed")
            KeepAcceptedPrintEditable();
        PrintDiagnostics.Record(code);
        status = message;
        AddActivity(code, message, notify: true);
    }

    static void AcceptCraftRefresh(Sheet? previous, CraftRefreshReview? review)
    {
        if (review == null) return;
        int unresolved = sheet!.Details.Count(d => d.SourceIssue != DetailSourceIssue.None);
        PrintDiagnostics.Record($"craft.refresh.accepted removed_labels={review.Labels} removed_poses={review.Poses} removed_specs={review.Specs} unresolved_views={unresolved}");
        if (review.Removed > 0)
            AddActivity("craft.refresh.removed", RemovalMessage(review), notify: true);
        foreach (var detail in sheet.Details)
            if (detail.SourceIssue == DetailSourceIssue.MissingRoot &&
                previous?.Details.Any(d => d.Id == detail.Id && d.SourceIssue == DetailSourceIssue.MissingRoot) != true)
                missingRoots.Enqueue(detail.Id);
        if (unresolved > 0)
            AddActivity("craft.refresh.unresolved", $"{unresolved} exploded {(unresolved == 1 ? "view needs" : "views need")} review.");
    }

    static string RemovalMessage(CraftRefreshReview review)
    {
        var counts = new List<string>(3);
        if (review.Labels > 0) counts.Add($"{review.Labels} {(review.Labels == 1 ? "label or flagnote" : "labels or flagnotes")}");
        if (review.Poses > 0) counts.Add($"{review.Poses} {(review.Poses == 1 ? "pose" : "poses")}");
        if (review.Specs > 0) counts.Add($"{review.Specs} {(review.Specs == 1 ? "specification block" : "specification blocks")}");
        return "Vehicle edits removed " + string.Join(", ", counts) + " from the drawing.";
    }

    static void DrawActivityToast()
    {
        if (activeNotice is not { } notice || Environment.TickCount64 >= noticeUntil)
            return;
        if (missingDetailDelete >= 0 || missingRoots.Count != 0)
        {
            noticeUntil = Environment.TickCount64 + 8000;
            return;
        }
        var workspace = ImGui.GetWindowSize();
        float width = Math.Min(475, workspace.X - 32);
        if (width < 80) return;
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + new float2(workspace.X * .5f, 88), ImGuiCond.Always, new float2(.5f, 0));
        ImGui.SetNextWindowSize(new float2(width, 0), ImGuiCond.Always);
        bool visible = true;
        bool shown = ImGui.Begin("##PawPrintsNotice"u8, ref visible, ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoFocusOnAppearing);
        try
        {
            if (shown)
            {
                if (notice.Code is "export.saved" or "render.cancelled" or "project.saved" or "project.opened") PrintUiText.Wrapped(notice.Message);
                else PrintUiStyle.Warning(notice.Message);
            }
        }
        finally { ImGui.End(); }
    }

    static void ShowActivityInPreferences()
    {
        requestedSettingsTab = "Preferences";
        scrollToActivity = true;
    }

    static void DrawActivity()
    {
        ImGui.Separator();
        float sectionTop = ImGui.GetCursorScreenPos().Y - ImGui.GetWindowPos().Y;
        PrintUiStyle.SectionHeader("Activity");
        if (scrollToActivity)
        {
            ImGui.SetScrollFromPosY(sectionTop, 0f);
            scrollToActivity = false;
        }

        if (ImGui.Button("Open PawPrints log"))
            OpenActivityLogs(openFile: true);
        if (ImGui.Button("Open logs folder"))
            OpenActivityLogs(openFile: false);

        if (ExportRefreshBlock is { } block) PrintUiText.Wrapped(block);
        foreach (var view in document?.Details ?? [])
            if (view.SourceIssue != DetailSourceIssue.None)
                DrawUnresolvedDetail(view);
        var entries = drawingNotices.Entries;
        if (entries.Count == 0)
            PrintUiText.Wrapped("No warnings or failed operations this session.");
        foreach (var entry in entries.Reverse())
        {
            ImGui.Separator();
            PrintUiText.Wrapped(entry.Message);
        }
        if (sheet is { Warnings.Length: > 0 } && ImGui.CollapsingHeader("Capture warnings"u8))
            foreach (var warning in sheet.Warnings)
                PrintUiStyle.Warning(warning);
    }

    static void OpenActivityLogs(bool openFile)
    {
        try
        {
            string folder = Path.Combine(KSA.Constants.DocumentsFolderPath, "logs");
            string log = Path.Combine(folder, PrintDiagnostics.LogFileName);
            if (openFile && !File.Exists(log))
            {
                ReportFailure("No PawPrints log is available yet.");
                return;
            }
            if (!openFile && !Directory.Exists(folder))
            {
                ReportFailure("No logs folder is available yet.");
                return;
            }
            OpenDesktopPath(openFile ? log : folder);
        }
        catch (Exception error)
        {
            PrintDiagnostics.Error("diagnostics.open", error);
            ReportFailure("Could not open PawPrints diagnostics.");
        }
    }

    static void DrawUnresolvedDetail(ExplodedDetail view)
    {
        ImGui.PushID(view.Id);
        try
        {
            PrintUiText.Wrapped($"{SheetLayout.ViewName(view.ViewId)} / {view.Caption}");
            PrintUiStyle.Warning(view.SourceIssue == DetailSourceIssue.MissingRoot
                ? "Root part deleted." : "A saved cut no longer matches an attachment.");
            ImGui.BeginDisabled(ControlsLocked || CraftNeedsRefresh || !PrintIsCurrent);
            try
            {
                if (ImGui.Button("Choose root..."))
                {
                    replacementDetail = view.Id;
                    rootSearch.SetValue("".AsSpan());
                }
                ImGui.SameLine();
                if (ImGui.Button("Remove view...")) missingDetailDelete = view.Id;
                if (replacementDetail == view.Id && basePrint?.DetailSource?.Assembly is { } assembly)
                {
                    PrintUiText.Wrapped("Choose a part to start a new scope for this view.");
                    ImGui.InputText("Find part"u8, rootSearch);
                    var query = rootSearch.ToString();
                    uint? id = uint.TryParse(query, out uint numeric) ? numeric : null;
                    var matches = assembly.Nodes.Where(n => !n.Subpart &&
                        (n.Id == id || n.Name.Contains(query, StringComparison.OrdinalIgnoreCase))).Take(21).ToArray();
                    if (ImGui.Button("Cancel root selection"))
                        replacementDetail = -1;
                    if (matches.Length == 0) PrintUiText.Wrapped("No matching parts.");
                    foreach (var node in matches.Take(20))
                        if (ImGui.Selectable($"{node.Name} ({node.Id})##root-{node.Id}"))
                        {
                            replacementDetail = -1;
                            BeginDetailScope(node.Id, view.Id, replaceRoot: true);
                            break;
                        }
                    if (matches.Length > 20) PrintUiText.Wrapped("Search to narrow the list.");
                }
            }
            finally { ImGui.EndDisabled(); }
        }
        finally { ImGui.PopID(); }
    }

    static void DrawMissingDetailConfirmation()
    {
        while (missingDetailDelete < 0 && !busy && missingRoots.TryDequeue(out int next))
            if (document?.Details.Any(d => d.Id == next && d.SourceIssue == DetailSourceIssue.MissingRoot) == true)
                missingDetailDelete = next;
        var target = document?.Details.FirstOrDefault(d => d.Id == missingDetailDelete && d.SourceIssue != DetailSourceIssue.None);
        const string popup = "Exploded view needs attention";
        if (target == null)
        {
            missingDetailDelete = -1;
            if (ImGui.IsPopupOpen(popup) && ImGui.BeginPopupModal(popup))
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            return;
        }
        if (!ImGui.IsPopupOpen(popup)) ImGui.OpenPopup(popup);
        CenterDrawingDialog();
        ImGui.SetNextWindowSize(new float2(Math.Min(500, ImGui.GetWindowSize().X - 32), 0), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(popup, ImGuiWindowFlags.AlwaysAutoResize)) return;
        try
        {
            annotationKeysActive = false;
            string name = SheetLayout.ViewName(target.ViewId);
            string detail = "Detail " + name["DETAIL ".Length..];
            PrintUiStyle.Warning(target.SourceIssue == DetailSourceIssue.MissingRoot
                ? $"{detail}'s root part was deleted from the vehicle."
                : $"An attachment used by {detail} changed.");
            PrintUiText.Wrapped("Remove the view or choose a replacement root.");
            ImGui.BeginDisabled(busy || !PrintIsCurrent);
            if (ImGui.Button("Remove view"))
            {
                DeleteDetailCore(missingDetailDelete, confirmed: true);
                missingDetailDelete = -1;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Choose replacement root"))
            {
                replacementDetail = target.Id;
                ShowActivityInPreferences();
                rootSearch.SetValue("".AsSpan());
                missingDetailDelete = -1;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Keep for now"))
            {
                missingDetailDelete = -1;
                ImGui.CloseCurrentPopup();
            }
        }
        finally { ImGui.EndPopup(); }
    }
}
