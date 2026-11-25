using Blueprint.Rendering;
using Brutal.ImGuiApi;
using System.Text;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static int textStart, textEnd, textCursor;
    static bool restoreTextSelection, textTypingOverride;
    static string? pendingEditorText;
    static bool textCallbackActive;
    static FreeTextStyle textTypingStyle;
    static int textTypingSize;

    sealed record TextEditState(FreeTextBox Box, int Start, int End, int Cursor, FreeTextStyle Typing, bool Override, int Size);
    static readonly List<TextEditState> textUndo = [], textRedo = [];
    static unsafe readonly ImGuiInputTextCallback textSelectionCallback = CaptureTextSelection;

    static void ResetTextEditor(FreeTextBox box)
    {
        textStart = textEnd = textCursor = 0;
        restoreTextSelection = true;
        pendingEditorText = null;
        textTypingStyle = RichText.Default(box);
        textTypingOverride = false;
        textTypingSize = box.FontSize;
        textUndo.Clear();
        textRedo.Clear();
    }

    static unsafe int CaptureTextSelection(ImGuiInputTextCallbackData* data)
    {
        textCallbackActive = true;
        if (pendingEditorText is { } text)
        {
            int bytes = Encoding.UTF8.GetByteCount(text);
            if (bytes < data->BufSize)
            {
                var buffer = new Span<byte>((byte*)data->Buf, data->BufSize);
                Encoding.UTF8.GetBytes(text, buffer);
                buffer[bytes] = 0;
                data->BufTextLen = bytes;
                data->BufDirty = true;
            }
            pendingEditorText = null;
        }
        if (restoreTextSelection)
        {
            data->SelectionStart = Math.Clamp(textStart, 0, data->BufTextLen);
            data->SelectionEnd = Math.Clamp(textEnd, 0, data->BufTextLen);
            data->CursorPos = Math.Clamp(textCursor, 0, data->BufTextLen);
            restoreTextSelection = false;
        }
        // Native insert/paste can leave an empty selection at the pre-insert
        // position. A collapsed selection must follow the actual caret so the
        // next Shift+Home/End or Shift-click anchors at the displayed position.
        textCursor = data->CursorPos;
        textStart = data->SelectionStart == data->SelectionEnd ? textCursor : data->SelectionStart;
        textEnd = data->SelectionStart == data->SelectionEnd ? textCursor : data->SelectionEnd;
        return 0;
    }

    static (int Start, int End) TextRange(FreeTextBox box)
        => (RichText.CharacterIndex(box.Text, Math.Min(textStart, textEnd)), RichText.CharacterIndex(box.Text, Math.Max(textStart, textEnd)));

    static FreeTextStyle TypingStyle(FreeTextBox box)
    {
        if (textTypingOverride)
            return textTypingStyle;
        var range = TextRange(box);
        var styles = RichText.Styles(box);
        int index = range.End > range.Start ? range.Start : Math.Max(0, RichText.CharacterIndex(box.Text, textCursor) - 1);
        return styles.Length == 0 ? RichText.Default(box) : styles[Math.Min(index, styles.Length - 1)];
    }

    static int TypingSize(FreeTextBox box)
    {
        if (textTypingOverride)
            return textTypingSize;
        var range = TextRange(box);
        var sizes = RichText.Sizes(box);
        int index = range.End > range.Start ? range.Start : Math.Max(0, RichText.CharacterIndex(box.Text, textCursor) - 1);
        return sizes.Length == 0 ? box.FontSize : sizes[Math.Min(index, sizes.Length - 1)];
    }

    static FreeTextBox ResizeTextSelection(FreeTextBox box, int size)
    {
        RememberText(box, textStart, textEnd, textCursor);
        var range = TextRange(box);
        if (range.End > range.Start)
            box = RichText.Resize(box, range.Start, range.End, size);
        else
        {
            textTypingStyle = TypingStyle(box);
            textTypingSize = size;
            textTypingOverride = true;
        }
        focusText = restoreTextSelection = true;
        return box;
    }

    static void RememberText(FreeTextBox box, int start, int end, int cursor)
    {
        textUndo.Add(new(box, start, end, cursor, textTypingStyle, textTypingOverride, textTypingSize));
        if (textUndo.Count > 64)
            textUndo.RemoveAt(0);
        textRedo.Clear();
    }

    static FreeTextBox ToggleTextStyle(FreeTextBox box, FreeTextStyle flag)
    {
        RememberText(box, textStart, textEnd, textCursor);
        var range = TextRange(box);
        if (range.End > range.Start)
            box = RichText.Toggle(box, range.Start, range.End, flag);
        else
        {
            textTypingSize = TypingSize(box);
            textTypingStyle = TypingStyle(box) ^ flag;
            textTypingOverride = true;
        }
        focusText = restoreTextSelection = true;
        return box;
    }

    static void TextEditorHistory()
    {
        if (textDraft == null || !ImGui.IsKeyDown(ImGuiKey.LeftCtrl) && !ImGui.IsKeyDown(ImGuiKey.RightCtrl))
            return;
        if (textInputActive)
        {
            foreach (var pair in new[] { (ImGuiKey.B, FreeTextStyle.Bold), (ImGuiKey.I, FreeTextStyle.Italic), (ImGuiKey.U, FreeTextStyle.Underline) })
                if (ImGui.IsKeyPressed(pair.Item1, false))
                    textDraft = ToggleTextStyle(textDraft, pair.Item2);
        }
        bool redo = ImGui.IsKeyPressed(ImGuiKey.Y) || ImGui.IsKeyPressed(ImGuiKey.Z) && (ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift));
        if (!redo && !ImGui.IsKeyPressed(ImGuiKey.Z))
            return;
        var from = redo ? textRedo : textUndo;
        var to = redo ? textUndo : textRedo;
        if (from.Count == 0)
            return;
        to.Add(new(textDraft, textStart, textEnd, textCursor, textTypingStyle, textTypingOverride, textTypingSize));
        var state = from[^1];
        from.RemoveAt(from.Count - 1);
        textDraft = state.Box;
        textStart = state.Start;
        textEnd = state.End;
        textCursor = state.Cursor;
        textTypingStyle = state.Typing;
        textTypingOverride = state.Override;
        textTypingSize = state.Size;
        textContent.SetValue(state.Box.Text.AsSpan());
        pendingEditorText = state.Box.Text;
        focusText = restoreTextSelection = true;
    }
}
