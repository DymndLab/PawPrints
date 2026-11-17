using Blueprint.Rendering;
using Brutal.ImGuiApi;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static readonly string[] DesktopNames = ["1920 x 1080 — Full HD", "2560 x 1440 — QHD", "3840 x 2160 — 4K", "3440 x 1440 — Ultrawide", "5120 x 1440 — Super ultrawide", "Custom pixels"];
    static readonly (int W, int H)[] DesktopSizes = [(1920, 1080), (2560, 1440), (3840, 2160), (3440, 1440), (5120, 1440)];


    static void DrawDesktopSize()
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Desktop size"u8, ref desktopPreset, DesktopNames, DesktopNames.Length) && desktopPreset < DesktopSizes.Length)
        {
            var size = DesktopSizes[desktopPreset];
            customWidth = customWidthEdit = size.W;
            customHeight = customHeightEdit = size.H;
        }
        if (desktopPreset == 5)
        {
            ImGui.SetNextItemWidth(160);
            ImGui.InputInt("Width px"u8, ref customWidthEdit);
            ImGui.SetNextItemWidth(160);
            ImGui.InputInt("Height px"u8, ref customHeightEdit);
            if (ImGui.Button("Apply pixel size"u8))
            {
                try
                {
                    PageSize.Custom(customWidthEdit, customHeightEdit);
                    customWidth = customWidthEdit;
                    customHeight = customHeightEdit;
                }
                catch (InvalidOperationException e) { ReportFailure("Invalid page size: " + e.Message); }
            }
        }
        ImGui.Text($"PNG: {customWidth} x {customHeight} pixels");
    }
}
