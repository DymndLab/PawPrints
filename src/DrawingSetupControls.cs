using Blueprint.Rendering;
using Brutal.ImGuiApi;
using KSA;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    enum SetupStage { Choose, Appearance, Editing }
    static SetupStage setupStage;
    static bool creatingBlank, captureForView;
    static int? captureViewCamera;
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
            if (PrintUiStyle.PrimaryButton("Blank drawing")) setupStage = SetupStage.Appearance;
            ImGui.BeginDisabled();
            ImGui.Button("Select Views");
            ImGui.Button("Build from template");
            ImGui.EndDisabled();
            PrintUiText.Wrapped("Select Views and templates are not available in this development build.");
        }
        else
        {
            ImGui.Text("Appearance");
            ImGui.Combo("Colors", ref theme, Themes, Themes.Length);
            ImGui.Checkbox("Graph paper", ref grid);
            ImGui.Checkbox("Zoned borders", ref zones);
            ImGui.Combo("Archive wear", ref archivalWear, WearNames, WearNames.Length);
            if (ImGui.Button("Back")) setupStage = SetupStage.Choose;
            ImGui.SameLine();
            if (PrintUiStyle.PrimaryButton("Start editing"))
            {
                EnsureDrawingTitle(editor);
                var options = Options;
                string title = editor.EditingSpace.Id;
                sourceEditor = editor; sourceCraft = title;
                renderKeepsDocument = false; renderAdoptsHistory = true;
                renderAppliesSettings = true; creatingBlank = true;
                StartRender(token => BlankDrawing.Create(title, options, token));
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
