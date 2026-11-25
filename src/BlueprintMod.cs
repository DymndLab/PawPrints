using System.Diagnostics;
using Blueprint.Storage;
using System.Security.Cryptography;
using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.GlfwApi;
using Brutal.Numerics;
using HarmonyLib;
using KSA;
using StarMap.API;

namespace Blueprint;

[StarMapMod]
public sealed partial class BlueprintMod
{
    const string Owner = "blueprint.exporter.5801";
    public const string SupportedHash = "FA741419E287FB7F673FBA3212BE622B5FC27DB91FA4A6632828FB9DA7B15C7E";
    const string SupportedBuild = "2026.11.25.5949";
    static readonly string[] Views = ["Three-quarter", "Front", "Side", "Top", "Isometric"];
    static readonly string[] Themes = ["Blue", "White", "Cream", "Charcoal"];
    static readonly string[] GridWeights = ["Uniform", "Every 5 squares", "Every 5 and 10 squares"];
    Harmony? harmony;
    static bool enabled, open;
    static readonly PrintWorkspaceFocus workspaceFocus = new();
    static int view = 0, theme, emphasis = 2;
    static bool grid = true;
    static int archivalWear;
    static readonly string[] WearNames = ["Off", "1", "2", "3", "4", "5"];
    static bool menuReported;
    static int layout = 2, visibleViews = 15, drawingSize = 100, arrangement = 0;
    static bool captions = true, partsTable, flagTable;
    static FixedViewLayout boxLayout = new();
    static bool zones = true;
    static int customWidth = ImagePresets.Default.Width, customHeight = ImagePresets.Default.Height;
    static int desktopPreset = 1, customWidthEdit = customWidth, customHeightEdit = customHeight;
    static AnnotationDocument? document;
    internal static bool MenuHookRan
    {
        get; private set;
    }


    static bool HasEditorVehicle => KSA.Program.Editor?.EditingSpace.Parts is { } tree && tree.Parts.Length > 0;
    static bool CanOpenWorkspace => HasEditorVehicle || sheet != null && KSA.Program.Editor is { } editor &&
        ReferenceEquals(editor, sourceEditor) && sourceCraft == editor.EditingSpace.Id;
    static bool NativeDialogOpen => GameSettings.Show || GameSaves.IsOpen || VehicleSaves.IsOpen;

    internal static void OpenExport()
    {
        if (!enabled || !CanOpenWorkspace)
            return;
        if (!open)
            fallbackReopen++;
        open = true;
        workspaceFocus.Request();
    }
    static VehicleEditor? sourceEditor;
    static string? sourceCraft;
    static Task<Sheet>? renderTask;
    static CancellationTokenSource? cancellation;
    static Sheet? sheet;
    static PreviewTexture? preview;
    static Sheet? basePrint;
    static bool renderKeepsDocument;
    static bool renderAdoptsHistory;
    static string status = "Place a primary view, then project additional views from it.";
    static string progress = "";
    static string? exportFolder;
    static bool busy => renderTask != null || saveTask != null || logoTask != null || exportFolderTask != null;
    static PrintOptions Options => new(view, theme, grid, archivalWear > 0, emphasis switch { 1 => 5, 2 => 10, _ => 0 }, layout, visibleViews, captions, partsTable, flagTable, drawingSize, pageOrientation, pageAspect, brand, TitleData, arrangement, boxLayout, drawingPlacement, zones, customWidth, customHeight, archivalWear, outputTarget == 0 ? DrawingOutput.DesktopBackground : DrawingOutput.Image, FreeLayout: true, TitleBlockVisible: titleBlockVisible, WearSeed: sheet?.Options.WearSeed ?? 0, ProjectionAngle: CurrentProjectionAngle);



    [StarMapBeforeMain]
    public void Load()
    {
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(VehicleEditor).Assembly.Location)));
            if (hash != SupportedHash)
                throw new NotSupportedException(
                    $"PawPrints requires the inspected KSA build {SupportedBuild}. Installed KSA.dll SHA-256: {hash}. No patches applied.");
            // StarMap resolves both -InstancePath and its environment override here.
            // Reading the environment first would bypass its precedence rules.
            PrintDiagnostics.Start(Path.Combine(KSA.Constants.DocumentsFolderPath, "logs"), PrintIdentity.Version);
#if PAWPRINTS_PORTABLE_RENDERING
            if (OperatingSystem.IsLinux())
                PortableRendering.Initialize(Path.Combine(Path.GetDirectoryName(typeof(VehicleEditor).Assembly.Location)!,
                    "Content", "Core", "JetBrainsMono-Regular.ttf"));
#else
            if (OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("This PawPrints build uses the Windows drawing backend.");
#endif
            InitializeExportFolder();
            InitializeTitleBlock();
            harmony = new Harmony(Owner);
            harmony.PatchAll(typeof(BlueprintMod).Assembly);
            enabled = true;
            PrintDiagnostics.Record("mod.loaded");
            Console.WriteLine($"[Paw Prints] V{PrintIdentity.Version} loaded for KSA {SupportedBuild}. PNG destination: {exportFolder}");
        }
        catch (Exception e) { PrintDiagnostics.Error("mod.load", e); Console.WriteLine("[PawPrints] Disabled: " + e); harmony?.UnpatchAll(Owner); }
    }


    [StarMapUnload]
    public void Unload()
    {
        DiscardDrawingDraft();
        ReleaseSetupPaper();
        enabled = false;
        open = false;
        InvalidateRendering();
        CancelTextPreview();
        CancelGhost();
        DisposeGhosts();
        DisposeWearTextures();
        ClearSetupGesture();
        ReleaseSetupSketch();
        // Worker owns only managed snapshots. Its task is observed even if the UI never draws again.
        if (renderTask != null)
            _ = renderTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        if (saveTask != null)
            _ = saveTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        preview?.Dispose();
        preview = null;
        sheet = null;
        basePrint = null;
        sourceEditor = null;
        document = null;
        acceptedDocument = null;
        acceptedDefinition = null;
        ResetCraftRefresh();
        CancelPlacement();
        ResetDetailEditor();
        ReleasePrecisionCursor();
        harmony?.UnpatchAll(Owner);
        PrintDiagnostics.Stop();
    }



    [HarmonyPatch(typeof(KSA.Program), nameof(KSA.Program.Dispose))]
    static class ShutdownPatch
    {


        static void Prefix()
        {
            // Release Vulkan resources while the device and ImGui backend still exist.
            DiscardDrawingDraft();
            ReleaseSetupPaper();
            PrintDiagnostics.Record("game.shutdown");
            ReleasePrecisionCursor();
            enabled = false;
            open = false;
            InvalidateRendering();
            CancelTextPreview();
            CancelGhost();
            DisposeGhosts();
            preview?.Dispose();
            preview = null;
            sheet = null;
            basePrint = null;
            sourceEditor = null;
            document = null;
            acceptedDocument = null;
            acceptedDefinition = null;
            ResetCraftRefresh();
            CancelPlacement();
            ResetDetailEditor();
        }
    }



    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.Dispose))]
    static class EditorClosedPatch
    {


        static void Prefix(VehicleEditor __instance)
        {
            if (!ReferenceEquals(sourceEditor, __instance))
                return;
            InvalidateRendering();
            preview?.Dispose();
            preview = null;
            sheet = null;
            basePrint = null;
            sourceEditor = null;
            document = null;
            acceptedDocument = null;
            acceptedDefinition = null;
            ResetCraftRefresh();
            CancelPlacement();
            ResetDetailEditor();
            drawingPlacement = new(100, []);
            status = "Editor closed. Generate a fresh preview after loading a craft.";
        }
    }



    internal static void Generate(VehicleEditor editor, bool startFresh = false,
        LayoutTemplate? starter = null, bool fitStarter = false) =>
        GenerateDrawing(editor, startFresh, starter, fitStarter, null);

    static void GenerateDrawing(VehicleEditor editor, bool startFresh, LayoutTemplate? starter, bool fitStarter, PrintOptions? setupOptions)
    {
        if (setupOptions == null) EnsureDrawingTitle(editor);
        if (busy || (setupOptions == null && !ValidTitleDates()))
            return;
        try
        {
            PrintDiagnostics.Record("generate.capture.begin");
            status = "Capturing current craft";
            composingLabels = false;
            renderKeepsDocument = false;
            renderAdoptsHistory = false;
            var revision = ObserveCraft(editor);
            bool refresh = !startFresh && sheet?.PoseSource != null && ReferenceEquals(sourceEditor, editor) &&
                sourceCraft == editor.EditingSpace.Id && acceptedCraft != revision;
            var options = setupOptions ?? Options;
            var capture = CraftCapture.TakeForDrawing(editor, options.Page, sheet, basePrint);
            if (revision != ObserveCraft(editor))
                throw new InvalidOperationException("The vehicle changed during capture. Generate PawPrint to try again.");
            PrintDiagnostics.Record("generate.capture.complete");
            var parts = CraftCapture.LabelParts(capture);
            var previous = !startFresh && ReferenceEquals(sourceEditor, editor) && sourceCraft == capture.Name && sheet != null && document != null
                ? sheet with
                {
                    Scene = document.Scene,
                    Labels = document.Labels.ToArray(),
                    Details = document.Details.ToArray()
                } : null;
            if (previous != null)
                previous = PendingStyle(previous);
            if (setupOptions == null && (previous == null || startFresh))
                options = options with { WearSeed = WearPlacement.NewSeed() };
            renderAppliesSettings = true;
            renderKeepsDocument = !startFresh && previous != null;
            sourceEditor = editor;
            sourceCraft = capture.Name;
            observedCraft = capturedCraft = revision;
            if (refresh) PrintDiagnostics.Record("craft.refresh.begin");
            progress = "Reading materials";
            StartRender(token =>
            {
                PrintDiagnostics.Record("generate.render.begin");
                long started = Stopwatch.GetTimestamp();
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                var maps = new Dictionary<string, Texture>();
                foreach (var pair in capture.Textures)
                {
                    token.ThrowIfCancellationRequested();
                    maps.Add(pair.Key, pair.Value.Decode(token));
                }
                long decoded = Stopwatch.GetTimestamp();
                var poseSource = new PoseSource(capture.Assembly!, capture.Poses, capture.Geometry.Meshes.Length, parts, maps, capture.Performance) { DetachedGeometryBytes = capture.DetachedGeometryBytes };
                var result = starter != null
                    ? StarterDrawings.Render(poseSource, capture.Geometry, capture.Name, options, capture.Warnings, starter, fitStarter, token)
                    : PrintGeneration.Render(poseSource, capture.Geometry, capture.Name, options, capture.Warnings, previous, startFresh, token, refresh);
                PrintDiagnostics.Record($"generate.render.complete materials={maps.Count} material_ms={(long)Stopwatch.GetElapsedTime(started, decoded).TotalMilliseconds} drawing_ms={(long)Stopwatch.GetElapsedTime(decoded).TotalMilliseconds} worker_allocated_bytes={GC.GetAllocatedBytesForCurrentThread() - allocated}");
                return result;
            });
        }
        catch (Exception e) { capturedCraft = null; renderAppliesSettings = false; PrintDiagnostics.Error("generate.capture", e); ReportFailure("Export unavailable: " + e.Message); Console.WriteLine("[PawPrints] " + e); }
    }


    internal static void Poll()
    {
        PollProfileTransfer();
        PollExportFolder();
        PollLogo();
        PollGhost();
        if (textDraft == null && textPreview != null)
            CancelTextPreview();
        if (renderTask?.IsCompleted == true)
        {
            var completed = renderTask;
            var job = renderJobs.Current;
            renderTask = null;
            PreviewTexture? replacement = null;
            try
            {
                if (job == null || !ReferenceEquals(job.Task, completed) || !renderJobs.Matches(job, CurrentRenderRevision))
                {
                    _ = completed.Exception;
                    PrintDiagnostics.Record("preview.discarded.stale");
                    drawingNotices.Add(new("preview.stale", "Discarded an outdated render result."));
                    return;
                }
                var result = completed.GetAwaiter().GetResult();
                if (!renderJobs.CanAccept(job, CurrentRenderRevision))
                {
                    KeepAcceptedPrintEditable();
                    AddActivity("render.cancelled", "Preview cancelled. Your drawing is still available.", notify: true);
                    return;
                }
                var policy = renderPolicy ?? throw new InvalidOperationException("Render request has no commit policy.");
                renderKeepsDocument = policy.KeepDocument;
                renderAdoptsHistory = policy.AdoptHistory;
                renderAppliesSettings = policy.ApplySettings;
                long publishStarted = Stopwatch.GetTimestamp();
                var prepared = DrawingAcceptance.Prepare(result, document, renderKeepsDocument, renderAdoptsHistory);
                double prepareMs = Stopwatch.GetElapsedTime(publishStarted).TotalMilliseconds;
                result = prepared.Sheet;
                var regenerated = prepared.RegeneratedBase;
                // Allocate/upload before changing accepted state or disposing its preview.
                long uploadStarted = Stopwatch.GetTimestamp();
                replacement = new PreviewTexture(result);
                double uploadMs = Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds;
                if (renderAdoptsHistory || (creatingBlank && newDrawing != null))
                {
                    if (pendingProjectLogo is { } file && result.Options.TitleBlock?.Logo is { } image)
                        knownLogoNames.AddOrUpdate(image, file);
                    AdoptPrintSettings(result.Options);
                    printStyleDirty = false;
                    var firstLabel = result.Labels.FirstOrDefault();
                    var firstSpec = result.Scene?.Specs.FirstOrDefault();
                    if (firstLabel != null || firstSpec != null)
                    {
                        defaultLine = (int)(firstLabel?.Line ?? firstSpec!.Line);
                        defaultTip = (int)(firstLabel?.Tip ?? firstSpec!.Tip);
                    }
                    else if (result.Details.FirstOrDefault() is { } firstDetail)
                        defaultLine = (int)firstDetail.GuideLine;
                    if (creatingBlank && newDrawing != null) ApplySavedEditorPreferences();
                }
                if (renderAppliesSettings)
                {
                    printStyleDirty = false;
                    renderAppliesSettings = false;
                }
                drawingPlacement = result.Scene?.Drawing;
                draftRatio = drawingPlacement?.Ratio ?? 100;
                partsTable = result.Options.PartsTable;
                flagTable = result.Options.FlagTable;
                long retireStarted = Stopwatch.GetTimestamp();
                preview?.Dispose();
                double retireMs = Stopwatch.GetElapsedTime(retireStarted).TotalMilliseconds;
                preview = replacement;
                replacement = null;
                var previous = sheet;
                ReportCanvasClipping(previous, result);
                sheet = result with { Refresh = null };
                if (capturedCraft is { } revision)
                {
                    acceptedCraft = revision;
                    AcceptCraftRefresh(previous, result.Refresh);
                }
                if (result.PoseMain != null && result.Options.Boxes != null)
                    boxLayout = result.Options.Boxes;
                if (!renderKeepsDocument)
                {
                    missingRoots.Clear();
                    replacementDetail = missingDetailDelete = -1;
                    basePrint = result;
                    if (result.Options.Boxes != null)
                        boxLayout = result.Options.Boxes;
                    ResetDetailEditor();
                    templatesOpen = false;
                    beforeTemplates = null;
                    beforeDrawing = null;
                }
                if (regenerated != null)
                {
                    basePrint = regenerated;
                    if (result.Options.Boxes != null)
                        boxLayout = result.Options.Boxes;
                    templatesOpen = false;
                    beforeTemplates = null;
                    beforeDrawing = null;
                }
                document = prepared.Document;
                acceptedDocument = document.Fork();
                acceptedDefinition = prepared.Definition;
                if (pendingProjectOpen is { } openedProject)
                {
                    MarkProjectSaved(openedProject);
                    DiscardDrawingDraft();
                    setupStage = SetupStage.Editing;
                    AddActivity("project.opened", "Editable drawing opened.", notify: true);
                }
                if (!renderKeepsDocument)
                {
                    selectedLabel = -1;
                    CancelPlacement();
                }
                if (renderKeepsDocument)
                {
                    int label = selectedLabel;
                    var group = selectedGroup;
                    SelectDetail(selectedDetail);
                    selectedLabel = document.Labels.Any(l => l.Id == label) ? label : -1;
                    selectedGroup = group;
                }
                if (result.Refresh != null)
                {
                    if (listedObject is { Kind: DrawingObjectKind.Label } selected && !document.Labels.Any(l => l.Id == selected.Id))
                        listedObject = null;
                    canvasTarget = null;
                }
                detailCommitQueued = false;
                renderAdoptsHistory = false;
                status = result.Notice ?? $"Preview ready - {result.Options.OutputPage.Width} x {result.Options.OutputPage.Height} PNG. Hover over a part and drag out a label.";
                if (creatingBlank)
                {
                    DiscardDrawingDraft();
                    setupStage = SetupStage.Editing;
                }
                if (captureForView)
                {
                    captureForView = false;
                    BeginViewDraft(null, captureViewCamera);
                }
                if (captureForDetail)
                {
                    captureForDetail = false;
                    BeginExplodedPlacement();
                }
                double publishMs = Stopwatch.GetElapsedTime(publishStarted).TotalMilliseconds;
                if (publishMs >= 16)
                    PrintDiagnostics.Record($"preview.publish page_pixels={(long)result.PixelWidth * result.PixelHeight} prepare_ms={(long)prepareMs} upload_ms={(long)uploadMs} retire_ms={(long)retireMs} total_ms={(long)publishMs}");
                PrintDiagnostics.Record("preview.ready");
                Console.WriteLine("[PawPrints] Preview ready: " + sheet.Title);
            }
            catch (OperationCanceledException)
            {
                KeepAcceptedPrintEditable();
                AddActivity("render.cancelled", "Preview cancelled. Your drawing is still available.", notify: true);
            }
            catch (Exception e)
            {
                if (detailCommitQueued)
                {
                    SelectDetail(selectedDetail);
                    detailCommitQueued = false;
                }
                renderAppliesSettings = false;
                PrintDiagnostics.Error("preview.complete", e);
                ReportFailure("Preview failed: " + e.Message);
                Console.WriteLine("[PawPrints] " + e);
                renderAdoptsHistory = false;
            }
            finally
            {
                creatingBlank = false;
                captureForView = captureForDetail = false;
                replacement?.Dispose();
                if (job != null)
                    renderJobs.Finish(job);
                cancellation = null;
                renderPolicy = null;
                capturedCraft = null;
                pendingProjectOpen = pendingProjectLogo = null;
                renderAppliesSettings = false;
                renderAdoptsHistory = false;
            }
        }
        PollFinalExport();
    }


    static void DrawRenderProgress()
    {
        if (renderTask == null || activeProgress == null) return;
        double elapsed = Stopwatch.GetElapsedTime(renderStarted).TotalSeconds;
        if (elapsed < .3) return;

        var size = ImGui.GetWindowSize();
        float width = Math.Min(300, size.X - 32);
        if (width < 80 || size.Y < 60) return;
        var origin = ImGui.GetWindowPos() + new float2((size.X - width) / 2, size.Y - 52);
        var draw = ImGui.GetWindowDrawList();
        var state = activeProgress.Latest;
        draw.AddRectFilled(origin, origin + new float2(width, 34), 0xF0292521);
        draw.AddText(origin + new float2(8, 3), 0xFFF3F0E8, state.Label);
        float track = width - 16;
        var start = origin + new float2(8, 26);
        draw.AddRectFilled(start, start + new float2(track, 3), 0xFF595550);
        float left = 0, right = track * state.Fraction.GetValueOrDefault();
        if (state.Fraction == null)
        {
            left = (float)(.5 - .5 * Math.Cos(elapsed * 4)) * track * .75f;
            right = left + track * .25f;
        }
        draw.AddRectFilled(start + new float2(left, 0), start + new float2(right, 3), 0xFF6DCAFA);
    }

    internal static void Save()
    {
        if (busy || sheet == null || !PrintIsCurrent || ExportRefreshBlock != null)
            return;
        if (exportFolder == null)
        {
            exportSetupOpen = true;
            return;
        }
        BeginFinalExport(review: false);
    }


    static void Draw(VehicleEditor editor, IGameViewport viewport)
    {
        if (!enabled)
            return;
        PrintDiagnostics.State("editor", open ? "open" : "closed");
        EnsureDrawingTitle(editor);
        if (sourceEditor != null && (!ReferenceEquals(sourceEditor, editor) || sourceCraft != editor.EditingSpace.Id))
        {
            InvalidateRendering();
            sheet = null;
            preview?.Dispose();
            preview = null;
            sourceEditor = null;
            document = null;
            acceptedDocument = null;
            acceptedDefinition = null;
            ResetCraftRefresh();
            basePrint = null;
            CancelPlacement();
            ResetDetailEditor();
            drawingPlacement = new(100, []);
            livePreviewGate.Reset();
            printStyleDirty = false;
            status = "Craft changed. Generate a fresh preview.";
        }
        ObserveCraftChanges(editor);
        Poll();
        if (!open || setupStage != SetupStage.SelectViews) ReleaseSetupPaper();
        if (!open)
        {
            viewSizeDialog = false;
            CancelPlacement();
            placingText = false;
            textBoxStart = null;
            annotationKeysActive = false;
            spaceHeld = false;
            panning = false;
            return;
        }
        if (!workspaceFocus.Prepare(new float2(viewport.Size.X, viewport.Size.Y), NativeDialogOpen))
        {
            annotationKeysActive = false;
            spaceHeld = false;
            panning = false;
            return;
        }
        ImGui.SetNextWindowViewport(viewport.ImGuiId);
        // Full editor workspace below KSA's native menu bar. No floating or
        // movable root window; pointer gestures belong to the print canvas.
        ImGui.SetNextWindowPos(viewport.Position + new float2(0, 28), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new float2(viewport.Size.X, viewport.Size.Y - 28), ImGuiCond.Always);
        ConsoleStyle.PushWidgetStyle();
        ImGui.PushStyleColor(ImGuiCol.WindowBg, ConsoleStyle.Panel);
        ImGui.PushStyleColor(ImGuiCol.TitleBg, ConsoleStyle.PanelRaised);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, ConsoleStyle.PanelRaised);
        try
        {
            bool shown = ImGui.Begin("Paw Prints - Editor###PawPrintsExport"u8, ref open,
                ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);
            try
            {
                if (shown)
                {
                    if (sheet == null && setupStage == SetupStage.Editing)
                        setupStage = SetupStage.Choose;
                    if (setupStage != SetupStage.Editing)
                    {
                        DrawDrawingSetup(editor);
                        return;
                    }
                    if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
                        (choosingProjectionSource || ghostPress != null || groupDrag != null || dragView != null || dragText != null || dragSpec != null || dragKitten != null || dragHeight != null || dragLabel != null || dragDetail != null || dragTable >= 0))
                        CancelPlacement();
                    TryAutoRefresh(editor);
                    DrawWorkspaceHeader(editor);
                    ImGui.Separator();
                    annotationKeysActive = !ShowingTemplates && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput;
                    if (!annotationKeysActive)
                    {
                        spaceHeld = false;
                        panning = false;
                    }
                    float controlsWidth = Math.Min(390, ImGui.GetContentRegionAvail().X * .40f);
                    ImGui.BeginChild("Print controls"u8, new float2(controlsWidth, 0), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
                    try
                    {
                        DrawControls();
                    }
                    finally { ImGui.EndChild(); }
                    ImGui.SameLine();
                    ImGui.BeginGroup();
                    try
                    {
                        if (ShowingTemplates)
                            DrawTemplateToolbar(editor);
                        else
                            DrawAnnotationToolbar();
                        ImGui.BeginChild(ShowingTemplates ? "Template gallery" : "Print canvas", new float2(0, 0), ImGuiChildFlags.None,
                            ShowingTemplates ? ImGuiWindowFlags.NoMove : ImGuiWindowFlags.HorizontalScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoMove);
                        try
                        {
                            if (ShowingTemplates)
                                DrawTemplateGallery(editor);
                            else
                                DrawPreview();
                            DrawRenderProgress();
                        }
                        finally { ImGui.EndChild(); }
                    }
                    finally { ImGui.EndGroup(); }
                    DrawFlagnoteDialog();
                    DrawExplodedScopePopup();
                    DrawScopeDeleteConfirmation();
                    DrawMissingDetailConfirmation();
                    DrawPoseDialog();
                    DrawPrimaryDeletion();
                    DrawViewSizeDialog();
                    DrawSpecDialog();
                    DrawTextDialog();
                    DrawNewDrawingDialog(editor);
                    DrawOpenDrawing(editor);
                    DrawExportFolderDialog();
                    DrawFinalExport();
                    DrawActivityToast();
                    PollLivePreview();
                }
                else
                {
                    placingText = false;
                    textBoxStart = null;
                    annotationKeysActive = false;
                    spaceHeld = false;
                    panning = false;
                }
            }
            finally { ImGui.End(); }
        }
        finally { ImGui.PopStyleColor(3); ConsoleStyle.PopWidgetStyle(); }
    }


    [HarmonyPatch(typeof(KSA.Program), nameof(KSA.Program.OnKey))]
    static class ShortcutPatch
    {


        static bool Prefix(GlfwKey key, GlfwKeyAction action, GlfwModifier mods)
        {
            if (enabled && AnnotationKey(key, action, mods))
                return false;
            if (key == GlfwKey.P && action == GlfwKeyAction.Press && ((int)mods & 2) != 0)
                Console.WriteLine($"[Paw Prints] Shortcut input: {key} {action} mods={(int)mods}; enabled={enabled} editor={KSA.Program.Editor != null} console={KSA.Program.ConsoleWindow.IsOpen} settings={GameSettings.Show} popup={Popup.AnyOpen}");
            if (!enabled || !ExportShortcut.Matches(key == GlfwKey.P, action == GlfwKeyAction.Press, (int)mods,
                (open || CanOpenWorkspace), KSA.Program.ConsoleWindow.IsOpen || NativeDialogOpen || Popup.AnyOpen))
                return true;
            if (open)
                CloseExport();
            else
                OpenExport();
            Console.WriteLine("[Paw Prints] Export shortcut received; workspace " + (open ? "opened." : "closed."));
            return false; // Only the accepted press is consumed; other input remains native.
        }
    }


    [HarmonyPatch(typeof(KSA.Program), nameof(KSA.Program.DrawMenuBar))]
    static class MenuPatch
    {
        // Keep the native menu and its actions intact. Only intercept the value
        // overload used by its menu items, inserting our entry before Exit Editor.
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(ImGui), nameof(ImGui.MenuItem),
                [typeof(ImString), typeof(ImString), typeof(bool), typeof(bool)]);
            var replacement = AccessTools.Method(typeof(MenuPatch), nameof(DrawMenuItem));
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(original))
                    instruction.operand = replacement;
                yield return instruction;
            }
        }

        static bool DrawMenuItem(ImString label, ImString shortcut, bool selected, bool itemEnabled)
        {
            if (enabled && KSA.Program.Editor != null && label.ToString() == "Exit Editor")
            {
                MenuHookRan = true;
                ImGui.PushStyleColor(ImGuiCol.Text, ConsoleStyle.Pending);
                try
                {
                    if (ImGui.MenuItem("Open PawPrint Editor"u8, "Ctrl+Alt+P"u8, false, CanOpenWorkspace))
                        OpenExport();
                    if (!CanOpenWorkspace)
                        PrintUiStyle.Help("No vehicle loaded");
                }
                finally { ImGui.PopStyleColor(); }
                if (!menuReported)
                {
                    menuReported = true;
                    Console.WriteLine("[Paw Prints] Editor menu hook active; Ctrl+Alt+P opens PawPrint Editor.");
                }
            }
            return ImGui.MenuItem(label, shortcut, selected, itemEnabled);
        }
    }


    // These native panels may appear after capture has started, independently
    // of window focus. Do not submit their UI while the print owns the editor.
    // Keep VehicleEditor.OnDrawUi itself running for dialogs and lifecycle work.
    [HarmonyPatch(typeof(ResourcesPanel), nameof(ResourcesPanel.DrawEditor))]
    static class EditorResourcesPatch
    {
        static bool Prefix() => !enabled || !open;
    }

    [HarmonyPatch(typeof(VehicleEditingSpace), nameof(VehicleEditingSpace.DrawStageWindow))]
    static class EditorStagingPatch
    {
        static bool Prefix() => !enabled || !open;
    }

    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.OnDrawUi))]
    static class EditorToolPromptPatch
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Build 5949 paints the no-selection transform prompt into the foreground
            // draw list from OnDrawUi. Replace only that draw, keeping the gizmo state.
            var code = instructions.ToList();
            var addText = AccessTools.Method(typeof(ImDrawListExtensions), nameof(ImDrawListExtensions.AddText),
                [typeof(ImDrawListPtr), typeof(float2).MakeByRefType(), typeof(ImColor8), typeof(ImString)]);
            if (addText == null || !code.Any(i => i.operand is string text && text == "Select a part to "))
                return code;

            var draws = code.Where(i => i.Calls(addText)).ToArray();
            if (draws.Length == 1)
                draws[0].operand = AccessTools.Method(typeof(EditorToolPromptPatch), nameof(DrawPrompt));
            return code;
        }

        static void DrawPrompt(ImDrawListPtr draw, in float2 position, ImColor8 color, ImString text)
        {
            if (!enabled || !open)
                draw.AddText(in position, color, text);
        }
    }


    [HarmonyPatch(typeof(VehicleEditor), nameof(VehicleEditor.OnDrawUi))]
    static class EditorPatch
    {


        static void Postfix(VehicleEditor __instance, IGameViewport inViewport)
        {
            try
            {
                Draw(__instance, inViewport);
            }
            catch (Exception e) { PrintDiagnostics.Error("ui.draw", e); Console.WriteLine("[Paw Prints] UI disabled: " + e); enabled = false; cancellation?.Cancel(); }
        }
    }
}
