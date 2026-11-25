using Blueprint.Rendering;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    sealed record RenderCommitPolicy(bool KeepDocument, bool AdoptHistory, bool ApplySettings);
    static readonly RenderJobs<Sheet> renderJobs = new();
    static RenderCommitPolicy? renderPolicy;
    static RenderProgress? activeProgress;
    static long renderStarted;
    static long sourceEpoch;
    static AnnotationDocument? acceptedDocument;
    static DrawingDefinition? acceptedDefinition;
    static readonly DrawingNotices drawingNotices = new();
    static RenderRevision CurrentRenderRevision => new(document?.Id ?? Guid.Empty, document?.Revision ?? 0, sourceEpoch);

    static void StartRender(Func<CancellationToken, Sheet> render)
    {
        renderPolicy = new(renderKeepsDocument, renderAdoptsHistory, renderAppliesSettings);
        var reporter = new RenderProgress(progress);
        renderStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        long retained = RenderMemory.Retained(sheet, basePrint);
        var job = renderJobs.Start(CurrentRenderRevision, token => RenderMemory.InSlot(() => reporter.Run(() => render(token)), token, retained));
        activeProgress = reporter;
        cancellation = job.Cancellation;
        renderTask = job.Task;
    }

    static void InvalidateRendering()
    {
        ResetFinalExport();
        sourceEpoch = checked(sourceEpoch + 1);
        renderJobs.Clear();
        cancellation = null;
        renderTask = null;
        capturedCraft = null;
        pendingProjectOpen = pendingProjectLogo = null;
        projectsToOpen = null;
        renderPolicy = null;
        renderAppliesSettings = renderAdoptsHistory = false;
        creatingBlank = captureForView = captureForDetail = detailCommitQueued = false;
    }

    static void RememberAcceptedDocument()
    {
        acceptedDocument = document?.Fork();
        acceptedDefinition = sheet != null && document != null ? DrawingDefinitions.FromAccepted(sheet, document) : null;
    }

    static void RestoreAcceptedDocument()
    {
        if (document != null && acceptedDocument?.Id == document.Id)
            document.Restore(acceptedDocument);
    }
}
