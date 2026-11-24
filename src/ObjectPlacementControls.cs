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


    static bool PanelClear(Box box, int except) => PanelNeighbors(except).All(b => !b.Overlaps(box)) && document!.Scene.Dimensions.All(d => !HeightDimensions.Place(document.Scene, d).Bounds.Overlaps(box));


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
        var labels = FlagnoteNumbers.Normalize(document.Labels, options.FlagTable).Select(l => l with { ManualPlacement = true, Key = l.FlagNote ? l.Key : options.PartsTable && !scene.Parts.Single(p => p.Id == l.PartId).DetailOnly ? PrintTables.PartKey(scene, l.PartId) : null }).ToArray();
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
        var clear = FrameLayout.Clear(box, PanelNeighbors(-1 - id), FrameLayout.Area(options), false,
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
        if (document.Scene.Views.All(v => v.Id >= 1000))
        {
            status = "Place the primary view first to establish the reference scale.";
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
        }, b => { document!.Rebind(document.Scene.WithKittens(document.Scene.Kittens, model), document.Labels, document.Details, false); QueueKittens([.. document.Scene.Kittens, kitten with { X = b.X, Y = b.Y }]); });
    }


    static void PreviewDetail(ExplodedDetail detail)
    {
        var current = sheet!;
        var source = current.PoseSource!;
        var options = current.Options;
        StartGhost("exploded detail", token =>
        {
            var assembly = PartPoses.Apply(source, current.Scene!.Poses, detail.ViewId);
            var tile = ExplodedGeometry.Render(new(assembly, source.Maps), detail, token, options);
            System.Numerics.Vector2? anchor = null;
            var preview = PlacementPreviews.Panel(current.Scene!, detail.Bounds, c =>
            {
                for (int y = 0; y < tile.Pixels.Height; y++)
                    for (int x = 0; x < tile.Pixels.Width; x++)
                        c.Ink((int)detail.Bounds.X + 12 + x, (int)detail.Bounds.Y + 12 + y, tile.Pixels.Ink[y * tile.Pixels.Width + x] / 255f);
                int bottom = 0, left = tile.Pixels.Width, right = 0;
                for (int y = 0; y < tile.Pixels.Height; y++)
                    for (int x = 0; x < tile.Pixels.Width; x++)
                        if (tile.Pixels.MeshOwners[y * tile.Pixels.Width + x] >= 0)
                        {
                            bottom = Math.Max(bottom, y);
                            left = Math.Min(left, x);
                            right = Math.Max(right, x);
                        }
                string caption = SheetLayout.ViewName(detail.ViewId) + " - " + AnnotationText.Clean(detail.Caption, 60);
                int font = Math.Min(ViewCaption.DefaultSize, Math.Max(12, (int)((detail.Bounds.Width - 32) / Math.Max(1, caption.Length * .62f))));
                float center = detail.Bounds.X + 12 + (left + right) * .5f;
                float textWidth = caption.Length * NativeText.MonoWidth(font);
                int labelX = (int)Math.Clamp(center - textWidth / 2, detail.Bounds.X + 8, Math.Max(detail.Bounds.X + 8, detail.Bounds.Right - 8 - textWidth)), labelY = (int)detail.Bounds.Y + 12 + bottom + 18;
                c.Text(caption, labelX, labelY, font, 1);
                anchor = new(labelX + textWidth / 2 - detail.Bounds.X, labelY + font - detail.Bounds.Y);
                string scale = "SCALE " + PrintScale.Ratio(tile.Frame, options);
                c.Text(scale, (int)(center - scale.Length * NativeText.MonoWidth(15) / 2), labelY + font + 4, 15, 1);
            });
            return preview with
            {
                CaptionAnchor = anchor
            };
        }, b => QueueDetail(detail with { Bounds = b }), b => PanelClear(b, detail.Id), b => PositionPanel(b, detail.Id));
    }
}
