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
            var square = Copy(accepted, ("Orientation", 3), ("CustomWidth", 2048), ("CustomHeight", 2048),
                ("OutputTarget", Enum.Parse(Type("DrawingOutput"), "Image")));
            object Size(int width, int height) => Activator.CreateInstance(Type("PageSize"), [width, height])!;
            Call("AdoptPrintSettings", square);
            check(Property("CurrentOutputSize").Equals(Size(2048, 2048)),
                "Shared output controls read the accepted drawing's square image dimensions");
            Set("desktopPreset", 1);
            Call("ChangeOutputTarget", 0);
            check(Property("CurrentOutputSize").Equals(Size(2560, 1440)), "Switching to Desktop Background applies its own preset");
            Call("ChangeOutputTarget", 1);
            check(Property("CurrentOutputSize").Equals(Size(2048, 2048)), "Returning to Image restores the previously chosen square size");
            Call("AdoptPrintSettings", accepted);
            var scene = Activator.CreateInstance(Type("RenderScene"), new object?[]
                { Array.Empty<byte>(), Array.Empty<int>(), Empty("LabelPart"), Empty("ViewRegion"), null, accepted, null })!;
            var sheet = Activator.CreateInstance(Type("Sheet"), new object[]
                { "Recovery fixture", accepted, Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<string>() })!;
            Type("Sheet").GetProperty("Scene")!.SetValue(sheet, scene);
            var doc = Activator.CreateInstance(Type("AnnotationDocument"), new object?[] { scene, null })!;
            Set("sheet", sheet);
            Set("document", doc);
            // Open adopts the project's output. Export quality belongs to the profile.
            Set("detailedExport", true);
            foreach (var size in new[] { (3840, 2160, true), (2048, 2048, false), (2160, 3840, true) })
            {
                var output = Copy(accepted, ("Orientation", 3), ("CustomWidth", size.Item1), ("CustomHeight", size.Item2));
                Call("AdoptPrintSettings", output);
                var reopened = Copy(sheet, ("Options", output));
                Set("sheet", reopened);
                var key = Property("CurrentExportKey");
                check((bool)key.GetType().GetProperty("Detailed")!.GetValue(key)! == size.Item3 &&
                    (bool)Get("detailedExport")!, $"Opening {size.Item1} x {size.Item2} applies the remembered 4K preference");
            }
            Set("detailedExport", false);
            Set("sheet", sheet);
            Call("AdoptPrintSettings", accepted);
            Set("exportSource", sheet);
            Set("exportKey", Property("CurrentExportKey"));
            check((bool)Property("ExportMatches"), "Final PNG key matches the accepted drawing");
            long epoch = (long)Get("sourceEpoch")!;
            Set("sourceEpoch", epoch + 1);
            check(!(bool)Property("ExportMatches"), "A source revision change invalidates already encoded final pixels");
            Set("sourceEpoch", epoch);
            Set("exportKey", null);
            Set("exportSource", null);
            var setup = modType.GetField("setupStage", flags)!.FieldType;
            Set("setupStage", Enum.Parse(setup, "Editing"));
            Call("RequestNewDrawing");
            check((bool)Get("newDrawingDialog")! && Get("setupStage")!.ToString() == "Editing"
                && ReferenceEquals(Get("sheet"), sheet) && ReferenceEquals(Get("document"), doc),
                "New drawing asks first without clearing or leaving the accepted drawing");
            Set("newDrawingDialog", false);
            check(Get("setupStage")!.ToString() == "Editing" && ReferenceEquals(Get("document"), doc),
                "Canceling New drawing retains the current editor and document");
            Call("ConfirmNewDrawing");
            check(!(bool)Get("newDrawingDialog")! && Get("setupStage")!.ToString() == "Choose"
                && ReferenceEquals(Get("sheet"), sheet) && ReferenceEquals(Get("document"), doc),
                "Confirming New drawing opens setup while keeping the previous drawing recoverable");

            var changed = Copy(accepted, ("Theme", 3), ("Orientation", 2), ("Drawing", null),
                ("TitleBlock", Copy(accepted.GetType().GetProperty("TitleBlock")!.GetValue(accepted)!, ("Title", "Pending title"))));
            Call("AdoptPrintSettings", changed);
            Set("printStyleDirty", true);
            Set("templatesOpen", true);
            check(!(bool)Property("CanEditAnnotations") && !(bool)Property("PrintIsCurrent"),
                "Failed regeneration fixture reproduces disabled editing and PNG save");
            Call("ReportFailure", "Preview failed: fixture");
            var notice = Get("activeNotice")!;
            check((string?)notice.GetType().GetProperty("Message")!.GetValue(notice) == "Preview failed: fixture" &&
                (string?)notice.GetType().GetProperty("Code")!.GetValue(notice) == "render.failed",
                "Automatic failure recovery retains a structured Activity notice");
            check((bool)Property("CanEditAnnotations") && (bool)Property("PrintIsCurrent"),
                "Reporting generation failure automatically restores annotation editing and PNG save while the notice is active");
            check(Property("Options").Equals(accepted)
                && !(bool)Get("templatesOpen")! && Get("beforeDrawing") == null && !(bool)Get("printStyleDirty")!,
                "Recovery restores accepted page, title, scale and style and exits templates");
            check(ReferenceEquals(Get("sheet"), sheet) && ReferenceEquals(Get("document"), doc)
                && Get("renderTask") == null,
                "Recovery retains the existing sheet and document without a render or GPU call");
            Set("newDrawingDialog", true);
            check((bool)Property("ControlsLocked") && !(bool)Property("CanEditAnnotations"),
                "Fresh-drawing confirmation blocks edits behind the modal");
            Set("newDrawingDialog", false);
            check((bool)Property("CanEditAnnotations") && ReferenceEquals(Get("document"), doc),
                "Canceling fresh-drawing confirmation leaves the retained document editable");

            var detailType = Type("ExplodedDetail");
            var ctor = detailType.GetConstructors().Single();
            var args = ctor.GetParameters().Select(p => p.DefaultValue).ToArray();
            args[0] = 1;
            args[1] = 17u;
            args[2] = Activator.CreateInstance(Type("Box"), [100f, 120f, 300f, 420f]);
            args[3] = "Coupler";
            var detail = ctor.Invoke(args);
            var details = Array.CreateInstance(detailType, 1);
            details.SetValue(detail, 0);
            doc.GetType().GetMethod("Rebind")!.Invoke(doc, [scene, Empty("PartLabel"), details, false]);
            Call("SelectDetail", 1);
            var listed = Get("listedObject")!;
            check((int)listed.GetType().GetProperty("Id")!.GetValue(listed)! == 1001,
                "Accepting a new detail selects its canvas object for immediate resize");
            Call("SelectDetail", -1);

            var stale = detailType.GetMethod("<Clone>$")!.Invoke(detail, null)!;
            detailType.GetProperty("SourceIssue")!.SetValue(stale, Enum.Parse(Type("DetailSourceIssue"), "MissingRoot"));
            var staleDetails = Array.CreateInstance(detailType, 1);
            staleDetails.SetValue(stale, 0);
            var refreshed = Type("Sheet").GetMethod("<Clone>$")!.Invoke(sheet, null)!;
            Type("Sheet").GetProperty("Details")!.SetValue(sheet, details);
            Type("Sheet").GetProperty("Details")!.SetValue(refreshed, staleDetails);
            Set("sheet", refreshed);
            var review = Activator.CreateInstance(Type("CraftRefreshReview"), [1, 0, 0]);
            var queue = (System.Collections.IEnumerable)Get("missingRoots")!;
            Call("AcceptCraftRefresh", sheet, review);
            check(queue.Cast<int>().SequenceEqual(new[] { 1 }), "Accepting a craft edit queues the missing-root dialog without pressing Delete");
            Call("AcceptCraftRefresh", refreshed, review);
            check(queue.Cast<int>().SequenceEqual(new[] { 1 }), "The same already-unresolved detail does not queue a second dialog");
            queue.GetType().GetMethod("Clear")!.Invoke(queue, null);
            Call("AdoptPrintSettings", changed);
            var taskType = modType.GetField("renderTask", flags)!.FieldType;
            var completion = Activator.CreateInstance(typeof(TaskCompletionSource<>).MakeGenericType(taskType.GenericTypeArguments))!;
            Set("renderTask", completion.GetType().GetProperty("Task")!.GetValue(completion));
            Call("ReportFailure", "Preview failed: fixture");
            check(Property("Options").Equals(changed), "Recovery cannot race an active render");
        }
        finally
        {
            for (int i = 0; i < fields.Length; i++)
                fields[i].SetValue(null, saved[i]);
        }
    }
}
