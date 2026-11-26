using Blueprint.Rendering;
using Brutal.ImGuiApi;
using KSA;
namespace Blueprint;

public sealed partial class BlueprintMod
{


    static void BeginHeight(HeightDimension dimension)
    {
        var scene = document!.Scene;
        var labels = document.Labels;
        dimension = HeightDimensions.Move(scene, dimension, HeightDimensions.Place(scene, dimension, false).X);
        var mark = HeightDimensions.Place(scene, dimension);
        StartGhost("height dimension", _ => PlacementPreviews.Panel(scene, mark.Bounds, c => HeightDimensions.Paint(c, scene.WithoutPanels().WithDimensions([dimension]), labels)),
            b => QueueHeight(HeightDimensions.Move(document!.Scene, dimension, b.Center.X), dimension.ViewId),
            b => PanelClear(b, int.MinValue), b => b with { Y = mark.Bounds.Y });
    }


    static bool PanelClear(Box box, int except) => Options.FreeLayout ? CanvasBounds.Safe(box) : PanelNeighbors(except).All(b => !b.Overlaps(box)) && document!.Scene.Dimensions.All(d => !HeightDimensions.Place(document.Scene, d).Bounds.Overlaps(box));


    static Box PositionPanel(Box box, int id) => FrameLayout.Clamp(SnapPanel(box, id, false, 1), Options, box.Width, box.Height);


    static void PlaceSpec(SpecBlock block)
    {
        var scene = document!.Scene.WithSpecs([block]);
        var labels = document.Labels;
        StartGhost("performance block", _ => PlacementPreviews.Panel(scene, block.Bounds, c => PerformancePainting.Paint(c, scene, labels)),
            b => QueueSpecs([.. document!.Scene.Specs, block with { Bounds = b }]), b => PanelClear(b, -1000 - block.Id), b => PositionPanel(b, -1000 - block.Id));
    }


    static void QueueTableVisibility(int id, bool visible, Box? bounds = null)
    {
        if (busy || sheet == null || document == null)
            return;
        var options = sheet.Options with
        {
            PartsTable = id == 0 ? visible : sheet.Options.PartsTable,
            FlagTable = id == 1 ? visible : sheet.Options.FlagTable
        };
        var scene = document.Scene.WithOptions(options);
        if (bounds is { } b)
            scene = scene.WithTables(scene.Tables.Set(id, b));
        var labels = document.Labels.Select(l => l with { ManualPlacement = true }).ToArray();
        var current = sheet with
        {
            Options = options,
            Scene = scene
        };
        renderKeepsDocument = true;
        renderAdoptsHistory = false;
        composingLabels = true;
        progress = "Placing table";
        StartRender(token => AnnotationPainting.Compose(current, labels, token) with { Notice = "Table updated. Drag its heading or resize grip. Ctrl+Z undoes this change." });
    }


    static void ShowTable(int id)
    {
        if (sheet == null || document == null)
            return;
        var options = sheet.Options with
        {
            PartsTable = id == 0 || sheet.Options.PartsTable,
            FlagTable = id == 1 || sheet.Options.FlagTable
        };
        var scene = document.Scene.WithOptions(options);
        var labels = document.Labels;
        var box = PrintTables.Bounds(scene, labels, id);
        var clear = ClearPanelPlacement(box, PanelNeighbors(-1 - id), FrameLayout.Area(options), false,
            200, 100, FrameLayout.Gap, b => PrintTables.Font(scene, labels, id, b) > 0,
            boundary: PageLayout.TitleBounds(options));
        if (clear is not { } placed)
        {
            ReportFailure("There is no clear space for this table.");
            return;
        }
        QueueTableVisibility(id, true, placed);
    }


    static void DrawTablePlacementControls()
    {
        ImGui.BeginDisabled(ControlsLocked);
        TableToggle("Parts Table", 0, partsTable);
        TableToggle("Flagnotes Table", 1, flagTable);
        ImGui.EndDisabled();
    }

    static void TableToggle(string name, int id, bool shown)
    {
        if (!ImGui.Checkbox(name, ref shown))
            return;
        if (id == 0)
            partsTable = shown;
        else
            flagTable = shown;
    }


    static void BeginKitten(KittenReference kitten)
    {
        if (sheet?.Scene == null || document == null)
            return;
        if (kitten.SourceView == null && document.Scene.Views.All(v => v.Id >= 1000))
        {
            ReportFailure("Place the primary view first to establish the reference scale.");
            return;
        }
        var scene = document.Scene;
        string? path = kitten.Kind == ScaleReferenceKind.Kitten && scene.KittenModel == null ? ModLibrary.Get<Gltf2Reference>("KittenGlb").Source?.Value?.ModPath : null;
        KittenModel? model = scene.KittenModel;
        StartGhost(kitten.Kind.ToString().ToLowerInvariant() + " reference", token =>
        {
            if (kitten.Kind == ScaleReferenceKind.Kitten)
                model ??= KittenModel.Load(path ?? throw new InvalidOperationException("Kitten model unavailable."));
            var posed = KittenReferences.PlacementScene(scene, kitten, model);
            var b = KittenReferences.Bounds(posed, posed.Kittens[0]);
            return PlacementPreviews.Panel(posed, b, c => KittenReferences.Paint(c, posed, token));
        }, b =>
        {
            document!.Rebind(document.Scene.WithKittens(document.Scene.Kittens, model), document.Labels, document.Details, false);
            QueueKittens([.. document.Scene.Kittens, kitten with { X = b.X, Y = b.Y }]);
            activePlaceTool = PlaceTool.None;
        });
    }


    static void PreviewDetail(ExplodedDetail detail)
    {
        var current = sheet!;
        var source = current.PoseSource!;
        var options = current.Options;
        ExplodedDetail prepared = detail;
        float previewInset = 0;
        StartGhost("exploded detail", token =>
        {
            var assembly = PartPoses.Apply(source, current.Scene!.Poses, detail.ViewId);
            var tile = ExplodedGeometry.Render(new(assembly, source.Maps), detail, token, options);
            prepared = tile.Definition;
            string heading = SheetLayout.ViewName(detail.ViewId), caption = AnnotationText.Clean(detail.Caption, 60);
            string scale = "SCALE " + PrintScale.Ratio(tile.Frame, options);
            int font = 32;
            float textWidth = Math.Max(Math.Max(heading.Length, caption.Length) * NativeText.MonoWidth(font), scale.Length * NativeText.MonoWidth(15));
            int width = (int)MathF.Ceiling(Math.Max(prepared.Bounds.Width, textWidth + 16));
            int height = (int)MathF.Ceiling(prepared.Bounds.Height);
            previewInset = MathF.Floor((width - prepared.Bounds.Width) / 2);
            var canvas = new Canvas(new byte[checked(width * height * 3)], width, height, new(255));
            int left = tile.Pixels.Width, right = -1, bottom = -1;
            for (int y = 0; y < tile.Pixels.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < tile.Pixels.Width; x++)
                {
                    int i = y * tile.Pixels.Width + x;
                    canvas.Ink((int)previewInset + 12 + x, 12 + y, tile.Pixels.Ink[i] / 255f);
                    if (tile.Pixels.Ink[i] > 0 || tile.Pixels.MeshOwners[i] >= 0)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }
            float center = previewInset + 12 + (left + right) / 2f;
            int captionY = 12 + Math.Max(0, bottom) + 18;
            canvas.Text(heading, (int)(center - heading.Length * NativeText.MonoWidth(font) / 2), captionY, font, 1);
            canvas.Text(caption, (int)(center - caption.Length * NativeText.MonoWidth(font) / 2), captionY + font + 4, font, 1);
            canvas.Text(scale, (int)(center - scale.Length * NativeText.MonoWidth(15) / 2), captionY + 2 * font + 8, 15, 1);
            NativeText.Apply(canvas.Pixels, width, height, canvas.Runs);
            var rgba = new byte[checked(width * height * 4)];
            for (int i = 0; i < width * height; i++)
            {
                rgba[i * 4] = 255;
                rgba[i * 4 + 1] = 221;
                rgba[i * 4 + 2] = 68;
                rgba[i * 4 + 3] = canvas.Pixels[i * 3];
            }
            return new PlacementPreview(width, height, rgba, new(0, 0, width, height), new(center, captionY + font));
        }, b => QueueDetail(prepared with { Bounds = new(b.X + previewInset, b.Y, prepared.Bounds.Width, prepared.Bounds.Height) }),
            b => PanelClear(b, detail.Id), b => PositionPanel(b, detail.Id));
    }
}
