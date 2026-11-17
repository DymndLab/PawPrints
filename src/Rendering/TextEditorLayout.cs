using System.Text;
using System.Numerics;
namespace Blueprint.Rendering;

// Source offsets stay attached to displayed glyphs, including soft wraps and tabs.
// Painting and hit testing therefore use the same line breaks and alignment.
sealed record EditorGlyph(string Text, FreeTextStyle Style, int Start, int End, int FontSize = 32);
sealed record EditorLine(int Start, int End, EditorGlyph[] Glyphs)
{
    public float Width => Glyphs.Sum(g => g.Text.Length * NativeText.MonoWidth(g.FontSize));
    public int FontSize => Glyphs.Select(g => g.FontSize).DefaultIfEmpty(32).Max();
    public int Cells => Glyphs.Sum(g => g.Text.Length);
    public StyledText[] Runs(FreeTextStyle fallback)
    {
        var runs = new List<StyledText>();
        foreach (var glyph in Glyphs)
            if (runs.Count > 0 && runs[^1].Style == glyph.Style && runs[^1].FontSize == glyph.FontSize)
                runs[^1] = runs[^1] with
                {
                    Text = runs[^1].Text + glyph.Text
                };
            else
                runs.Add(new(glyph.Text, glyph.Style, glyph.FontSize));
        return runs.Count == 0 ? [new("", fallback)] : runs.ToArray();
    }
}

sealed class TextEditorLayout
{
    public EditorLine[] Lines
    {
        get;
    }
    public float CellWidth
    {
        get;
    }
    public float LineHeight
    {
        get;
    }
    public float Top(int row) => 8 + Lines.Take(row).Sum(l => l.FontSize * 1.3f);
    public float RowHeight(int row) => Lines[row].FontSize * 1.3f;
    public float Height => Lines.Sum(l => l.FontSize * 1.3f) + 16;
    readonly FreeTextBox box;

    public TextEditorLayout(FreeTextBox box)
    {
        this.box = box;
        CellWidth = NativeText.MonoWidth(box.FontSize);
        LineHeight = box.FontSize * 1.3f;
        Lines = Wrap(box, Math.Max(1, (int)((box.Bounds.Width - 16) / CellWidth)));
    }

    public float Left(int row) => box.Alignment switch
    {
        1 => (box.Bounds.Width - Lines[row].Width) / 2,
        2 => box.Bounds.Width - 8 - Lines[row].Width,
        _ => 8
    };

    public int Row(int index)
    {
        for (int i = Lines.Length - 1; i >= 0; i--)
            if (index >= Lines[i].Start)
                return i;
        return 0;
    }

    public Vector2 Caret(int index)
    {
        int row = Row(index);
        float x = Left(row);
        foreach (var glyph in Lines[row].Glyphs)
        {
            if (index <= glyph.Start)
                break;
            x += glyph.Text.Length * NativeText.MonoWidth(glyph.FontSize);
        }
        int font = Lines[row].Glyphs.FirstOrDefault(g => index >= g.Start && index < g.End)?.FontSize
            ?? Lines[row].Glyphs.LastOrDefault()?.FontSize ?? box.FontSize;
        return new(x, Top(row) + Lines[row].FontSize - font);
    }

    public int Hit(Vector2 point)
    {
        int row = Lines.Length - 1;
        for (int i = 0; i < Lines.Length; i++)
            if (point.Y < Top(i) + RowHeight(i))
            {
                row = i;
                break;
            }
        float x = Left(row);
        foreach (var glyph in Lines[row].Glyphs)
        {
            float width = glyph.Text.Length * NativeText.MonoWidth(glyph.FontSize);
            if (point.X < x + width / 2)
                return glyph.Start;
            x += width;
        }
        return Lines[row].End;
    }

    public IEnumerable<Box> Selection(int start, int end)
    {
        if (start == end)
            yield break;
        for (int row = 0; row < Lines.Length; row++)
        {
            float x = Left(row), left = float.PositiveInfinity, right = x;
            foreach (var glyph in Lines[row].Glyphs)
            {
                float width = glyph.Text.Length * NativeText.MonoWidth(glyph.FontSize);
                if (glyph.End > start && glyph.Start < end)
                {
                    left = Math.Min(left, x);
                    right = x + width;
                }
                x += width;
            }
            // A selected newline/soft-wrap separator is visible even on an empty line.
            if (start <= Lines[row].End && end > Lines[row].End)
            {
                left = Math.Min(left, x);
                right = x + CellWidth / 2;
            }
            if (float.IsFinite(left))
                yield return new(left, Top(row), right - left, RowHeight(row));
        }
    }

    public static (int Start, int End) WordRange(string text, int index)
    {
        if (text.Length == 0)
            return (0, 0);
        index = Math.Clamp(index, 0, text.Length - 1);
        if (char.IsLowSurrogate(text[index]) && index > 0)
            index--;
        int Kind(int at) => char.IsWhiteSpace(text[at]) ? 0 : char.IsLetterOrDigit(text[at]) || text[at] == '_' || char.IsSurrogate(text[at]) ? 1 : 2;
        int kind = Kind(index), start = index, end = index + 1;
        while (start > 0 && Kind(start - 1) == kind)
            start--;
        while (end < text.Length && Kind(end) == kind)
            end++;
        return (start, end);
    }

    public static EditorLine[] Wrap(FreeTextBox box, int columns)
    {
        columns = Math.Max(1, columns);
        var styles = RichText.Styles(box);
        var sizes = RichText.Sizes(box);
        float available = columns * NativeText.MonoWidth(box.FontSize);
        var lines = new List<EditorLine>();
        var line = new List<EditorGlyph>();
        int index = 0, lineStart = 0;
        float cells = 0;
        void Flush(int end, int nextStart)
        {
            lines.Add(new(lineStart, end, line.Count == 0 ? [new("", RichText.Default(box), lineStart, end, sizes.Length > 0 ? sizes[Math.Clamp(lineStart, 0, sizes.Length - 1)] : box.FontSize)] : line.ToArray()));
            line = [];
            cells = 0;
            lineStart = nextStart;
        }
        foreach (var rune in box.Text.EnumerateRunes())
        {
            int start = index;
            index += rune.Utf16SequenceLength;
            if (rune.Value == '\r')
                continue;
            if (rune.Value == '\n')
            {
                Flush(start, index);
                continue;
            }
            foreach (string glyph in rune.Value == '\t' ? new[] { " ", " ", " ", " " } : new[] { rune.ToString() })
            {
                if (line.Count > 0 && cells + glyph.Length * NativeText.MonoWidth(sizes[start]) > available)
                {
                    int space = line.FindLastIndex(g => g.Text == " ");
                    if (space > 0)
                    {
                        var separator = line[space];
                        var tail = line.Skip(space + 1).ToList();
                        line = line.Take(space).ToList();
                        Flush(separator.Start, separator.End);
                        line = tail;
                        cells = line.Sum(g => g.Text.Length * NativeText.MonoWidth(g.FontSize));
                    }
                    else
                        Flush(start, start);
                }
                line.Add(new(glyph, styles[start], start, index, sizes[start]));
                cells += glyph.Length * NativeText.MonoWidth(sizes[start]);
            }
        }
        Flush(box.Text.Length, box.Text.Length);
        return lines.ToArray();
    }
}
