using System.Numerics;
namespace Blueprint.Rendering;

static class CanvasObjects
{
    public static DrawingObject[] All(RenderScene s, PartLabel[] labels)
    {
        var items = s.Views.Select(v => new DrawingObject(DrawingObjectKind.View, v.Id))
            .Concat(labels.Select(l => new DrawingObject(DrawingObjectKind.Label, l.Id)))
            .Concat(s.FreeText.Select(t => new DrawingObject(DrawingObjectKind.Text, t.Id)))
            .Concat(s.Dimensions.Select(d => new DrawingObject(DrawingObjectKind.Dimension, d.ViewId)))
            .Concat(s.Specs.Select(b => new DrawingObject(DrawingObjectKind.Performance, b.Id)))
            .Concat(s.Kittens.Select(k => new DrawingObject(DrawingObjectKind.ScaleReference, k.Id)))
            .Concat(PrintTables.Panels(s, labels).Select(t => new DrawingObject(t.Id == 0 ? DrawingObjectKind.PartsTable : DrawingObjectKind.FlagTable, t.Id))).ToArray();
        return (s.Drawing?.Layers ?? []).Where(items.Contains).Concat(items).Distinct().ToArray();
    }

    public static Box Bounds(RenderScene s, PartLabel[] labels, DrawingObject item) => item.Kind switch
    {
        DrawingObjectKind.View => s.Details.FirstOrDefault(d => d.Definition.ViewId == item.Id)?.Definition.Bounds ?? s.Views.Single(v => v.Id == item.Id).Bounds,
        DrawingObjectKind.Label => AnnotationText.Bounds(labels.Single(l => l.Id == item.Id)),
        DrawingObjectKind.Text => s.FreeText.Single(t => t.Id == item.Id).Bounds,
        DrawingObjectKind.Dimension => HeightDimensions.Place(s, s.Dimensions.Single(d => d.ViewId == item.Id), false).Bounds,
        DrawingObjectKind.Performance => s.Specs.Single(b => b.Id == item.Id).Bounds,
        DrawingObjectKind.ScaleReference => KittenReferences.Bounds(s, s.Kittens.Single(k => k.Id == item.Id)),
        _ => PrintTables.Bounds(s, labels, item.Kind == DrawingObjectKind.PartsTable ? 0 : 1)
    };

    public static Box ContentBounds(RenderScene s, PartLabel[] labels, DrawingObject item)
    {
        if (item.Kind != DrawingObjectKind.View)
            return Bounds(s, labels, item);
        var detail = s.Details.FirstOrDefault(d => d.Definition.ViewId == item.Id);
        if (detail?.GeometryBounds is { } geometry)
            return ProjectionGroups.Bounds([geometry, detail.CaptionBounds]);
        var view = s.Views.Single(v => v.Id == item.Id);
        var ink = s.Visible.Where(p => p.Key.View == view.Id).Select(p => p.Value.Bounds).ToArray();
        return ProjectionGroups.Bounds((ink.Length > 0 ? ink : [view.Bounds]).Concat(s.Captions.Where(c => c.ViewId == item.Id).Select(c => c.Bounds)));
    }

    public static DrawingObject? Pick(Sheet sheet, RenderScene s, PartLabel[] labels, Vector2 point, float tolerance, DrawingObject? selected = null)
    {
        if (selected is { Kind: DrawingObjectKind.View, Id: >= 1000 } detailSelection &&
            s.Details.FirstOrDefault(d => d.Definition.ViewId == detailSelection.Id) is { } selectedDetail)
        {
            var box = selectedDetail.Definition.Bounds;
            if (Vector2.Distance(point, new(box.Right, box.Bottom)) <= tolerance * 3.6f)
                return detailSelection;
        }
        foreach (var item in All(s, labels).Reverse())
        {
            var b = Bounds(s, labels, item);
            if (selected == item && item.Kind == DrawingObjectKind.Label && LeaderRouting.TryLayout(s, labels, out var routes, out _))
            {
                var route = routes.FirstOrDefault(r => r.Label.Id == item.Id);
                if (route != null && (Vector2.Distance(point, route.Start) <= tolerance * 2 || Vector2.Distance(point, route.End) <= tolerance * 2))
                    return item;
            }
            bool grip = item.Kind is DrawingObjectKind.Text or DrawingObjectKind.Performance or DrawingObjectKind.PartsTable or DrawingObjectKind.FlagTable;
            if (selected == item && grip && Vector2.Distance(point, new(b.Right, b.Bottom)) <= tolerance * 3.6f)
                return item;
            if (item.Kind == DrawingObjectKind.View)
            {
                if (s.Captions.Any(c => c.ViewId == item.Id && c.Bounds.Contains(point)))
                    return item;
                if (s.Options.FreeLayout && s.PickSurface(item.Id, point) != null)
                    return item;
                var detail = s.Details.FirstOrDefault(d => d.Definition.ViewId == item.Id);
                if (detail?.CaptionBounds.Contains(point) == true)
                    return item;
                if (detail != null)
                {
                    if (Ink(detail.Pixels.Ink, detail.Pixels.Width, detail.Pixels.Height, point - new Vector2(b.X + 12, b.Y + 12)))
                        return item;
                }
                else if (s.Views.FirstOrDefault(v => v.Id == item.Id) is { } view)
                {
                    var tile = sheet.MainInk?.FirstOrDefault(t => t.ViewId == item.Id);
                    if (tile != null && Ink(tile.Pixels, tile.Width, tile.Height, point - new Vector2(tile.Bounds.X, tile.Bounds.Y)))
                        return item;
                    if (tile == null && s.Pick(point)?.View == view.Id)
                        return item;
                }
            }
            else if (item.Kind == DrawingObjectKind.Dimension)
            {
                if (HeightDimensions.Hit(HeightDimensions.Place(s, s.Dimensions.Single(d => d.ViewId == item.Id), false), point, tolerance))
                    return item;
            }
            else if (item.Kind == DrawingObjectKind.Text)
            {
                var text = s.FreeText.Single(t => t.Id == item.Id);
                var layout = new TextEditorLayout(text);
                if (selected == item && Vector2.Distance(point, new(b.Right, b.Bottom)) <= tolerance)
                    return item;
                if (text.Outline && b.Contains(point) && (point.X - b.X < tolerance || b.Right - point.X < tolerance || point.Y - b.Y < tolerance || b.Bottom - point.Y < tolerance))
                    return item;
                for (int row = 0; row < layout.Lines.Length; row++)
                {
                    float x = b.X + layout.Left(row), y = b.Y + layout.Top(row);
                    foreach (var glyph in layout.Lines[row].Glyphs)
                    {
                        float width = glyph.Text.Length * NativeText.MonoWidth(glyph.FontSize);
                        if (!string.IsNullOrWhiteSpace(glyph.Text) && new Box(x, y + layout.Lines[row].FontSize - glyph.FontSize, width, glyph.FontSize).Contains(point))
                            return item;
                        x += width;
                    }
                }
            }
            else if (selected == item && Vector2.Distance(point, new(b.Right, b.Bottom)) <= tolerance)
                return item;
            else if (sheet.ObjectHits?.TryGetValue(item, out var shape) == true)
            {
                if (shape.Hit(point, tolerance))
                    return item;
            }
            else if (!s.Options.FreeLayout && b.Contains(point))
                return item;
        }
        return null;
        bool Ink(byte[] pixels, int w, int h, Vector2 local)
        {
            int radius = Math.Clamp((int)MathF.Ceiling(tolerance), 1, 12);
            int x = (int)MathF.Floor(local.X), y = (int)MathF.Floor(local.Y);
            for (int yy = Math.Max(0, y - radius); yy < Math.Min(h, y + radius + 1); yy++)
                for (int xx = Math.Max(0, x - radius); xx < Math.Min(w, x + radius + 1); xx++)
                    if (pixels[yy * w + xx] > 0)
                        return true;
            return false;
        }
    }
}
