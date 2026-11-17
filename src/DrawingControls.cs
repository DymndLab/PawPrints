using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static DrawingPlacement? drawingPlacement = new(100, []);
    static DrawingView? viewDraft, dragView;
    static float draftRatio = 100;
    static Vec viewPress;
    static int viewMenu = -1, selectedView = -1, selectedCanvasView = -1;
    static int projectionStyle; // Third-angle default; first-angle reverses the unfolded faces.
    static PartTree? workspaceAttempt;
    static Task<PlacementPreview>? ghostTask;
    static CancellationTokenSource? ghostCancel;
    static PlacementPreview? ghostPixels;
    static PreviewTexture? ghostTexture;
    static readonly List<PreviewTexture> retiredGhosts = [];
    static Action<Box>? ghostCommit;
    static Func<Box, Box>? ghostPosition;
    static Func<Box, bool>? ghostValid;
    static string ghostName = "";
    static bool ghostWholePage;
    static string viewPlacementIssue = "";
    static bool GhostActive => ghostTask != null || ghostCommit != null;


    static void CancelGhost()
    {
        ghostCancel?.Cancel();
        ghostCancel?.Dispose();
        ghostCancel = null;
        if (ghostTask != null)
            _ = ghostTask.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        ghostTask = null;
        ghostPixels = null;
        ghostCommit = null;
        ghostPosition = null;
        ghostValid = null;
        ghostWholePage = false;
        viewDraft = null;
        dragView = null;
        if (ghostTexture != null)
        {
            retiredGhosts.Add(ghostTexture);
            ghostTexture = null;
        }
    }


    static void DisposeGhosts()
    {
        foreach (var texture in retiredGhosts)
            texture.Dispose();
        retiredGhosts.Clear();
    }


    static void PollGhost()
    {
        DisposeGhosts();
        if (ghostTask?.IsCompleted != true)
            return;
        var job = ghostTask;
        ghostTask = null;
        try
        {
            ghostPixels = job.GetAwaiter().GetResult();
            ghostTexture = new(ghostPixels.Width, ghostPixels.Height, ghostPixels.Rgba);
            status = "Click to place " + ghostName + ". Right-click cancels.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { var keep = viewDraft; CancelGhost(); viewDraft = keep; ReportFailure("Placement unavailable: " + e.Message); }
    }


    static void StartGhost(string name, Func<CancellationToken, PlacementPreview> prepare, Action<Box> commit, Func<Box, bool>? valid = null, Func<Box, Box>? position = null, bool wholePage = false)
    {
        CancelGhost();
        ghostWholePage = wholePage;
        ghostName = name;
        ghostCommit = commit;
        ghostValid = valid;
        ghostPosition = position;
        ghostCancel = new();
        var token = ghostCancel.Token;
        ghostTask = Task.Run(() => prepare(token), token);
        status = "Preparing " + name + " preview";
    }


    static bool DrawGhost(Vec point, float factor, float2 origin, bool hovered)
    {
        if (!GhostActive)
            return viewDraft != null;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CancelGhost();
            status = "Placement cancelled.";
            return true;
        }
        if (hovered && viewDraft is { RollDirection: >= 0, Parent: { } parentId } rolling)
        {
            var parent = CurrentDrawing().Views.Single(v => v.Id == parentId);
            var delta = point - new Vec(parent.X * sheet!.PixelWidth, parent.Y * sheet.PixelHeight);
            int direction = DrawingPlacements.ProjectionDirection(delta, rolling.RollDirection);
            if (direction != rolling.RollDirection && delta.Length() > 24 / factor)
            {
                viewDraft = DrawingPlacements.Roll(rolling, parent, direction, projectionStyle == 0 ? 3 : 1);
                PrepareView();
                return true;
            }
        }
        if (ghostPixels == null || ghostTexture?.Image is not { } image)
            return true;
        var area = ghostWholePage ? new Box(0, 0, Options.Page.Width, Options.Page.Height) : viewDraft != null ? DrawingPlacements.Area(Options) : FrameLayout.Area(Options);
        var raw = new Box(point.X - ghostPixels.Width / 2f, point.Y - ghostPixels.Height / 2f, ghostPixels.Width, ghostPixels.Height);
        var box = ghostPosition?.Invoke(raw) ?? FrameLayout.Clamp(raw, Options, raw.Width, raw.Height);
        if (ghostPixels.CaptionAnchor is { } anchor)
        {
            int alignment = viewDraft?.Parent != null ? viewDraft.Alignment : 0;
            if (viewDraft is { Parent: not null, RollDirection: < 0 } manual && !DrawingPlacements.Oblique(manual.Camera))
            {
                var parent = CurrentDrawing().Views.Single(v => v.Id == manual.Parent);
                alignment = Math.Abs(box.Center.X - parent.X * sheet!.PixelWidth) < Math.Abs(box.Y + (box.Height - (Options.Captions ? ViewCaption.Band : 0)) / 2 - parent.Y * sheet.PixelHeight) ? 1 : 2;
            }
            box = SnapCaptionBox(box, anchor, viewDraft?.Id ?? -1, factor, alignment == 1, alignment == 2,
                b => b.X >= area.X && b.Y >= area.Y && b.Right <= area.Right && b.Bottom <= area.Bottom && (ghostWholePage || PageLayout.Fits(b, Options)) && (ghostValid?.Invoke(b) ?? true));
        }
        bool inside = box.X >= area.X && box.Y >= area.Y && box.Right <= area.Right && box.Bottom <= area.Bottom && (ghostWholePage || PageLayout.Fits(box, Options));
        bool valid = (ghostValid?.Invoke(box) ?? true) && inside;
        if (hovered)
        {
            var a = origin + new float2(box.X * factor, box.Y * factor);
            var z = origin + new float2(box.Right * factor, box.Bottom * factor);
            var draw = ImGui.GetWindowDrawList();
            draw.AddImage(image, a, z);
            draw.AddRect(a, z, valid ? 0xFF8EDD8Eu : 0xFF7777FFu, 0, ImDrawFlags.None, 2);
            DrawAlignmentGuides(factor, origin);
            if (viewDraft is { } draft && (draft.RollDirection >= 0 || !DrawingPlacements.Oblique(draft.Camera)) && draft.Parent is { } parent && document?.Scene.Views.FirstOrDefault(v => v.Id == parent) is { } source)
            {
                var c = origin + new float2(source.Bounds.Center.X * factor, source.Bounds.Center.Y * factor);
                draw.AddLine(c, origin + new float2(box.Center.X * factor, (box.Y + (box.Height - (Options.Captions ? ViewCaption.Band : 0)) / 2) * factor), 0x889DDDFF, 1);
            }
            if (valid && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                var commit = ghostCommit!;
                CancelGhost();
                commit(box);
            }
            else if (!valid && viewDraft != null)
                PrintUiText.Tooltip(!inside ? "Keep the view and caption inside the drawing area." : viewPlacementIssue);
        }
        return true;
    }


    static void EnsureWorkspace(VehicleEditor editor)
    {
        if (templatesOpen || busy || sheet?.PoseSource != null)
            return;
        var tree = editor.EditingSpace.Parts;
        if (tree != null && !ReferenceEquals(workspaceAttempt, tree))
        {
            workspaceAttempt = tree;
            Generate(editor);
            return;
        }
        if (sheet != null)
            return;
        var options = Options;
        var colors = SheetLayout.Colors(options.Theme);
        var c = new Canvas(options.Page.Width, options.Page.Height, colors.Paper, colors.Ink, options);
        PageLayout.Decorate(c, editor.EditingSpace.Id, options);
        NativeText.Apply(c.Pixels, options.Page.Width, options.Page.Height, c.Runs);
        var scene = new RenderScene(c.Pixels, Enumerable.Repeat(-1, options.Page.Width * options.Page.Height).ToArray(), [], [], options: options);
        sheet = new(editor.EditingSpace.Id, options, c.Pixels, Sheet.MakePreview(c.Pixels), [])
        {
            Scene = scene
        };
        preview = new(sheet);
        document = new(scene);
        sourceEditor = editor;
        sourceCraft = editor.EditingSpace.Id;
        status = "Load a craft, then place its primary view.";
    }


    static DrawingPlacement CurrentDrawing() => document!.Scene.Drawing ?? DrawingPlacements.FromScene(document.Scene);


    static IEnumerable<Box> DrawingObstacles() => PanelNeighbors(int.MinValue).Concat(document!.Scene.Dimensions.Select(d => HeightDimensions.Place(document.Scene, d).Bounds));


    static void DeleteDrawingView(int id)
    {
        if (!CanEditAnnotations || !CurrentDrawing().Views.Any(v => v.Id == id))
            return;
        selectedCanvasView = -1;
        QueueDrawing(DrawingPlacements.Remove(CurrentDrawing(), id), removed: id);
    }


    static void QueueDrawing(DrawingPlacement drawing, bool undo = false, int removed = -1)
    {
        if (busy || sheet?.PoseSource == null || document == null)
            return;
        var current = sheet with
        {
            Scene = document.Scene,
            Labels = document.Labels,
            Details = document.Details
        };
        var source = sheet.PoseSource;
        var options = Options with
        {
            Drawing = drawing
        };
        if (removed >= 0)
        {
            var remaining = document.Scene.WithDimensions(document.Scene.Dimensions.Where(d => d.ViewId != removed).ToArray())
                .WithPoses(document.Scene.Poses.Where(p => p.View != removed).ToArray()).WithSpecs(document.Scene.Specs.Select(b => b.ViewId == removed ? b with { Leader = false } : b).ToArray());
            current = current with
            {
                Scene = remaining,
                Labels = current.Labels.Where(l => l.ViewId != removed).ToArray()
            };
        }
        var poses = current.Scene!.Poses;
        var obstacles = document.Details.Select(d => d.Bounds).Concat(PrintTables.Panels(document.Scene, current.Labels).Select(t => t.Bounds)).Concat(document.Scene.Specs.Select(b => b.Bounds)).ToArray();
        cancellation?.Dispose();
        cancellation = new();
        var token = cancellation.Token;
        renderKeepsDocument = true;
        renderAdoptsHistory = undo;
        composingLabels = true;
        progress = "Composing drawing views";
        renderTask = Task.Run(() =>
        {
            var fresh = PosePainting.RenderMain(source, current.Title, options, current.Warnings, poses, token, composeAnnotations: false);
            DrawingPlacements.Validate(fresh.Scene!.Views, options);
            var changed = fresh.Scene.Views.Where(v => current.Scene!.Views.FirstOrDefault(p => p.Id == v.Id)?.Bounds != v.Bounds).ToArray();
            DrawingPlacements.Validate(changed, options, obstacles);
            var result = CompositionRegeneration.Rebuild(fresh, current, token);
            return result with
            {
                RegeneratedBase = null,
                PoseMain = fresh,
                Notice = "Drawing updated. Project from any view, or place an independent view. Ctrl+Z undoes this change."
            };
        }, token);
    }


    static void BeginView(int? parent = null) => BeginViewDraft(parent, null);


    static void BeginIsometricView() => BeginViewDraft(null, 4);


    static void BeginViewDraft(int? parent, int? cameraOverride)
    {
        requestedSettingsTab = "Place";
        if (sheet?.PoseSource == null || document == null)
            return;
        var drawing = CurrentDrawing();
        if (drawing.Views.Length >= DrawingPlacements.Limit)
        {
            ReportFailure("Up to 12 main views per print.");
            return;
        }
        int id = Enumerable.Range(100, 899).First(i => !drawing.Views.Any(v => v.Id == i));
        int camera = cameraOverride ?? (parent is { } p ? drawing.Views.Single(v => v.Id == p).Camera switch
        {
            3 => 2,
            1 => 2,
            2 => 5,
            5 => 6,
            6 => 1,
            _ => 3
        } : drawing.Views.Length == 0 ? 0 : 4);
        draftRatio = drawing.Views.Length == 0 ? DrawingPlacements.SuggestRatio(PartPoses.Exterior(sheet.PoseSource, sheet.PoseSource.Assembly), camera, Options) : drawing.Ratio;
        viewDraft = new(id, camera, .5f, .5f, parent);
        if (parent is { } sourceId)
            viewDraft = DrawingPlacements.Roll(viewDraft, drawing.Views.Single(v => v.Id == sourceId), 0, projectionStyle == 0 ? 3 : 1);
        PrepareView();
    }


    static void PrepareView()
    {
        if (viewDraft is not { } view || sheet?.PoseSource == null || document == null)
            return;
        float ratio = draftRatio;
        var source = sheet.PoseSource;
        var options = Options;
        var drawing = CurrentDrawing();
        var poses = document.Scene.Poses;
        if (view.RollDirection >= 0 && view.Parent is { } sourceId)
            view = DrawingPlacements.Roll(view, drawing.Views.Single(v => v.Id == sourceId), view.RollDirection, projectionStyle == 0 ? 3 : 1);
        DrawingView Located(Box b)
        {
            var centre = new Vec(b.Center.X, b.Y + (b.Height - (options.Captions ? ViewCaption.Band : 0)) / 2);
            int align = 0;
            if (view.Parent is { } parent && (view.RollDirection is >= 0 and < 4 || view.RollDirection < 0 && !DrawingPlacements.Oblique(view.Camera)))
            {
                var p = drawing.Views.Single(v => v.Id == parent);
                var pc = new Vec(p.X * options.Page.Width, p.Y * options.Page.Height);
                align = view.RollDirection >= 0 ? view.Alignment : Math.Abs(centre.X - pc.X) < Math.Abs(centre.Y - pc.Y) ? 1 : 2;
                if (align == 1)
                    centre.X = pc.X;
                else
                    centre.Y = pc.Y;
            }
            return view with
            {
                X = centre.X / options.Page.Width,
                Y = centre.Y / options.Page.Height,
                Alignment = align,
                Parent = view.RollDirection < 0 && DrawingPlacements.Oblique(view.Camera) ? null : view.Parent
            };
        }
        DrawingPlacement Proposed(Box b) => drawing with
        {
            Ratio = ratio,
            Views = [.. drawing.Views, Located(b)]
        };
        bool Valid(Box b)
        {
            try
            {
                var placed = Located(b);
                var region = new ViewRegion(placed.Id, "", b with
                {
                    Height = b.Height - (options.Captions ? ViewCaption.Band : 0)
                }, null);
                DrawingPlacements.Validate([.. document!.Scene.Views.Where(v => v.Id < 1000), region], options);
                DrawingPlacements.Validate([region], options, DrawingObstacles());
                viewPlacementIssue = "";
                return true;
            }
            catch (InvalidOperationException e) { viewPlacementIssue = e.Message; return false; }
        }
        Box Position(Box box)
        {
            var placed = Located(box);
            return box with
            {
                X = MathF.Round(placed.X * options.Page.Width - box.Width / 2),
                Y = MathF.Round(placed.Y * options.Page.Height - (box.Height - (options.Captions ? ViewCaption.Band : 0)) / 2)
            };
        }
        StartGhost("view", token => PlacementPreviews.View(source, view, ratio, options, poses, token), b => QueueDrawing(Proposed(b)), Valid, Position);
        viewDraft = view;
        draftRatio = ratio;
    }


    static void DrawDrawingControls()
    {
        if (viewDraft is { } draft)
        {
            var parentView = CurrentDrawing().Views.FirstOrDefault(v => v.Id == draft.Parent);
            ImGui.Text(parentView == null ? "Place independent view" : "Project from " + DrawingPlacements.Name(parentView));
            if (parentView != null)
            {
                bool roll = draft.RollDirection >= 0;
                if (ImGui.Checkbox("Roll from source"u8, ref roll))
                {
                    viewDraft = roll ? DrawingPlacements.Roll(draft, parentView, 0, projectionStyle == 0 ? 3 : 1) : draft with
                    {
                        Axes = null,
                        RollDirection = -1
                    };
                    PrepareView();
                    draft = viewDraft!;
                }
            }
            if (draft.RollDirection >= 0)
            {
                ImGui.Text("Preview: " + DrawingPlacements.Name(draft));
                PrintUiText.Wrapped("Move along an axis for an aligned projection, or diagonally for a corner view. Return toward an axis to snap back. Click when green to place.");
            }
            else
            {
                int camera = draft.Camera;
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##View direction"u8, ref camera, DrawingPlacements.Names, DrawingPlacements.Names.Length))
                {
                    viewDraft = draft with
                    {
                        Camera = camera,
                        Axes = null,
                        RollDirection = -1
                    };
                    PrepareView();
                }
                // A compact visual compass complements the named directions.
                float cell = (ImGui.GetContentRegionAvail().X - 12) / 3;
                var start = ImGui.GetCursorScreenPos();
                int[] compass = [6, 3, 2, 5, 1, 7, 0, 4];
                for (int i = 0; i < compass.Length; i++)
                {
                    int c = compass[i];
                    var pos = start + new float2(i % 3 * (cell + 6), i / 3 * 80);
                    ImGui.SetCursorScreenPos(pos);
                    if (ImGui.InvisibleButton("##Direction" + c, new float2(cell, 74)))
                    {
                        viewDraft = viewDraft! with
                        {
                            Camera = c,
                            Axes = null,
                            RollDirection = -1
                        };
                        PrepareView();
                    }
                    var draw = ImGui.GetWindowDrawList();
                    draw.AddRect(pos, pos + new float2(cell, 74), viewDraft?.Camera == c ? 0xFFDBA86Du : 0xFF63513Du);
                    DrawTemplateView(pos + new float2(cell / 2, 28), 23, c >= 5 ? c - 4 : c);
                    string name = DrawingPlacements.Names[c];
                    float font = Math.Min(ImGui.GetFontSize(), (cell - 6) / Math.Max(1, ImGui.CalcTextSize(name).X) * ImGui.GetFontSize());
                    draw.AddText(ImGui.GetFont(), font, pos + new float2(3, 55), 0xFFE5CFB4, name);
                }
                ImGui.SetCursorScreenPos(start + new float2(0, 240));
                ImGui.Dummy(new float2(1, 1));
            }
            if (CurrentDrawing().Views.Length == 0)
            {
                ImGui.Text("Print scale 1 :");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputFloat("##Primary scale"u8, ref draftRatio))
                {
                    var keep = viewDraft;
                    CancelGhost();
                    viewDraft = keep;
                }
                if (ImGui.Button("Preview scale"u8))
                    PrepareView();
            }
            else
                ImGui.Text($"Shared print scale 1:{CurrentDrawing().Ratio:0.##}");
            PrintUiText.Wrapped("Move over the paper, then click. Green fits; red needs more room. Orthographic projections align to their source. Wheel zooms; Space pans.");
            if (ImGui.Button("Cancel placement"u8))
                CancelGhost();
        }
        else
        {
            ImGui.BeginDisabled(ControlsLocked || sheet?.PoseSource == null);
            bool empty = document?.Scene.Views.All(v => v.Id >= 1000) != false;
            if (PrintUiStyle.PrimaryButton(empty ? "Place primary view" : "Place independent view"))
                BeginView();
            if (!empty)
            {
                var views = CurrentDrawing().Views;
                int index = Array.FindIndex(views, v => v.Id == selectedView);
                if (index < 0)
                {
                    index = 0;
                    selectedView = views[0].Id;
                }
                string[] names = views.Select((v, i) => $"{i + 1}. {DrawingPlacements.Name(v)}" + (v.Parent != null ? " (projected)" : "")).ToArray();
                if (ImGui.Combo("##Drawing views"u8, ref index, names, names.Length) && index >= 0)
                    selectedView = views[index].Id;
                if (index >= 0)
                {
                    if (ImGui.Button("Add projected view"u8))
                        BeginView(selectedView);
                    ImGui.SameLine();
                    if (ImGui.Button("Delete view"u8))
                        DeleteDrawingView(selectedView);
                    if (ImGui.IsItemHovered())
                        PrintUiText.Tooltip("Removes this view and its attached notes. Other views stay in place. Ctrl+Z restores it.");
                    if (ImGui.Button("Unlock alignment"u8))
                        QueueDrawing(CurrentDrawing() with
                        {
                            Views = views.Select(v => v.Id == selectedView ? v with { Parent = null, Alignment = 0 } : v).ToArray()
                        });
                }
                if (ImGui.CollapsingHeader("Shared drawing scale"u8))
                {
                    ImGui.InputFloat("1 :##SharedScale"u8, ref draftRatio);
                    if (ImGui.Button("Apply scale to views"u8))
                        QueueDrawing(CurrentDrawing() with
                        {
                            Ratio = draftRatio
                        });
                }
            }
            if (ImGui.Button("Optional templates..."u8))
                OpenTemplates();
            ImGui.EndDisabled();
            PrintUiText.Wrapped("Drag a view caption to move it. Right-click a view or part to project another view. Templates are editable starting arrangements.");
        }
    }


    static bool DrawViewCanvas(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (!editable || document == null)
            return false;
        var drawing = CurrentDrawing();
        void Outline(Box b, uint color) => ImGui.GetWindowDrawList().AddRect(origin + new float2(b.X * factor, b.Y * factor), origin + new float2(b.Right * factor, b.Bottom * factor), color, 0, ImDrawFlags.None, 2);
        if (dragView is { } dragging)
        {
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                dragView = null;
                return true;
            }
            var centre = new Vec(dragging.X * sheet!.PixelWidth, dragging.Y * sheet.PixelHeight) + point - viewPress;
            var scene = document.Scene;
            var page = Options.Page;
            var moved = DrawingPlacements.Move(drawing, dragging.Id, centre, Options);
            var affected = DrawingPlacements.Dependents(drawing, dragging.Id);
            ViewRegion[] Plans(DrawingPlacement placement) => scene.Views.Where(v => v.Id < 1000).Select(v =>
            {
                var before = drawing.Views.Single(d => d.Id == v.Id);
                var next = placement.Views.Single(d => d.Id == v.Id);
                return v with
                {
                    Bounds = v.Bounds with
                    {
                        X = MathF.Round(v.Bounds.X + (next.X - before.X) * page.Width),
                        Y = MathF.Round(v.Bounds.Y + (next.Y - before.Y) * page.Height)
                    }
                };
            }).ToArray();
            bool Valid(DrawingPlacement placement)
            {
                try
                {
                    var candidate = Plans(placement);
                    DrawingPlacements.Validate(candidate, Options);
                    DrawingPlacements.Validate(candidate.Where(v => affected.Contains(v.Id)).ToArray(), Options, DrawingObstacles());
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }
            var moving = ViewAlignment.Anchors(scene).Where(a => affected.Contains(a.ViewId)).Select(a =>
            {
                var before = drawing.Views.Single(v => v.Id == a.ViewId);
                var next = moved.Views.Single(v => v.Id == a.ViewId);
                return a with
                {
                    Position = a.Position + new Vec((next.X - before.X) * page.Width, (next.Y - before.Y) * page.Height)
                };
            }).ToArray();
            var snap = SnapCaptions(moving, affected, factor, dragging.Parent != null && dragging.Alignment == 1, dragging.Parent != null && dragging.Alignment == 2,
                delta => Valid(DrawingPlacements.Move(drawing, dragging.Id, centre + delta, Options)));
            moved = DrawingPlacements.Move(drawing, dragging.Id, centre + snap.Delta, Options);
            alignmentGuides = snap.Guides;
            DrawAlignmentGuides(factor, origin);
            bool valid = Valid(moved);
            var plans = Plans(moved);
            foreach (var plan in plans)
                if (drawing.Views.Single(v => v.Id == plan.Id) != moved.Views.Single(v => v.Id == plan.Id))
                    Outline(DrawingPlacements.Footprint(plan, Options.Captions), valid ? 0xFF8EDD8Eu : 0xFF7777FFu);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                dragView = null;
                if (valid)
                {
                    if (!moved.Views.SequenceEqual(drawing.Views) || moved.Ratio != drawing.Ratio)
                        QueueDrawing(moved);
                }
                else
                    ReportFailure("View unchanged: keep views and supporting blocks clear of each other.");
            }
            return true;
        }
        var hit = document.Scene.Details.Any(d => d.Definition.Bounds.Contains(point)) ? null :
            document.Scene.Views.LastOrDefault(v => v.Id < 1000 && DrawingPlacements.Footprint(v, Options.Captions).Contains(point));
        if (selectedCanvasView >= 0 && document.Scene.Views.FirstOrDefault(v => v.Id == selectedCanvasView) is { } selected)
            Outline(DrawingPlacements.Footprint(selected, Options.Captions), 0xFF88D8FF);
        bool caption = hit != null && document.Scene.Captions.Any(c => c.ViewId == hit.Id && c.Bounds.Contains(point));
        if (!ControlsLocked && hovered && hit != null)
        {
            if (caption)
            {
                Outline(DrawingPlacements.Footprint(hit, Options.Captions), 0xFF88D8FF);
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
            if (caption && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragView = drawing.Views.Single(v => v.Id == hit.Id);
                viewPress = point;
                selectedView = selectedCanvasView = hit.Id;
                SelectLabel(-1);
                SelectDetail(-1);
                selectedText = selectedKitten = selectedSpec = selectedTable = -1;
                return true;
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && (caption || document.Scene.Pick(point) == null))
            {
                viewMenu = hit.Id;
                captionSize = document.Scene.Captions.FirstOrDefault(c => c.ViewId == hit.Id)?.FontSize ?? ViewCaption.DefaultSize;
                ImGui.OpenPopup("View actions"u8);
            }
        }
        if (ImGui.BeginPopup("View actions"u8))
        {
            partMenuOpen = true;
            if (ImGui.MenuItem("Adjust Scale"u8))
            {
                BeginViewSize(viewMenu);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.MenuItem("Project view..."u8))
            {
                BeginView(viewMenu);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.MenuItem("Place kitten for scale"u8, enabled: document!.Scene.Kittens.Length < 4 && document.Scene.Views.Any(v => v.Id == viewMenu && v.Frame != null)))
            {
                BeginKittenFromView(viewMenu);
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.MenuItem("Add Isometric view"u8))
            {
                BeginIsometricView();
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.MenuItem("Place independent view..."u8))
            {
                BeginView();
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.MenuItem("Unlock alignment"u8))
            {
                QueueDrawing(drawing with
                {
                    Views = drawing.Views.Select(v => v.Id == viewMenu ? v with { Parent = null, Alignment = 0 } : v).ToArray()
                });
                ImGui.CloseCurrentPopup();
            }
            if (document.Scene.Captions.Any(c => c.ViewId == viewMenu))
            {
                ImGui.Separator();
                ImGui.Text("Caption font size");
                ImGui.SetNextItemWidth(220);
                ImGui.SliderInt("##View caption size"u8, ref captionSize, 20, ViewCaption.MaxSize, "%d px"u8);
                if (ImGui.Button("Apply caption size"u8))
                {
                    ResizeCaption(viewMenu, captionSize);
                    ImGui.CloseCurrentPopup();
                }
            }
            if (ImGui.MenuItem("Delete view and its attached notes"u8))
            {
                DeleteDrawingView(viewMenu);
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
            return true;
        }
        return false;
    }
}
