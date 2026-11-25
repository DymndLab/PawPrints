using System.Linq.Expressions;
using System.Reflection;

static class RenderBoundaryChecks
{
    public static void Run(Type modType, Assembly mod, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        string[] names = ["document", "sourceEditor", "renderTask", "cancellation", "sourceEpoch", "capturedCraft",
            "creatingBlank", "captureForView", "captureForDetail", "detailCommitQueued",
            "renderPolicy", "renderKeepsDocument", "renderAdoptsHistory", "renderAppliesSettings", "selectedTable", "selectedCanvasView", "selectedHeight", "selectedText", "selectedSpec", "selectedLabel", "selectedKitten", "hoveredLabel", "requestedEdit", "requestedDelete", "requestedViewDelete", "requestedHeightDelete", "requestedTableDelete", "requestedReference", "requestedDetailDelete", "selectedDetail", "listedObject", "detailEdit", "selectedGroup"];
        var fields = names.Select(n => modType.GetField(n, flags)!).ToArray();
        var before = fields.Select(f => f.GetValue(null)).ToArray();
        void Set(string name, object? value) => modType.GetField(name, flags)!.SetValue(null, value);
        object? Get(string name) => modType.GetField(name, flags)!.GetValue(null);
        var options = modType.GetProperty("Options", flags)!.GetValue(null)!;
        check((bool)options.GetType().GetProperty("FreeLayout")!.GetValue(options)!,
            "Production editor options enable free layout consistently with blank drawing creation");
        var queue = Get("renderJobs")!;
        check(queue.GetType().GetProperty("Current")!.GetValue(queue) == null, "Headless render boundary begins without a real active job");
        try
        {
            foreach (string field in new[] { "selectedCanvasView", "selectedHeight", "selectedText", "selectedSpec", "selectedLabel", "selectedKitten" })
                Set(field, -1);
            Set("selectedTable", 0);
            Set("hoveredLabel", 42);
            modType.GetMethod("RequestAnnotationEdit", flags)!.Invoke(null, [1]);
            check((int)Get("requestedTableDelete")! == 0 && (int)Get("requestedDelete")! == -1,
                "Delete snapshots the selected table instead of an unrelated hovered label");
            foreach (string prior in new[] { "selectedCanvasView", "selectedHeight", "selectedText", "selectedSpec", "selectedKitten", "selectedTable", "selectedDetail" })
            {
                Set(prior, 3);
                modType.GetMethod("SelectLabel", flags)!.Invoke(null, [12]);
                modType.GetMethod("RequestAnnotationEdit", flags)!.Invoke(null, [1]);
                check((int)Get("requestedDelete")! == 12 && (int)Get("requestedViewDelete")! == -1
                    && (int)Get("requestedTableDelete")! == -1 && (int)Get("requestedDetailDelete")! == -1
                    && (int)Get("requestedHeightDelete")! == -1 && (int)Get("requestedReference")! == -1,
                    "Selecting a flagnote after " + prior + " makes Delete target the note, not stale selection");
            }
            var previewType = mod.GetType("Blueprint.Rendering.PlacementPreview", true)!;
            var boxType = mod.GetType("Blueprint.Rendering.Box", true)!;
            var box = Activator.CreateInstance(boxType, [0f, 0f, 40f, 40f])!;
            var oldPreview = Activator.CreateInstance(previewType, [40, 40, new byte[6400], box, null])!;
            var nextPreview = Activator.CreateInstance(previewType, [40, 40, new byte[6400], box, null])!;
            Set("ghostPixels", oldPreview);
            Set("ghostLastBox", box);
            var prepareType = typeof(Func<,>).MakeGenericType(typeof(CancellationToken), previewType);
            var prepare = Expression.Lambda(prepareType, Expression.Constant(nextPreview, previewType),
                Expression.Parameter(typeof(CancellationToken))).Compile();
            var commitType = typeof(Action<>).MakeGenericType(boxType);
            var commit = Expression.Lambda(commitType, Expression.Empty(), Expression.Parameter(boxType)).Compile();
            modType.GetMethod("StartGhost", flags)!.Invoke(null, ["projection", prepare, commit, null, null, false, true]);
            check(ReferenceEquals(Get("ghostPixels"), oldPreview) && Equals(Get("ghostLastBox"), box),
                "Projection regeneration retains displayed ink and location until replacement is ready");
            ((Task)Get("ghostTask")!).GetAwaiter().GetResult();
            modType.GetMethod("CancelGhost", flags)!.Invoke(null, null);
            check(Get("ghostPixels") == null && Get("ghostLastBox") == null && Get("ghostCommit") == null,
                "Right-click ghost cancellation clears retained preview and pending confirmation target");
            modType.GetMethod("StartGhost", flags)!.Invoke(null, ["primary view", prepare, commit, null, null, false, false]);
            var viewType = mod.GetType("Blueprint.Rendering.DrawingView", true)!;
            Set("viewDraft", Activator.CreateInstance(viewType, [100, 0, .5f, .5f, null, 0, null, -1, 3, null]));
            var confirm = modType.GetMethod("RequestViewConfirmation", flags)!;
            check((bool)confirm.Invoke(null, null)! && Get("ghostConfirmRequested") is true
                && Get("ghostLastBox") == null && Get("ghostPress") == null,
                "A primary view accepts keyboard confirmation before any canvas interaction or preview upload");
            ((Task)Get("ghostTask")!).GetAwaiter().GetResult();
            modType.GetMethod("CancelGhost", flags)!.Invoke(null, null);
            check(Get("ghostConfirmRequested") is false && !(bool)confirm.Invoke(null, null)!,
                "Canceling the draft clears its queued confirmation");
            Set("document", null);
            Set("sourceEditor", null);
            Set("renderKeepsDocument", true);
            Set("renderAdoptsHistory", false);
            Set("renderAppliesSettings", true);
            var sheetType = mod.GetType("Blueprint.Rendering.Sheet", true)!;
            var callbackType = typeof(Func<,>).MakeGenericType(typeof(CancellationToken), sheetType);
            var callback = Expression.Lambda(callbackType, Expression.Constant(null, sheetType),
                Expression.Parameter(typeof(CancellationToken), "token")).Compile();
            modType.GetMethod("StartRender", flags)!.Invoke(null, [callback]);
            var task = (Task)Get("renderTask")!;
            task.GetAwaiter().GetResult();
            var policy = Get("renderPolicy")!;
            Set("renderKeepsDocument", false);
            Set("renderAdoptsHistory", true);
            Set("renderAppliesSettings", false);
            check((bool)policy.GetType().GetProperty("KeepDocument")!.GetValue(policy)!
                && !(bool)policy.GetType().GetProperty("AdoptHistory")!.GetValue(policy)!
                && (bool)policy.GetType().GetProperty("ApplySettings")!.GetValue(policy)!,
                "Production render submission captures its commit policy independently of later UI flags");
            long epoch = (long)Get("sourceEpoch")!;
            modType.GetMethod("InvalidateRendering", flags)!.Invoke(null, null);
            check(Get("renderTask") == null && Get("cancellation") == null && Get("renderPolicy") == null
                && queue.GetType().GetProperty("Current")!.GetValue(queue) == null && (long)Get("sourceEpoch")! > epoch,
                "Production source invalidation retires even a completed job before preview upload");
        }
        finally
        {
            queue.GetType().GetMethod("Clear")!.Invoke(queue, null);
            for (int i = 0; i < fields.Length; i++)
                fields[i].SetValue(null, before[i]);
        }
    }
}
