using System.Numerics;
namespace Blueprint.Rendering;

static class ExplodedPainting
{
    public const int Limit = 4;
    public const int CaptionSpace = 144;
    static readonly ViewPixels emptyTile = new(1, 1, [0], []);

    public static uint? WholeCraftRoot(AssemblyCapture assembly)
    {
        var roots = assembly.Nodes.Where(n => n.Parent == null).Take(2).ToArray();
        return roots.Length == 1 ? roots[0].Id : null;
    }


    public static Box ClampFrame(Box b, PrintOptions options)
    {
        return FrameLayout.Clamp(b, options, options.FreeLayout ? 64 : 200, 180);
    }


    public static Sheet Compose(Sheet original, Sheet displayed, ExplodedDetail[] details, PartLabel[] labels, CancellationToken token,
        bool moveLabels = false, TableLayout? tables = null, ViewCaptionMark[]? captions = null, bool reuseTiles = true,
        bool preserveDisplayedContent = true)
    {
        if (details.Length > Limit)
            throw new InvalidOperationException($"Up to {Limit} exploded details per sheet.");
        long remainingPixels = 16_777_216 - (original.MainInk?.Sum(t => (long)t.Width * t.Height) ?? 0);
        var source = original.DetailSource ?? throw new InvalidOperationException("Refresh the craft to capture its assembly hierarchy.");
        var baseline = original.Scene!;
        int w = original.PixelWidth, h = original.PixelHeight;
        var colors = SheetLayout.Colors(original.Options.Theme);
        var canvas = new Canvas((byte[])baseline.BaseRgb.Clone(), w, h, colors.Ink);
        var owners = (int[])baseline.Owners.Clone();
        var meshOwners = (int[])baseline.MeshOwners!.Clone();
        var viewOwners = Enumerable.Repeat(-1, w * h).ToArray();
        foreach (var view in baseline.Views)
            for (int y = Math.Max(0, (int)view.Bounds.Y); y < Math.Min(h, view.Bounds.Bottom); y++)
                for (int x = Math.Max(0, (int)view.Bounds.X); x < Math.Min(w, view.Bounds.Right); x++)
                    viewOwners[y * w + x] = baseline.PixelViews?[y * w + x] ?? view.Id;
        var nodes = source.Assembly.Nodes;
        var partIds = nodes.Select((n, i) => (n.Id, Part: baseline.Parts.Length + i)).ToDictionary(p => p.Id, p => p.Part);
        var parts = baseline.Parts.Concat(nodes.Select(n => new LabelPart(partIds[n.Id], n.Name, n.TemplateId, [], true, n.Id))).ToArray();
        var rendered = new List<RenderedDetail>();
        var fullVisuals = new Dictionary<(int View, int Part), PartVisual>();
        var views = baseline.Views.ToList();
        string? titleScale = PrintScale.PrintedTitleScale(baseline.Views, original.Options);
        var reserved = baseline.Reserved.Where(b => !baseline.Captions.Any(c => c.Bounds == b)).ToList();
        foreach (var detail in details)
        {
            token.ThrowIfCancellationRequested();
            CanvasBounds.Require(detail.Bounds);
            var previousTile = displayed.Scene?.Details.FirstOrDefault(d => d.Definition.Id == detail.Id);
            if (detail.SourceIssue != DetailSourceIssue.None)
            {
                var unresolved = PaintUnresolved(canvas, detail, previousTile, token);
                remainingPixels -= (long)unresolved.Pixels.Width * unresolved.Pixels.Height;
                if (original.Options.FreeLayout && remainingPixels < 0)
                    throw new InvalidOperationException("View layers exceed the retained-pixel memory budget (16,777,216 pixels). Reduce view sizes.");
                rendered.Add(unresolved);
                views.Add(new(detail.ViewId, SheetLayout.ViewName(detail.ViewId), detail.Bounds, unresolved.Frame));
                reserved.Add(unresolved.CaptionBounds);
                continue;
            }
            if (detail.Bounds.Width < (original.Options.FreeLayout ? 64 : 200) || detail.Bounds.Height < 180 ||
                (double)(detail.Bounds.Width - 24) * (detail.Bounds.Height - CaptionSpace) > (long)w * h)
                throw new InvalidOperationException("Exploded view exceeds the render tile size budget.");
            // Movement reuses the exact tile; geometry edits/resize rerender only this detail.
            var poses = baseline.Poses;
            bool samePose = displayed.Scene != null && !displayed.Scene.detailsCacheInvalid && displayed.Scene.Poses.SequenceEqual(poses);
            var prior = reuseTiles && samePose && previousTile?.Definition.SourceIssue == DetailSourceIssue.None ? previousTile : null;
            // Moving a fitted detail pins its existing camera. That is still the
            // same projection and can reuse the cached geometry pixels.
            bool sameCamera = prior != null && (detail.Camera == prior.Definition.Camera || detail.Camera == prior.Frame);
            var tile = prior != null && sameCamera && prior.Definition with
            {
                Bounds = detail.Bounds,
                Caption = detail.Caption,
                Camera = detail.Camera
            } == detail && prior.Pixels.Width == (int)detail.Bounds.Width - 24 && prior.Pixels.Height == (int)detail.Bounds.Height - CaptionSpace
                ? prior with
                {
                    Definition = detail
                } : ExplodedGeometry.Render(original.PoseSource is { } poseSource ? new ExplodedSource(PartPoses.Apply(poseSource, poses, detail.ViewId), source.Maps) : source, detail, token, original.Options, original.Options.FreeLayout ? remainingPixels : long.MaxValue);
            if (original.Options.FreeLayout)
            {
                remainingPixels -= (long)tile.Pixels.Width * tile.Pixels.Height;
                if (remainingPixels < 0)
                    throw new InvalidOperationException("View layers exceed the retained-pixel memory budget (16,777,216 pixels). Reduce view sizes.");
            }
            var box = tile.Definition.Bounds;
            int left = (int)box.X + 12, top = (int)box.Y + 12;
            int inkLeft = tile.Pixels.Width, inkTop = tile.Pixels.Height, inkRight = -1, inkBottom = -1;
            // Only ink is composited. Empty pixels preserve lower views and picking.
            for (int y = 0; y < tile.Pixels.Height; y++)
                for (int x = 0; x < tile.Pixels.Width; x++)
                {
                    int q = y * tile.Pixels.Width + x;
                    if (tile.Pixels.MeshOwners[q] < 0 && tile.Pixels.Ink[q] == 0)
                        continue;
                    inkLeft = Math.Min(inkLeft, x);
                    inkTop = Math.Min(inkTop, y);
                    inkRight = Math.Max(inkRight, x);
                    inkBottom = Math.Max(inkBottom, y);
                }
            for (int y = 0; y < tile.Pixels.Height; y++)
                for (int x = 0; x < tile.Pixels.Width; x++)
                {
                    int q = y * tile.Pixels.Width + x, index = (top + y) * w + left + x;
                    canvas.Ink(left + x, top + y, tile.Pixels.Ink[q] / 255f);
                    int mesh = tile.Pixels.MeshOwners[q];
                    if (mesh < 0 || tile.Pixels.Ink[q] == 0 || left + x < 0 || top + y < 0 || left + x >= w || top + y >= h)
                        continue;
                    inkLeft = Math.Min(inkLeft, x);
                    inkTop = Math.Min(inkTop, y);
                    inkRight = Math.Max(inkRight, x);
                    inkBottom = Math.Max(inkBottom, y);
                    viewOwners[index] = detail.ViewId;
                    owners[index] = partIds[tile.Groups[mesh]];
                    meshOwners[index] = tile.Meshes[mesh];
                }
            if (original.Options.FreeLayout)
                foreach (var pair in ViewAnchors.Measure(tile.Pixels, tile.Groups.Select(g => partIds[g]).ToArray(), new(left, top, tile.Pixels.Width, tile.Pixels.Height), token))
                    fullVisuals[(detail.ViewId, pair.Key)] = pair.Value;
            views.Add(new(detail.ViewId, SheetLayout.ViewName(detail.ViewId), new(left, top, tile.Pixels.Width, tile.Pixels.Height), tile.Frame));
            string heading = SheetLayout.ViewName(detail.ViewId);
            string caption = AnnotationText.Clean(detail.Caption, 60);
            int longest = Math.Max(heading.Length, caption.Length);
            int font = original.Options.FreeLayout ? 32 : Math.Min(40, Math.Max(12, (int)((box.Width - 32) / Math.Max(1, longest * .62f))));
            float center = inkLeft <= inkRight ? left + (inkLeft + inkRight) * .5f : box.Center.X;
            string ratio = PrintScale.Ratio(tile.Frame, original.Options);
            string scale = ratio == titleScale ? "" : "SCALE " + ratio;
            float textWidth = Math.Max(longest * NativeText.MonoWidth(font), scale.Length * NativeText.MonoWidth(15));
            int captionY = top + Math.Max(0, inkBottom) + 18;
            canvas.Text(heading, (int)(center - heading.Length * NativeText.MonoWidth(font) / 2), captionY, font, 1);
            canvas.Text(caption, (int)(center - caption.Length * NativeText.MonoWidth(font) / 2), captionY + font + 4, font, 1);
            if (scale.Length > 0)
                canvas.Text(scale, (int)(center - scale.Length * NativeText.MonoWidth(15) / 2), captionY + 2 * font + 8, 15, 1);
            var captionBounds = new Box(center - textWidth / 2, captionY, textWidth, 2 * font + (scale.Length > 0 ? 27 : 4));
            reserved.Add(captionBounds);
            rendered.Add(tile with
            {
                CaptionBounds = captionBounds,
                CaptionFont = font,
                GeometryBounds = inkRight < inkLeft ? new(left, top, 0, 0) : new(left + inkLeft, top + inkTop, inkRight - inkLeft + 1, inkBottom - inkTop + 1)
            });
        }
        NativeText.Apply(canvas.Pixels, w, h, canvas.Runs);
        var scene = new RenderScene(canvas.Pixels, owners, parts, views.ToArray(), reserved.ToArray(), original.Options, viewOwners)
        {
            BaseLettering = baseline.BaseLettering,
            PickingInk = baseline.PickingInk,
            MeshOwners = meshOwners,
            MeshNodes = source.Assembly.MeshNodes,
            Details = rendered.ToArray(),
            CraftHeight = baseline.CraftHeight,
            ViewHeights = baseline.ViewHeights
        };
        var content = preserveDisplayedContent ? displayed.Scene : null;
        scene = scene.WithWear(content?.WearMarks ?? []).WithTables(tables ?? content?.Tables ?? new()).WithCaptions(captions ?? content?.Captions ?? baseline.Captions);
        scene = scene.WithDimensions(content?.Dimensions ?? []).WithFreeText(content?.FreeText ?? []);
        var references = content?.Kittens.Where(k => k.SourceView is not (>= 1000) || details.Any(d => d.ViewId == k.SourceView));
        scene = scene.WithKittens(references?.ToArray() ?? [], content?.KittenModel);
        scene = scene.WithPoses(baseline.Poses).WithPerformance(baseline.Performance).WithSpecs(content?.Specs ?? []).WithDrawing(baseline.Drawing);
        // A frame may cover the main drawing, but must not destroy its label anchors.
        foreach (var pair in fullVisuals)
            scene.Visible[pair.Key] = pair.Value;
        foreach (var item in baseline.Visible)
            scene.Visible[item.Key] = item.Value;
        var adjusted = labels.Where(l => l.ViewId < 1000 || scene.Visible.ContainsKey((l.ViewId, l.PartId)) ||
            details.Any(d => d.ViewId == l.ViewId && d.SourceIssue != DetailSourceIssue.None)).Select(label =>
        {
            if (!moveLabels)
                return label;
            // Moving a frame is a manual composition edit. Keep previously placed
            // annotations even if the new frame crosses their automatic clearance.
            label = label with
            {
                ManualPlacement = true
            };
            var old = displayed.Details.FirstOrDefault(d => d.ViewId == label.ViewId);
            var now = details.FirstOrDefault(d => d.ViewId == label.ViewId);
            if (old == null || now == null)
                return label;
            var shift = new Vector2(now.Bounds.X - old.Bounds.X, now.Bounds.Y - old.Bounds.Y);
            var textBox = AnnotationText.Bounds(label);
            var changed = label with
            {
                X = original.Options.FreeLayout ? label.X + shift.X : Math.Clamp(label.X + shift.X, 0, w - textBox.Width),
                Y = original.Options.FreeLayout ? label.Y + shift.Y : Math.Clamp(label.Y + shift.Y, 0, h - textBox.Height)
            };
            if (changed.Attachment is { } anchor && displayed.Scene!.Visible.TryGetValue((label.ViewId, label.PartId), out var before) && scene.Visible.TryGetValue((label.ViewId, label.PartId), out var after))
            {
                var move = after.Bounds.Center - before.Bounds.Center;
                changed = changed with
                {
                    Attachment = new(anchor.X + move.X, anchor.Y + move.Y)
                };
            }
            return changed;
        }).ToArray();
        return AnnotationPainting.Compose(original with
        {
            Scene = scene,
            Details = rendered.Select(d => d.Definition).ToArray(),
            PoseMain = original
        }, adjusted, token);
    }

    static RenderedDetail PaintUnresolved(Canvas canvas, ExplodedDetail detail, RenderedDetail? previous, CancellationToken token)
    {
        // Reuse only the small view tile. Keep the former ink as dim context
        // without inserting its owner IDs into the current capture.
        var pixels = previous?.Pixels ?? emptyTile;
        if (pixels.MeshOwners.Length > 0) pixels = pixels with { MeshOwners = [] };
        var frame = previous?.Frame ?? new ProjectionFrame(1, 1, 1, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        var b = detail.Bounds;
        int width = (int)Math.Max(1, Math.Min(b.Width - 24, pixels.Width));
        int height = (int)Math.Max(1, Math.Min(b.Height - CaptionSpace, pixels.Height));
        int left = (int)(b.Center.X - width / 2f), top = (int)b.Y + 12;
        int rightInk = -1, bottomInk = -1, leftInk = width, topInk = height;
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            int row = y * pixels.Height / height * pixels.Width;
            for (int x = 0; x < width; x++)
            {
                byte ink = pixels.Ink[row + x * pixels.Width / width];
                if (ink == 0) continue;
                canvas.Ink(left + x, top + y, ink / 560f);
                leftInk = Math.Min(leftInk, x);
                rightInk = Math.Max(rightInk, x);
                topInk = Math.Min(topInk, y);
                bottomInk = Math.Max(bottomInk, y);
            }
        }
        string title = SheetLayout.ViewName(detail.ViewId) + " / " + AnnotationText.Clean(detail.Caption, 60);
        string warning = detail.SourceIssue == DetailSourceIssue.MissingRoot ? "ROOT PART DELETED" : "ASSEMBLY CONNECTION CHANGED";
        const int font = 28;
        var color = new Vector3(220, 156, 44);
        int yText = top + Math.Max(16, bottomInk) + 18;
        float textWidth = Math.Max(title.Length, warning.Length) * NativeText.MonoWidth(font);
        void Line(string text, int y) => canvas.Runs.Add(new(text,
            (int)(b.Center.X - text.Length * NativeText.MonoWidth(font) / 2), y, font, color));
        Line(title, yText);
        Line(warning, yText + font + 4);
        var geometry = rightInk < 0 ? new Box(left, top, 0, 0)
            : new(left + leftInk, top + topInk, rightInk - leftInk + 1, bottomInk - topInk + 1);
        return new(detail, frame, [], [], pixels)
        {
            CaptionBounds = new(b.Center.X - textWidth / 2, yText, textWidth, 2 * font + 4),
            CaptionFont = font,
            GeometryBounds = geometry
        };
    }
}
