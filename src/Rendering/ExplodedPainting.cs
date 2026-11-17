using System.Numerics;
namespace Blueprint.Rendering;

static class ExplodedPainting
{
    public const int Limit = 4;


    public static Box ClampFrame(Box b, PrintOptions options)
    {
        return FrameLayout.Clamp(b, options, 200, 180);
    }


    public static Sheet Compose(Sheet original, Sheet displayed, ExplodedDetail[] details, PartLabel[] labels, CancellationToken token, bool moveLabels = false, TableLayout? tables = null, ViewCaptionMark[]? captions = null)
    {
        if (details.Length > Limit)
            throw new InvalidOperationException($"Up to {Limit} exploded details per sheet.");
        var source = original.DetailSource ?? throw new InvalidOperationException("Refresh the craft to capture its assembly hierarchy.");
        var baseline = original.Scene!;
        int w = original.PixelWidth, h = original.PixelHeight;
        var colors = SheetLayout.Colors(original.Options.Theme);
        var canvas = new Canvas((byte[])baseline.BaseRgb.Clone(), w, h, colors.Ink);
        var paper = new Canvas(w, h, colors.Paper, colors.Ink, original.Options);
        var owners = (int[])baseline.Owners.Clone();
        var meshOwners = (int[])baseline.MeshOwners!.Clone();
        var viewOwners = Enumerable.Repeat(-1, w * h).ToArray();
        foreach (var view in baseline.Views)
            for (int y = (int)view.Bounds.Y; y < view.Bounds.Bottom; y++)
                for (int x = (int)view.Bounds.X; x < view.Bounds.Right; x++)
                    viewOwners[y * w + x] = view.Id;
        var nodes = source.Assembly.Nodes;
        var partIds = nodes.Select((n, i) => (n.Id, Part: baseline.Parts.Length + i)).ToDictionary(p => p.Id, p => p.Part);
        var parts = baseline.Parts.Concat(nodes.Select(n => new LabelPart(partIds[n.Id], n.Name, n.TemplateId, [], true, n.Id))).ToArray();
        var rendered = new List<RenderedDetail>();
        var views = baseline.Views.ToList();
        var reserved = baseline.Reserved.Where(b => !baseline.Captions.Any(c => c.Bounds == b)).ToList();
        foreach (var detail in details)
        {
            token.ThrowIfCancellationRequested();
            if (detail.Bounds != ClampFrame(detail.Bounds, original.Options) || !PageLayout.Fits(detail.Bounds, original.Options))
                throw new InvalidOperationException("Keep the detail frame inside the drawing area.");
            if (details.Any(other => other.Id != detail.Id && detail.Bounds.Overlaps(other.Bounds)))
                throw new InvalidOperationException("Keep exploded detail frames clear of one another.");
            // Movement reuses the exact tile; geometry edits/resize rerender only this detail.
            var poses = baseline.Poses;
            bool samePose = displayed.Scene != null && !displayed.Scene.detailsCacheInvalid && displayed.Scene.Poses.SequenceEqual(poses);
            var prior = samePose ? displayed.Scene?.Details.FirstOrDefault(d => d.Definition.Id == detail.Id) : null;
            // Moving a fitted detail pins its existing camera. That is still the
            // same projection and can reuse the cached geometry pixels.
            bool sameCamera = prior != null && (detail.Camera == prior.Definition.Camera || detail.Camera == prior.Frame);
            var tile = prior != null && sameCamera && prior.Definition with
            {
                Bounds = detail.Bounds,
                Caption = detail.Caption,
                Camera = detail.Camera
            } == detail && prior.Pixels.Width == (int)detail.Bounds.Width - 24 && prior.Pixels.Height == (int)detail.Bounds.Height - 100
                ? prior with
                {
                    Definition = detail
                } : ExplodedGeometry.Render(original.PoseSource is { } poseSource ? new ExplodedSource(PartPoses.Apply(poseSource, poses, detail.ViewId), source.Maps) : source, detail, token, original.Options);
            var box = detail.Bounds;
            PageLayout.ClearPanel(canvas, paper, box, original.Options);
            for (int y = (int)box.Y; y < box.Bottom; y++)
                for (int x = (int)box.X; x < box.Right; x++)
                {
                    int q = y * w + x;
                    owners[q] = -1;
                    meshOwners[q] = -1;
                    viewOwners[q] = detail.ViewId;
                }
            int left = (int)box.X + 12, top = (int)box.Y + 12;
            int inkLeft = tile.Pixels.Width, inkRight = 0, inkBottom = 0;
            for (int y = 0; y < tile.Pixels.Height; y++)
                for (int x = 0; x < tile.Pixels.Width; x++)
                {
                    int q = y * tile.Pixels.Width + x, index = (top + y) * w + left + x;
                    canvas.Ink(left + x, top + y, tile.Pixels.Ink[q] / 255f);
                    int mesh = tile.Pixels.MeshOwners[q];
                    if (mesh < 0)
                        continue;
                    inkLeft = Math.Min(inkLeft, x);
                    inkRight = Math.Max(inkRight, x);
                    inkBottom = Math.Max(inkBottom, y);
                    owners[index] = partIds[tile.Groups[mesh]];
                    meshOwners[index] = tile.Meshes[mesh];
                }
            views.Add(new(detail.ViewId, SheetLayout.ViewName(detail.ViewId), new(left, top, tile.Pixels.Width, tile.Pixels.Height), tile.Frame));
            string caption = $"{SheetLayout.ViewName(detail.ViewId)} - {AnnotationText.Clean(detail.Caption, 60)}";
            int font = Math.Min(ViewCaption.DefaultSize, Math.Max(12, (int)((box.Width - 32) / Math.Max(1, caption.Length * .62f))));
            float center = inkLeft <= inkRight ? left + (inkLeft + inkRight) * .5f : box.Center.X;
            float textWidth = caption.Length * NativeText.MonoWidth(font);
            int captionX = (int)Math.Clamp(center - textWidth / 2, box.X + 8, Math.Max(box.X + 8, box.Right - 8 - textWidth));
            int captionY = top + inkBottom + 18;
            canvas.Text(caption, captionX, captionY, font, 1);
            string scale = "SCALE " + PrintScale.Ratio(tile.Frame, original.Options);
            canvas.Text(scale, (int)(center - scale.Length * NativeText.MonoWidth(15) / 2), captionY + font + 4, 15, 1);
            reserved.Add(new(box.X, captionY, box.Width, font + 23));
            rendered.Add(tile with
            {
                CaptionBounds = new(captionX, captionY, textWidth, font + 23)
            });
        }
        NativeText.Apply(canvas.Pixels, w, h, canvas.Runs);
        var scene = new RenderScene(canvas.Pixels, owners, parts, views.ToArray(), reserved.ToArray(), original.Options, viewOwners)
        {
            MeshOwners = meshOwners,
            MeshNodes = source.Assembly.MeshNodes,
            Details = rendered.ToArray(),
            CraftHeight = baseline.CraftHeight,
            ViewHeights = baseline.ViewHeights
        };
        scene = scene.WithTables(tables ?? displayed.Scene?.Tables ?? new()).WithCaptions(captions ?? displayed.Scene?.Captions ?? baseline.Captions);
        scene = scene.WithDimensions(displayed.Scene?.Dimensions ?? []).WithFreeText(displayed.Scene?.FreeText ?? []);
        scene = scene.WithKittens(displayed.Scene?.Kittens ?? [], displayed.Scene?.KittenModel);
        scene = scene.WithPoses(baseline.Poses).WithPerformance(baseline.Performance).WithSpecs(displayed.Scene?.Specs ?? []).WithDrawing(baseline.Drawing);
        // A frame may cover the main drawing, but must not destroy its label anchors.
        foreach (var item in baseline.Visible)
            scene.Visible[item.Key] = item.Value;
        var adjusted = labels.Where(l => l.ViewId < 1000 || scene.Visible.ContainsKey((l.ViewId, l.PartId))).Select(label =>
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
                X = Math.Clamp(label.X + shift.X, 0, w - textBox.Width),
                Y = Math.Clamp(label.Y + shift.Y, 0, h - textBox.Height)
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
}
