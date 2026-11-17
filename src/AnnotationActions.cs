using Blueprint.Rendering;
using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using KSA;
using Vec = System.Numerics.Vector2;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool annotationKeysActive, canvasHovered, spaceHeld, panning;
    static int hoveredLabel = -1, requestedEdit, requestedDelete = -1, requestedViewDelete = -1;
    static Vec panMouse, panScroll;
    static RenderScene? partMenuScene;
    static int partMenuView, partMenuPart;
    static bool partMenuOpen, focusFlagnoteText;
    static FlagnotePlacement? flagnotePlacement;
    static RenderScene? flagnoteScene;
    static string flagnoteError = "";
    static bool CanEditAnnotations => !placingText && !exportSetupOpen && warningNotice == null && !viewSizeDialog && textDraft == null && dragText == null && !GhostActive && viewDraft == null && dragView == null && specDraft == null && dragSpec == null && !poseDialog && !busy && document != null && sheet?.Options == Options && dragKitten == null && dragHeight == null && dragLabel == null && pendingLabel == null && flagnotePlacement == null && !panning && !DetailGesture;


    static void DeleteAnnotation(int id)
    {
        if (!CanEditAnnotations || document == null || !document.Labels.Any(l => l.Id == id))
            return;
        if (document.Delete(id, out var reason))
        {
            SelectLabel(-1);
            Repaint();
        }
        else
            ReportFailure(reason);
    }


    static void UndoAnnotations(bool redo)
    {
        if (!CanEditAnnotations || document == null)
            return;
        if (redo && !document.CanRedo || !redo && !document.CanUndo)
            return;
        if (redo)
            document.Redo();
        else
            document.Undo();
        SelectLabel(-1);
        selectedText = -1;
        selectedCanvasView = -1;
        Repaint();
    }


    static bool AnnotationKey(GlfwKey key, GlfwKeyAction action, GlfwModifier mods)
    {
        if (key == GlfwKey.Space && spaceHeld && action == GlfwKeyAction.Release)
        {
            spaceHeld = false;
            return true;
        }
        if (!open || !annotationKeysActive || !ReferenceEquals(sourceEditor, KSA.Program.Editor) || KSA.Program.Editor == null || KSA.Program.ConsoleWindow.IsOpen || GameSettings.Show || Popup.AnyOpen)
            return false;
        if (key == GlfwKey.Space && canvasHovered && !partMenuOpen)
        {
            spaceHeld = action != GlfwKeyAction.Release;
            return true;
        }
        int edit = AnnotationShortcuts.Match(key == GlfwKey.Delete, key == GlfwKey.Z, key == GlfwKey.Y, (int)mods, annotationKeysActive, ImGui.GetIO().WantTextInput, key == GlfwKey.F);
        if (edit == 0)
            return false;
        if (action == GlfwKeyAction.Press && CanEditAnnotations && !partMenuOpen)
        {
            requestedEdit = edit;
            requestedViewDelete = selectedCanvasView;
            requestedReference = selectedCanvasView >= 0 ? -1 : hoveredKitten >= 0 ? hoveredKitten : hoveredText < 0 && hoveredSpec < 0 && hoveredLabel < 0 ? selectedKitten : -1;
            requestedDelete = selectedCanvasView >= 0 ? -1 : hoveredText >= 0 ? -2000 - hoveredText : hoveredSpec >= 0 ? -1000 - hoveredSpec : hoveredLabel >= 0 ? hoveredLabel : selectedText >= 0 ? -2000 - selectedText : selectedSpec >= 0 ? -1000 - selectedSpec : selectedLabel;
        }
        return true;
    }


    static void BeginPartPlacement(int viewId, int partId, bool flagnote, bool held, Vec point)
    {
        if (document == null)
            return;
        if (flagnote)
        {
            BeginFlagnote(viewId, partId, null);
            if (held)
                flagnotePlacement?.PickStart(point);
            return;
        }
        var existing = !flagnote ? document.Labels.FirstOrDefault(l => !l.FlagNote && l.ViewId == viewId && l.PartId == partId) : null;
        if (existing != null)
        {
            SelectLabel(existing.Id);
            if (held)
            {
                var mouse = ImGui.GetMousePos();
                pressMouse = new(mouse.X, mouse.Y);
                dragLabel = existing;
                draggingAttachment = false;
                dragOffset = new(-16, -16);
            }
            else
                status = "Existing part label selected. Drag its text or either leader handle to move it.";
            return;
        }
        string? note = flagnote ? AnnotationText.Clean(noteText.ToString()) : null;
        if (note != null && note.Length == 0)
        {
            status = "Enter the flagnote text, then drag from a part.";
            return;
        }
        pendingLabel = document.Draft(viewId, partId, out var reason, (LeaderLine)defaultLine, (LeaderTip)defaultTip, note);
        var startMouse = ImGui.GetMousePos();
        placementGesture = pendingLabel != null ? new(new(startMouse.X, startMouse.Y), held) : null;
        status = pendingLabel != null ? "Move onto the paper and click to place. Right-click cancels." : reason;
    }


    static void BeginFlagnote(int viewId, int partId, string? preset)
    {
        if (document == null)
            return;
        var label = document.Draft(viewId, partId, out var reason, (LeaderLine)defaultLine, (LeaderTip)defaultTip, preset ?? "Custom flagnote");
        if (label == null)
        {
            ReportFailure(reason);
            return;
        }
        flagnotePlacement = new(label, preset == null, document.Scene.Width, document.Scene.Height);
        flagnoteScene = document.Scene;
        flagnoteError = "";
        if (preset == null)
            noteText.SetValue("".AsSpan());
        status = "Click the leader start, then its end. Right-click cancels.";
    }


    static bool CommitFlagnote()
    {
        if (document == null || flagnotePlacement?.Stage != FlagnoteStage.Ready || !ReferenceEquals(flagnoteScene, document.Scene))
            return false;
        var label = flagnotePlacement.Label;
        if (!document.Apply([.. document.Labels, label], out var reason))
        {
            ReportFailure(reason);
            return false;
        }
        CancelPlacement();
        SelectLabel(label.Id);
        Repaint();
        return true;
    }


    static bool DrawPartMenu(bool hovered, Vec point, bool editable)
    {
        partMenuOpen = false;
        if (editable && hovered && dragLabel == null && pendingLabel == null && flagnotePlacement == null && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && document!.Scene.Pick(point) is { } target)
        {
            partMenuScene = document.Scene;
            partMenuView = target.View;
            partMenuPart = target.Part;
            partMenuNode = document.Scene.PickNode(point);
            ImGui.OpenPopup("Part actions"u8);
        }
        bool menuDrawn = false;
        if (ImGui.BeginPopup("Part actions"u8))
        {
            menuDrawn = partMenuOpen = true;
            if (!editable || !ReferenceEquals(partMenuScene, document?.Scene))
                ImGui.CloseCurrentPopup();
            else
            {
                ImGui.Text(document!.Scene.Parts.Single(p => p.Id == partMenuPart).Name);
                ImGui.Separator();
                if (ImGui.MenuItem("Adjust Scale"u8))
                {
                    BeginViewSize(partMenuView);
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Adjust Pose"u8, enabled: PoseChoices(partMenuNode, partMenuPart).Length > 0))
                {
                    BeginPose(partMenuNode, partMenuPart, partMenuView);
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Add label"u8))
                    BeginPartPlacement(partMenuView, partMenuPart, false, false, point);
                if (ImGui.BeginMenu("Add flagnote"u8))
                {
                    if (ImGui.MenuItem("Custom..."u8))
                        BeginFlagnote(partMenuView, partMenuPart, null);
                    if (ImGui.MenuItem("Remove before flight"u8))
                        BeginFlagnote(partMenuView, partMenuPart, "REMOVE BEFORE FLIGHT");
                    ImGui.EndMenu();
                }
                if (flagnotePlacement != null)
                    ImGui.CloseCurrentPopup();
                if (ImGui.MenuItem("Place kitten for scale"u8, enabled: document!.Scene.Kittens.Length < 4 && document.Scene.Views.Any(v => v.Id == partMenuView && v.Frame != null)))
                {
                    BeginKittenFromView(partMenuView);
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Add Isometric view"u8))
                {
                    BeginIsometricView();
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Add exploded detail view"u8, enabled: partMenuNode.HasValue))
                {
                    BeginDetail(partMenuNode);
                    ImGui.CloseCurrentPopup();
                }
                if (ImGui.MenuItem("Add engine specs"u8, enabled: EngineOwner(partMenuPart) != null))
                {
                    BeginSpec(partMenuPart, partMenuView);
                    ImGui.CloseCurrentPopup();
                }
                if (partMenuView < 1000)
                {
                    ImGui.Separator();
                    if (ImGui.MenuItem("Project view..."u8))
                    {
                        BeginView(partMenuView);
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.MenuItem("Delete this view and its notes"u8))
                    {
                        DeleteDrawingView(partMenuView);
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            ImGui.EndPopup();
        }
        return menuDrawn; // Menu selection must never also place the draft.
    }


    static void DrawFlagnoteDialog()
    {
        // Own the modal at the export window, outside the scrollable canvas and
        // its navigation early returns. Text is a persistent placement stage,
        // not a one-frame notification: recover if native focus closes a popup.
        bool needsText = flagnotePlacement is { Custom: true, Stage: FlagnoteStage.Text or FlagnoteStage.Ready };
        bool editable = !busy && sheet?.Options == Options && document != null;
        if (needsText && (!editable || !ReferenceEquals(flagnoteScene, document?.Scene)))
        {
            CancelPlacement();
            needsText = false;
        }
        if (needsText && !ImGui.IsPopupOpen("Create flagnote"u8))
        {
            focusFlagnoteText = true;
            ImGui.OpenPopup("Create flagnote"u8);
        }
        if (ImGui.BeginPopupModal("Create flagnote"u8, ImGuiWindowFlags.AlwaysAutoResize))
        {
            partMenuOpen = true;
            if (!needsText || flagnotePlacement == null)
                ImGui.CloseCurrentPopup();
            else
            {
                ImGui.Text("Attach to: " + document!.Scene.Parts.Single(p => p.Id == flagnotePlacement.Label.PartId).Name);
                ImGui.Text("Flagnote text");
                if (ImGui.IsItemHovered())
                    PrintUiText.Tooltip("Leader positioned. Enter custom text to finish.");
                ImGui.SetNextItemWidth(480);
                if (focusFlagnoteText)
                {
                    ImGui.SetKeyboardFocusHere();
                    focusFlagnoteText = false;
                }
                bool entered = ImGui.InputText("##Custom flagnote"u8, noteText, ImGuiInputTextFlags.EnterReturnsTrue);
                // Brutal's wrapper refreshes Length only when InputText returns
                // true. EnterReturnsTrue delays that until Enter, leaving the
                // visible text invisible to validation/button clicks meanwhile.
                noteText.EvaluateLength();
                if (ImGui.IsItemHovered())
                    PrintUiText.Tooltip("Leader positioned. Enter custom text to finish.");
                bool valid = AnnotationText.Clean(noteText.ToString()).Length > 0;
                if (entered && !valid)
                    flagnoteError = "Enter custom text to finish.";
                else if (valid && flagnoteError == "Enter custom text to finish.")
                    flagnoteError = "";
                ImGui.BeginDisabled(!valid);
                bool confirm = ImGui.Button("Confirm flagnote"u8) || entered;
                ImGui.EndDisabled();
                ImGui.SameLine();
                bool cancel = ImGui.Button("Cancel"u8);
                if (confirm && valid)
                {
                    if (flagnotePlacement.SetText(noteText.ToString()) && CommitFlagnote())
                        ImGui.CloseCurrentPopup();
                }
                if (flagnoteError.Length > 0)
                    PrintUiText.Wrapped(flagnoteError);
                if (cancel)
                {
                    CancelPlacement();
                    ImGui.CloseCurrentPopup();
                    status = "Flagnote cancelled.";
                }
            }
            ImGui.EndPopup();
        }
    }


    static void DrawAnnotationToolbar()
    {
        if (requestedEdit != 0)
        {
            int edit = requestedEdit;
            requestedEdit = 0;
            if (edit == 1)
            {
                if (requestedReference >= 0)
                    DeleteReference(requestedReference);
                else if (requestedDelete <= -2000)
                    DeleteTextBox(-2000 - requestedDelete);
                else if (requestedDelete <= -1000)
                    DeleteSpec(-1000 - requestedDelete);
                else if (requestedDelete >= 0)
                    DeleteAnnotation(requestedDelete);
                else if (requestedViewDelete >= 0)
                    DeleteDrawingView(requestedViewDelete);
                else
                    DeleteDetail();
            }
            else if (edit == 4)
                FlipReference(requestedReference, true);
            else
                UndoAnnotations(edit == 3);
        }
        ImGui.BeginDisabled(!CanEditAnnotations);
        ImGui.BeginDisabled(selectedLabel < 0 && selectedDetail < 0 && selectedSpec < 0 && selectedText < 0 && selectedKitten < 0 && selectedCanvasView < 0);
        if (ImGui.Button(selectedCanvasView >= 0 ? "Delete view" : "Delete selected"))
        {
            if (selectedKitten >= 0)
                DeleteReference(selectedKitten);
            else if (selectedText >= 0)
                DeleteTextBox(selectedText);
            else if (selectedSpec >= 0)
                DeleteSpec(selectedSpec);
            else if (selectedLabel >= 0)
                DeleteAnnotation(selectedLabel);
            else if (selectedCanvasView >= 0)
                DeleteDrawingView(selectedCanvasView);
            else
                DeleteDetail();
        }
        ImGui.EndDisabled();
        if (ImGui.GetContentRegionAvail().X > 250)
            ImGui.SameLine();
        ImGui.BeginDisabled(document?.CanUndo != true);
        if (ImGui.Button("Undo"u8))
            UndoAnnotations(false);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(document?.CanRedo != true);
        if (ImGui.Button("Redo"u8))
            UndoAnnotations(true);
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(dragLabel != null || pendingLabel != null || flagnotePlacement != null || panning);
        if (ImGui.Button("Fit page"u8))
        {
            zoom = 1;
            previewFit = 0;
        }
        if (ImGui.GetContentRegionAvail().X > 120)
            ImGui.SameLine();
        if (ImGui.Button("Actual pixels"u8))
        {
            zoom = 1;
            previewFit = 2;
        }
        ImGui.EndDisabled();
        if (flagnotePlacement != null)
        {
            PrintUiText.Wrapped(flagnotePlacement.Stage == FlagnoteStage.Start ? "Flagnote: click the leader START point. Right-click cancels." : flagnotePlacement.Stage == FlagnoteStage.End ? (flagnotePlacement.Custom ? "Flagnote: click the END / label position. Custom text comes next. Right-click cancels." : "Flagnote: click the END / label position to finish. Right-click cancels.") : "Flagnote: enter custom text to finish.");
            if (ImGui.Button("Cancel placement##Flag"u8))
            {
                CancelPlacement();
                status = "Placement cancelled.";
            }
        }
        else if (pendingLabel != null)
        {
            PrintUiText.Wrapped(pendingLabel.FlagNote ? "Placing flagnote: click on the paper to position it. Right-click cancels." : "Placing part label: click on the paper to position it. Right-click cancels.");
            if (ImGui.Button("Cancel placement##Toolbar"u8))
            {
                CancelPlacement();
                status = "Placement cancelled.";
            }
        }
        else if (pendingDetail != null || placeKind == 2)
        {
            PrintUiText.Wrapped(pendingDetail != null ? "Click to place the detail frame. Right-click cancels." : "Click a part to choose its assembly scope. Drag detail headers to move frames; drag groups within a detail to separate them.");
            // Keep toolbar height stable throughout frame/group drags. A new
            // button row here shifts paper under a stationary screen pointer.
        }
        else if (placeKind == 3)
            PrintUiText.Wrapped("Click an engine to configure and place its specification sheet.");
        else if (placeKind == 1)
        {
            PrintUiText.Wrapped("Custom flagnote: click a part to start the leader, click its end, then enter text. Right-click a part for presets.");
        }
        else
            PrintUiText.Wrapped("Click a view caption to select it; Del deletes it. Drag its caption to move. Right-click parts for notes. Wheel: zoom. Space + drag: pan. Ctrl+Z / Ctrl+Y: undo/redo.");
    }
}
