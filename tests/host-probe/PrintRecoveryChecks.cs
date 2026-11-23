using System.Reflection;

static class PrintRecoveryChecks
{
    public static void Run(Type modType, Assembly mod, Action<bool, string> check)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        object? Get(string name) => modType.GetField(name, flags)!.GetValue(null);
        void Set(string name, object? value) => modType.GetField(name, flags)!.SetValue(null, value);
        object Property(string name) => modType.GetProperty(name, flags)!.GetValue(null)!;
        void Call(string name, params object?[] args) => modType.GetMethod(name, flags)!.Invoke(null, args);
        Type Type(string name) => mod.GetType("Blueprint.Rendering." + name, true)!;
        Array Empty(string name) => Array.CreateInstance(Type(name), 0);
        object Copy(object source, params (string Key, object? Value)[] changes)
        {
            var ctor = source.GetType().GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            return ctor.Invoke(ctor.GetParameters().Select(p => changes.Any(c => c.Key == p.Name)
                ? changes.Single(c => c.Key == p.Name).Value : source.GetType().GetProperty(p.Name!)!.GetValue(source)).ToArray());
        }
        var fields = modType.GetFields(flags).Where(f => !f.IsInitOnly && !f.IsLiteral).ToArray();
        var saved = fields.Select(f => f.GetValue(null)).ToArray();
        try
        {
            var accepted = Property("Options");
            var scene = Activator.CreateInstance(Type("RenderScene"), new object?[]
                { Array.Empty<byte>(), Array.Empty<int>(), Empty("LabelPart"), Empty("ViewRegion"), null, accepted, null })!;
            var sheet = Activator.CreateInstance(Type("Sheet"), new object[]
                { "Recovery fixture", accepted, Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<string>() })!;
            Type("Sheet").GetProperty("Scene")!.SetValue(sheet, scene);
            var doc = Activator.CreateInstance(Type("AnnotationDocument"), new object?[] { scene, null })!;
            Set("sheet", sheet);
            Set("document", doc);
            var changed = Copy(accepted, ("Theme", 3), ("Orientation", 2), ("Drawing", null),
                ("TitleBlock", Copy(accepted.GetType().GetProperty("TitleBlock")!.GetValue(accepted)!, ("Title", "Pending title"))));
            Call("AdoptPrintSettings", changed);
            Set("printStyleDirty", true);
            Set("templatesOpen", true);
            Set("warningNotice", "Preview failed: Cannot keep label 'removed part'.");
            check(!(bool)Property("CanEditAnnotations") && !(bool)Property("PrintIsCurrent"),
                "Failed regeneration fixture reproduces disabled editing and PNG save");
            Call("ReportFailure", "Preview failed: fixture");
            check((string?)Get("warningNotice") == "Preview failed: fixture", "Automatic failure recovery retains the error explanation");
            Set("warningNotice", null); // Dismiss the warning, as OK does in the UI.
            check((bool)Property("CanEditAnnotations") && (bool)Property("PrintIsCurrent"),
                "Reporting generation failure automatically restores annotation editing and PNG save without capturing the changed craft");
            check(Property("Options").Equals(accepted) && Get("warningNotice") == null
                && !(bool)Get("templatesOpen")! && Get("beforeDrawing") == null && !(bool)Get("printStyleDirty")!,
                "Recovery restores accepted page, title, scale and style; exits templates after warning dismissal");
            check(ReferenceEquals(Get("sheet"), sheet) && ReferenceEquals(Get("document"), doc)
                && Get("renderTask") == null,
                "Recovery retains the existing sheet and document without a render or GPU call");
            Set("newDrawingDialog", true);
            check((bool)Property("ControlsLocked") && !(bool)Property("CanEditAnnotations"),
                "Fresh-drawing confirmation blocks edits behind the modal");
            Set("newDrawingDialog", false);
            check((bool)Property("CanEditAnnotations") && ReferenceEquals(Get("document"), doc),
                "Canceling fresh-drawing confirmation leaves the retained document editable");
            Call("AdoptPrintSettings", changed);
            var taskType = modType.GetField("renderTask", flags)!.FieldType;
            var completion = Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(taskType.GenericTypeArguments))!;
            Set("renderTask", completion.GetType().GetProperty("Task")!.GetValue(completion));
            Call("ReportFailure", "Preview failed: fixture");
            check(Property("Options").Equals(changed), "Recovery cannot race an active render");
        }
        finally
        {
            for (int i = 0; i < fields.Length; i++) fields[i].SetValue(null, saved[i]);
        }
    }
}
