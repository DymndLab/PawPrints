using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static SpecBlock? specDraft, dragSpec;
    static bool specResize;
    static Vec specPress;
    static int selectedSpec = -1, hoveredSpec = -1;


    static void DeleteSpec(int id)
    {
        if (!CanEditAnnotations || document == null)
            return;
        selectedSpec = -1;
        QueueSpecs(document.Scene.Specs.Where(s => s.Id != id).ToArray());
    }
    static string specError = "";
    static readonly ImInputString specVehicleName = new(512);
    internal static bool SpecPopupDrawn
    {
        get; private set;
    }
    static readonly string[] SpecFormats = ["Compact table", "Stacked metrics"];
    static readonly string[] SpecBarModes = ["Value inside bar", "Value before bar", "Value only"];


    static uint? EngineOwner(int part) => document?.Scene.Parts.FirstOrDefault(p => p.Id == part)?.NodeId is { } node && document.Scene.Performance?.Engines.Any(e => e.Part == node) == true ? node : null;


    static void QueueSpecs(SpecBlock[] specs)
    {
        if (busy || sheet == null || document == null)
            return;
        var current = sheet with
        {
            Scene = document.Scene.WithSpecs(specs)
        };
        var labels = document.Labels.Select(l => l with { ManualPlacement = true }).ToArray();
        renderKeepsDocument = true;
        renderAdoptsHistory = false;
        composingLabels = true;
        progress = "Drawing performance blocks";
        StartRender(token => AnnotationPainting.Compose(current, labels, token) with { Notice = "Performance updated. Drag a heading to move, corner to resize; right-click for options." });
    }


    static IEnumerable<Box> SpecObstacles(int id) => PanelNeighbors(-1000 - id).Concat(document!.Scene.Dimensions.Select(d => HeightDimensions.Place(document.Scene, d).Bounds));


    static SpecBlock? FitSpec(SpecBlock block)
    {
        var scene = document!.Scene;
        var data = scene.Performance!;
        var area = FrameLayout.Area(scene.Options);
        bool existing = scene.Specs.Any(b => b.Id == block.Id);
        if (existing && block.Bounds.X >= area.X && block.Bounds.Y >= area.Y && block.Bounds.Right <= area.Right && block.Bounds.Bottom <= area.Bottom
            && SpecObstacles(block.Id).All(o => !o.Overlaps(block.Bounds)) && PerformancePainting.Font(data, block, scene.Options.TitleBlock?.Title) > 0)
            return block;
        var wanted = block.Bounds with
        {
            Height = Math.Max(block.Bounds.Height, PerformancePainting.Height(data, block, block.FontSize, scene.Options.TitleBlock?.Title))
        };
        // Prefer the requested size. When a long block cannot fit, try wider
        // columns before reducing type to the existing table minimum.
        foreach (float width in new[] { wanted.Width, Math.Min(area.Width, 900), Math.Min(area.Width, 1200), area.Width }.Distinct())
        {
            var candidate = block with
            {
                Bounds = wanted with
                {
                    Width = width
                }
            };
            var box = FrameLayout.Clamp(candidate.Bounds with
            {
                Height = Math.Min(area.Height, Math.Max(150, PerformancePainting.Height(data, candidate, candidate.FontSize, scene.Options.TitleBlock?.Title)))
            }, scene.Options, 300, 150);
            var clear = (existing ? null : ClearPanelPlacement(box, SpecObstacles(block.Id).Concat(scene.Visible.Values.Select(v => v.Bounds)).Concat(scene.Captions.Select(c => c.Bounds)), area, false, 300, 150, FrameLayout.Gap, b => PerformancePainting.Font(data, candidate with { Bounds = b }, scene.Options.TitleBlock?.Title) > 0, boundary: PageLayout.TitleBounds(Options)))
                ?? ClearPanelPlacement(box, SpecObstacles(block.Id), area, false, 300, 150, snapPanels ? FrameLayout.Gap : 0, b => PerformancePainting.Font(data, candidate with { Bounds = b }, scene.Options.TitleBlock?.Title) > 0, boundary: PageLayout.TitleBounds(Options));
            if (clear is { } found)
                return block with
                {
                    Bounds = found
                };
        }
        return null;
    }


    static void BeginSpec(int part, int view, bool vehicle = false)
    {
        if (document?.Scene.Performance == null)
            return;
        if (document.Scene.Specs.Length >= 12)
        {
            ReportFailure("This print supports up to 12 performance blocks.");
            return;
        }
        uint? owner = EngineOwner(part);
        if (!vehicle && owner == null)
            return;
        var area = FrameLayout.Area(document.Scene.Options);
        int id = document.Scene.Specs.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1;
        var b = new SpecBlock(id, new(area.Right - 650, area.Y, 650, 150), Vehicle: vehicle, EnginePart: owner, ViewId: view, Line: (LeaderLine)defaultLine, Tip: (LeaderTip)defaultTip);
        if (vehicle)
            b = b with
            {
                Fields = SpecField.Name | SpecField.Quantity | SpecField.Thrust | SpecField.Isp | SpecField.LoadedMass | SpecField.DryMass | SpecField.FuelMass | SpecField.Twr
            };
        specDraft = b;
        specError = "";
        SpecPopupDrawn = false;
    }


    static bool DrawSpecCanvas(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (!editable || document?.Scene.Performance == null)
        {
            dragSpec = null;
            return false;
        }
        void Outline(Box b, uint color) => ImGui.GetWindowDrawList().AddRect(origin + new float2(b.X * factor, b.Y * factor), origin + new float2(b.Right * factor, b.Bottom * factor), color, 0, ImDrawFlags.None, 2);
        if (dragSpec is { } dragging)
        {
            ImGui.SetMouseCursor(specResize ? ImGuiMouseCursor.ResizeNWSE : ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                dragSpec = null;
                return true;
            }
            var delta = point - specPress;
            var box = specResize ? dragging.Bounds with
            {
                Width = dragging.Bounds.Width + delta.X,
                Height = dragging.Bounds.Height + delta.Y
            } : dragging.Bounds with
            {
                X = dragging.Bounds.X + delta.X,
                Y = dragging.Bounds.Y + delta.Y
            };
            box = SnapPanel(box, -1000 - dragging.Id, specResize, factor);
            box = specResize ? FrameLayout.Resize(box, Options, 300, 150) : FrameLayout.Clamp(box, Options, 300, 150);
            box = ClearPanelPlacement(box, SpecObstacles(dragging.Id), FrameLayout.Area(Options), specResize, 300, 150, snapPanels ? FrameLayout.Gap : 0, b => PerformancePainting.Font(document.Scene.Performance, dragging with { Bounds = b }, document.Scene.Options.TitleBlock?.Title) > 0, boundary: PageLayout.TitleBounds(Options)) ?? dragging.Bounds;
            if (!specResize)
                box = CanvasMotion.KeepOnPage(box, Options);
            Outline(box, 0xFF8EDD8E);
            PanelGrip(box, factor, origin);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                dragSpec = null;
                if (box != dragging.Bounds)
                    QueueSpecs(document.Scene.Specs.Select(b => b.Id == dragging.Id ? b with { Bounds = box } : b).ToArray());
            }
            return true;
        }
        if (ControlsLocked || dragTable >= 0 || DetailGesture || partMenuOpen)
            return false;
        foreach (var b in document.Scene.Specs.Reverse())
        {
            bool corner = Vec.Distance(point, new(b.Bounds.Right, b.Bounds.Bottom)) * factor < 18, over = b.Bounds.Contains(point);
            if (b.Id == selectedSpec || hovered && (over || corner))
            {
                Outline(b.Bounds, 0xFF88D8FF);
                PanelGrip(b.Bounds, factor, origin);
            }
            if (!hovered || canvasTarget?.Id != b.Id || !over && !corner)
                continue;
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                specDraft = b;
                specError = "";
                SpecPopupDrawn = false;
            }
            hoveredSpec = b.Id;
            if (corner || over)
            {
                ImGui.SetMouseCursor(corner ? ImGuiMouseCursor.ResizeNWSE : ImGuiMouseCursor.ResizeAll);
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    dragSpec = b;
                    specResize = corner;
                    specPress = point;
                    SelectLabel(-1);
                    selectedSpec = b.Id;
                    selectedDetail = selectedTable = -1;
                }
            }
            return true;
        }
        return false;
    }


    static void DrawSpecDialog()
    {
        if (specDraft != null && !ImGui.IsPopupOpen("Performance block"u8))
            ImGui.OpenPopup("Performance block"u8);
        if (!ImGui.BeginPopupModal("Performance block"u8, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            partMenuOpen = true;
            if (specDraft is not { } b || document?.Scene.Performance is not { } data)
            {
                ImGui.CloseCurrentPopup();
                return;
            }
            string? drawingName = document.Scene.Options.TitleBlock?.Title;
            if (!SpecPopupDrawn)
                specVehicleName.SetValue(SpecMetrics.VehicleName(data, b, drawingName).AsSpan());
            SpecPopupDrawn = true;
            ImGui.BeginChild("Performance options"u8, new float2(610, Math.Min(560, ImGui.GetIO().DisplaySize.Y - 160)));
            try
            {
                PrintUiText.Wrapped(SpecMetrics.Title(data, b, drawingName));
                if (b.Vehicle)
                {
                    bool followsTitle = b.VehicleName == null;
                    if (ImGui.Checkbox("Use title-block title", ref followsTitle))
                    {
                        b = b with
                        {
                            VehicleName = followsTitle ? null : specVehicleName.ToString()
                        };
                        specVehicleName.SetValue(SpecMetrics.VehicleName(data, b, drawingName).AsSpan());
                    }
                    if (followsTitle)
                        specVehicleName.SetValue(SpecMetrics.VehicleName(data, b, drawingName).AsSpan());
                    ImGui.BeginDisabled(followsTitle);
                    ImGui.SetNextItemWidth(420);
                    bool renamed = ImGui.InputText("Vehicle name", specVehicleName);
                    specVehicleName.EvaluateLength();
                    if (renamed)
                        b = b with
                        {
                            VehicleName = specVehicleName.ToString()
                        };
                    ImGui.EndDisabled();
                    PrintUiText.Wrapped(followsTitle
                        ? "Follows the drawing title; uses the captured craft name when the title is blank."
                        : "This name applies only to this performance block.");
                }
                int format = b.Format;
                if (ImGui.Combo("Format"u8, ref format, SpecFormats, 2))
                    b = b with
                    {
                        Format = format
                    };
                bool vac = (b.Environment & 1) != 0, asl = (b.Environment & 2) != 0;
                if (ImGui.Checkbox("VAC", ref vac) && (!vac && asl || vac))
                    b = b with
                    {
                        Environment = vac ? b.Environment | 1 : b.Environment & ~1
                    };
                ImGui.SameLine();
                if (ImGui.Checkbox("ASL", ref asl) && (!asl && vac || asl))
                    b = b with
                    {
                        Environment = asl ? b.Environment | 2 : b.Environment & ~2
                    };
                if (b.Vehicle)
                {
                    string[] scopes = ["All installed engines", .. data.Stages.Select(s => "Stage " + s.Number + " / KSA " + s.Environment), "Custom engine group"];
                    int scope = b.Stage >= 0 ? Array.FindIndex(data.Stages, s => s.Number == b.Stage) + 1 : b.SelectedEngines != null ? scopes.Length - 1 : 0;
                    if (ImGui.Combo("Scope"u8, ref scope, scopes, scopes.Length))
                        b = b with
                        {
                            Stage = scope > 0 && scope < scopes.Length - 1 ? data.Stages[scope - 1].Number : -1,
                            SelectedEngines = scope == scopes.Length - 1 ? data.Engines.Select(e => e.Id).ToArray() : null
                        };
                    if (b.SelectedEngines != null)
                        foreach (var e in data.Engines)
                        {
                            bool yes = b.SelectedEngines.Contains(e.Id);
                            if (ImGui.Checkbox(e.Name + " / " + e.Id, ref yes))
                                b = b with
                                {
                                    SelectedEngines = yes ? [.. b.SelectedEngines, e.Id] : b.SelectedEngines.Where(id => id != e.Id).ToArray()
                                };
                        }
                    PrintUiText.Wrapped("Installed/custom engine totals use captured whole-vehicle mass. Stage uses its initial active engine set and mass. Burn time and delta-v follow KSA's staged fuel-feed estimate and its labeled VAC/ASL environment; no estimate is invented for custom groups.");
                }
                else
                {
                    bool group = b.Identical, combined = b.Combined;
                    if (ImGui.Checkbox("Group identical engines", ref group))
                        b = b with
                        {
                            Identical = group
                        };
                    if (ImGui.Checkbox("Combined group ratings", ref combined))
                        b = b with
                        {
                            Combined = combined
                        };
                    ImGui.Text(SpecMetrics.Engines(data, b).Select(e => e.Part).Distinct().Count() + " engine part(s)");
                }
                ImGui.Separator();
                ImGui.Text("Show fields");
                void Field(SpecField field, string title)
                {
                    bool value = b.Shows(field);
                    if (ImGui.Checkbox(title, ref value))
                        b = b with
                        {
                            Fields = value ? b.Fields | field : b.Fields & ~field
                        };
                }
                Field(SpecField.Name, "Name");
                Field(SpecField.Quantity, "Engine quantity");
                Field(SpecField.Thrust, "Thrust");
                Field(SpecField.Isp, "Specific impulse");
                Field(SpecField.Propellants, "Propellants");
                Field(SpecField.Throttle, "Throttle range");
                Field(SpecField.Gimbal, "Gimbal range / angle diagram");
                Field(SpecField.Flow, "Propellant flow");
                Field(SpecField.DryMass, "Dry mass");
                if (b.Vehicle)
                {
                    Field(SpecField.LoadedMass, "Loaded mass");
                    Field(SpecField.FuelMass, "Stored mass (stage: propellant)");
                    Field(SpecField.Twr, "TWR (Earth g0)");
                    Field(SpecField.BurnTime, "Estimated burn time");
                    Field(SpecField.DeltaV, "Estimated delta-v");
                    Field(SpecField.Dimensions, "Craft dimensions");
                    Field(SpecField.PartCount, "Part count");
                    Field(SpecField.Loading, "Loading by substance");
                }
                if (b.Format == 1)
                {
                    ImGui.Separator();
                    int mode = b.BarMode;
                    if (ImGui.Combo("Numeric style"u8, ref mode, SpecBarModes, 3))
                        b = b with
                        {
                            BarMode = mode
                        };
                    if (ImGui.CollapsingHeader("Individual metric styles"u8))
                    {
                        string[] choices = ["Use block style", .. SpecBarModes];
                        foreach (var row in SpecMetrics.Rows(data, b).Where(r => r.Text == null && !r.Angle))
                        {
                            var old = b.Styles?.FirstOrDefault(s => s.Key == row.Key);
                            int choice = old == null ? 0 : old.Mode + 1;
                            float max = (float)(old?.Maximum ?? 0);
                            bool changed = ImGui.Combo(row.Label + "##Style", ref choice, choices, 4);
                            changed |= ImGui.InputFloat("Bar maximum##" + row.Key, ref max);
                            if (changed)
                                b = b with
                                {
                                    Styles = [.. (b.Styles ?? []).Where(s => s.Key != row.Key), .. (choice == 0 ? Array.Empty<MetricStyle>() : [new MetricStyle(row.Key, choice - 1, float.IsFinite(max) ? Math.Max(0, max) : 0)])]
                                };
                        }
                        PrintUiText.Wrapped("Maximum 0 chooses a labeled automatic range. Values use the units printed on the metric.");
                    }
                }
                ImGui.Separator();
                int font = b.FontSize;
                if (ImGui.SliderInt("Text size"u8, ref font, 14, 40))
                    b = b with
                    {
                        FontSize = font
                    };
                bool border = b.Border, fill = b.Fill, reference = b.Reference;
                if (ImGui.Checkbox("Border", ref border))
                    b = b with
                    {
                        Border = border
                    };
                if (!Options.FreeLayout && ImGui.Checkbox("Paper background", ref fill))
                    b = b with
                    {
                        Fill = fill
                    };
                if (ImGui.Checkbox("Calculation basis / reference notes", ref reference))
                    b = b with
                    {
                        Reference = reference
                    };
                if (!b.Vehicle)
                {
                    bool leader = b.Leader;
                    if (ImGui.Checkbox("Leader to engine", ref leader))
                        b = b with
                        {
                            Leader = leader
                        };
                    int line = (int)b.Line, tip = (int)b.Tip;
                    if (ImGui.Combo("Leader"u8, ref line, LineNames, LineNames.Length))
                        b = b with
                        {
                            Line = (LeaderLine)line
                        };
                    if (ImGui.Combo("Endpoint"u8, ref tip, TipNames, TipNames.Length))
                        b = b with
                        {
                            Tip = (LeaderTip)tip
                        };
                }
            }
            finally { ImGui.EndChild(); }
            specDraft = b;
            if (specError.Length > 0)
                PrintUiText.Wrapped(specError);
            if (ImGui.Button("Apply block"u8))
            {
                var fitted = FitSpec(b);
                if (fitted == null)
                    ReportFailure("No clear room for this block. Hide fields or move/resize another table or detail, then try again.");
                else
                {
                    specDraft = null;
                    ImGui.CloseCurrentPopup();
                    if (document.Scene.Specs.Any(s => s.Id == b.Id))
                        QueueSpecs([.. document.Scene.Specs.Where(s => s.Id != b.Id), fitted]);
                    else
                        PlaceSpec(fitted);
                }
            }
            if (document.Scene.Specs.Any(s => s.Id == b.Id))
            {
                ImGui.SameLine();
                if (ImGui.Button("Remove block"u8))
                {
                    specDraft = null;
                    ImGui.CloseCurrentPopup();
                    QueueSpecs(document.Scene.Specs.Where(s => s.Id != b.Id).ToArray());
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"u8))
            {
                specDraft = null;
                ImGui.CloseCurrentPopup();
            }
        }
        finally { DrawWarningDialog(true); ImGui.EndPopup(); }
    }
}
