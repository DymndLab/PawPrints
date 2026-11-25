using Blueprint.Rendering;
using Blueprint.Storage;
using Brutal.Numerics;
using Brutal.ImGuiApi;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    enum SetupStage { Choose, Appearance, Editing, SelectViews, Templates }
    static SetupStage setupStage;
    static bool creatingBlank, captureForView, captureForDetail;
    static int? captureViewCamera;
    static readonly bool[] setupViews = new bool[9];
    static readonly (int Columns, int Rows)[] setupGrids = [(0, 0), (2, 1), (1, 2), (2, 2), (3, 2), (4, 2), (2, 3), (3, 3), (4, 3)];
    static readonly string[] setupGridNames = setupGrids.Select(g => g.Columns == 0 ? "Automatic" : $"{g.Columns} x {g.Rows}").ToArray();
    static int setupGrid;
    static string? setupGridWarning;
    static readonly (string? Message, string? Hint, string? PrimaryIssue)[] setupViewIssues = new (string?, string?, string?)[setupViews.Length];
    static int? setupPrimary, resolvedSetupPrimary;
    static int[] SelectedCameras() => Enumerable.Range(0, setupViews.Length).Where(i => setupViews[i]).ToArray();
    static readonly string[] setupViewNames = [.. DrawingPlacements.Names, "Exploded"];
    static bool setupParts, setupFlags, setupFitStarter;
    static LayoutTemplate? setupLayout;
    static LayoutTemplate? selectedSetupTemplate;
    static IReadOnlyList<(string Name, LayoutTemplate Template)> customTemplates = [];
    static readonly ImInputString templateName = new(128);
    static bool namingTemplate;
    static string? templateMessage;

    static LayoutTemplateStore TemplateStore() => new(ProfileStorage.FromResolvedProfile(KSA.Constants.DocumentsFolderPath));

    static void BeginTemplateSetup()
    {
        setupLayout = selectedSetupTemplate = null;
        customTemplates = [];
        templateMessage = null;
        try { customTemplates = TemplateStore().ReadAll(); }
        catch (Exception e) { templateMessage = "Could not read saved templates: " + e.Message; }
        setupStage = SetupStage.Templates;
    }

    static void ChooseLayout(LayoutTemplate? layout, bool fit, SetupStage back, bool includeParts = false, bool includeFlags = false)
    {
        setupLayout = layout;
        setupFitStarter = fit;
        partsTable = includeParts;
        flagTable = includeFlags;
        setupStage = SetupStage.Appearance;
    }

    static void DrawSaveTemplate()
    {
        if (document == null || sheet == null) return;
        if (ImGui.Button("Save as template...")) namingTemplate = !namingTemplate;
        if (!namingTemplate) return;
        ImGui.InputText("Template name"u8, templateName);
        if (ImGui.Button("Save template"))
        {
            try
            {
                var layout = DrawingDefinitions.ToTemplate(DrawingDefinitions.FromAccepted(sheet, document));
                TemplateStore().Save(templateName.ToString(), layout);
                templateMessage = "Template saved.";
                templateName.SetValue("".AsSpan());
            }
            catch (Exception e) { templateMessage = e.Message; }
        }
        if (templateMessage != null) PrintUiText.Wrapped(templateMessage);
    }

    static void DrawPrimaryMark(float2 center, uint ink = 0xFFE5CFB4, bool active = false)
    {
        var draw = ImGui.GetWindowDrawList();
        var glyph = ImGui.CalcTextSize("P"u8);
        float radius = Math.Max(7, glyph.Y * .52f);
        if (active) draw.AddCircleFilled(center, radius, ink, 24);
        else draw.AddCircle(center, radius, ink, 24, 1.4f);
        draw.AddText(center - glyph * .5f, active ? 0xFF202923u : ink, "P");
    }

    static bool TemplateThumbnail(string name, LayoutTemplate layout, string id, float width, bool selected)
    {
        var size = new float2(width, Math.Max(ImGui.GetFontSize() * 5.5f, Math.Clamp(width * .375f, 105, 145)));
        var start = ImGui.GetCursorScreenPos();
        bool click = ImGui.InvisibleButton(id, size);
        bool hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRect(start, start + size, selected ? 0xFF54D0E8u : hovered ? 0xFFE5CFB4u : 0xFF89735Fu);
        draw.PushClipRect(start + new float2(5, 0), start + size - new float2(5, 0), true);
        draw.AddText(start + new float2(8, 6), 0xFFE5CFB4, name);
        var primaryIds = StarterDrawings.PrimaryViewIds(layout.Layout);
        float top = ImGui.GetFontSize() + 10;
        float body = size.Y - top - 12;
        foreach (var view in layout.Layout.Views)
        {
            var b = view.Bounds;
            if (b.Center.X < 0 || b.Center.Y < 0 || b.Center.X > 1 || b.Center.Y > 1) continue;
            var center = start + new float2(b.Center.X * width, top + b.Center.Y * body);
            if (view.Exploded)
                draw.AddText(center - new float2(14, 6), 0xFFE5CFB4, "EXP");
            else
                DrawTemplateView(center, Math.Clamp(Math.Min(b.Width * width, b.Height * body) * .36f, 6, 35), view.Camera >= 5 ? view.Camera - 4 : view.Camera);
            if (primaryIds.Contains(view.Id))
            {
                var badge = center + new float2(17, -8);
                DrawPrimaryMark(new(Math.Clamp(badge.X, start.X + 11, start.X + width - 11),
                    Math.Clamp(badge.Y, start.Y + top, start.Y + size.Y - 11)));
            }
        }
        draw.PopClipRect();
        if (hovered && ImGui.CalcTextSize(name).X + 16 > width) PrintUiText.Tooltip(name);
        return click;
    }

    static void StartDrawing(VehicleEditor editor)
    {
        if (busy) return;
        var draft = newDrawing;
        PrintOptions? settings = draft == null ? null : draft.Options with {
            PartsTable = setupStage == SetupStage.SelectViews && setupParts, FlagTable = setupStage == SetupStage.SelectViews && setupFlags };
        if (setupLayout is { } selected &&
            (selected.Layout.Views.Length != 0 || (settings?.PartsTable ?? partsTable) || (settings?.FlagTable ?? flagTable)))
        {
            GenerateDrawing(editor, startFresh: true, selected, setupFitStarter, settings);
            creatingBlank = busy;
            return;
        }

        if (draft == null) EnsureDrawingTitle(editor);
        var options = settings ?? Options with { WearSeed = WearPlacement.NewSeed() };
        string title = editor.EditingSpace.Id;
        sourceEditor = editor;
        sourceCraft = title;
        observedCraft = ObserveCraft(editor);
        renderKeepsDocument = false;
        renderAdoptsHistory = true;
        renderAppliesSettings = true;
        creatingBlank = true;
        progress = "Preparing drawing";
        StartRender(token => BlankDrawing.Create(title, options, token));
    }

    static SetupViewGrid setupWorkingGrid = SetupViewGrid.Empty(3);
    static SetupViewGrid? setupUnlink;
    static int? setupPicked;
    static bool setupPointerDown, setupDragging;
    static string? setupMoveHint;
    static SetupCraftSketch? setupSketch;
    static CraftRevision? setupSketchRevision;
    static void ClearSetupGesture()
    {
        setupPicked = null;
        setupPointerDown = setupDragging = false;
        setupUnlink = null;
        setupMoveHint = null;
    }

    static void RefreshSetupViewIssues(int gridChoice, int[] cameras, int? primary)
    {
        var (columns, rows) = setupGrids[gridChoice];
        int angle = setupWorkingGrid.Angle;
        resolvedSetupPrimary = setupWorkingGrid.Primary;
        var linked = setupWorkingGrid.Slots.Where(s => !s.Detached).Select(s => s.Camera).ToArray();
        for (int i = 0; i < setupViewIssues.Length; i++)
        {
            string? issue = StarterDrawings.ViewAdditionIssue(cameras, i, angle, primary, resolvedSetupPrimary, columns, rows, setupWorkingGrid);
            string? axis = issue == StarterDrawings.GridFullMessage
                ? StarterDrawings.ProjectionAxisHint(linked, i, angle, resolvedSetupPrimary, columns, rows) : null;
            string? promotion = StarterDrawings.PrimaryChoiceIssue(cameras, i, angle, columns, rows, setupWorkingGrid);
            setupViewIssues[i] = (issue, axis == null ? issue : $"{issue} {axis}", promotion);
        }
    }

    static bool TrySetupLayout(int gridChoice, int? primary, int angle)
    {
        var cameras = SelectedCameras();
        var (columns, rows) = setupGrids[gridChoice];
        var proposal = setupWorkingGrid.Arrange(cameras, columns, rows, angle, primary);
        if (proposal == null)
        {
            setupGridWarning = columns > 0 && cameras.Length > columns * rows ? StarterDrawings.GridFullMessage :
                "These linked views need a different grid shape.";
            return false;
        }
        setupWorkingGrid = proposal;
        setupGrid = gridChoice;
        setupPrimary = primary;
        ClearSetupGesture();
        RefreshSetupViewIssues(gridChoice, cameras, primary);
        return true;
    }

    static void DrawSetupPrimaryChoice(int camera, float width)
    {
        string? issue = setupViewIssues[camera].PrimaryIssue;
        bool active = resolvedSetupPrimary == camera;
        bool pinned = setupPrimary == camera;
        bool included = setupViews[camera];
        var size = new float2(width, Math.Max(25f, ImGui.GetFrameHeight()));
        var center = ImGui.GetCursorScreenPos() + size * .5f;
        ImGui.PushStyleColor(ImGuiCol.Button, 0u);
        bool clicked = ImGui.Button("##primary-projection-" + camera, size);
        ImGui.PopStyleColor();
        uint ink = issue != null ? 0xFF777875u : active ? 0xFFA6E9DDu : 0xFFE5CFB4u;
        DrawPrimaryMark(center, ink, active);
        // Mouse focus can outlive a click. Only the control under the pointer owns the tooltip.
        if (ImGui.IsItemHovered())
        {
            string name = setupViewNames[camera];
            PrintUiText.Tooltip(issue ?? (pinned ? "Use automatic primary selection." :
                active ? $"Automatically selected. Keep {name} as primary." :
                included ? $"Make {name} primary." : $"Add {name} and make it primary."));
        }
        if (!clicked) return;
        if (issue != null) { setupGridWarning = issue; return; }
        int? preferred = pinned ? null : camera;
        setupViews[camera] = true;
        if (!TrySetupLayout(setupGrid, preferred, setupWorkingGrid.Angle)) setupViews[camera] = included;
    }

    static void DrawSetupGridWarning()
    {
        if (setupGridWarning == null) return;
        if (!ImGui.IsPopupOpen("Layout grid"u8)) ImGui.OpenPopup("Layout grid"u8);
        CenterDrawingDialog();
        if (!ImGui.BeginPopupModal("Layout grid"u8, ImGuiWindowFlags.AlwaysAutoResize)) return;
        try
        {
            ImGui.Text(setupGridWarning);
            if (ImGui.Button("OK"u8) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                setupGridWarning = null;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SetItemDefaultFocus();
        }
        finally { ImGui.EndPopup(); }
    }

    static void DrawSetupSelection()
    {
        float primaryColumn = ImGui.GetCursorPosX() + 38 + ImGui.GetFrameHeight() + ImGui.CalcTextSize("Three-quarter"u8).X + 3 * ImGui.GetStyle().ItemSpacing.X;
        float columnWidth = ImGui.CalcTextSize("Primary"u8).X;
        ImGui.Text("View");
        ImGui.SameLine(primaryColumn);
        ImGui.Text("Primary");
        for (int i = 0; i < setupViews.Length; i++)
        {
            var (issue, hint, _) = setupViewIssues[i];
            uint icon = issue == null ? 0xFFE5CFB4u : 0x808E8985u;
            var start = ImGui.GetCursorScreenPos();
            ImGui.Dummy(new float2(38, 25));
            if (i == 8)
                ImGui.GetWindowDrawList().AddText(start + new float2(2, 5), icon, "EXP");
            else
                DrawTemplateView(start + new float2(19, 13), 11, i >= 5 ? i - 4 : i, icon);
            bool iconHovered = ImGui.IsItemHovered();
            ImGui.SameLine();
            bool chosen = setupViews[i];
            if (issue != null) ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * ImGui.GetStyle().DisabledAlpha);
            bool clicked = ImGui.Checkbox(setupViewNames[i], ref chosen);
            if (issue != null)
            {
                ImGui.PopStyleVar();
                if (iconHovered || ImGui.IsItemHovered()) PrintUiText.Tooltip(hint ?? issue);
                if (clicked) setupGridWarning = issue;
            }
            else if (clicked)
            {
                setupViews[i] = chosen;
                int? primary = !chosen && setupPrimary == i ? null : setupPrimary;
                if (!TrySetupLayout(setupGrid, primary, setupWorkingGrid.Angle)) setupViews[i] = !chosen;
            }
            ImGui.SameLine(primaryColumn);
            DrawSetupPrimaryChoice(i, columnWidth);
        }
        ImGui.Separator();
        ImGui.Checkbox("Parts table", ref setupParts);
        ImGui.Checkbox("Flagnote table", ref setupFlags);
    }

    static bool DrawDrawingSetupHeader()
    {
        const string openLabel = "Open Drawing";
        string returnLabel = PrintUiStyle.ReturnToVehicleLabel;
        bool entry = setupStage == SetupStage.Choose;
        ImGui.AlignTextToFramePadding();
        ImGui.Text("New Drawing");
        if (entry)
        {
            var style = ImGui.GetStyle();
            float actionWidth = ImGui.CalcTextSize(openLabel).X + ImGui.CalcTextSize(returnLabel).X +
                style.FramePadding.X * 4 + style.ItemSpacing.X;
            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X < actionWidth) ImGui.NewLine();
            ImGui.BeginDisabled(busy);
            if (ImGui.Button(openLabel)) RequestOpenDrawing();
            ImGui.EndDisabled();
        }
        ContinueSetupRow(returnLabel);
        if (ImGui.Button(returnLabel))
        {
            CloseExport();
            return true;
        }
        ImGui.Separator();
        return false;
    }

    static void DrawDrawingSetup(VehicleEditor editor)
    {
        annotationKeysActive = false;
        if (newDrawing == null || !ReferenceEquals(newDrawing.Editor, editor) || newDrawing.Craft != editor.EditingSpace.Id)
        {
            setupStage = SetupStage.Choose;
            BeginDrawingDraft(editor);
        }
        if (DrawDrawingSetupHeader()) return;
        ImGui.BeginDisabled(busy);
        if (setupStage == SetupStage.Choose)
            DrawNewDrawingChoices(editor);
        else if (setupStage == SetupStage.SelectViews)
            DrawSelectViews(editor);
        else
            DrawNewDrawingTemplates(editor, newDrawing!);

        if (sheet != null && ImGui.Button("Return to drawing"))
        {
            AdoptPrintSettings(sheet.Options);
            DiscardDrawingDraft();
            setupStage = SetupStage.Editing;
        }
        ImGui.EndDisabled();
        DrawRenderProgress();
        if (renderTask != null && ImGui.Button("Cancel render")) cancellation?.Cancel();
        DrawOpenDrawing(editor);
        DrawActivityToast();
    }

    static void DrawNewDrawingTemplates(VehicleEditor editor, NewDrawingDraft draft)
    {
        if (ImGui.Button("Back"))
        {
            setupStage = SetupStage.Choose;
            return;
        }
        int angle = setupLayout == null ? draft.Angle : StarterDrawings.ProjectionConvention(setupLayout.Layout.Views) ?? draft.Angle;
        if (setupLayout != null)
        {
            ImGui.SameLine();
            if (PrintUiStyle.PrimaryButton("Start editing")) StartDrawing(editor);
            if (angle != draft.Angle)
                PrintUiText.Wrapped($"The selected template uses {(angle == 3 ? "third-angle" : "first-angle")} projection.");
        }
        ImGui.BeginChild("Template choices", new float2(0, Math.Max(140, ImGui.GetContentRegionAvail().Y - 64)));
        try
        {
            float available = ImGui.GetContentRegionAvail().X;
            float gap = ImGui.GetStyle().ItemSpacing.X;
            float minimum = Math.Max(220, ImGui.GetFontSize() * 12);
            int columns = Math.Clamp((int)((available + gap) / (minimum + gap)), 1, 4);
            float width = Math.Clamp(MathF.Floor((available - gap * (columns - 1)) / columns), 1, 380);

            ImGui.Text("Starters");
            for (int i = 0; i < StarterDrawings.All.Length; i++)
            {
                var entry = StarterDrawings.All[i];
                var starter = StarterDrawings.ForAngle(entry.Template, draft.Angle);
                if (i % columns != 0) ImGui.SameLine();
                if (TemplateThumbnail(entry.Name, starter, "##starter" + i, width, ReferenceEquals(selectedSetupTemplate, entry.Template)))
                    SelectNewDrawingTemplate(entry.Template, starter, true);
            }
            ImGui.Spacing();
            ImGui.Text("My templates");
            if (templateMessage != null) PrintUiText.Wrapped(templateMessage);
            if (customTemplates.Count == 0) ImGui.Text("No saved templates yet.");
            for (int i = 0; i < customTemplates.Count; i++)
            {
                var entry = customTemplates[i];
                if (i % columns != 0) ImGui.SameLine();
                if (TemplateThumbnail(entry.Name, entry.Template, "##custom" + i, width, ReferenceEquals(selectedSetupTemplate, entry.Template)))
                    SelectNewDrawingTemplate(entry.Template, entry.Template, false);
            }
        }
        finally { ImGui.EndChild(); }
    }

    static void SelectNewDrawingTemplate(LayoutTemplate identity, LayoutTemplate layout, bool fit)
    {
        selectedSetupTemplate = identity;
        setupLayout = layout;
        setupFitStarter = fit;
    }
}
