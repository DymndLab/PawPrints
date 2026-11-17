namespace Blueprint.Rendering;

sealed record ViewCaptionMark(int ViewId, string Text, Box Occupied, Box Bounds, int FontSize);

static class ViewCaption
{
    public const int DefaultSize = 44, MaxSize = 72, Band = MaxSize + 26;

    // Position captions relative to visible craft bounds within their layout slot.
    public static (Box Bounds, int FontSize) Place(ViewRegion view, Box occupied, int size = DefaultSize)
    {
        int font = Math.Min(Math.Clamp(size, 20, MaxSize), Math.Max(12, (int)((view.Bounds.Width - 24) / Math.Max(1, view.Name.Length * .62f))));
        float width = view.Name.Length * NativeText.MonoWidth(font) + 6;
        float x = Math.Clamp(occupied.Center.X - width / 2, view.Bounds.X, Math.Max(view.Bounds.X, view.Bounds.Right - width));
        return (new((int)x, (int)occupied.Bottom + 18, width, font + 8), font);
    }

    public static ViewCaptionMark Resize(ViewCaptionMark mark, ViewRegion view, int size)
    {
        var placed = Place(view, mark.Occupied, size);
        return mark with
        {
            Bounds = placed.Bounds,
            FontSize = placed.FontSize
        };
    }

    public static ViewCaptionMark[] AlignRows(ViewCaptionMark[] captions, ViewRegion[] views)
    {
        var result = captions.ToArray();
        foreach (var row in captions.GroupBy(c =>
        {
            var view = views.Single(v => v.Id == c.ViewId);
            return (Y: (int)MathF.Round(view.Bounds.Y), Bottom: (int)MathF.Round(view.Bounds.Bottom));
        }))
        {
            float baseline = row.Max(c => c.Occupied.Bottom + 18 + c.FontSize);
            foreach (var mark in row)
            {
                int i = Array.FindIndex(result, c => c.ViewId == mark.ViewId);
                result[i] = mark with
                {
                    Bounds = mark.Bounds with
                    {
                        Y = baseline - mark.FontSize
                    }
                };
            }
        }
        return result;
    }
}
