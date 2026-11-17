namespace Blueprint.Rendering;

readonly record struct ArrangementChoice(int Id, string Name);

static class ViewArrangements
{
    public const int Gap = 40, Padding = 32;

    public static int[] Views(PrintOptions o) => new[] { 3, 4, 1, 2 }.Where(id => (o.VisibleViews & (id == 4 ? 8 : 1 << (id - 1))) != 0 && (id != 4 || o.Layout == 2)).ToArray();

    public static ArrangementChoice[] Choices(PrintOptions o)
    {
        int[] ids = Views(o);
        if (o.Layout == 0 || ids.Length < 2)
            return [new(0, "Best fit")];
        var choices = new List<ArrangementChoice> { new(0, "Best fit") };
        foreach (var choice in new[] { new ArrangementChoice(1, "Grid"), new(2, "Across page"), new(3, "Down page"), new(4, "Large isometric") })
        {
            if (choice.Id == 1 && ids.Length < 3 || choice.Id == 4 && !ids.Contains(4))
                continue;
            if (Cells(o, choice.Id, ids).Length == ids.Length)
                choices.Add(choice);
        }
        return choices.ToArray();
    }

    public static Box[] Cells(PrintOptions o, int arrangement, int[] ids, int autoColumns = 0)
    {
        var area = PageLayout.Drawing(o);
        int caption = o.Captions ? 56 : 0, n = ids.Length;
        if (n == 0)
            return [];
        Box Cell(float x, float y, float width, float height) => new((int)x, (int)y, (int)width, (int)height - caption);
        Box[] boxes;
        if (arrangement == 4)
        {
            if (n < 2 || !ids.Contains(4))
                return [];
            bool across = area.Width >= area.Height;
            float main = (across ? area.Width - Gap : area.Height - Gap) * .58f;
            var hero = across ? Cell(area.X, area.Y, main, area.Height) : Cell(area.X, area.Y, area.Width, main);
            var other = new List<Box>();
            for (int i = 0; i < n - 1; i++)
            {
                if (across)
                {
                    float h = (area.Height - Gap * (n - 2)) / (n - 1);
                    other.Add(Cell(area.X + main + Gap, area.Y + i * (h + Gap), area.Width - main - Gap, h));
                }
                else
                {
                    float w = (area.Width - Gap * (n - 2)) / (n - 1);
                    other.Add(Cell(area.X + i * (w + Gap), area.Y + main + Gap, w, area.Height - main - Gap));
                }
            }
            int next = 0;
            boxes = ids.Select(id => id == 4 ? hero : other[next++]).ToArray();
        }
        else
        {
            int cols = autoColumns > 0 ? autoColumns : arrangement switch
            {
                1 => 2,
                2 => n,
                _ => 1
            };
            int rows = (n + cols - 1) / cols;
            int w = (int)((area.Width - Gap * (cols - 1)) / cols), h = (int)((area.Height - Gap * (rows - 1)) / rows);
            boxes = ids.Select((id, index) =>
            {
                if (cols == 2 && ids.SequenceEqual(new[] { 3, 1, 2 }) && index > 0)
                    index++;
                return Cell(area.X + index % cols * (w + Gap), area.Y + index / cols * (h + Gap), w, h);
            }).ToArray();
        }
        // Keep captions legible and reject strips too shallow to be useful.
        return boxes.All(b => b.Width >= 220 && b.Height >= 140) ? boxes : [];
    }
}
