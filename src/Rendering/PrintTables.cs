namespace Blueprint.Rendering;

sealed record PartsRow(string Key, string Name, int Quantity, int[] PartIds);
static class PrintTables
{

    public static PartsRow[] Catalog(RenderScene scene) => scene.Parts.Where(p => !p.DetailOnly).GroupBy(p => (p.TemplateId, p.Name))
        .OrderBy(g => g.Key.Name, StringComparer.Ordinal).ThenBy(g => g.Key.TemplateId, StringComparer.Ordinal)
        .Select((g, i) => new PartsRow(Letters(i), g.Key.Name, g.Count(), g.Select(p => p.Id).ToArray())).ToArray();

    static string Letters(int index)
    {
        string result = "";
        do
        {
            result = (char)('A' + index % 26) + result;
            index = index / 26 - 1;
        } while (index >= 0);
        return result;
    }

    static Dictionary<int, string> LabelKeys(RenderScene scene)
    {
        var keys = Catalog(scene).SelectMany(row => row.PartIds.Select(part => (Part: part, row.Key))).ToDictionary(p => p.Part, p => p.Key);
        // An exploded pick ID aliases the physical part through its assembly
        // node. It can reference the existing row without adding quantity.
        if (scene.Details.Length > 0)
        {
            var physicalParts = scene.Parts.Where(p => !p.DetailOnly && p.NodeId.HasValue).ToLookup(p => p.NodeId);
            foreach (var alias in scene.Parts.Where(p => p.DetailOnly && p.NodeId.HasValue))
            {
                var matches = physicalParts[alias.NodeId];
                if (matches.Count() == 1)
                    keys.Add(alias.Id, keys[matches.Single().Id]);
            }
        }
        return keys;
    }

    public static string? PartKey(RenderScene scene, int part) => LabelKeys(scene).GetValueOrDefault(part);

    public static PartLabel[] ResolveLabelKeys(RenderScene scene, IReadOnlyList<PartLabel> labels)
    {
        var normalized = FlagnoteNumbers.Normalize(labels, scene.Options.FlagTable);
        Dictionary<int, string>? keys = null;
        if (scene.Options.PartsTable && normalized.Any(l => !l.FlagNote && !l.FullText))
            keys = LabelKeys(scene);
        return normalized.Select(l => l.FlagNote ? l : l with
        {
            Key = l.FullText ? null : keys?.GetValueOrDefault(l.PartId)
        }).ToArray();
    }

    public static (string Key, string Text, string? Quantity)[] Rows(RenderScene scene, IReadOnlyList<PartLabel> labels, int id) => id == 0
        ? Catalog(scene).Select(r => (r.Key, r.Name, (string?)r.Quantity.ToString())).ToArray()
        : FlagnoteNumbers.Normalize(labels, true).Where(l => l.Visible && l.FlagNote).OrderBy(l => l.FlagnoteNumber).Select(l => (l.Key!, l.Text, (string?)null)).ToArray();

    static int Columns(float width, int font, int id) => (int)Math.Max(6, (width - font * (id == 0 ? 6 : 4)) / (font * .62f));

    static string[] Lines(string text, float width, int font, int id) => AnnotationText.Wrap(text, Columns(width, font, id), 160);

    public static float Height(RenderScene scene, IReadOnlyList<PartLabel> labels, int id, float width, int font)
    {
        var rows = Rows(scene, labels, id);
        return font * 3 + Math.Max(font * 1.8f, rows.Sum(r => Lines(r.Text, width, font, id).Length * font * 1.25f + font * .6f)) + font * .8f;
    }

    public static Box Bounds(RenderScene scene, IReadOnlyList<PartLabel> labels, int id)
    {
        var explicitBox = id == 0 ? scene.Tables.Parts : scene.Tables.Flagnotes;
        if (explicitBox is { } box)
            return box;
        var lane = PageLayout.Table(scene.Options);
        float y = lane.Y + (id == 1 && scene.Options.PartsTable ? MathF.Ceiling(Height(scene, labels, 0, lane.Width, 24)) + FrameLayout.Gap : 0);
        return new(lane.X, y, lane.Width, Math.Min(MathF.Ceiling(Height(scene, labels, id, lane.Width, 24)), Math.Max(100, FrameLayout.Area(scene.Options).Bottom - y)));
    }

    public static IEnumerable<(int Id, Box Bounds)> Panels(RenderScene scene, IReadOnlyList<PartLabel> labels)
    {
        if (scene.Options.PartsTable)
            yield return (0, Bounds(scene, labels, 0));
        if (scene.Options.FlagTable)
            yield return (1, Bounds(scene, labels, 1));
    }

    public static IEnumerable<Box> Reserved(RenderScene scene, IReadOnlyList<PartLabel> labels) => scene.Reserved.Append(PageLayout.TitleBounds(scene.Options)).Concat(scene.FreeText.Select(b => b.Bounds)).Concat(scene.Specs.Select(b => b.Bounds)).Concat(Panels(scene, labels).Select(p => p.Bounds)).Concat(scene.Dimensions.Select(d => HeightDimensions.Place(scene, d).Bounds));

    public static int Font(RenderScene scene, IReadOnlyList<PartLabel> labels, int id, Box box)
    {
        for (int size = Math.Clamp((int)(24 * box.Width / 620), 14, 28); size >= 14; size--)
            if (Height(scene, labels, id, box.Width, size) <= box.Height + .01f)
                return size;
        return 0;
    }

    // Regeneration may change the catalog without changing paper dimensions.
    // Retain valid user boxes; grow only overflowing tables and move neighbors
    // only when necessary to keep the resulting stack clear.
    public static TableLayout Refit(RenderScene scene, IReadOnlyList<PartLabel> labels, IEnumerable<Box> obstacles)
    {
        var tables = scene.Tables;
        var occupied = obstacles.Append(PageLayout.TitleBounds(scene.Options)).ToList();
        var area = FrameLayout.Area(scene.Options);
        foreach (int id in new[] { 0, 1 })
        {
            if (id == 0 ? !scene.Options.PartsTable : !scene.Options.FlagTable)
                continue;
            var current = scene.WithTables(tables);
            var box = Bounds(current, labels, id);
            bool FitsHere(Box candidate) => (scene.Options.FreeLayout ? CanvasBounds.Safe(candidate) : PageLayout.Fits(candidate, scene.Options) && occupied.All(b => !b.Overlaps(candidate)))
                && Font(scene, labels, id, candidate) > 0;
            if (!FitsHere(box))
            {
                Box? fitted = null;
                foreach (float width in new[] { box.Width, Math.Min(area.Width, 900), Math.Min(area.Width, 1200), area.Width }.Distinct())
                {
                    int font = Math.Clamp((int)(24 * width / 620), 14, 28);
                    var wanted = FrameLayout.Clamp(box with
                    {
                        Width = width,
                        Height = Math.Max(box.Height, MathF.Ceiling(Height(scene, labels, id, width, font)))
                    }, scene.Options);
                    fitted = scene.Options.FreeLayout && Font(scene, labels, id, wanted) > 0 ? wanted : FrameLayout.Clear(wanted, occupied, area, false, 200, 100, FrameLayout.Gap,
                        b => Font(scene, labels, id, b) > 0, boundary: PageLayout.TitleBounds(scene.Options));
                    if (fitted != null)
                        break;
                }
                box = fitted ?? throw new InvalidOperationException("The tables need more room on this sheet.");
            }
            occupied.Add(box);
            tables = tables.Set(id, box);
        }
        return tables;
    }

    public static bool Fits(RenderScene scene, IReadOnlyList<PartLabel> labels, out string reason)
    {
        var area = FrameLayout.Area(scene.Options);
        var panels = Panels(scene, labels).ToArray();
        if (!scene.Options.FreeLayout && panels.Any(p => panels.Any(q => q.Id != p.Id && p.Bounds.Overlaps(q.Bounds)) || scene.Details.Any(d => p.Bounds.Overlaps(d.Definition.Bounds)) || scene.Specs.Any(b => p.Bounds.Overlaps(b.Bounds))))
        {
            reason = "Keep tables and detail frames clear of one another.";
            return false;
        }
        bool fits = panels.All(p => (scene.Options.FreeLayout ? CanvasBounds.Safe(p.Bounds) : PageLayout.Fits(p.Bounds, scene.Options)) && Font(scene, labels, p.Id, p.Bounds) > 0);
        reason = fits ? "" : "Enlarge the table to fit its rows, or hide a note.";
        return fits;
    }

    public static void Paint(Canvas canvas, RenderScene scene, IReadOnlyList<PartLabel> labels)
    {
        if (!Fits(scene, labels, out var reason))
            throw new InvalidOperationException(reason);
        if (!scene.Options.PartsTable && !scene.Options.FlagTable)
            return;
        var colors = SheetLayout.Colors(scene.Options.Theme);
        var paper = scene.Options.FreeLayout ? null : new Canvas(scene.Width, scene.Height, colors.Paper, colors.Ink, scene.Options);
        foreach (var panel in Panels(scene, labels))
        {
            canvas.HitTarget = new(panel.Id == 0 ? DrawingObjectKind.PartsTable : DrawingObjectKind.FlagTable, panel.Id);
            var box = panel.Bounds;
            int id = panel.Id, font = Font(scene, labels, id, box);
            // Clear only inside the printed frame. Tables may touch its rules,
            // but plain grid paper must not erase those rules or zone ticks.
            var title = PageLayout.TitleBounds(scene.Options);
            if (!scene.Options.FreeLayout) PageLayout.ClearPanel(canvas, paper!, box, scene.Options);
            int left = (int)box.X, right = (int)box.Right, textX = left + font * 3, qtyX = right - font * 3;
            void Rule(int y, float opacity)
            {
                int end = !scene.Options.FreeLayout && y >= title.Y && y <= title.Bottom ? Math.Min(right, (int)title.X - 1) : right;
                if (end >= left)
                    canvas.Line(left, y, end, y, opacity);
            }
            float rowY = box.Y + font * 2.5f;
            canvas.Text(id == 0 ? "PARTS" : "FLAGNOTES", left + font / 2, (int)box.Y + font / 2, font + 2, 1);
            if (id == 0)
                canvas.Text("QTY", qtyX, (int)box.Y + font / 2, font, 1);
            Rule((int)(box.Y + font * 2), .65f);
            var rows = Rows(scene, labels, id);
            foreach (var row in rows)
            {
                canvas.Text(row.Key, left + font / 2, (int)rowY, font, 1);
                var lines = Lines(row.Text, box.Width, font, id);
                for (int i = 0; i < lines.Length; i++)
                    canvas.Text(lines[i], textX, (int)(rowY + i * font * 1.25f), font, 1);
                if (row.Quantity != null)
                    canvas.Text(row.Quantity, qtyX, (int)rowY, font, 1);
                rowY += lines.Length * font * 1.25f + font * .6f;
                Rule((int)(rowY - font * .2f), .25f);
            }
            if (rows.Length == 0)
                canvas.Text("No placed notes", left + font / 2, (int)rowY, font, .6f);
        }
    }
}
