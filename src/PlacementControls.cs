using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    enum PlaceTool { None, Text, Label, Flagnote, Dimension, VehicleSpecs, PartSpecs, Kitten, Banana, Wear }

    static PlaceTool activePlaceTool;
    static ScaleReferenceKind? referenceViewPick;

    static void LeavePlaceTab()
    {
        selectedPlacedWear = null;
        if (activePlaceTool == PlaceTool.None)
            return;
        if (placingText || textDraft != null || pendingLabel != null || flagnotePlacement != null || specDraft != null || GhostActive)
            CancelPlacement();
        activePlaceTool = PlaceTool.None;
        referenceViewPick = null;
        pickingPartTool = false;
    }

    static void FinishLabelPlacement()
    {
        var tool = activePlaceTool;
        CancelPlacement();
        activePlaceTool = tool;
    }

    static bool BeginRepeatedCanvasPlacement(bool hovered, bool editable)
    {
        if (!hovered || !editable || !CanEditAnnotationState(true))
            return false;
        if (activePlaceTool is not (PlaceTool.Dimension or PlaceTool.VehicleSpecs))
            return false;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            activePlaceTool = PlaceTool.None;
            status = "Placement cancelled.";
            return true;
        }
        if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return false;
        switch (activePlaceTool)
        {
            case PlaceTool.Dimension: BeginDirectDimension(); break;
            case PlaceTool.VehicleSpecs: BeginSpec(-1, 0, true); break;
        }
        return true;
    }

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
        bool Button(string label, bool available = true, bool active = false, bool captureRequired = true, PlaceTool tool = PlaceTool.None)
        {
            bool disabled = !(captureRequired ? ready : canSwitchTool) || !available;
            ImGui.BeginDisabled(disabled);
            bool clicked = active || (tool != PlaceTool.None && activePlaceTool == tool)
                ? PrintUiStyle.YellowButton(label, new float2(-1, 0))
                : ImGui.Button(label, new float2(-1, 0));
            PrintUiStyle.ShowUnavailableCursor(disabled);
            ImGui.EndDisabled();
            if (clicked)
            {
                if (placingText) CancelTextPlacement();
                pickingPartTool = false;
                selectedPlacedWear = null;
                activePlaceTool = tool;
            }
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
        if (Button("Text Box", captureRequired: false, tool: PlaceTool.Text))
            BeginTextBox();
        Button("Part Label", tool: PlaceTool.Label);
        Button("Flagnote", tool: PlaceTool.Flagnote);
        if (Button("Dimension", tool: PlaceTool.Dimension))
            BeginDirectDimension();
        if (Button("Vehicle Specs", document?.Scene.Performance != null, tool: PlaceTool.VehicleSpecs))
            BeginSpec(-1, 0, true);
        Button("Part Specs", document?.Scene.Performance != null, tool: PlaceTool.PartSpecs);
        if (ImGui.IsItemHovered())
            PrintUiText.Tooltip("Select an engine in a view to add its specifications.");

        ImGui.Separator();
        if (Button("Kitten for Scale", document?.Scene.Kittens.Length < 4, tool: PlaceTool.Kitten))
            OpenReferencePlacement(ScaleReferenceKind.Kitten);
        if (Button("Banana for Scale", document?.Scene.Kittens.Length < 4, tool: PlaceTool.Banana))
            OpenReferencePlacement(ScaleReferenceKind.Banana);

        Button("Place wear", document?.Scene.WearMarks.Length < WearPlacement.Limit, captureRequired: false, tool: PlaceTool.Wear);
        if (activePlaceTool == PlaceTool.Wear) DrawWearControls();
        DrawPlacedWearControls();
        DrawGroupControls();
        DrawPlacementSettings();
        if (referenceViewPick != null)
        {
            PrintUiText.Wrapped("Click the drawing or caption of a view to match its scale and orientation.");
            if (ImGui.Button("Cancel view selection"u8))
                CancelReferenceViewPick();
        }

        if (!CanEditAnnotationState(false))
            return;
        if (activePlaceTool == PlaceTool.Text && document!.Scene.FreeText.Length < FreeTextPainting.Limit)
            BeginTextBox();
        else if (sheet?.PoseSource != null && activePlaceTool is (PlaceTool.Label or PlaceTool.Flagnote or PlaceTool.PartSpecs))
        {
            placeKind = activePlaceTool switch { PlaceTool.Flagnote => 1, PlaceTool.PartSpecs => 3, _ => 0 };
            pickingPartTool = true;
        }
        else if (activePlaceTool is PlaceTool.Dimension or PlaceTool.VehicleSpecs)
            PrintUiText.Wrapped("Click the drawing to place another. Right-click cancels.");
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
            var kind = (ScaleReferenceKind)referenceKind;
            ImGui.SetNextItemWidth(240);
            ImGui.Combo("Style"u8, ref kittenStyle, new[] { "Solid silhouette", "Blueprint outline" }, 2);
            bool canMatch = document?.Scene.Views.Any(v => CanMatchReferenceView(document.Scene, v)) == true;
            ImGui.BeginDisabled(!canMatch);
            if (PrintUiStyle.PrimaryButton("Place in view"))
            {
                SelectReferenceView(kind);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SetItemDefaultFocus();
            ImGui.EndDisabled();
            PrintUiText.Wrapped(canMatch ? "Match a view's scale and orientation." : "Add a view to match its scale and orientation.");
            if (ImGui.CollapsingHeader("Choose orientation manually"))
            {
                ImGui.SetNextItemWidth(240);
                ImGui.Combo("Orientation"u8, ref kittenView, DrawingPlacements.Names, DrawingPlacements.Names.Length);
                if (ImGui.Button("Place on sheet"u8))
                {
                    BeginScaleReference(kind);
                    ImGui.CloseCurrentPopup();
                }
            }
            if (ImGui.Button("Cancel"u8))
            {
                activePlaceTool = PlaceTool.None;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    static bool CanMatchReferenceView(RenderScene scene, ViewRegion view) =>
        view.Frame != null && !scene.Details.Any(d => d.Definition.ViewId == view.Id && d.Definition.SourceIssue != DetailSourceIssue.None);

    static void SelectReferenceView(ScaleReferenceKind kind)
    {
        referenceViewPick = kind;
        status = "Click a view to match it. Right-click cancels.";
    }

    static void CancelReferenceViewPick()
    {
        referenceViewPick = null;
        activePlaceTool = PlaceTool.None;
        status = "Placement cancelled.";
    }

    static bool DrawReferenceSource(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (referenceViewPick is not { } kind)
            return false;
        if (!editable || document == null || hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CancelReferenceViewPick();
            return true;
        }
        if (!hovered) return true;

        var scene = document.Scene;
        var hit = CanvasObjects.Pick(sheet!, scene, document.Labels, point, 5 / factor);
        if (hit is not { Kind: DrawingObjectKind.View } item ||
            scene.Views.FirstOrDefault(v => v.Id == item.Id) is not { } view || !CanMatchReferenceView(scene, view))
            return true;

        var bounds = CanvasObjects.ContentBounds(scene, document.Labels, item);
        ImGui.GetWindowDrawList().AddRect(origin + new float2(bounds.X, bounds.Y) * factor,
            origin + new float2(bounds.Right, bounds.Bottom) * factor, 0xFF44DDFF, 0, ImDrawFlags.None, 2);
        PrintUiText.Tooltip("Match " + view.Name);
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            referenceViewPick = null;
            BeginScaleReference(kind, view.Id);
        }
        return true;
    }

    static void BeginScaleReference(ScaleReferenceKind kind, int? viewId = null)
    {
        if (document == null || document.Scene.Kittens.Length >= 4)
            return;
        var kittens = document.Scene.Kittens;
        var area = FrameLayout.Area(document.Scene.Options);
        int id = kittens.Select(k => k.Id).DefaultIfEmpty(0).Max() + 1;
        BeginKitten(viewId is { } view
            ? KittenReferences.FromView(document.Scene, id, view, kittenStyle == 1, kind)
            : new(id, kittenView, area.X + 80, area.Bottom - 160, kittenStyle == 1, kind));
    }
}
