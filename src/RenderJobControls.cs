using Blueprint.Rendering;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    sealed record RenderCommitPolicy(bool KeepDocument, bool AdoptHistory, bool ApplySettings);
    static readonly RenderJobs<Sheet> renderJobs = new();
    static RenderCommitPolicy? renderPolicy;
    static long sourceEpoch;
    static PartTree? renderSourceTree;
    static AnnotationDocument? acceptedDocument;
    static DrawingDefinition? acceptedDefinition;
    static readonly DrawingNotices drawingNotices = new();
    static RenderRevision CurrentRenderRevision => new(document?.Id ?? Guid.Empty, document?.Revision ?? 0, sourceEpoch);

    static void StartRender(Func<CancellationToken, Sheet> render)
    {
        renderSourceTree = sourceEditor?.EditingSpace.Parts;
        renderPolicy = new(renderKeepsDocument, renderAdoptsHistory, renderAppliesSettings);
        var job = renderJobs.Start(CurrentRenderRevision, render);
        cancellation = job.Cancellation;
        renderTask = job.Task;
    }

    static void InvalidateRendering()
    {
        sourceEpoch = checked(sourceEpoch + 1);
        renderJobs.Clear();
        cancellation = null;
        renderTask = null;
        renderPolicy = null;
        renderAppliesSettings = renderAdoptsHistory = false;
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
