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
    static bool creatingBlank, captureForView;
    static int? captureViewCamera;
    static readonly bool[] setupViews = new bool[9];
    static readonly string[] setupViewNames = ["Three-quarter", "Front", "Right side", "Top", "Isometric", "Rear", "Left side", "Bottom", "Exploded"];
    static bool setupParts, setupFlags, setupFitStarter;
    static LayoutTemplate? setupLayout;
    static SetupStage appearanceBack;
    static IReadOnlyList<(string Name, LayoutTemplate Template)> customTemplates = [];
    static readonly ImInputString templateName = new(128);
    static bool namingTemplate;
    static string? templateMessage;

    static LayoutTemplateStore TemplateStore() => new(ProfileStorage.FromResolvedProfile(KSA.Constants.DocumentsFolderPath));

    static void BeginTemplateSetup()
    {
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
        appearanceBack = back;
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

    static bool TemplateThumbnail(string name, LayoutTemplate layout, string id)
    {
        float width = Math.Clamp(ImGui.GetContentRegionAvail().X, 160, 280);
        var size = new float2(width, 105);
        var start = ImGui.GetCursorScreenPos();
        bool click = ImGui.InvisibleButton(id, size);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRect(start, start + size, ImGui.IsItemHovered() ? 0xFFE5CFB4u : 0xFF89735Fu);
        draw.AddText(start + new float2(8, 6), 0xFFE5CFB4, name);
        foreach (var view in layout.Layout.Views)
        {
            var b = view.Bounds;
            if (b.Center.X < 0 || b.Center.Y < 0 || b.Center.X > 1 || b.Center.Y > 1) continue;
            var center = start + new float2(b.Center.X * width, 28 + b.Center.Y * 64);
            if (view.Exploded)
                draw.AddText(center - new float2(14, 6), 0xFFE5CFB4, "EXP");
            else
                DrawTemplateView(center, Math.Clamp(Math.Min(b.Width * width, b.Height * 68) * .36f, 6, 35), view.Camera >= 5 ? view.Camera - 4 : view.Camera);
        }
        return click;
    }
    static int outputTarget = 1, imageOrientation, imageSize = 2;
    static readonly string[] ImageSizes = ["1280 x 720", "1920 x 1080", "3840 x 2160 (4K)"];

    static void DrawDrawingSetup(VehicleEditor editor)
    {
        annotationKeysActive = false;
        ImGui.Text("New drawing");
        ImGui.BeginDisabled(busy);
        if (setupStage == SetupStage.Choose)
        {
            ImGui.Combo("Output", ref outputTarget, new[] { "Desktop Background", "Image" }, 2);
            pageOrientation = 3;
            if (outputTarget == 0) DrawDesktopSize();
            else
            {
                ImGui.Combo("Orientation", ref imageOrientation, new[] { "Landscape", "Portrait", "Square" }, 3);
                ImGui.Combo("Size", ref imageSize, ImageSizes, ImageSizes.Length);
                var size = DesktopSizes[imageSize == 0 ? 0 : imageSize == 1 ? 0 : 2];
                if (imageSize == 0) size = (1280, 720);
                customWidth = customWidthEdit = imageOrientation == 1 ? size.H : size.W;
                customHeight = customHeightEdit = imageOrientation == 0 ? size.H : size.W;
                PrintUiText.Wrapped($"PNG: {customWidth} x {customHeight}. Smaller images are easier to share in chat and forums; file size depends on the drawing.");
            }
            if (PrintUiStyle.PrimaryButton("Blank drawing"))
                ChooseLayout(null, false, SetupStage.Choose);
            if (ImGui.Button("Select Views"))
            {
                Array.Clear(setupViews);
                setupParts = setupFlags = false;
                setupStage = SetupStage.SelectViews;
            }
            if (ImGui.Button("Build from template")) BeginTemplateSetup();
        }
        else if (setupStage == SetupStage.SelectViews)
        {
            for (int i = 0; i < setupViews.Length; i++)
            {
                var start = ImGui.GetCursorScreenPos();
                ImGui.Dummy(new float2(38, 25));
                if (i == 8)
                    ImGui.GetWindowDrawList().AddText(start + new float2(2, 5), 0xFFE5CFB4, "EXP");
                else
                    DrawTemplateView(start + new float2(19, 13), 11, i >= 5 ? i - 4 : i);
                ImGui.SameLine();
                ImGui.Checkbox(setupViewNames[i], ref setupViews[i]);
            }
            ImGui.Separator();
            ImGui.Checkbox("Parts table", ref setupParts);
            ImGui.Checkbox("Flagnote table", ref setupFlags);
            if (ImGui.Button("Back")) setupStage = SetupStage.Choose;
            ImGui.SameLine();
            if (PrintUiStyle.PrimaryButton("Continue to appearance"))
                ChooseLayout(StarterDrawings.FromSelection(Enumerable.Range(0, setupViews.Length).Where(i => setupViews[i])),
                    true, SetupStage.SelectViews, setupParts, setupFlags);
        }
        else if (setupStage == SetupStage.Templates)
        {
            if (ImGui.Button("Back")) setupStage = SetupStage.Choose;
            ImGui.BeginChild("Template choices", new float2(0, Math.Max(140, ImGui.GetContentRegionAvail().Y - 78)));
            try
            {
                ImGui.Text("Starters");
                for (int i = 0; i < StarterDrawings.All.Length; i++)
                {
                    var entry = StarterDrawings.All[i];
                    if (TemplateThumbnail(entry.Name, entry.Template, "##starter" + i))
                        ChooseLayout(entry.Template, true, SetupStage.Templates);
                }
                ImGui.Text("My templates");
                if (templateMessage != null) PrintUiText.Wrapped(templateMessage);
                if (customTemplates.Count == 0) ImGui.Text("No saved templates yet.");
                for (int i = 0; i < customTemplates.Count; i++)
                {
                    var entry = customTemplates[i];
                    if (TemplateThumbnail(entry.Name, entry.Template, "##custom" + i))
                        ChooseLayout(entry.Template, false, SetupStage.Templates);
                }
            }
            finally { ImGui.EndChild(); }
        }
        else
        {
            ImGui.Text("Appearance");
            ImGui.Combo("Colors", ref theme, Themes, Themes.Length);
            ImGui.Checkbox("Graph paper", ref grid);
            ImGui.Checkbox("Zoned borders", ref zones);
            ImGui.Combo("Archive wear", ref archivalWear, WearNames, WearNames.Length);
            ImGui.Checkbox("Show title block", ref titleBlockVisible);
            if (setupLayout is { } layout)
                TemplateThumbnail("Layout preview", layout, "##setup-layout-preview");
            if (ImGui.Button("Back")) setupStage = appearanceBack;
            ImGui.SameLine();
            if (PrintUiStyle.PrimaryButton("Start editing"))
            {
                if (setupLayout is { } selected && (selected.Layout.Views.Length != 0 || partsTable || flagTable))
                {
                    Generate(editor, startFresh: true, starter: selected, fitStarter: setupFitStarter);
                    creatingBlank = busy;
                }
                else
                {
                    EnsureDrawingTitle(editor);
                    var options = Options;
                    string title = editor.EditingSpace.Id;
                    sourceEditor = editor;
                    sourceCraft = title;
                    renderKeepsDocument = false;
                    renderAdoptsHistory = true;
                    renderAppliesSettings = true;
                    creatingBlank = true;
                    StartRender(token => BlankDrawing.Create(title, options, token));
                }
            }
        }
        if (sheet != null && ImGui.Button("Return to drawing"))
        {
            AdoptPrintSettings(sheet.Options);
            setupStage = SetupStage.Editing;
        }
        ImGui.EndDisabled();
        if (busy) ImGui.Text("Preparing drawing...");
        if (ImGui.Button("Back to editor")) CloseExport();
        DrawWarningDialog();
    }
}
