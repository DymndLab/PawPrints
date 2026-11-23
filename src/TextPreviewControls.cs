using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using System.Text;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    sealed record TextSurfaceImage(FreeTextBox Box, int Theme, int Width, int Height, float Scale, float Scroll);
    static TextSurfaceImage? textSurfaceImage;
    static PreviewTexture? textPreview;
    static float textScroll;
    static bool textMouseSelecting, textInputActive;
    static int textVisibleCursor = -1;

    static void CancelTextPreview()
    {
        if (textPreview != null)
            retiredGhosts.Add(textPreview);
        textPreview = null;
        textSurfaceImage = null;
        textScroll = 0;
        textMouseSelecting = textInputActive = false;
        textVisibleCursor = -1;
    }

    static void SetTextCaret(string text, int anchor, int cursor)
    {
        textStart = Encoding.UTF8.GetByteCount(text.AsSpan(0, Math.Clamp(anchor, 0, text.Length)));
        textEnd = textCursor = Encoding.UTF8.GetByteCount(text.AsSpan(0, Math.Clamp(cursor, 0, text.Length)));
        restoreTextSelection = focusText = true;
        textTypingOverride = false;
    }

    static void NavigateTextSurface(FreeTextBox draft, TextEditorLayout layout)
    {
        if (!textInputActive || ImGui.IsKeyDown(ImGuiKey.LeftCtrl) || ImGui.IsKeyDown(ImGuiKey.RightCtrl))
            return;
        int cursor = RichText.CharacterIndex(draft.Text, textCursor), target = cursor;
        var caret = layout.Caret(cursor);
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow))
            target = layout.Hit(new Vec(caret.X, layout.Top(Math.Max(0, layout.Row(cursor) - 1)) + .1f));
        else if (ImGui.IsKeyPressed(ImGuiKey.DownArrow))
            target = layout.Hit(new Vec(caret.X, layout.Top(Math.Min(layout.Lines.Length - 1, layout.Row(cursor) + 1)) + .1f));
        else if (ImGui.IsKeyPressed(ImGuiKey.Home))
            target = layout.Lines[layout.Row(cursor)].Start;
        else if (ImGui.IsKeyPressed(ImGuiKey.End))
            target = layout.Lines[layout.Row(cursor)].End;
        else
            return;
        bool extend = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        SetTextCaret(draft.Text, extend ? RichText.CharacterIndex(draft.Text, textStart) : target, target);
    }

    // ImGui retains native text input, clipboard and Unicode editing. Its plain-text
    // paint/hit testing is hidden; the visible surface owns formatted geometry.
    static unsafe FreeTextBox DrawEditableText(FreeTextBox draft, float height)
    {
        float width = Math.Max(160, ImGui.GetContentRegionAvail().X);
        if (warningNotice != null)
        {
            ImGui.Dummy(new float2(width, height));
            return draft;
        }
        var origin = ImGui.GetCursorScreenPos();
        var size = new float2(width, height);
        float scale = Math.Min(1, width / draft.Bounds.Width);
        var layout = new TextEditorLayout(draft);
        float visibleHeight = height / scale, maxScroll = Math.Max(0, layout.Height - visibleHeight);
        bool hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && ImGui.IsMouseHoveringRect(origin, origin + size);
        var mouse = ImGui.GetMousePos();
        Vec Point() => new((mouse.X - origin.X) / scale, (mouse.Y - origin.Y) / scale + textScroll);
        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.TextInput);
            if (ImGui.GetIO().MouseWheel != 0)
                textScroll = Math.Clamp(textScroll - ImGui.GetIO().MouseWheel * layout.LineHeight * 3, 0, maxScroll);
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            int cursor = layout.Hit(Point());
            bool extend = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
            int anchor = extend ? RichText.CharacterIndex(draft.Text, textStart) : cursor;
            if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            {
                (anchor, cursor) = TextEditorLayout.WordRange(draft.Text, cursor);
                textMouseSelecting = false;
            }
            else
                textMouseSelecting = true;
            SetTextCaret(draft.Text, anchor, cursor);
        }
        else if (textMouseSelecting && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (mouse.Y < origin.Y || mouse.Y > origin.Y + height)
                textScroll = Math.Clamp(textScroll + Math.Clamp((mouse.Y - origin.Y - height / 2) / height, -1, 1) * layout.LineHeight * 12 * ImGui.GetIO().DeltaTime, 0, maxScroll);
            SetTextCaret(draft.Text, RichText.CharacterIndex(draft.Text, textStart), layout.Hit(Point()));
        }
        else
            textMouseSelecting = false;
        NavigateTextSurface(draft, layout);
        var caretPoint = layout.Caret(RichText.CharacterIndex(draft.Text, textCursor));
        if (textVisibleCursor != textCursor)
        {
            textVisibleCursor = textCursor;
            if (caretPoint.Y < textScroll)
                textScroll = caretPoint.Y;
            else if (caretPoint.Y + layout.LineHeight > textScroll + visibleHeight)
                textScroll = caretPoint.Y + layout.LineHeight - visibleHeight;
        }
        textScroll = Math.Clamp(textScroll, 0, maxScroll);
        var wanted = new TextSurfaceImage(draft, theme, (int)width, (int)height, scale, textScroll);
        if (wanted != textSurfaceImage)
        {
            try
            {
                var pixels = FreeTextPainting.EditorPreview(draft, theme, wanted.Width, wanted.Height, scale, textScroll);
                var replacement = new PreviewTexture(pixels.Width, pixels.Height, pixels.Rgba);
                if (textPreview != null)
                    retiredGhosts.Add(textPreview);
                textPreview = replacement;
                textSurfaceImage = wanted;
                textError = "";
            }
            catch (Exception e) { textError = "Text rendering unavailable: " + e.Message; }
        }
        // Keep the formatted surface in its dialog's layer so tooltips and other
        // popups render above it. The native input child below has transparent
        // text, selection, frame and scrollbar paint, including its native caret.
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(origin, origin + size, true);
        try
        {
            if (textPreview?.Image is { } texture)
                draw.AddImage(texture, origin, origin + size);
            float2 Screen(float x, float y) => origin + new float2(x * scale, (y - textScroll) * scale);
            var range = TextRange(draft);
            foreach (var box in layout.Selection(range.Start, range.End))
                draw.AddRectFilled(Screen(box.X, box.Y), Screen(box.Right, box.Bottom), 0x665C9BFF);
            if (textInputActive || focusText)
            {
                uint ink = theme is 1 or 2 ? 0xFF303030u : 0xFFE5E5E5u;
                draw.AddLine(Screen(caretPoint.X, caretPoint.Y), Screen(caretPoint.X, caretPoint.Y + TypingSize(draft)), ink, 1.5f);
            }
            if (maxScroll > 0)
            {
                float thumb = Math.Max(14, height * visibleHeight / layout.Height);
                float top = (height - thumb) * textScroll / maxScroll;
                draw.AddRectFilled(origin + new float2(width - 4, top), origin + new float2(width - 1, top + thumb), 0x88888888);
            }
        }
        finally { draw.PopClipRect(); }
        int oldStart = textStart, oldEnd = textEnd, oldCursor = textCursor;
        var rangeBefore = TextRange(draft);
        var insertedStyle = TypingStyle(draft);
        int insertedSize = TypingSize(draft);
        bool restoring = restoreTextSelection;
        // Keep the native child active: zero window alpha makes ImGui skip it.
        // Hide only its paint, retaining keyboard, clipboard and Unicode support.
        ImGui.PushStyleColor(ImGuiCol.Text, 0u);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.Border, 0u);
        ImGui.PushStyleColor(ImGuiCol.TextSelectedBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, 0u);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, 0u);
        try
        {
            bool requestedFocus = focusText;
            if (focusText)
            {
                ImGui.SetKeyboardFocusHere();
                focusText = false;
            }
            // ImGui #8928: AllowTabInput blocks API activation in the game's native build.
            // Enable tabs only after focus is acquired, not during the focus request.
            textCallbackActive = false;
            ImGui.InputTextMultiline("##Text keyboard"u8, textContent, size,
                ImGuiInputTextFlags.CallbackAlways | ImGuiInputTextFlags.NoUndoRedo |
                (textInputActive && !requestedFocus ? ImGuiInputTextFlags.AllowTabInput : ImGuiInputTextFlags.None), textSelectionCallback);
            textInputActive = textCallbackActive;

        }
        finally
        {
            ImGui.PopStyleColor(8);
        }
        textContent.EvaluateLength();
        string content = textContent.ToString();
        var changed = RichText.Edit(draft, content, rangeBefore.Start, rangeBefore.End, RichText.CharacterIndex(content, textCursor), insertedStyle, insertedSize);
        if (changed != draft)
            RememberText(draft, oldStart, oldEnd, oldCursor);
        else if (!restoring && (oldStart != textStart || oldEnd != textEnd || oldCursor != textCursor))
            textTypingOverride = false;
        return changed;
    }
}
