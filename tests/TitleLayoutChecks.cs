using Blueprint.Rendering;
using System.Numerics;
using System.Text.Json;
using System.Buffers.Binary;

static class TitleLayoutChecks
{

    public static void Run(Action<bool, string> check, Capture craft, Dictionary<string, Texture> maps, string output)
    {
        var frame = new ProjectionFrame(1000, 1000, 100, Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
        check(PrintScale.Ratio(frame) == "1:118.11" && PrintScale.Ratio(frame with
        {
            Scale = 11811
        }) == "1:1" && PrintScale.Ratio(frame with
        {
            Scale = 23622
        }) == "2:1", "Print scale converts world metres to printed metres at encoded PNG density");
        check(PrintScale.Ratio(null) == "NOT TO SCALE" && PrintScale.Ratio(frame with
        {
            Scale = 0
        }) == "NOT TO SCALE", "Unknown projection cannot claim a calculated scale");
        var old = JsonSerializer.Deserialize<TitleBlockData>("{\"Company\":\"Existing company\"}")!;
        check(old.Subtitle == "GENERAL ARRANGEMENT" && old.Revision == "0" && old.Title == "" && old.Scale == "", "Existing title preferences inherit safe title/revision/scale defaults");
        var meta = new TitleBlockData(Company: "Example Aerospace Lab", Creator: "Your name", CreatorInitials: "YN", DrawingDate: "2026-11-12", ApprovalDate: "2026-11-12", Title: "Lander Systems", Subtitle: "RCS SERVICE ASSEMBLY", Revision: "03");
        string dir = Path.Combine(output, "title-layout-0.4.1");
        Directory.CreateDirectory(dir);
        foreach (var (name, shape, aspect) in new[] { ("portrait", 0, 0), ("narrow-portrait", 0, 3), ("square", 1, 0), ("landscape", 2, 3) })
        {
            var options = new PrintOptions(Layout: 2, DrawingSize: 100, Theme: 3, Orientation: shape, Aspect: aspect, TitleBlock: meta, Boxes: new(A: 3, B: -1, C: 2, D: 4));
            var colors = SheetLayout.Colors(options.Theme);
            var canvas = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
            PageLayout.Decorate(canvas, "NewRocket_1", options, "1:100");
            check(canvas.Runs.Any(r => r.Text == "LANDER SYSTEMS") && canvas.Runs.Any(r => r.Text == meta.Subtitle) && canvas.Runs.Any(r => r.Text == "REV") && canvas.Runs.Any(r => r.Text == "03"), name + ": custom title, subtitle and revision replace defaults");
            var revLabel = canvas.Runs.Single(r => r.Text == "REV");
            var revValue = canvas.Runs.Single(r => r.Text == "03");
            check(revLabel.Height == revValue.Height && revLabel.Y == revValue.Y && Math.Abs(revLabel.X + 3 * NativeText.MonoWidth(revLabel.Height) / 2 - (CompanyTitleBlock.Layout(options).Bounds.X + 32)) < 1 && Math.Abs(revValue.X + 2 * NativeText.MonoWidth(revValue.Height) / 2 - (CompanyTitleBlock.Layout(options).Bounds.X + 114)) < 1, name + ": REV and revision share a centered baseline, font size and cell alignment");
            check(canvas.Runs.Where(r => r.Text == "LANDER SYSTEMS" || r.Text == meta.Subtitle || r.Text.StartsWith("SCALE ") || r.Text == SheetLayout.ViewSummary(options) || r.Text.StartsWith("EXPORTED ")).All(r => r.X == revLabel.X), name + ": title, subtitle, scale, view list and export date align to the centred REV label's left edge");
            var credit = canvas.Runs.Where(r => r.Y == CompanyTitleBlock.CreditTop(options) && r.X >= 238).ToArray();
            string creditText = string.Concat(credit.Select(r => r.Text));
            int right = CompanyTitleBlock.Layout(options).Split;
            check(creditText.StartsWith("PRINT GENERATED IN PAWPRINTS") && creditText.EndsWith("DYNAMIC MANUFACTURING N' DESIGN") && credit.All(r => r.Italic) && credit.Max(r => r.X + r.Height * .6f) < right - 8, name + ": entire credit is italic and fits after REV cells");
            NativeText.Apply(canvas.Pixels, options.Page.Width, options.Page.Height, canvas.Runs);
            int cropHeight = PageLayout.Bottom(options) - CompanyTitleBlock.Top(options) + 30;
            var crop = new byte[options.Page.Width * cropHeight * 3];
            Array.Copy(canvas.Pixels, (CompanyTitleBlock.Top(options) - 15) * options.Page.Width * 3, crop, 0, crop.Length);
            string cropPath = Path.Combine(dir, name + "-title.png");
            if (File.Exists(cropPath))
                File.Delete(cropPath);
            Png.Write(cropPath, options.Page.Width, cropHeight, crop);
        }
        var custom = meta with
        {
            Scale = "REFERENCE ONLY"
        };
        check(PrintScale.Title("NewRocket_1", meta) == "Lander Systems" && PrintScale.Title("NewRocket_1", meta with
        {
            Title = " "
        }) == "NewRocket_1" && PrintScale.Text(custom, "1:20") == "REFERENCE ONLY", "Blank titles use craft name and custom scale text overrides calculation");
        // Fast analytical fixture for exhaustive box positions/fitting. Rendering
        // below still uses the full original Lander capture and accepted renderer.
        var mesh = craft.Meshes[0] with
        {
            Vertices = [[-2, -3, -5], [2, 3, 5], [-2, 3, 5], [2, -3, 5], [-2, -3, 5], [2, 3, -5], [-2, 3, -5], [2, -3, -5]]
        };
        var tiny = new Capture([mesh]);
        bool fits = true, stable = true, repeat = true;
        int cases = 0;
        for (int shape = 0; shape < 3; shape++)
            for (int aspect = 0; aspect < 6; aspect++)
                for (int template = 0; template < FixedViewLayouts.Names.Length; template++)
                    foreach (bool table in new[] { false, true })
                        foreach (bool captions in new[] { false, true })
                            foreach (float share in new[] { .1f, .5f, .9f })
                            {
                                var options = new PrintOptions(Layout: 2, DrawingSize: 100, Orientation: shape, Aspect: aspect, PartsTable: table, Captions: captions, TitleBlock: meta, Boxes: new(template, 3, 4, 1, 2, 0, 1, TopShare: share, AutoRows: false));
                                var planned = SheetLayout.Plan(tiny, options);
                                var area = PageLayout.Drawing(options);
                                cases++;
                                foreach (var v in planned)
                                {
                                    var f = v.Frame!;
                                    fits &= v.Bounds.X >= area.X && v.Bounds.Y >= area.Y && v.Bounds.Right <= area.Right && v.Bounds.Bottom + (captions ? 56 : 0) <= area.Bottom && f.Scale > 0;
                                    fits &= mesh.Vertices.Select(ExplodedGeometry.World).All(p => Math.Abs(Vector3.Dot(p - f.Center, f.Right)) * f.Scale < f.Width / 2 && Math.Abs(Vector3.Dot(p - f.Center, f.Up)) * f.Scale < f.Height / 2);
                                    fits &= !planned.Any(other => other.Id != v.Id && other.Bounds.Overlaps(v.Bounds));
                                }
                                foreach (var omitted in planned)
                                {
                                    var blank = options with
                                    {
                                        Boxes = options.Boxes!.Set(omitted.Id - 100, -1)
                                    };
                                    var after = SheetLayout.Plan(tiny, blank);
                                    stable &= after.Length == planned.Length - 1 && after.All(v => v == planned.Single(p => p.Id == v.Id));
                                    var cell = FixedViewLayouts.Cells(options.Boxes!, area)[omitted.Id - 100];
                                    stable &= FixedViewLayouts.EmptyFrame(blank, cell.Center) == cell && ExplodedPainting.ClampFrame(cell, blank) == cell;
                                }
                                var repeated = SheetLayout.Plan(tiny, options with
                                {
                                    Boxes = new(template, 2, 2, 2, 2, 2, 2)
                                });
                                repeat &= repeated.Select(v => v.Id).Distinct().Count() == repeated.Length && repeated.All(v => v.CameraView == 2);
                            }
        check(fits, $"All {cases} fixed layouts fit across page ratios, table states and captions without overlapping or clipping");
        check(stable, "Blanking any box preserves every other view's identity, position and scale; blank frames fit exactly");
        check(repeat, "Repeated camera directions retain independent box identities for picking and labels");
        var tall = new Capture([mesh with { Vertices = mesh.Vertices.Select(v => new[] { v[0] * 5, v[1] * .2, v[2] * .2 }).ToArray() }]);
        var equal = new PrintOptions(Layout: 2, DrawingSize: 100, TitleBlock: meta, Boxes: new(A: 3, B: -1, C: 2, D: 4, AutoRows: false));
        var fitted = equal with
        {
            Boxes = FixedViewLayouts.FitRows(tall, equal)
        };
        var before = SheetLayout.Plan(tall, equal);
        var fittedViews = SheetLayout.Plan(tall, fitted);
        check(fitted.Boxes!.Assignments.SequenceEqual(equal.Boxes!.Assignments) && fitted.Boxes.TopShare < .35f && fittedViews.Min(v => v.Frame!.Scale) > before.Min(v => v.Frame!.Scale) * 1.3f, "Explicit row fitting gives tall lower views more room without moving or filling numbered boxes");
        var justTop = equal with
        {
            Boxes = new(Template: 1, A: 3)
        };
        var top = SheetLayout.Plan(tall, justTop);
        check(top[0].Frame!.Scale > before[0].Frame!.Scale * 3, "An unused tall side camera no longer shrinks an assigned top-only view");
        var blankMemory = new FixedViewLayout(Template: 2, A: 3, B: 2).Set(1, -1);
        check(blankMemory.FittingViews[1] == 2 && blankMemory.Set(1, -1) == blankMemory && blankMemory.ClearBlankFits().FittingViews[1] == -1, "Blanking retains only its previous camera envelope until an explicit refit");
        check(FixedViewLayouts.FitRows(tall, fitted) == fitted.Boxes, "Repeated row fitting is stable and does not drift the layout");
        var automatic = equal with
        {
            Boxes = equal.Boxes! with
            {
                AutoRows = true
            }
        };
        check(SheetLayout.Plan(tall, automatic).SequenceEqual(fittedViews), "Default automatic fitting produces the same enlarged views without a fit-button click");
        bool autoStable = true, autoFits = true;
        for (int shape = 0; shape < 3; shape++)
            for (int aspect = 0; aspect < 6; aspect++)
                for (int template = 0; template < FixedViewLayouts.Names.Length; template++)
                {
                    var auto = automatic with
                    {
                        Orientation = shape,
                        Aspect = aspect,
                        Boxes = new(template, 3, 4, 1, 2, 0, 1)
                    };
                    var resolved = FixedViewLayouts.Resolve(tall, auto);
                    var views = SheetLayout.Plan(tall, auto);
                    foreach (var v in views)
                    {
                        var f = v.Frame!;
                        autoFits &= tall.Meshes[0].Vertices.Select(ExplodedGeometry.World).All(p => Math.Abs(Vector3.Dot(p - f.Center, f.Right)) * f.Scale < f.Width / 2 && Math.Abs(Vector3.Dot(p - f.Center, f.Up)) * f.Scale < f.Height / 2);
                        var blank = resolved with
                        {
                            Boxes = resolved.Boxes!.Set(v.Id - 100, -1)
                        };
                        var after = SheetLayout.Plan(tall, blank);
                        autoStable &= after.All(p => p == views.Single(before => before.Id == p.Id));
                        var cell = FixedViewLayouts.Cells(resolved.Boxes!, PageLayout.Drawing(auto))[v.Id - 100];
                        autoStable &= FixedViewLayouts.EmptyFrame(blank, cell.Center) == cell;
                    }
                }
        check(autoFits, "Automatic row fitting fits all 216 template and page-shape combinations");
        check(autoStable, "Automatic fitting preserves other views and blank-frame targets when any slot is cleared");
        var enlarged = SheetLayout.Plan(tall, automatic with
        {
            DrawingSize = 150
        });
        var supercharged = SheetLayout.Plan(tall, automatic with
        {
            DrawingSize = 250
        });
        var normal = SheetLayout.Plan(tall, automatic);
        check(enlarged.All(v => v.Frame!.Scale > normal.Single(n => n.Id == v.Id).Frame!.Scale) && supercharged.All(v => v.Frame!.Scale > enlarged.Single(n => n.Id == v.Id).Frame!.Scale), "View scale above 100 percent enlarges actual projection through 250 percent");
        check(enlarged.Select(v => v.Frame!.Scale).Distinct().Count() == 1 && enlarged.Select(v => v.Bounds).SequenceEqual(normal.Select(v => v.Bounds)), "Enlargement preserves global physical scale and fixed view slots");
        var captionView = new ViewRegion(100, "TOP", new(100, 100, 1000, 800), null);
        var caption = ViewCaption.Place(captionView, new(520, 240, 160, 150));
        check(Math.Abs(caption.Bounds.Center.X - 600) < 1 && caption.Bounds.Y == 408 && caption.Bounds.Bottom < captionView.Bounds.Bottom, "View captions center directly below the visible craft instead of the slot corner");
        var choice = new PrintOptions(Layout: 2, DrawingSize: 100, Theme: 3, Orientation: 2, Aspect: 3, TitleBlock: meta, Boxes: new(A: 3, B: -1, C: 2, D: 4));
        var parts = craft.Meshes.Select((m, i) => new LabelPart(i, m.Name, m.Name, [i])).ToArray();
        var print = SheetLayout.Render(craft, maps, "NewRocket_1", parts, choice, [], default, _ => { });
        var heights = HeightDimensions.Measure(craft);
        check(Math.Abs((heights.Top - heights.Bottom) - (craft.Meshes.SelectMany(m => m.Vertices).Max(v => v[0]) - craft.Meshes.SelectMany(m => m.Vertices).Min(v => v[0]))) < .0001, "Craft height comes from captured metre geometry, independent of paper scale");
        var dim = new HeightDimension(102, Left: true, Gap: 90);
        var markedScene = print.Scene!.WithDimensions([dim]);
        var mark = HeightDimensions.Place(markedScene, dim);
        check(Math.Abs(mark.Bottom - mark.Top - (heights.Top - heights.Bottom) * print.Scene.Views.Single(v => v.Id == 102).Frame!.Scale) < .01, "Side height ticks span the actual scaled height axis");
        var isoDim = dim with
        {
            ViewId = 103
        };
        var iso = HeightDimensions.Place(print.Scene, isoDim);
        check(iso.Text == mark.Text && iso.Bottom - iso.Top < mark.Bottom - mark.Top && !HeightDimensions.Eligible(print.Scene.Views.Single(v => v.Id == 100)), "Isometric foreshortens the bracket without changing metres; top view cannot claim height");
        var measured = AnnotationPainting.Compose(print with
        {
            Scene = markedScene
        }, [], default);
        check(!measured.Rgb.SequenceEqual(print.Rgb) && measured.Scene!.Dimensions.Single() == dim, "Height bracket is rendered into actual export pixels");
        var dimensionDoc = new AnnotationDocument(print.Scene);
        dimensionDoc.Rebind(markedScene, [], []);
        dimensionDoc.Undo();
        check(dimensionDoc.Scene.Dimensions.Length == 0, "Undo removes a height dimension");
        dimensionDoc.Redo();
        check(dimensionDoc.Scene.Dimensions.Single() == dim, "Redo restores its view, side and gap");
        var remeasured = CompositionRegeneration.Rebuild(print, measured, default);
        check(remeasured.Scene!.Dimensions.Single() == dim && HeightDimensions.Place(remeasured.Scene, dim).Text == mark.Text, "Generate retains measured height annotations");
        var moved = HeightDimensions.Move(markedScene, dim, print.Scene.Views.Single(v => v.Id == 102).Bounds.Right + 40);
        var movedMark = HeightDimensions.Place(markedScene, moved);
        check(moved.X != null && !moved.Left && movedMark.Top == mark.Top && movedMark.Bottom == mark.Bottom && movedMark.Text == mark.Text && moved.ViewId == dim.ViewId, "Dragging height between views crosses sides without changing its measurement or source view");
        check(Math.Abs(movedMark.X - (print.Scene.Views.Single(v => v.Id == 102).Bounds.Right + 40)) < .01, "Dragged height uses the requested paper coordinate, not a box-constrained side slot");
        var backLeft = HeightDimensions.Move(markedScene, moved, mark.X);
        check(backLeft.Left && Math.Abs(HeightDimensions.Place(markedScene, backLeft).X - mark.X) < .01, "Dragging a height back across the craft restores the left-side position");
        var edge = HeightDimensions.Place(markedScene, HeightDimensions.Move(markedScene, dim, -10000));
        check(edge.Bounds.X >= FrameLayout.Area(choice).X, "Height drag keeps its full tick and text width inside printable paper");
        check(HeightDimensions.Hit(mark, mark.TextBounds.Center, 8) && HeightDimensions.Hit(mark, new(mark.X, mark.Top + 12), 8) && HeightDimensions.Hit(mark, new(mark.X + 20, mark.Bottom), 8) && !HeightDimensions.Hit(mark, new(mark.X + 100, mark.Top + 12), 8), "Height drag can grab the number, vertical bracket or end ticks without claiming nearby paper");
        var movedScene = markedScene.WithDimensions([moved]);
        dimensionDoc.Rebind(movedScene, [], []);
        dimensionDoc.Undo();
        check(dimensionDoc.Scene.Dimensions.Single() == dim, "One Undo restores the bracket before the drag");
        dimensionDoc.Redo();
        check(dimensionDoc.Scene.Dimensions.Single() == moved, "Redo restores the exact dragged bracket coordinate");
        var movedPrint = AnnotationPainting.Compose(print with
        {
            Scene = movedScene
        }, [], default);
        var movedAgain = CompositionRegeneration.Rebuild(print, movedPrint, default);
        check(movedAgain.Scene!.Dimensions.Single() == moved, "Regeneration retains a bracket placed between views");
        string heightPath = Path.Combine(dir, "lander-height.png");
        if (File.Exists(heightPath))
            File.Delete(heightPath);
        Png.Write(heightPath, measured.PixelWidth, measured.PixelHeight, measured.Rgb);
        check(print.Options == FixedViewLayouts.Resolve(craft, choice) && print.Scene!.Options == print.Options, "Rendered sheet and scene retain resolved layout options for UI editing and export");
        check(print.Title == meta.Title && print.Scene!.Views.Select(v => v.Id).SequenceEqual(new[] { 100, 102, 103 }), "Real Lander render leaves upper-right box blank and uses custom export title");
        check(print.Scene!.Views.Select(v => v.Frame!.Scale).Distinct().Count() == 1 && PrintScale.Summary(print.Scene.Views, choice) == PrintScale.Ratio(print.Scene.Views[0].Frame, choice), "All main views share a single paper-based global scale");
        check(print.Scene.Reserved.Length == print.Scene.Views.Length && print.Scene.Views.Select((v, i) =>
        {
            var bounds = print.Scene.Visible.Where(p => p.Key.View == v.Id).Select(p => p.Value.Bounds).ToArray();
            var reserved = print.Scene.Reserved[i];
            float center = (bounds.Min(b => b.X) + bounds.Max(b => b.Right)) / 2;
            var rowViews = print.Scene.Views.Where(other => Math.Abs(other.Bounds.Y - v.Bounds.Y) < 1 && Math.Abs(other.Bounds.Bottom - v.Bounds.Bottom) < 1).Select(other => other.Id).ToHashSet();
            float rowBottom = print.Scene.Visible.Where(p => rowViews.Contains(p.Key.View)).Max(p => p.Value.Bounds.Bottom);
            return Math.Abs(reserved.Center.X - center) < 1 && reserved.Y == rowBottom + 18;
        }).All(ok => ok), "Real captions remain centered under each craft while sharing a row baseline and reserved regions");
        var sizes = print.Scene.Captions.Select(c => c.ViewId == 102 ? ViewCaption.Resize(c, print.Scene.Views.Single(v => v.Id == 102), 60) : c).ToArray();
        var aligned = print.Scene.WithCaptions(sizes);
        check(aligned.Captions.Where(c => c.ViewId is 102 or 103).Select(c => c.Bounds.Y + c.FontSize).Distinct().Count() == 1 && aligned.WithCaptions(aligned.Captions).Captions.SequenceEqual(aligned.Captions), "Caption rows keep a shared baseline with different font sizes and do not drift on repaint");
        string movedPath = Path.Combine(dir, "lander-height-dragged.png");
        if (File.Exists(movedPath))
            File.Delete(movedPath);
        Png.Write(movedPath, movedPrint.PixelWidth, movedPrint.PixelHeight, movedPrint.Rgb);
        string exported = PrintFiles.Save(print, dir);
        var bytes = File.ReadAllBytes(exported);
        bool physical = false;
        for (int p = 8; p < bytes.Length;)
        {
            int n = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(p));
            if (System.Text.Encoding.ASCII.GetString(bytes, p + 4, 4) == "pHYs")
                physical = n == 9 && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(p + 8)) == PrintScale.Density(choice) && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(p + 12)) == PrintScale.Density(choice) && bytes[p + 16] == 1;
            p += n + 12;
        }
        check(physical && Png.ReadOwn(exported).SequenceEqual(print.Rgb), "Export embeds paper-based physical metadata without changing print pixels");
        File.Copy(exported, Path.Combine(dir, "lander-box-layout.png"), true);
        File.WriteAllText(Path.Combine(dir, "validation.json"), JsonSerializer.Serialize(new
        {
            cases,
            fits,
            stable,
            repeat,
            physical,
            exported
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
