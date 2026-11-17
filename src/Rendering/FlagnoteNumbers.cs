namespace Blueprint.Rendering;

// Annotation IDs identify edits and history. Flagnote numbers belong to a
// separate sequence, including hidden notes, and survive table toggles.
static class FlagnoteNumbers
{
    public static PartLabel[] Normalize(IReadOnlyList<PartLabel> labels, bool keyed)
    {
        var numbers = new Dictionary<int, int>();
        var used = new HashSet<int>();
        var notes = labels.Where(l => l.FlagNote).OrderBy(l => l.Id).ToArray();
        foreach (var note in notes)
            if (note.FlagnoteNumber > 0 && used.Add(note.FlagnoteNumber))
                numbers[note.Id] = note.FlagnoteNumber;
        foreach (var note in notes)
        {
            if (numbers.ContainsKey(note.Id))
                continue;
            int number = 1;
            while (used.Contains(number))
                number++;
            used.Add(number);
            numbers[note.Id] = number;
        }
        return labels.Select(l => !l.FlagNote ? l : l with
        {
            FlagnoteNumber = numbers[l.Id],
            Key = keyed ? $"F{numbers[l.Id]}" : null
        }).ToArray();
    }

    public static int Next(IReadOnlyList<PartLabel> labels)
    {
        var used = Normalize(labels, false).Where(l => l.FlagNote).Select(l => l.FlagnoteNumber).ToHashSet();
        int number = 1;
        while (used.Contains(number))
            number++;
        return number;
    }
}
