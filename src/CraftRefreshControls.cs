using KSA;
using System.Reflection;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    readonly record struct CraftRevision(PartTree Tree, int PartCount, ulong History, bool Pending, int Reopen);

    static readonly FieldInfo? editorHistory = typeof(VehicleEditor).GetField("_history", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly PropertyInfo? editorHistoryVersion = editorHistory?.FieldType.GetProperty("Version");
    static readonly PropertyInfo? editorSnapshot = editorHistory?.FieldType.GetProperty("Current");
    static readonly FieldInfo? snapshotHash = editorSnapshot?.PropertyType.GetField("_layoutHash", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo? historyPending = typeof(VehicleEditor).GetField("_historyCommitPending", BindingFlags.Instance | BindingFlags.NonPublic);
    static CraftRevision? observedCraft, acceptedCraft, capturedCraft, attemptedRefresh;
    static int fallbackReopen;

    static CraftRevision? ObserveCraft(VehicleEditor editor)
    {
        if (editor.EditingSpace.Parts is not { } tree) return null;
        var history = editorHistory?.GetValue(editor);
        var snapshot = history == null ? null : editorSnapshot?.GetValue(history);
        // KSA replaces equivalent history snapshots as well as committing edits.
        // Its cached content hash includes transforms, topology and module state.
        ulong? revision = snapshot == null ? null : snapshotHash?.GetValue(snapshot) as ulong?;
        if (revision == null && history != null && editorHistoryVersion?.GetValue(history) is int version)
            revision = unchecked((uint)version);
        bool pending = historyPending?.GetValue(editor) is true;
        return new(tree, tree.Parts.Length, revision ?? 0, pending, revision.HasValue ? 0 : fallbackReopen);
    }

    static void ResetCraftRefresh()
    {
        observedCraft = acceptedCraft = capturedCraft = attemptedRefresh = null;
        missingRoots.Clear();
        activeNotice = null;
        missingDetailDelete = replacementDetail = -1;
    }

    static bool CraftNeedsRefresh => sheet?.PoseSource != null && acceptedCraft != observedCraft;

    static void ObserveCraftChanges(VehicleEditor editor)
    {
        if (!ReferenceEquals(editor, sourceEditor)) return;
        var current = ObserveCraft(editor);
        if (observedCraft == current) return;
        observedCraft = current;
        attemptedRefresh = null;
        if (capturedCraft != null || renderTask != null)
        {
            InvalidateRendering();
            KeepAcceptedPrintEditable();
            AddActivity("preview.stale", "The vehicle changed while PawPrints was rendering.", notify: sheet?.PoseSource == null);
        }
        if (CraftNeedsRefresh)
            PrintDiagnostics.Record("craft.refresh.changed");
    }

    static void TryAutoRefresh(VehicleEditor editor)
    {
        if (!open || setupStage != SetupStage.Editing || ControlsLocked || missingDetailDelete >= 0 || !CraftNeedsRefresh ||
            observedCraft is not { PartCount: > 0, Pending: false } || observedCraft == attemptedRefresh || !ValidTitleDates()) return;
        attemptedRefresh = observedCraft;
        Generate(editor);
    }
}
