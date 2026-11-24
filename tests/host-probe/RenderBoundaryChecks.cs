using System.Linq.Expressions;
using System.Reflection;

static class RenderBoundaryChecks
{
    public static void Run(Type modType, Assembly mod, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance;
        string[] names = ["document", "sourceEditor", "renderTask", "cancellation", "sourceEpoch", "renderSourceTree",
            "renderPolicy", "renderKeepsDocument", "renderAdoptsHistory", "renderAppliesSettings"];
        var fields = names.Select(n => modType.GetField(n, flags)!).ToArray();
        var before = fields.Select(f => f.GetValue(null)).ToArray();
        void Set(string name, object? value) => modType.GetField(name, flags)!.SetValue(null, value);
        object? Get(string name) => modType.GetField(name, flags)!.GetValue(null);
        var queue = Get("renderJobs")!;
        check(queue.GetType().GetProperty("Current")!.GetValue(queue) == null, "Headless render boundary begins without a real active job");
        try
        {
            Set("document", null); Set("sourceEditor", null);
            Set("renderKeepsDocument", true); Set("renderAdoptsHistory", false); Set("renderAppliesSettings", true);
            var sheetType = mod.GetType("Blueprint.Rendering.Sheet", true)!;
            var callbackType = typeof(Func<,>).MakeGenericType(typeof(CancellationToken), sheetType);
            var callback = Expression.Lambda(callbackType, Expression.Constant(null, sheetType),
                Expression.Parameter(typeof(CancellationToken), "token")).Compile();
            modType.GetMethod("StartRender", flags)!.Invoke(null, [callback]);
            var task = (Task)Get("renderTask")!;
            task.GetAwaiter().GetResult();
            var policy = Get("renderPolicy")!;
            Set("renderKeepsDocument", false); Set("renderAdoptsHistory", true); Set("renderAppliesSettings", false);
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
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, before[i]);
        }
    }
}
