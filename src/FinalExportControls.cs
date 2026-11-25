using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    sealed record ExportKey(RenderRevision Revision, PageSize Output, bool Detailed, string Date);
    sealed record ExportResult(FinalPng Image, PendingPng? File = null);
    enum ExportAction { Save, Preview, Smaller, Write }
    static Task<ExportResult>? saveTask;
    static CancellationTokenSource? exportCancel;
    static RenderProgress? exportProgress;
    static ExportKey? exportKey;
    static Sheet? exportSource;
    static ExportAction exportAction;
    static FinalPng? finalOriginal, finalCopy;
    static PreviewTexture? finalTexture;
    static bool reviewExport, actualPixels, detailedExport;
    static int targetChoice, customMegabytes = 5;
    static readonly string[] targetNames = ["None", "5 MB", "10 MB", "20 MB", "Custom"];
    static long ExportTarget => 1_000_000L * (targetChoice switch
    {
        1 => 5, 2 => 10, 3 => 20, 4 => customMegabytes, _ => 0
    });

    static ExportKey CurrentExportKey => new(CurrentRenderRevision, sheet!.Options.OutputPage,
        detailedExport && FinalPng.Is4K(sheet.Options.OutputPage), CompanyTitleBlock.LocalDate());
    static bool ExportMatches => ReferenceEquals(exportSource, sheet) && sheet != null &&
        PrintIsCurrent && ExportRefreshBlock == null && CurrentExportKey == exportKey;

    static void DrawExportTarget()
    {
        ImGui.BeginDisabled(ControlsLocked);
        ImGui.Text("PNG size target");
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##PNG size target", ref targetChoice, targetNames, targetNames.Length);
        if (targetChoice == 4 && ImGui.InputInt("Target (MB)", ref customMegabytes))
            customMegabytes = Math.Clamp(customMegabytes, 1, 1000);
        ImGui.EndDisabled();
    }

    static void BeginFinalExport(bool review)
    {
        if (busy || sheet == null || !PrintIsCurrent || ExportRefreshBlock != null) return;
        ResetFinalExport();
        exportSource = sheet;
        var key = exportKey = CurrentExportKey;
        var accepted = sheet;
        var content = key.Detailed ? document?.Snapshot() : null;
        long retained = RenderMemory.Retained(sheet, basePrint);
        RunExport(review ? ExportAction.Preview : ExportAction.Save, "Preparing PNG", token =>
            new(FinalPng.Prepare(accepted, content, key.Detailed, key.Date, retained, token)), retained);
    }

    static void RunExport(ExportAction action, string label, Func<CancellationToken, ExportResult> work, long retained = 0)
    {
        exportAction = action;
        var token = (exportCancel = new()).Token;
        var reporter = exportProgress = new(label);
        status = label;
        saveTask = Task.Run(() => RenderMemory.InSlot(() => reporter.Run(() => work(token)), token, retained), token);
    }

    static void BeginSmallerCopy(FinalPng original, long target)
    {
        long retained = RenderMemory.Retained(sheet, basePrint) + original.Rgb.LongLength +
            original.Encoded.Array!.LongLength + 8L * original.Size.Width * original.Size.Height;
        RunExport(ExportAction.Smaller, "Preparing smaller PNG",
            token => new(original.Smaller(target, token, retained)), retained);
    }

    static void WriteFinal(FinalPng image)
    {
        var folder = exportFolder;
        if (folder == null) { ResetFinalExport(); exportSetupOpen = true; return; }
        string title = sheet!.Title;
        RunExport(ExportAction.Write, "Saving PNG", token => new(image, image.Stage(folder, title, token)));
    }

    static void ResetFinalExport()
    {
        if (exportCancel is { } cancel && saveTask is { } task)
        {
            cancel.Cancel();
            _ = task.ContinueWith(t =>
            {
                try
                {
                    if (t.IsCompletedSuccessfully) t.Result.File?.Dispose();
                    else _ = t.Exception;
                }
                finally { cancel.Dispose(); }
            }, TaskScheduler.Default);
        }
        saveTask = null;
        exportCancel = null;
        exportProgress = null;
        finalTexture?.Dispose();
        finalTexture = null;
        finalOriginal = finalCopy = null;
        exportKey = null;
        exportSource = null;
        reviewExport = actualPixels = false;
    }

    static void PollFinalExport()
    {
        if (exportKey == null) return;
        if (!ExportMatches)
        {
            ResetFinalExport();
            AddActivity("export.stale", "The drawing changed. Prepare the PNG again.", notify: true);
            return;
        }
        if (saveTask?.IsCompleted != true) return;
        var task = saveTask;
        saveTask = null;
        using var cancel = exportCancel!;
        exportCancel = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            using var pending = result.File;
            cancel.Token.ThrowIfCancellationRequested();
            if (pending != null)
            {
                string path = pending.Publish();
                status = "Saved: " + path;
                AddActivity("export.saved", $"PNG saved. {Describe(result.Image)}", notify: true);
                PrintDiagnostics.Record("save.complete");
                ResetFinalExport();
                return;
            }
            if (exportAction == ExportAction.Save && (ExportTarget == 0 || result.Image.Encoded.Count <= ExportTarget))
            {
                WriteFinal(result.Image);
                return;
            }
            var texture = new PreviewTexture(result.Image.Size.Width, result.Image.Size.Height, Sheet.MakePreview(result.Image.Rgb));
            finalTexture?.Dispose();
            finalTexture = texture;
            if (exportAction == ExportAction.Smaller) finalCopy = result.Image;
            else finalOriginal = result.Image;
            reviewExport = true;
            status = "Final PNG ready";
        }
        catch (OperationCanceledException)
        {
            ResetFinalExport();
            AddActivity("export.cancelled", "PNG export cancelled.", notify: true);
        }
        catch (Exception e)
        {
            if (!reviewExport) ResetFinalExport();
            PrintDiagnostics.Error("save", e);
            ReportFailure("PNG export failed: " + e.Message);
        }
    }

    static string Describe(FinalPng image) => $"{image.Size.Width} x {image.Size.Height}, {image.Encoded.Count:N0} bytes";

    static void DrawFinalExport()
    {
        const string popup = "Final PNG";
        if (!reviewExport || finalOriginal is not { } original)
        {
            if (ImGui.IsPopupOpen(popup) && ImGui.BeginPopupModal(popup))
            {
                ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            return;
        }
        if (!ImGui.IsPopupOpen(popup)) ImGui.OpenPopup(popup);
        CenterDrawingDialog();
        ImGui.SetNextWindowSize(ImGui.GetWindowSize() * .85f, ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(popup)) return;
        try
        {
            annotationKeysActive = false;
            var displayed = finalCopy ?? original;
            ImGui.Text($"{(exportKey!.Detailed ? "Detailed" : "Normal")} original   {Describe(original)}");
            if (ExportTarget > 0) ImGui.Text($"Target {ExportTarget:N0} bytes");
            if (finalCopy != null) ImGui.Text($"Smaller copy   {Describe(displayed)}");
            ImGui.BeginDisabled(busy);
            if (ImGui.Button("Save original")) WriteFinal(original);
            if (ExportTarget > 0 && original.Encoded.Count > ExportTarget)
            {
                ImGui.SameLine();
                if (finalCopy == null)
                {
                    long target = ExportTarget;
                    if (ImGui.Button("Prepare smaller copy"))
                        BeginSmallerCopy(original, target);
                }
                else if (ImGui.Button("Save smaller copy")) WriteFinal(finalCopy);
            }
            ImGui.SameLine();
            ImGui.Checkbox("Actual pixels", ref actualPixels);
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button(busy ? "Cancel export" : "Back to drawing"))
            {
                ImGui.CloseCurrentPopup();
                ResetFinalExport();
                return;
            }
            if (busy) ImGui.Text(exportProgress?.Latest.Label ?? "Preparing PNG");
            ImGui.BeginChild("Final pixels"u8, new float2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
            try
            {
                var room = ImGui.GetContentRegionAvail();
                float scale = actualPixels ? 1 : Math.Min(room.X / displayed.Size.Width, room.Y / displayed.Size.Height);
                if (finalTexture?.Image is { } id && scale > 0)
                    ImGui.Image(id, new float2(displayed.Size.Width * scale, displayed.Size.Height * scale));
            }
            finally { ImGui.EndChild(); }
        }
        finally { ImGui.EndPopup(); }
    }
}
