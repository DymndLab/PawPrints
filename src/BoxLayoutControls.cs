using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static readonly string[] BoxViewNames = ["Blank", "Front", "Side", "Top", "Isometric", "Three-quarter"];
    static readonly int[] BoxViewValues = [-1, 1, 2, 3, 4, 0];


    static void DrawBoxLayoutControls()
    {
        if (ImGui.Button("Choose a template"u8))
            OpenTemplates();
        PrintUiText.Wrapped(PrintTemplates.Name(boxLayout));
        if (!ImGui.CollapsingHeader("Customize layout"u8))
            return;
        float height = boxLayout.Template is 5 or 11 ? 246 : boxLayout.Template is 1 or 2 or 4 ? 80 : 164;
        ImGui.BeginChild("View boxes"u8, new float2(0, height), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        try
        {
            var origin = ImGui.GetCursorScreenPos();
            float width = Math.Max(100, ImGui.GetContentRegionAvail().X - 2);
            var cells = FixedViewLayouts.Cells(boxLayout.Template, new(0, 0, width, height - 2), 8);
            for (int i = 0; i < cells.Length; i++)
            {
                var box = cells[i];
                var start = origin + new float2(box.X, box.Y);
                ImGui.GetWindowDrawList().AddRect(start, start + new float2(box.Width, box.Height), 0xFF70553B);
                ImGui.SetCursorScreenPos(start + new float2(5, 5));
                ImGui.Text("Box " + (i + 1));
                ImGui.SetCursorScreenPos(start + new float2(5, 31));
                ImGui.SetNextItemWidth(box.Width - 10);
                int selected = Array.IndexOf(BoxViewValues, boxLayout.Assignments[i]);
                if (ImGui.Combo("##Box camera " + i, ref selected, BoxViewNames, BoxViewNames.Length))
                    boxLayout = boxLayout.Set(i, BoxViewValues[selected]);
                if (ImGui.IsItemHovered())
                    PrintUiText.Tooltip("Box " + (i + 1) + ": " + BoxViewNames[selected]);
            }
        }
        finally { ImGui.EndChild(); }
        if (FixedViewLayouts.CanSplit(boxLayout.Template))
        {
            bool automatic = boxLayout.AutoRows;
            if (ImGui.Checkbox("Fit views automatically", ref automatic))
                boxLayout = boxLayout with
                {
                    AutoRows = automatic
                };
            ImGui.BeginDisabled(automatic);
            int topPercent = (int)Math.Round(boxLayout.TopShare * 100);
            ImGui.Text("Row heights");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderInt("##Top row height"u8, ref topPercent, 10, 90, "Top row %d%%"u8))
                boxLayout = boxLayout with
                {
                    TopShare = topPercent / 100f
                };
            ImGui.EndDisabled();
        }
        if (ImGui.Button("Refit blank space"u8))
        {
            boxLayout = boxLayout.ClearBlankFits() with
            {
                AutoRows = true
            };
            drawingSize = 100;
            status = "Blank space released for fitting. Generate PawPrint applies this layout and keeps composition edits.";
        }
        ImGui.Checkbox("View captions", ref captions);
        PrintUiText.Wrapped("Views fit automatically at one shared scale. Your box assignments stay fixed. Blanking preserves the previous fit; Refit blank space releases that allowance. Generate PawPrint applies changes.");
    }
}
