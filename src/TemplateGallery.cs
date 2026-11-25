using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool templatesOpen;
    static FixedViewLayout? beforeTemplates;
    static DrawingPlacement? beforeDrawing;
    static bool ShowingTemplates => templatesOpen;

    static void OpenTemplates()
    {
        if (ControlsLocked || ShowingTemplates)
            return;
        BeginTemplateSetup();
    }

    static void DrawTemplateToolbar(VehicleEditor editor)
    {
        ImGui.Text("Choose a print template");
        if (sheet != null)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(ControlsLocked);
            if (ImGui.Button("Back to print"u8))
            {
                if (beforeTemplates != null)
                    boxLayout = beforeTemplates;
                beforeTemplates = null;
                drawingPlacement = beforeDrawing;
                beforeDrawing = null;
                templatesOpen = false;
            }
            ImGui.EndDisabled();
        }
        if (GenerationBlock(editor) is { } reason)
            PrintUiStyle.Warning(reason);
        else if (sheet != null && (document?.Labels.Length > 0 || document?.Details.Length > 0))
            PrintUiText.Wrapped("Generate keeps your notes and detail frames. Referenced views must remain available.");
        else
            PrintUiText.Wrapped("Double-click a template to generate. Single-click to select. Blank spaces can hold details.");
    }

    static void DrawTemplateGallery(VehicleEditor editor)
    {
        canvasHovered = false;
        hoveredLabel = -1;
        PrintUiText.Wrapped("Layout diagrams - your craft will be fitted at one shared scale. Row heights adjust to its shape.");
        var options = Options;
        var origin = ImGui.GetCursorScreenPos();
        float available = ImGui.GetContentRegionAvail().X;
        const float gap = 14, height = 286;
        int columns = Math.Clamp((int)((available + gap) / 254), 1, 6);
        float width = (available - gap * (columns - 1)) / columns;
        ImGui.BeginDisabled(ControlsLocked);
        for (int i = 0; i < PrintTemplates.All.Length; i++)
        {
            var item = PrintTemplates.All[i];
            var start = origin + new float2(i % columns * (width + gap), i / columns * (height + gap));
            ImGui.SetCursorScreenPos(start);
            var size = new float2(width, height);
            if (ImGui.InvisibleButton("##PrintTemplate" + i, in size))
                boxLayout = item.Layout;
            if (!ControlsLocked && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                boxLayout = item.Layout;
                TryGenerate(editor);
            }
            bool selected = PrintTemplates.Matches(item.Layout, boxLayout), hovered = ImGui.IsItemHovered();
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(start, start + size, selected ? 0xFF493321u : hovered ? 0xFF302820u : 0xFF201C18u);
            draw.AddRect(start, start + size, selected ? 0xFFDBA86Du : 0xFF63513Du, 0, ImDrawFlags.None, selected ? 2 : 1);
            float font = ImGui.GetFontSize();
            void Text(string text, float x, float y, float maxWidth, uint color)
            {
                float fitted = Math.Min(font, maxWidth / Math.Max(1, ImGui.CalcTextSize(text).X) * font);
                draw.AddText(ImGui.GetFont(), fitted, start + new float2(x, y), color, text);
            }
            Text(item.Name, 12, 12, width - 24, 0xFFF0E3D4);
            Text(item.Hint, 12, height - 28, width - 24, 0xFFB7AA9C);
            var page = options.Page;
            float scale = Math.Min((width - 32) / page.Width, 210f / page.Height);
            var paper = start + new float2((width - page.Width * scale) / 2, 42 + (210 - page.Height * scale) / 2);
            var end = paper + new float2(page.Width * scale, page.Height * scale);
            draw.AddRectFilled(paper, end, 0xFF583819);
            draw.AddRect(paper, end, 0xFF95714B);
            var drawing = PageLayout.Drawing(options);
            // Use the same page area and fixed topology as export; diagrams keep
            // equal rows because the real craft determines automatic row heights.
            var cells = FixedViewLayouts.Cells(item.Layout.Template, drawing);
            for (int j = 0; j < cells.Length; j++)
            {
                var cell = cells[j];
                var p = paper + new float2(cell.X * scale, cell.Y * scale);
                var s = new float2(cell.Width * scale, cell.Height * scale);
                int camera = item.Layout.Assignments[j];
                draw.AddRect(p, p + s, camera < 0 ? 0xFFAA885Eu : 0xFF85603Au);
                string label = camera < 0 ? "BLANK" : SheetLayout.ViewName(camera).ToUpperInvariant();
                float fs = Math.Min(12, Math.Min(s.X / Math.Max(1, label.Length * .63f), s.Y * .24f));
                draw.AddText(ImGui.GetFont(), fs, p + new float2((s.X - label.Length * fs * .6f) / 2, s.Y - fs - 3), 0xFFE5CFB4, label);
                DrawTemplateView(p + new float2(s.X * .5f, (s.Y - fs - 4) * .5f), Math.Max(1, Math.Min(s.X * .28f, (s.Y - fs - 10) * .42f)), camera);
            }
            float footer = PageLayout.TitleTop(options) * scale;
            var title = PageLayout.TitleBounds(options);
            draw.AddRect(paper + new float2(title.X * scale, footer), paper + new float2(title.Right * scale, title.Bottom * scale), 0xFF95714B);
            float split = CompanyTitleBlock.Layout(options).Split * scale;
            draw.AddLine(paper + new float2(split, footer), paper + new float2(split, PageLayout.Bottom(options) * scale), 0xFF95714B);
            if (options.PartsTable || options.FlagTable)
            {
                var table = PageLayout.Table(options);
                for (int row = 0; row < 6; row++)
                    draw.AddLine(paper + new float2(table.X * scale, (table.Y + row * 90) * scale), paper + new float2(table.Right * scale, (table.Y + row * 90) * scale), 0xFF95714B);
            }
            if (hovered)
                PrintUiText.Tooltip(item.Name + "\n" + item.Hint + "\nDouble-click to generate. Single-click to select.");
        }
        ImGui.EndDisabled();
        ImGui.SetCursorScreenPos(origin + new float2(0, ((PrintTemplates.All.Length + columns - 1) / columns) * (height + gap)));
        ImGui.Dummy(new float2(1, 1));
    }

    static void DrawTemplateView(float2 center, float radius, int camera, uint ink = 0xFFE5CFB4, ViewAxes? axes = null)
    {
        var draw = ImGui.GetWindowDrawList();
        var right = new float2(1, 0);
        var down = new float2(0, 1);
        if (axes != null)
        {
            var basis = SheetLayout.Basis(camera);
            right = new(System.Numerics.Vector3.Dot(basis.Right, axes.Right), -System.Numerics.Vector3.Dot(basis.Right, axes.Up));
            down = new(-System.Numerics.Vector3.Dot(basis.Up, axes.Right), System.Numerics.Vector3.Dot(basis.Up, axes.Up));
        }
        float2 P(float x, float y) => center + (right * x + down * y) * radius;
        void L(float x, float y, float a, float b) => draw.AddLine(P(x, y), P(a, b), ink, 1);
        if (camera < 0)
        {
            L(-.2f, 0, .2f, 0);
            L(0, -.2f, 0, .2f);
            return;
        }
        foreach (var line in TemplateRocket.Views[camera])
            L(line.A.X, line.A.Y, line.B.X, line.B.Y);
    }
}
