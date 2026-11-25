using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool snapPanels = true, resizeTable;
    static int dragTable = -1, selectedTable = -1;
    static Box tableStart;
    static Vec tablePress;

    static bool UpdateTableSelection(Vec point, float factor, bool hovered)
    {
        if (selectedTable < 0 && dragTable < 0)
            return false;
        // Run before other canvas tools can consume the click. Selection is UI
        // state only: clearing it must not repaint the print or add history.
        if (dragTable < 0 && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            bool overSelected = sheet?.Scene != null && document != null && PrintTables.Panels(sheet.Scene, document.Labels)
                .Any(p => p.Id == selectedTable && (p.Bounds.Contains(point) || Vec.Distance(point, new(p.Bounds.Right, p.Bounds.Bottom)) * factor < 18));
            if (!overSelected)
                selectedTable = -1;
        }
        return false;
    }

    static IEnumerable<Box> PanelNeighbors(int id) => document == null || sheet?.Scene == null ? [] :
        document.Details.Where(d => d.Id != id).Select(d => d.Bounds)
            .Concat(document.Scene.Specs.Where(b => -1000 - b.Id != id).Select(b => b.Bounds))
            .Concat(document.Scene.FreeText.Select(b => b.Bounds))
            .Concat(PrintTables.Panels(sheet.Scene, document.Labels).Where(p => -1 - p.Id != id).Select(p => p.Bounds)).Append(PageLayout.TitleBounds(sheet.Options));

    // Positive IDs are details; -1 and -2 identify the two tables.
    static Box SnapPanel(Box box, int id, bool resize, float factor)
    {
        if (!snapPanels || sheet?.Scene == null || document == null)
            return box;
        return FrameLayout.Snap(box, PanelNeighbors(id), FrameLayout.Area(Options), resize, 10 / Math.Max(.05f, factor), PageLayout.TitleBounds(Options));
    }

    static void PanelGrip(Box box, float factor, float2 origin)
    {
        var corner = origin + new float2(box.Right * factor, box.Bottom * factor);
        var draw = ImGui.GetWindowDrawList();
        draw.AddLine(corner - new float2(13, 0), corner - new float2(0, 13), 0xFF88D8FF, 2);
        draw.AddLine(corner - new float2(7, 0), corner - new float2(0, 7), 0xFF88D8FF, 2);
    }

    static void DeleteTable(int id)
    {
        if (!CanEditAnnotations || sheet == null || document == null || id is < 0 or > 1)
            return;
        var current = sheet with
        {
            Scene = document.Scene,
            Labels = document.Labels,
            Details = document.Details
        };
        var options = id == 0 ? current.Options with
        {
            PartsTable = false
        } : current.Options with
        {
            FlagTable = false
        };
        var main = sheet.PoseMain ?? basePrint ?? sheet;
        composingLabels = renderKeepsDocument = renderAppliesSettings = true;
        renderAdoptsHistory = false;
        progress = "Removing table";
        StartRender(token => PreviewUpdates.Render(current, main, options, token));
        selectedTable = -1;
    }

    static void QueueTables(TableLayout tables)
    {
        if (busy || sheet == null || document == null)
            return;
        var current = sheet;
        var labels = document.Labels.Select(l => l with { ManualPlacement = true }).ToArray();
        composingLabels = true;
        renderKeepsDocument = true;
        progress = "Laying out tables";
        StartRender(token => AnnotationPainting.Compose(current, labels, token, tables) with { Notice = "Table updated. Drag its heading to move; drag the corner to resize." });
    }

    static bool DrawTableCanvas(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (sheet?.Scene == null || document == null || !editable)
            return false;
        void Outline(Box b, uint color) => ImGui.GetWindowDrawList().AddRect(origin + new float2(b.X * factor, b.Y * factor), origin + new float2(b.Right * factor, b.Bottom * factor), color, 0, ImDrawFlags.None, 2);
        var panels = PrintTables.Panels(sheet.Scene, document.Labels).ToArray();
        if (dragTable >= 0)
        {
            ImGui.SetMouseCursor(resizeTable ? ImGuiMouseCursor.ResizeNWSE : ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                dragTable = -1;
                return true;
            }
            var delta = point - tablePress;
            var box = resizeTable ? tableStart with
            {
                Width = tableStart.Width + delta.X,
                Height = tableStart.Height + delta.Y
            } : tableStart with
            {
                X = tableStart.X + delta.X,
                Y = tableStart.Y + delta.Y
            };
            box = SnapPanel(box, -1 - dragTable, resizeTable, factor);
            box = resizeTable ? FrameLayout.Resize(box, Options, 280, 100) : FrameLayout.Clamp(box, Options, 280, 100);
            float minimum = PrintTables.Height(sheet.Scene, document.Labels, dragTable, box.Width, 14);
            box = resizeTable ? FrameLayout.Resize(box, Options, 280, minimum) : FrameLayout.Clamp(box, Options, 280, minimum);
            box = ClearPanelPlacement(box, PanelNeighbors(-1 - dragTable), FrameLayout.Area(Options), resizeTable, 280, 100, snapPanels ? FrameLayout.Gap : 0,
                b => PrintTables.Font(sheet.Scene, document.Labels, dragTable, b) > 0, boundary: PageLayout.TitleBounds(Options)) ?? tableStart;
            if (!resizeTable)
                box = CanvasMotion.KeepOnPage(box, Options);
            Outline(box, 0xFF8EDD8E);
            PanelGrip(box, factor, origin);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                int id = dragTable;
                dragTable = -1;
                if (box != tableStart)
                    QueueTables(document.Tables.Set(id, box));
            }
            return true;
        }
        // Match paint order: flagnotes are drawn last and must receive clicks
        // before a parts-table rectangle underneath them.
        foreach (var panel in panels.Reverse())
        {
            bool over = panel.Bounds.Contains(point), corner = Vec.Distance(point, new(panel.Bounds.Right, panel.Bounds.Bottom)) * factor < 18;
            if (panel.Id == selectedTable || hovered && (over || corner))
            {
                Outline(panel.Bounds, 0xFF88D8FF);
                PanelGrip(panel.Bounds, factor, origin);
            }
            if (!hovered || canvasTarget?.Id != panel.Id || !over && !corner)
                continue;
            bool header = over;
            if (header || corner)
            {
                ImGui.SetMouseCursor(corner ? ImGuiMouseCursor.ResizeNWSE : ImGuiMouseCursor.ResizeAll);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    selectedTable = dragTable = panel.Id;
                    selectedDetail = -1;
                    detailEdit = null;
                    SelectLabel(-1);
                    resizeTable = corner;
                    tableStart = panel.Bounds;
                    tablePress = point;
                }
            }
            // Tables are painted over geometry: never pick the hidden part below.
            return true;
        }
        return false;
    }
}
