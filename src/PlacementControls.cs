using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static void DrawPlaceControls()
    {
        if (placingText)
        {
            PrintUiText.Wrapped("Drag on the paper to draw a text box.");
            if (ImGui.Button("Cancel text box"u8))
                CancelTextPlacement();
        }
        bool canSwitchTool = CanEditAnnotationState(true);
        bool ready = canSwitchTool && sheet?.PoseSource != null;
        bool Button(string label, bool available = true, bool active = false, bool captureRequired = true)
        {
            ImGui.BeginDisabled(!(captureRequired ? ready : canSwitchTool) || !available);
            bool clicked = active ? PrintUiStyle.YellowButton(label, new float2(-1, 0)) : ImGui.Button(label, new float2(-1, 0));
            ImGui.EndDisabled();
            if (clicked && placingText) CancelTextPlacement();
            return clicked;
        }

        if (Button("Primary View", active: viewDraft is { Parent: null }, captureRequired: false))
            BeginView();
        if (choosingProjectionSource)
        {
            PrintUiText.Wrapped("Click a primary view in the drawing to project from it.");
            if (ImGui.Button("Cancel source selection"))
                choosingProjectionSource = false;
        }
        if (viewDraft != null)
            DrawDrawingControls();
        if (Button("Secondary View", document?.Scene.Drawing?.Views.Length > 0, choosingProjectionSource || viewDraft?.Parent != null))
            BeginProjectionSource();
        if (Button("Exploded View", active: detailScopeOpen, captureRequired: false))
            BeginExplodedPlacement();

        ImGui.Separator();
        if (Button("Text Box", active: placingText || textDraft != null, captureRequired: false))
            BeginTextBox();
        if (Button("Part Label", active: pickingPartTool && placeKind == 0 || pendingLabel is { FlagNote: false }))
        {
            placeKind = 0;
            pickingPartTool = true;
        }
        if (Button("Flagnote", active: pickingPartTool && placeKind == 1 || flagnotePlacement != null))
        {
            placeKind = 1;
            pickingPartTool = true;
        }
        if (Button("Dimension"))
            BeginDirectDimension();
        if (Button("Vehicle Specs", document?.Scene.Performance != null))
            BeginSpec(-1, 0, true);
        if (Button("Part Specs", document?.Scene.Performance != null))
        {
            placeKind = 3;
            pickingPartTool = true;
        }
        if (ImGui.IsItemHovered())
            PrintUiText.Tooltip("Select an engine in a view to add its specifications.");

        ImGui.Separator();
        if (Button("Kitten for Scale", document?.Scene.Kittens.Length < 4))
            OpenReferencePlacement(ScaleReferenceKind.Kitten);
        if (Button("Banana for Scale", document?.Scene.Kittens.Length < 4))
            OpenReferencePlacement(ScaleReferenceKind.Banana);

        DrawGroupControls();
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
