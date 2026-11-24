using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static void DrawPlaceControls()
    {
        bool ready = CanEditAnnotations && sheet?.PoseSource != null;
        bool Button(string label, bool available = true)
        {
            ImGui.BeginDisabled(!ready || !available);
            bool clicked = ImGui.Button(label, new float2(-1, 0));
            ImGui.EndDisabled();
            return clicked;
        }

        ImGui.BeginDisabled(!CanEditAnnotations);
        if (ImGui.Button("Primary View", new float2(-1, 0))) BeginView();
        ImGui.EndDisabled();
        if (viewDraft != null) DrawDrawingControls();
        if (Button("Secondary View", document?.Scene.Drawing?.Views.Length > 0))
            ImGui.OpenPopup("Choose source view"u8);
        if (Button("Exploded View"))
            placeKind = 2;

        ImGui.Separator();
        if (Button("Text Box"))
            BeginTextBox();
        if (Button("Part Label"))
            placeKind = 0;
        if (Button("Flagnote"))
            placeKind = 1;
        if (Button("Dimension"))
            ImGui.OpenPopup("Place dimension settings"u8);
        if (Button("Vehicle Specs", document?.Scene.Performance != null))
            BeginSpec(-1, 0, true);
        if (Button("Part Specs", document?.Scene.Performance != null))
            placeKind = 3;
        if (ImGui.IsItemHovered())
            PrintUiText.Tooltip("Select an engine in a view to add its specifications.");

        ImGui.Separator();
        if (Button("Kitten for Scale", document?.Scene.Kittens.Length < 4))
            OpenReferencePlacement(ScaleReferenceKind.Kitten);
        if (Button("Banana for Scale", document?.Scene.Kittens.Length < 4))
            OpenReferencePlacement(ScaleReferenceKind.Banana);

        DrawPlacementSettings();
    }

    static void OpenReferencePlacement(ScaleReferenceKind kind)
    {
        referenceKind = (int)kind;
        kittenStyle = 1;
        ImGui.OpenPopup("Place reference settings"u8);
    }

    static void DrawPlacementSettings()
    {
        if (ImGui.BeginPopup("Choose source view"u8))
        {
            ImGui.Text("Project from");
            foreach (var source in CurrentDrawing().Views)
                if (ImGui.MenuItem(DrawingPlacements.Name(source) + "##Source" + source.Id))
                {
                    BeginView(source.Id);
                    ImGui.CloseCurrentPopup();
                }
            ImGui.EndPopup();
        }
        ImGui.SetNextWindowSize(new float2(340, 0), ImGuiCond.Appearing);
        if (ImGui.BeginPopup("Place dimension settings"u8))
        {
            DrawHeightControls(false);
            if (GhostActive)
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopup("Place reference settings"u8))
        {
            ImGui.Text(referenceKind == 0 ? "Kitten for Scale" : "Banana for Scale");
            ImGui.SetNextItemWidth(240);
            ImGui.Combo("View"u8, ref kittenView, DrawingPlacements.Names, DrawingPlacements.Names.Length);
            ImGui.SetNextItemWidth(240);
            ImGui.Combo("Style"u8, ref kittenStyle, new[] { "Solid silhouette", "Blueprint outline" }, 2);
            if (ImGui.Button("Place on sheet"u8) && document != null)
            {
                var kittens = document.Scene.Kittens;
                var area = FrameLayout.Area(document.Scene.Options);
                int id = kittens.Select(k => k.Id).DefaultIfEmpty(0).Max() + 1;
                BeginKitten(new(id, kittenView, area.X + 80, area.Bottom - 160, kittenStyle == 1, (ScaleReferenceKind)referenceKind));
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"u8))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
