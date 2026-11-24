using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static int kittenView = 2, selectedKitten = -1, hoveredKitten = -1, requestedReference = -1;
    static int kittenStyle = 1, referenceKind, referenceMenuId = -1;
    static KittenReference? dragKitten;
    static Vec kittenGrab;


    static void BeginKittenFromView(int viewId)
    {
        if (document == null || document.Scene.Kittens.Length >= 4)
            return;
        var reference = KittenReferences.FromView(document.Scene, document.Scene.Kittens.Select(k => k.Id).DefaultIfEmpty(0).Max() + 1, viewId);
        kittenView = reference.View;
        kittenStyle = 1;
        referenceKind = 0;
        BeginKitten(reference);
    }

    static void FlipReference(int id, bool horizontal)
    {
        if (!CanEditAnnotations || document == null || !document.Scene.Kittens.Any(k => k.Id == id))
            return;
        QueueKittens(document.Scene.Kittens.Select(k => k.Id != id ? k : horizontal
            ? k with
            {
                FlipHorizontal = !k.FlipHorizontal
            }
            : k with
            {
                FlipVertical = !k.FlipVertical
            }).ToArray());
    }

    static bool DrawReferenceMenu(bool editable)
    {
        if (!ImGui.BeginPopup("Scale reference actions"u8))
            return false;
        partMenuOpen = true;
        var reference = document?.Scene.Kittens.FirstOrDefault(k => k.Id == referenceMenuId);
        if (!editable || reference == null)
            ImGui.CloseCurrentPopup();
        else
        {
            ImGui.Text(reference.Kind.ToString());
            ImGui.Separator();
            if (ImGui.MenuItem("Flip left / right (F)"u8))
                FlipReference(reference.Id, true);
            if (ImGui.MenuItem("Flip up / down"u8))
                FlipReference(reference.Id, false);
            if (ImGui.MenuItem("Remove reference"u8))
                DeleteReference(reference.Id);
        }
        ImGui.EndPopup();
        return true;
    }

    static void DeleteReference(int id)
    {
        if (!CanEditAnnotations || document == null || !document.Scene.Kittens.Any(k => k.Id == id))
            return;
        QueueKittens(document.Scene.Kittens.Where(k => k.Id != id).ToArray());
        selectedKitten = hoveredKitten = -1;
    }

    static void UpdateReferenceSelection(Vec point, bool hovered)
    {
        if (selectedKitten < 0 || document == null || dragKitten != null)
            return;
        var selected = document.Scene.Kittens.FirstOrDefault(k => k.Id == selectedKitten);
        if (selected == null ||
            hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && !KittenReferences.Bounds(document.Scene, selected).Contains(point))
            selectedKitten = -1;
    }

    static void QueueKittens(KittenReference[] kittens)
    {
        if (ControlsLocked || sheet == null || document == null)
            return;
        try
        {
            var current = sheet;
            var scene = document.Scene;
            var labels = document.Labels.Select(l => l with { ManualPlacement = true }).ToArray();
            // Resolve the asset on the game thread; only a path and detached data
            // enter the worker. No model asset is redistributed with the mod.
            string? path = kittens.Any(k => k.Kind == ScaleReferenceKind.Kitten) && scene.KittenModel == null ? ModLibrary.Get<Gltf2Reference>("KittenGlb").Source?.Value?.ModPath : null;
            renderKeepsDocument = true;
            composingLabels = true;
            progress = "Drawing scale references";
            StartRender(token =>
            {
                token.ThrowIfCancellationRequested();
                var model = scene.KittenModel;
                if (kittens.Any(k => k.Kind == ScaleReferenceKind.Kitten))
                    model ??= KittenModel.Load(path ?? throw new InvalidOperationException("KSA kitten body asset is unavailable."));
                var changed = scene.WithKittens([], model);
                changed = changed.WithKittens(kittens.Select(k => KittenReferences.Clamp(changed, k)).ToArray());
                return AnnotationPainting.Compose(current with
                {
                    Scene = changed
                }, labels, token) with
                {
                    Notice = "Scale reference updated. Drag it to move; Ctrl+Z undoes the edit."
                };
            });
        }
        catch (Exception e) { ReportFailure("Scale reference unavailable: " + e.Message); }
    }


    static void DrawKittenControls()
    {
        if (!ImGui.CollapsingHeader("Scale references"u8))
            return;
        if (sheet == null || document == null)
        {
            PrintUiText.Wrapped("Generate a PawPrint first.");
            return;
        }
        ImGui.BeginDisabled(ControlsLocked || !PrintIsCurrent);
        try
        {
            var kittens = document.Scene.Kittens;
            if (kittens.Length > 0)
            {
                int index = Array.FindIndex(kittens, k => k.Id == selectedKitten);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##Select kitten"u8, ref index, kittens.Select(k => $"{k.Kind} {k.Id} / {(DrawingPlacements.Names[k.View])}").ToArray(), kittens.Length) && index >= 0)
                {
                    SelectLabel(-1);
                    SelectDetail(-1);
                    selectedText = selectedTable = -1;
                    selectedKitten = kittens[index].Id;
                    kittenView = kittens[index].View;
                    kittenStyle = kittens[index].Outline ? 1 : 0;
                    referenceKind = (int)kittens[index].Kind;
                }
            }
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##Reference kind", ref referenceKind, new[] { "Kitten", "Banana (40 cm)" }, 2);
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##Kitten projection"u8, ref kittenView, DrawingPlacements.Names, DrawingPlacements.Names.Length);
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##Kitten style"u8, ref kittenStyle, new[] { "Solid silhouette", "Blueprint outline" }, 2);
            ImGui.BeginDisabled(kittens.Length >= 4);
            if (ImGui.Button(referenceKind == 0 ? "Add kitten" : "Add banana"))
            {
                int id = kittens.Select(k => k.Id).DefaultIfEmpty(0).Max() + 1;
                selectedKitten = id;
                var area = FrameLayout.Area(sheet.Options);
                BeginKitten(new(id, kittenView, area.X + 80 + kittens.Length * 120, area.Bottom - 160, kittenStyle == 1, (ScaleReferenceKind)referenceKind));
            }
            ImGui.EndDisabled();
            if (kittens.Any(k => k.Id == selectedKitten))
            {
                if (ImGui.Button("Apply reference style/view"))
                    QueueKittens(kittens.Select(k => k.Id == selectedKitten ? k with { View = kittenView, Outline = kittenStyle == 1, Kind = (ScaleReferenceKind)referenceKind, SourceView = null, Axes = null } : k).ToArray());
                if (ImGui.Button("Flip left / right"u8))
                    FlipReference(selectedKitten, true);
                ImGui.SameLine();
                if (ImGui.Button("Flip up / down"u8))
                    FlipReference(selectedKitten, false);
                if (ImGui.Button("Remove reference"))
                    DeleteReference(selectedKitten);
            }
            PrintUiText.Wrapped(referenceKind == 0 ? "Helmet-free kitten body at the main drawing scale." : "Upright 40 cm banana, tip to tip, at the main drawing scale.");
            PrintUiText.Wrapped("Drag to position. Right-click a view to place a matching kitten. Undo/Redo supported.");
        }
        finally { ImGui.EndDisabled(); }
    }


    static bool DrawKittenCanvas(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (!editable)
        {
            dragKitten = null;
            return false;
        }
        var scene = document!.Scene;
        void Outline(KittenReference kitten, uint color)
        {
            var b = KittenReferences.Bounds(scene, kitten);
            ImGui.GetWindowDrawList().AddRect(origin + new float2(b.X * factor, b.Y * factor), origin + new float2(b.Right * factor, b.Bottom * factor), color, 0, ImDrawFlags.None, 2);
        }
        if (dragKitten is { } dragging)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                dragKitten = null;
                return true;
            }
            var proposed = KittenReferences.Clamp(scene, dragging with
            {
                X = point.X - kittenGrab.X,
                Y = point.Y - kittenGrab.Y
            });
            var correction = CanvasMotion.Limit(KittenReferences.Bounds(scene, proposed), Vec.Zero, Options.Page);
            proposed = proposed with
            {
                X = proposed.X + correction.X,
                Y = proposed.Y + correction.Y
            };
            Outline(proposed, 0xFF8EDD8E);
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                dragKitten = null;
                if (proposed != dragging)
                    QueueKittens(scene.Kittens.Select(k => k.Id == dragging.Id ? proposed : k).ToArray());
            }
            return true;
        }
        var selected = scene.Kittens.FirstOrDefault(k => k.Id == selectedKitten);
        if (selected != null)
            Outline(selected, 0xFFFFDD99);
        if (!hovered || ControlsLocked || partMenuOpen)
            return false;
        var hit = scene.Kittens.FirstOrDefault(k => canvasTarget?.Id == k.Id);
        if (hit == null)
            return false;
        hoveredKitten = hit.Id;
        Outline(hit, 0xFFFFDD99);
        ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        PrintUiText.Tooltip(hit.Kind + " scale reference - drag to move; F to flip; right-click for options; Delete to remove");
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            referenceMenuId = hit.Id;
            ImGui.OpenPopup("Scale reference actions"u8);
            return true;
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            SelectDetail(-1);
            selectedText = selectedTable = -1;
            dragKitten = hit;
            selectedKitten = hit.Id;
            kittenView = hit.View;
            kittenStyle = hit.Outline ? 1 : 0;
            referenceKind = (int)hit.Kind;
            kittenGrab = point - new Vec(hit.X, hit.Y);
            SelectLabel(-1);
        }
        return true;
    }
}
