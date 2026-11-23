using System.Reflection;

static class SelectionChecks
{
    public static void Run(Type modType, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        object? Get(string name) => modType.GetField(name, flags)!.GetValue(null);
        void Set(string name, object? value) => modType.GetField(name, flags)!.SetValue(null, value);
        int Value(string name) => (int)Get(name)!;
        void Call(string name, params object?[] args) => modType.GetMethod(name, flags)!.Invoke(null, args);
        var fields = modType.GetFields(flags).Where(f => !f.IsInitOnly && !f.IsLiteral).ToArray();
        var saved = fields.Select(f => f.GetValue(null)).ToArray();
        try
        {
            Set("document", null);
            foreach (string name in new[] { "selectedLabel", "selectedDetail", "selectedText", "selectedKitten", "selectedSpec", "selectedTable", "selectedCanvasView" }) Set(name, 7);
            Call("SelectHeight", 102);
            check(Value("selectedHeight") == 102 && new[] { "selectedLabel", "selectedDetail", "selectedText", "selectedKitten", "selectedSpec", "selectedTable", "selectedCanvasView" }.All(n => Value(n) == -1),
                "Selecting a dimension persists its owning view ID and clears other delete targets");
            foreach (string name in new[] { "hoveredLabel", "hoveredText", "hoveredSpec", "hoveredKitten" }) Set(name, 8);
            Call("RequestAnnotationEdit", 1);
            check(Value("requestedHeightDelete") == 102 && Value("requestedDelete") == -1 && Value("requestedReference") == -1 && Value("requestedViewDelete") == -1,
                "Delete targets the selected dimension even when another object is hovered");
            Call("SelectCanvasView", 103);
            check(Value("selectedHeight") == -1 && Value("selectedCanvasView") == 103 && Value("selectedLabel") == -1 && Value("selectedDetail") == -1,
                "Caption selection selects a main view exclusively");
            Call("RequestAnnotationEdit", 1);
            check(Value("requestedViewDelete") == 103 && Value("requestedHeightDelete") == -1 && Value("requestedDelete") == -1 && Value("requestedReference") == -1,
                "Delete targets the selected view even when another object is hovered");
            Call("SelectHeight", 102);
            Call("SelectLabel", 9);
            check(Value("selectedHeight") == -1, "Selecting a label clears dimension selection");
            Call("SelectHeight", 102);
            Call("SelectDetail", 5);
            check(Value("selectedHeight") == -1, "Selecting a detail clears dimension selection");
            Call("SelectHeight", 102);
            Call("CancelPlacement");
            check(Value("selectedHeight") == -1 && Value("selectedCanvasView") == -1,
                "Canceling the workspace clears dimension and view selection");
        }
        finally
        {
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
        }
    }
}
