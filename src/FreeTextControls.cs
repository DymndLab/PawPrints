using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static FreeTextBox? textDraft, dragText;
    static int selectedText = -1, hoveredText = -1;
    static bool focusText, resizeText, placingText;
    static Vec? textBoxStart;
    static int textSizeInput;
    static bool textSizeEditing;
    static Vec textPress;
    static TextPlacementSpace? textPlacementSpace;
    static string textError = "";
    static readonly ImInputString textContent = new(8192);


    static void BeginTextBox(FreeTextBox? existing = null)
    {
        if (document == null || !CanEditAnnotations)
            return;
        if (existing == null && document.Scene.FreeText.Length >= FreeTextPainting.Limit)
        {
            ReportFailure("Up to 64 text boxes per print.");
            return;
        }
        if (existing == null)
        {
            pickingPartTool = false;
            placingText = true;
            textBoxStart = null;
            textPlacementSpace = TextBoxPlacement.Space(document.Scene, document.Labels, document.Routes);
            status = "Drag a box on the paper, then enter and format its text. Right-click cancels.";
            return;
        }
        textDraft = existing;
        CancelTextPreview();
        ResetTextEditor(textDraft);
        textContent.SetValue(textDraft.Text.AsSpan());
        focusText = true;
        textError = "";
        textSizeEditing = false;
    }


    static void QueueTextBoxes(FreeTextBox[] boxes)
    {
        if (busy || sheet == null || document == null)
            return;
        var source = sheet with
        {
            Scene = document.Scene.WithFreeText(boxes)
        };
        var labels = document.Labels;
        composingLabels = true;
        renderKeepsDocument = true;
        renderAdoptsHistory = false;
        progress = "Updating text";
        StartRender(token => AnnotationPainting.Compose(source, labels, token) with { Notice = "Text updated. Drag to move; double-click or right-click to edit." });
    }


    static void PutTextBox(FreeTextBox box)
    {
        SelectLabel(-1);
        SelectDetail(-1);
        selectedTable = selectedSpec = -1;
        selectedText = box.Id;
        var boxes = document!.Scene.FreeText;
        QueueTextBoxes(boxes.Any(b => b.Id == box.Id) ? boxes.Select(b => b.Id == box.Id ? box : b).ToArray() : [.. boxes, box]);
    }


    static void DeleteTextBox(int id)
    {
        if (!CanEditAnnotations || document == null)
            return;
        QueueTextBoxes(document.Scene.FreeText.Where(b => b.Id != id).ToArray());
        selectedText = -1;
    }


    static unsafe void DrawTextDialog()
    {
        if (textDraft != null && !ImGui.IsPopupOpen("Text box"u8))
            ImGui.OpenPopup("Text box"u8);
        // The parent is PawPrints' KSA viewport, not the desktop's main viewport.
        var parentPosition = ImGui.GetWindowPos();
        var parentSize = ImGui.GetWindowSize();
        float dialogWidth = Math.Min(620, parentSize.X - 32);
        ImGui.SetNextWindowPos(parentPosition + parentSize * .5f, ImGuiCond.Always, new float2(.5f, .5f));
        ImGui.SetNextWindowSize(new float2(dialogWidth, 0), ImGuiCond.Always);
        if (!ImGui.BeginPopupModal("Text box"u8, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove))
            return;
        try
        {
            partMenuOpen = true;
            TextEditorHistory();
            if (textDraft is not { } draft)
            {
                ImGui.CloseCurrentPopup();
                return;
            }
            void Toggle(string name, FreeTextStyle flag)
            {
                var range = TextRange(draft);
                bool active = range.End > range.Start ? RichText.Has(draft, range.Start, range.End, flag) : (TypingStyle(draft) & flag) != 0;
                ImGui.PushStyleColor(ImGuiCol.Button, active ? 0xFF80512Eu : 0xFF303030u);
                if (ImGui.Button(name))
                    draft = ToggleTextStyle(draft, flag);
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetNextWindowPos(ImGui.GetMousePos() - new float2(0, 35));
                    PrintUiText.Tooltip("Applies to selected text, or to what you type next.");
                }
            }
            Toggle("B", FreeTextStyle.Bold);
            ImGui.SameLine();
            Toggle("I", FreeTextStyle.Italic);
            ImGui.SameLine();
            Toggle("U", FreeTextStyle.Underline);
            ImGui.SameLine();
            ImGui.Text("Size");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(95);
            if (!textSizeEditing)
                textSizeInput = TypingSize(draft);
            bool sizeChanged = ImGui.InputInt("##Text size"u8, ref textSizeInput);
            textSizeEditing = ImGui.IsItemActive();
            if (ImGui.IsItemDeactivatedAfterEdit() || sizeChanged && !textSizeEditing)
                draft = ResizeTextSelection(draft, Math.Clamp(textSizeInput, 12, 144));
            ImGui.SameLine();
            int align = draft.Alignment;
            if (ImGui.RadioButton("Left"u8, align == 0))
                align = 0;
            ImGui.SameLine();
            if (ImGui.RadioButton("Center"u8, align == 1))
                align = 1;
            ImGui.SameLine();
            if (ImGui.RadioButton("Right"u8, align == 2))
                align = 2;
            bool outline = draft.Outline;
            ImGui.Checkbox("Outline", ref outline);
            PrintUiStyle.Help("Draw a border around the text box. Snapping and spacing always use the full box, even without an outline.");
            var sized = draft with
            {
                Alignment = align,
                Outline = outline
            };
            if (sized != draft)
            {
                RememberText(draft, textStart, textEnd, textCursor);
                if (align != draft.Alignment)
                    focusText = restoreTextSelection = true;
            }
            textDraft = DrawEditableText(sized, Math.Clamp(parentSize.Y - 200, 100, 360));
            ImGui.BeginDisabled(string.IsNullOrWhiteSpace(textDraft.Text));
            bool accept = ImGui.Button("Accept"u8);
            ImGui.EndDisabled();
            ImGui.SameLine();
            bool cancel = ImGui.Button("Cancel"u8);
            if (accept)
            {
                try
                {
                    var box = FreeTextPainting.Fit(textDraft, Options);
                    TextBoxPlacement.RequireClear(TextBoxPlacement.Space(document!.Scene, document.Labels, document.Routes, box.Id), box.Bounds);
                    textDraft = null;
                    ImGui.CloseCurrentPopup();
                    PutTextBox(box);
                }
                catch (InvalidOperationException e) { ReportFailure(e.Message); }
            }
            if (textError.Length > 0)
                PrintUiText.Wrapped(textError);
            if (cancel)
            {
                textDraft = null;
                textError = "";
                ImGui.CloseCurrentPopup();
            }
        }
        finally { DrawWarningDialog(true); ImGui.EndPopup(); }
    }


    static void CancelTextPlacement()
    {
        placingText = false;
        textBoxStart = null;
        textPlacementSpace = null;
        status = "Text box placement cancelled.";
    }


    static bool DrawTextPlacement(Vec point, float factor, float2 origin, bool hovered)
    {
        if (!placingText)
            return false;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CancelTextPlacement();
            return true;
        }
        if (hovered)
            RequestPrecisionCursor();
        if (textBoxStart == null)
        {
            const string hint = "Drag on the paper to draw a text box";
            var size = ImGui.CalcTextSize(hint);
            var startHint = ImGui.GetWindowPos() + new float2(12, 12);
            var draw = ImGui.GetWindowDrawList();
            draw.AddRectFilled(startHint, startHint + size + new float2(16, 12), 0xF0292521);
            draw.AddText(startHint + new float2(8, 6), 0xFF9FDEFF, hint);
        }
        var page = Options.Page;
        point = new(Math.Clamp(point.X, 0, page.Width), Math.Clamp(point.Y, 0, page.Height));
        if (snapPanels && textPlacementSpace != null)
            point = textPlacementSpace.SnapCorner(point, 10 / Math.Max(.05f, factor));
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            textBoxStart = point;
        if (textBoxStart is not { } start)
            return true;
        var box = new Box(Math.Min(start.X, point.X), Math.Min(start.Y, point.Y), Math.Abs(point.X - start.X), Math.Abs(point.Y - start.Y));
        bool clear = textPlacementSpace?.Clear(box) == true;
        ImGui.GetWindowDrawList().AddRect(origin + new float2(box.X * factor, box.Y * factor), origin + new float2(box.Right * factor, box.Bottom * factor), clear ? 0xFF8EDD8Eu : 0xFF7777FFu, 0, ImDrawFlags.None, 2);
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            textBoxStart = null;
            if (!clear)
            {
                ReportFailure("Draw the text box on clear paper, without overlapping another item.");
                placingText = false;
                return true;
            }
            if (box.Width < 160 || box.Height < 40)
            {
                ReportFailure("Draw a larger text box (at least 160 by 40 print pixels).");
                placingText = false;
                return true;
            }
            placingText = false;
            BeginTextBox(new(document!.Scene.FreeText.Select(b => b.Id).DefaultIfEmpty(0).Max() + 1, "", box));
        }
        return true;
    }

    static bool DrawTextCanvas(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        hoveredText = -1;
        if (!editable || document == null)
            return false;
        void Outline(Box b, uint color) => ImGui.GetWindowDrawList().AddRect(origin + new float2(b.X * factor, b.Y * factor), origin + new float2(b.Right * factor, b.Bottom * factor), color, 0, ImDrawFlags.None, 2);
        if (dragText is { } dragging)
        {
            ImGui.SetMouseCursor(resizeText ? ImGuiMouseCursor.ResizeNWSE : ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                dragText = null;
                selectedText = -1;
                return true;
            }
            var delta = point - textPress;
            var bounds = resizeText ? dragging.Bounds with
            {
                Width = dragging.Bounds.Width + delta.X,
                Height = dragging.Bounds.Height + delta.Y
            } : dragging.Bounds with
            {
                X = dragging.Bounds.X + delta.X,
                Y = dragging.Bounds.Y + delta.Y
            };
            FreeTextBox candidate = dragging;
            bool valid = true;
            try
            {
                candidate = FreeTextPainting.Fit(dragging with
                {
                    Bounds = bounds
                }, Options);
                if (textPlacementSpace != null)
                {
                    if (snapPanels)
                        candidate = textPlacementSpace.Snap(candidate, resizeText, 10 / Math.Max(.05f, factor));
                    valid = textPlacementSpace.Clear(candidate.Bounds);
                }
            }
            catch (InvalidOperationException) { valid = false; }
            if (!resizeText)
                candidate = candidate with
                {
                    Bounds = CanvasMotion.KeepOnPage(candidate.Bounds, Options)
                };
            Outline(candidate.Bounds, valid ? 0xFF8EDD8Eu : 0xFF7777FFu);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                dragText = null;
                if (valid && candidate != dragging)
                    PutTextBox(candidate);
                else if (!valid)
                    ReportFailure("Text box unchanged. Keep its full rectangle on clear paper.");
            }
            return true;
        }
        var hit = hovered ? document.Scene.FreeText.FirstOrDefault(b => canvasTarget?.Id == b.Id) : null;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && hit == null)
            selectedText = -1;
        foreach (var b in document.Scene.FreeText.Where(b => b.Id == selectedText || b == hit))
        {
            Outline(b.Bounds, 0xFF88D8FF);
            PanelGrip(b.Bounds, factor, origin);
        }
        if (hit == null || ControlsLocked || partMenuOpen)
            return false;
        hoveredText = hit.Id;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            BeginTextBox(hit);
            return true;
        }
        bool corner = Vec.Distance(point, new(hit.Bounds.Right, hit.Bounds.Bottom)) * factor < 18;
        ImGui.SetMouseCursor(corner ? ImGuiMouseCursor.ResizeNWSE : ImGuiMouseCursor.ResizeAll);
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            SelectLabel(-1);
            SelectDetail(-1);
            selectedTable = selectedSpec = -1;
            selectedText = hit.Id;
            dragText = hit;
            textPlacementSpace = TextBoxPlacement.Space(document.Scene, document.Labels, document.Routes, hit.Id);
            textPress = point;
            resizeText = corner;
        }
        return true;
    }
}
