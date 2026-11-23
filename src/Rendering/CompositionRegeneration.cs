namespace Blueprint.Rendering;

// Build off to the side. The caller publishes only a complete, valid print;
// failure never mutates the document the user is currently editing.
static class CompositionRegeneration
{


    static InvalidOperationException CannotKeep(string item) => new($"Cannot keep {item} with these changes. Your previous print is unchanged. Restore its view/part or remove that item before generating.");


    public static Box[] RepageObstacles(RenderScene previous, PrintOptions options)
    {
        float sx = (float)options.Page.Width / previous.Options.Page.Width;
        float sy = (float)options.Page.Height / previous.Options.Page.Height;
        Box Map(Box b) => FrameLayout.Clamp(new(b.X * sx, b.Y * sy, b.Width * sx, b.Height * sy), options);
        var boxes = previous.Details.Select(d => Map(d.Definition.Bounds)).Concat(previous.FreeText.Select(b => Map(b.Bounds))).ToList();
        foreach (var spec in previous.Specs)
        {
            var b = spec with
            {
                Bounds = Map(spec.Bounds)
            };
            if (previous.Performance is { } data && PerformancePainting.Font(data, b, options.TitleBlock?.Title) == 0)
                b = b with
                {
                    Bounds = FrameLayout.Clamp(b.Bounds with
                    {
                        Height = PerformancePainting.Height(data, b, 14, options.TitleBlock?.Title)
                    }, options)
                };
            boxes.Add(b.Bounds);
        }
        if (options.PartsTable && previous.Tables.Parts is { } parts)
            boxes.Add(Map(parts));
        if (options.FlagTable && previous.Tables.Flagnotes is { } flags)
            boxes.Add(Map(flags));
        return boxes.ToArray();
    }

    public static Sheet Rebuild(Sheet fresh, Sheet previous, CancellationToken token, bool preservePaperPositions = false, bool reuseDetailPixels = false)
    {
        var before = previous.Scene ?? throw CannotKeep("the composition");
        var after = fresh.Scene ?? throw CannotKeep("the composition");
        float sx = (float)fresh.PixelWidth / previous.PixelWidth, sy = (float)fresh.PixelHeight / previous.PixelHeight;
        Box MapBox(Box b) => new(MathF.Round(b.X * sx), MathF.Round(b.Y * sy), MathF.Round(b.Width * sx), MathF.Round(b.Height * sy));
        System.Numerics.Vector2 MapPoint(int id, float x, float y)
        {
            var old = before.Views.FirstOrDefault(v => v.Id == id);
            var next = after.Views.FirstOrDefault(v => v.Id == id);
            if (!preservePaperPositions && after.Drawing != null && old?.Frame != null && next?.Frame != null)
                return next.Bounds.Center + (new System.Numerics.Vector2(x, y) - old.Bounds.Center) * (next.Frame.Scale / old.Frame.Scale);
            return new(x * sx, y * sy);
        }
        Box? Table(Box? b, int id)
        {
            if (b is not { } value)
                return null;
            var box = FrameLayout.Clamp(MapBox(value), fresh.Options);
            if (fresh.Options.Page != previous.Options.Page && PrintTables.Font(after, previous.Labels, id, box) == 0)
                box = FrameLayout.Clamp(box with
                {
                    Height = MathF.Ceiling(PrintTables.Height(after, previous.Labels, id, box.Width, 14))
                }, fresh.Options);
            return box;
        }
        bool repaging = fresh.Options.Page != previous.Options.Page;
        var specs = before.Specs.Select(b => b with { Bounds = FrameLayout.Clamp(MapBox(b.Bounds), fresh.Options) }).ToArray();
        if (repaging && after.Performance is { } performance)
            specs = specs.Select(b => PerformancePainting.Font(performance, b, fresh.Options.TitleBlock?.Title) > 0 ? b : b with
            {
                Bounds = FrameLayout.Clamp(b.Bounds with
                {
                    Height = PerformancePainting.Height(performance, b, 14, fresh.Options.TitleBlock?.Title)
                }, fresh.Options)
            }).ToArray();
        var tables = new TableLayout(Table(before.Tables.Parts, 0), Table(before.Tables.Flagnotes, 1));
        var captions = after.Captions.Select(c => before.Captions.FirstOrDefault(old => old.ViewId == c.ViewId && old.Text == c.Text) is { } old
            ? ViewCaption.Resize(c, after.Views.Single(v => v.Id == c.ViewId), old.FontSize) : c).ToArray();
        var details = previous.Details.Select(d => d with { Bounds = ExplodedPainting.ClampFrame(MapBox(d.Bounds), fresh.Options), Camera = reuseDetailPixels ? d.Camera : null }).ToArray();
        if (repaging)
        {
            var occupied = after.Views.Select(v => DrawingPlacements.Footprint(v, fresh.Options.Captions)).Append(PageLayout.TitleBounds(fresh.Options)).ToList();
            Box Place(Box box, Func<Box, bool>? content = null)
            {
                var placed = FrameLayout.Clear(box, occupied, FrameLayout.Area(fresh.Options), false, 100, 100, 4,
                    content, boundary: PageLayout.TitleBounds(fresh.Options)) ?? throw CannotKeep("all supporting blocks on this paper size");
                occupied.Add(placed);
                return placed;
            }
            details = details.Select(d => d with { Bounds = Place(d.Bounds) }).ToArray();
            specs = specs.Select(b => b with { Bounds = Place(b.Bounds, candidate => after.Performance != null && PerformancePainting.Font(after.Performance, b with { Bounds = candidate }, fresh.Options.TitleBlock?.Title) > 0) }).ToArray();
            foreach (int id in new[] { 0, 1 })
            {
                if (id == 0 ? !fresh.Options.PartsTable : !fresh.Options.FlagTable)
                    continue;
                var box = id == 0 ? tables.Parts : tables.Flagnotes;
                if (box != null)
                    tables = tables.Set(id, Place(box.Value, candidate => PrintTables.Font(after, previous.Labels, id, candidate) > 0));
            }
        }
        tables = PrintTables.Refit(after.WithTables(tables), previous.Labels,
            details.Select(d => d.Bounds).Concat(specs.Select(b => b.Bounds)));
        foreach (var detail in details)
        {
            var nodes = fresh.DetailSource?.Assembly.Nodes.Select(n => n.Id).ToHashSet() ?? [];
            if (!nodes.Contains(detail.Scope) || (detail.Seed is { } seed && !nodes.Contains(seed))
                || (detail.Expanded ?? []).Any(n => !nodes.Contains(n)) || (detail.Groups ?? []).Any(g => !nodes.Contains(g.Node)))
                throw CannotKeep($"detail {detail.Caption}");
        }
        // Recapture invalidates detail pixels. Appearance-only updates keep the same geometry.
        var composed = details.Length > 0 ? ExplodedPainting.Compose(fresh, reuseDetailPixels ? previous : fresh, details, [], token, tables: tables, captions: captions)
            : fresh with
            {
                Scene = after.WithTables(tables).WithCaptions(captions)
            };
        composed = composed with
        {
            Scene = composed.Scene!.WithDimensions(before.Dimensions.Select(d => d.X is { } x ? HeightDimensions.Move(composed.Scene!, d, MapPoint(d.ViewId, x, 0).X) : d).ToArray())
        };
        after = composed.Scene!;
        after = after.WithKittens([], before.KittenModel);
        after = after.WithKittens(before.Kittens.Select(k => KittenReferences.Clamp(after, k with { X = k.X * sx, Y = k.Y * sy })).ToArray());
        after = after.WithSpecs(specs);
        after = after.WithFreeText(before.FreeText.Select(b => FreeTextPainting.Fit(b with { Bounds = MapBox(b.Bounds) }, fresh.Options)).ToArray());
        composed = composed with
        {
            Scene = after
        };
        var labels = FlagnoteNumbers.Normalize(previous.Labels, fresh.Options.FlagTable).Select(label =>
        {
            token.ThrowIfCancellationRequested();
            var oldPart = before.Parts.Single(p => p.Id == label.PartId);
            var part = after.Parts.SingleOrDefault(p => p.DetailOnly == oldPart.DetailOnly && (oldPart.NodeId is { } node
                ? p.NodeId == node : p.Id == oldPart.Id && p.TemplateId == oldPart.TemplateId));
            var oldView = before.Views.SingleOrDefault(v => v.Id == label.ViewId);
            var newView = after.Views.SingleOrDefault(v => v.Id == label.ViewId);
            if (part == null || newView == null || !preservePaperPositions && oldView?.Name != newView.Name || !after.Visible.TryGetValue((label.ViewId, part.Id), out var visible))
                throw CannotKeep($"label ‘{label.Text}’");
            LeaderPoint? pin = preservePaperPositions && label.AttachmentPinned ? label.Attachment : null;
            if (!preservePaperPositions && label.AttachmentPinned && label.Attachment is { } anchor && before.Visible.TryGetValue((label.ViewId, label.PartId), out var oldVisible))
            {
                var a = oldVisible.Bounds;
                var b = visible.Bounds;
                pin = new(b.X + (anchor.X - a.X) * b.Width / Math.Max(1, a.Width), b.Y + (anchor.Y - a.Y) * b.Height / Math.Max(1, a.Height));
            }
            var position = MapPoint(label.ViewId, label.X, label.Y);
            var changed = label with
            {
                PartId = part.Id,
                X = position.X,
                Y = position.Y,
                Attachment = pin,
                Socket = null,
                ManualPlacement = true,
                Key = label.FlagNote ? label.Key : (fresh.Options.PartsTable && !part.DetailOnly ? PrintTables.PartKey(after, part.Id) : null)
            };
            var bounds = AnnotationText.Bounds(changed);
            return changed with
            {
                X = Math.Clamp(changed.X, 0, fresh.PixelWidth - bounds.Width),
                Y = Math.Clamp(changed.Y, 0, fresh.PixelHeight - bounds.Height)
            };
        }).ToArray();
        if (repaging && after.FreeText.Length > 0)
        {
            var placedText = new List<FreeTextBox>();
            var clearScene = after.WithFreeText([]);
            if (!LeaderRouting.TryLayout(clearScene, labels, out var textRoutes, out var textReason))
                throw new InvalidOperationException(textReason);
            foreach (var text in after.FreeText)
            {
                var space = TextBoxPlacement.Space(clearScene.WithFreeText(placedText.ToArray()), labels, textRoutes);
                var box = FrameLayout.Clear(text.Bounds, space.Obstacles, FrameLayout.Area(fresh.Options), false,
                    text.Bounds.Width, text.Bounds.Height, fits: space.Clear)
                    ?? throw CannotKeep("all text boxes on this paper size");
                placedText.Add(text with
                {
                    Bounds = box
                });
            }
            after = after.WithFreeText(placedText.ToArray());
            composed = composed with
            {
                Scene = after
            };
        }
        var result = AnnotationPainting.Compose(composed, labels, token);
        return result with
        {
            RegeneratedBase = fresh,
            PoseMain = fresh,
            Notice = repaging ? "Paper updated. Ctrl+Z restores the previous layout." : "PawPrint generated. Notes, poses, detail frames and table positions retained. New undo history started."
        };
    }
}
