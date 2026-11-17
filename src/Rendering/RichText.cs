using System.Text;
namespace Blueprint.Rendering;

[Flags]
enum FreeTextStyle
{
    None = 0, Bold = 1, Italic = 2, Underline = 4
}
sealed record TextFormat(int Start, int Length, FreeTextStyle Style, int FontSize = 0);
sealed record StyledText(string Text, FreeTextStyle Style, int FontSize = 0);
static class RichText
{

    public static FreeTextStyle Default(FreeTextBox box) => (box.Bold ? FreeTextStyle.Bold : 0) | (box.Italic ? FreeTextStyle.Italic : 0) | (box.Underline ? FreeTextStyle.Underline : 0);

    public static FreeTextStyle[] Styles(FreeTextBox box)
    {
        var styles = Enumerable.Repeat(Default(box), box.Text.Length).ToArray();
        foreach (var run in box.Formats ?? [])
            for (int i = Math.Max(0, run.Start); i < Math.Min(styles.Length, run.Start + run.Length); i++)
                styles[i] = run.Style;
        return styles;
    }

    public static int[] Sizes(FreeTextBox box)
    {
        var sizes = Enumerable.Repeat(box.FontSize, box.Text.Length).ToArray();
        foreach (var run in box.Formats ?? [])
            if (run.FontSize > 0)
                for (int i = Math.Max(0, run.Start); i < Math.Min(sizes.Length, run.Start + run.Length); i++)
                    sizes[i] = Math.Clamp(run.FontSize, 12, 144);
        return sizes;
    }

    public static FreeTextBox Resize(FreeTextBox box, int start, int end, int size)
    {
        var sizes = Sizes(box);
        for (int i = Math.Max(0, start); i < Math.Min(end, sizes.Length); i++)
            sizes[i] = Math.Clamp(size, 12, 144);
        return box with
        {
            Formats = Compact(Styles(box), sizes)
        };
    }

    public static TextFormat[] Compact(FreeTextStyle[] styles, int[]? sizes = null)
    {
        var runs = new List<TextFormat>();
        for (int start = 0; start < styles.Length;)
        {
            int end = start + 1;
            while (end < styles.Length && styles[end] == styles[start] && (sizes == null || sizes[end] == sizes[start]))
                end++;
            runs.Add(new(start, end - start, styles[start], sizes?[start] ?? 0));
            start = end;
        }
        return runs.ToArray();
    }

    public static bool Has(FreeTextBox box, int start, int end, FreeTextStyle flag)
    {
        var styles = Styles(box);
        return end > start && styles.Skip(start).Take(end - start).All(s => (s & flag) != 0);
    }

    public static FreeTextBox Toggle(FreeTextBox box, int start, int end, FreeTextStyle flag)
    {
        start = Math.Clamp(start, 0, box.Text.Length);
        end = Math.Clamp(end, start, box.Text.Length);
        var styles = Styles(box);
        bool remove = Has(box, start, end, flag);
        for (int i = start; i < end; i++)
            styles[i] = remove ? styles[i] & ~flag : styles[i] | flag;
        return box with
        {
            Formats = Compact(styles, Sizes(box))
        };
    }

    public static FreeTextBox Edit(FreeTextBox box, string text, int start, int end, int cursorAfter, FreeTextStyle inserted, int insertedSize = 0)
    {
        if (box.Text == text)
            return box;
        start = Math.Clamp(start, 0, box.Text.Length);
        end = Math.Clamp(end, start, box.Text.Length);
        int added = text.Length - box.Text.Length + end - start;
        if (added < 0 || start + added > text.Length || start + added != cursorAfter || !text.AsSpan(0, start).SequenceEqual(box.Text.AsSpan(0, start)) || !text.AsSpan(start + added).SequenceEqual(box.Text.AsSpan(end)))
        {
            int prefix = 0, cap = Math.Min(cursorAfter, Math.Min(box.Text.Length, text.Length));
            while (prefix < cap && box.Text[prefix] == text[prefix])
                prefix++;
            // For an insertion among identical characters, the new caret gives
            // the actual insertion point even if it moved since the last frame.
            int growth = text.Length - box.Text.Length;
            if (growth > 0)
                prefix = Math.Min(prefix, Math.Max(0, cursorAfter - growth));
            int suffix = 0;
            while (suffix < box.Text.Length - prefix && suffix < text.Length - prefix && box.Text[^(suffix + 1)] == text[^(suffix + 1)])
                suffix++;
            start = prefix;
            end = box.Text.Length - suffix;
            added = text.Length - prefix - suffix;
        }
        var old = Styles(box);
        var next = old.Take(start).Concat(Enumerable.Repeat(inserted, added)).Concat(old.Skip(end)).ToArray();
        return box with
        {
            Text = text,
            Formats = Compact(next, Sizes(box).Take(start).Concat(Enumerable.Repeat(insertedSize > 0 ? insertedSize : box.FontSize, added)).Concat(Sizes(box).Skip(end)).ToArray())
        };
    }

    // ImGui selections use UTF-8 byte offsets; persisted ranges use .NET UTF-16.
    public static int CharacterIndex(string text, int bytes)
    {
        int used = 0, index = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > bytes)
                break;
            used += rune.Utf8SequenceLength;
            index += rune.Utf16SequenceLength;
        }
        return index;
    }

    public static StyledText[][] Lines(FreeTextBox box, int columns)
        => TextEditorLayout.Wrap(box, columns).Select(line => line.Runs(Default(box))).ToArray();
}
