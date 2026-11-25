using Blueprint.Rendering;
using Brutal.ImGuiApi;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static int outputTarget = 1;
    static readonly string[] OutputNames = ["Desktop Background", "Image"];
    static readonly string[] DesktopNames = ["1920 x 1080 — Full HD", "2560 x 1440 — QHD", "3840 x 2160 — 4K", "3440 x 1440 — Ultrawide", "5120 x 1440 — Super ultrawide", "Custom pixels"];
    static readonly (int W, int H)[] DesktopSizes = [(1920, 1080), (2560, 1440), (3840, 2160), (3440, 1440), (5120, 1440)];
    static PageSize lastCustomDesktop = new(2560, 1440);
    static PageSize lastImageSize = ImagePresets.Default;

    static PageSize CurrentOutputSize => pageOrientation == 3
        ? new(customWidth, customHeight) : PageSize.From(pageOrientation, pageAspect);

    static void SetOutputSize(PageSize size)
    {
        pageOrientation = 3;
        customWidth = customWidthEdit = size.Width;
        customHeight = customHeightEdit = size.Height;
    }

    static void ChangeOutputTarget(int target)
    {
        if (target == outputTarget) return;
        if (outputTarget == 1) lastImageSize = CurrentOutputSize;
        outputTarget = target;
        if (outputTarget == 0) ApplyDesktopPreset();
        else SetOutputSize(lastImageSize);
    }

    sealed class OutputSelection(DrawingOutput output, PageSize size)
    {
        public DrawingOutput Output = output;
        public PageSize Size = size;
        public PageSize Image = output == DrawingOutput.Image ? size : ImagePresets.Default;
        public PageSize Desktop = output == DrawingOutput.DesktopBackground ? size : new(2560, 1440);
        public int Preset = output == DrawingOutput.DesktopBackground ? DesktopIndex(size) : 1;
        public int WidthEdit = size.Width, HeightEdit = size.Height;
    }

    static int DesktopIndex(PageSize size)
    {
        int i = Array.IndexOf(DesktopSizes, (size.Width, size.Height));
        return i >= 0 ? i : DesktopSizes.Length;
    }

    static void DrawOutputControls()
    {
        var before = CurrentOutputSize;
        var output = outputTarget == 0 ? DrawingOutput.DesktopBackground : DrawingOutput.Image;
        var choice = new OutputSelection(output, before)
        {
            Preset = desktopPreset,
            WidthEdit = customWidthEdit,
            HeightEdit = customHeightEdit,
            Desktop = lastCustomDesktop,
            Image = lastImageSize
        };
        DrawOutputChoices(choice);
        outputTarget = choice.Output == DrawingOutput.DesktopBackground ? 0 : 1;
        desktopPreset = choice.Preset;
        lastImageSize = choice.Image;
        lastCustomDesktop = choice.Desktop;
        if (choice.Size != before || choice.Output != output) SetOutputSize(choice.Size);
        customWidthEdit = choice.WidthEdit;
        customHeightEdit = choice.HeightEdit;
    }

    static void DrawOutputChoices(OutputSelection choice, bool stackedLabels = false)
    {
        int target = choice.Output == DrawingOutput.DesktopBackground ? 0 : 1;
        if (ImGui.Combo(SetupLabel("Output", stackedLabels), ref target, OutputNames, OutputNames.Length))
        {
            if (choice.Output == DrawingOutput.Image) choice.Image = choice.Size;
            choice.Output = target == 0 ? DrawingOutput.DesktopBackground : DrawingOutput.Image;
            choice.Size = target == 0 ? DesktopSize(choice) : choice.Image;
            choice.WidthEdit = choice.Size.Width;
            choice.HeightEdit = choice.Size.Height;
        }

        if (choice.Output == DrawingOutput.DesktopBackground)
        {
            if (ImGui.Combo(SetupLabel("Size", stackedLabels), ref choice.Preset, DesktopNames, DesktopNames.Length))
            {
                choice.Size = DesktopSize(choice);
                choice.WidthEdit = choice.Size.Width;
                choice.HeightEdit = choice.Size.Height;
            }
            if (choice.Preset == DesktopSizes.Length)
            {
                string width = SetupLabel("Width px", stackedLabels);
                if (!stackedLabels) ImGui.SetNextItemWidth(160);
                ImGui.InputInt(width, ref choice.WidthEdit);
                string height = SetupLabel("Height px", stackedLabels);
                if (!stackedLabels) ImGui.SetNextItemWidth(160);
                ImGui.InputInt(height, ref choice.HeightEdit);
                if (ImGui.Button("Apply pixel size"u8))
                {
                    try { choice.Size = choice.Desktop = PageSize.Custom(choice.WidthEdit, choice.HeightEdit); }
                    catch (InvalidOperationException error) { ReportFailure("Invalid page size: " + error.Message); }
                }
            }
            ImGui.Text($"PNG: {choice.Size.Width} x {choice.Size.Height} pixels");
            return;
        }

        int orientation = ImagePresets.OrientationOf(choice.Size);
        int preset = ImagePresets.IndexOf(orientation, choice.Size);
        if (ImGui.Combo(SetupLabel("Orientation", stackedLabels), ref orientation, ImagePresets.Orientations, ImagePresets.Orientations.Length))
        {
            preset = preset < 0 ? 1 : preset;
            choice.Size = ImagePresets.Size(orientation, preset);
        }
        var sizes = ImagePresets.Labels(orientation);
        int presetCount = sizes.Length;
        int selected = preset >= 0 ? preset : presetCount;
        if (preset < 0) sizes = [.. sizes, $"{choice.Size.Width} x {choice.Size.Height}"];
        if (ImGui.Combo(SetupLabel("Size", stackedLabels), ref selected, sizes, sizes.Length) && selected < presetCount)
            choice.Size = ImagePresets.Size(orientation, selected);
    }

    static PageSize DesktopSize(OutputSelection choice) => choice.Preset < DesktopSizes.Length
        ? new(DesktopSizes[choice.Preset].W, DesktopSizes[choice.Preset].H) : choice.Desktop;

    static void ApplyDesktopPreset()
    {
        var size = desktopPreset < DesktopSizes.Length
            ? new PageSize(DesktopSizes[desktopPreset].W, DesktopSizes[desktopPreset].H) : lastCustomDesktop;
        SetOutputSize(size);
    }
}
