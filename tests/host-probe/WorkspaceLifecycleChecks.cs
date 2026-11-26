using System.Reflection;
using System.Runtime.CompilerServices;

static class WorkspaceLifecycleChecks
{
    public static void Run(Type mod, Type editorType, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        object? Get(string name) => mod.GetField(name, flags)!.GetValue(null);
        void Set(string name, object? value) => mod.GetField(name, flags)!.SetValue(null, value);

        var close = mod.GetNestedType("EditorClosedPatch", BindingFlags.NonPublic)!.GetMethod("Prefix", flags)!;
        var fields = mod.GetFields(flags).Where(f => !f.IsInitOnly && !f.IsLiteral).ToArray();
        var saved = fields.Select(f => f.GetValue(null)).ToArray();
        var owner = RuntimeHelpers.GetUninitializedObject(editorType);
        var other = RuntimeHelpers.GetUninitializedObject(editorType);
        var draft = RuntimeHelpers.GetUninitializedObject(mod.GetNestedType("NewDrawingDraft", BindingFlags.NonPublic)!);
        var stageType = mod.GetNestedType("SetupStage", BindingFlags.NonPublic)!;
        try
        {
            // An open setup has no sheet or capture owner until the first render.
            Set("sheet", null);
            Set("sourceEditor", null);
            Set("newDrawing", draft);
            Set("setupStage", Enum.Parse(stageType, "SelectViews"));
            Set("setupGridOpened", true);
            Set("open", true);
            Set("workspaceEditor", owner);
            Set("annotationKeysActive", true);
            Set("spaceHeld", true);

            close.Invoke(null, [other]);
            check(Get("open") is true && ReferenceEquals(Get("newDrawing"), draft),
                "Disposing an unrelated native editor keeps the current setup open");
            close.Invoke(null, [owner]);
            check(Get("open") is false && Get("workspaceEditor") == null && Get("newDrawing") == null &&
                Get("setupGridOpened") is false && Get("setupStage")!.ToString() == "Choose",
                "Leaving the native editor closes New Drawing even before a sheet exists");
            check(Get("annotationKeysActive") is false && Get("spaceHeld") is false,
                "Closing a native session releases PawPrints keyboard input");
        }
        finally
        {
            for (int i = 0; i < fields.Length; i++)
                fields[i].SetValue(null, saved[i]);
        }
    }
}
