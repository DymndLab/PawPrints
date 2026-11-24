using System.Diagnostics;
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
    static bool choosingProjectionSource;
    static Vec viewPress;
    static int viewMenu = -1, selectedView = -1, selectedCanvasView = -1;
    static int projectionStyle; // Third-angle default; first-angle reverses the unfolded faces.
    static Task<PlacementPreview>? ghostTask;
    static CancellationTokenSource? ghostCancel;
    static PlacementPreview? ghostPixels;
    static PreviewTexture? ghostTexture;
    static readonly List<PreviewTexture> retiredGhosts = [];
    static Action<Box>? ghostCommit;
    static Func<Box, Box>? ghostPosition;
    static Func<Box, bool>? ghostValid;
    static string ghostName = "";
    static bool ghostWholePage, ghostConfirmRequested;
    static Box? ghostLastBox;
    static Vec? ghostPin, ghostPress;
    static Box ghostOpening;
    static (Vec Anchor, int Corner)? ghostResizeAnchor;
    static int ghostCorner = -1;
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
        ghostConfirmRequested = false;
        ghostLastBox = null;
        ghostPin = ghostPress = null;
        ghostCorner = -1;
        ghostResizeAnchor = null;
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
            var replacement = new PreviewTexture(ghostPixels.Width, ghostPixels.Height, ghostPixels.Rgba);
            if (ghostTexture != null)
                retiredGhosts.Add(ghostTexture);
            ghostTexture = replacement;
            if (ghostResizeAnchor is { } resize)
            {
                ghostPin = resize.Anchor + new Vec(resize.Corner is 0 or 3 ? -ghostPixels.Width / 2f : ghostPixels.Width / 2f,
                    resize.Corner is 0 or 1 ? -ghostPixels.Height / 2f : ghostPixels.Height / 2f);
                ghostResizeAnchor = null;
            }
            if (ghostLastBox is { } retained)
            {
                var center = ghostPin ?? retained.Center;
                ghostLastBox = new(center.X - ghostPixels.Width / 2f, center.Y - ghostPixels.Height / 2f,
                    ghostPixels.Width, ghostPixels.Height);
            }
            status = "Click to place " + ghostName + ". Right-click cancels.";
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { var keep = viewDraft; CancelGhost(); viewDraft = keep; ReportFailure("Placement unavailable: " + e.Message); }
    }


    static void StartGhost(string name, Func<CancellationToken, PlacementPreview> prepare, Action<Box> commit, Func<Box, bool>? valid = null, Func<Box, Box>? position = null, bool wholePage = false, bool retainPreview = false)
    {
        var retainedPixels = retainPreview ? ghostPixels : null;
        var retainedTexture = retainPreview ? ghostTexture : null;
        var retainedBox = retainPreview ? ghostLastBox : null;
        if (retainPreview)
            ghostTexture = null;
        CancelGhost();
        ghostPixels = retainedPixels;
        ghostTexture = retainedTexture;
        ghostLastBox = retainedBox;
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
        if (hovered && ghostTask == null && viewDraft is { RollDirection: >= 0, Parent: { } parentId } rolling)
        {
            var parent = CurrentDrawing().Views.Single(v => v.Id == parentId);
            var delta = point - new Vec(parent.X * sheet!.PixelWidth, parent.Y * sheet.PixelHeight);
            int direction = DrawingPlacements.ProjectionDirection(delta, rolling.RollDirection);
            if (direction != rolling.RollDirection && delta.Length() > 24 / factor)
            {
                viewDraft = DrawingPlacements.Roll(rolling, parent, direction, projectionStyle == 0 ? 3 : 1);
                PrepareView();
            }
        }
        if (ghostPixels == null || ghostTexture?.Image is not { } image)
            return true;
        var area = ghostWholePage ? new Box(0, 0, Options.Page.Width, Options.Page.Height) : viewDraft != null ? DrawingPlacements.Area(Options) : FrameLayout.Area(Options);
        var pointer = point;
        if (viewDraft is { Parent: null })
        {
            ghostPin ??= area.Center;
            point = ghostPin.Value;
        }
        else if (viewDraft != null && !hovered)
            point = ghostLastBox?.Center ?? area.Center;
        if (hovered && viewDraft != null && viewDraft.Parent == null && ImGui.GetIO().KeyShift && ImGui.GetIO().MouseWheel != 0)
        {
            draftRatio = Math.Clamp(draftRatio * MathF.Pow(1.1f, -ImGui.GetIO().MouseWheel), .01f, 100000f);
            var keep = ghostPin;
            PrepareView();
            ghostPin = keep;
            return true;
        }
        var raw = new Box(point.X - ghostPixels.Width / 2f, point.Y - ghostPixels.Height / 2f, ghostPixels.Width, ghostPixels.Height);
        var box = ghostPosition?.Invoke(raw) ?? raw;
        if (ghostPixels.CaptionAnchor is { } anchor)
        {
            int alignment = viewDraft?.Parent != null ? viewDraft.Alignment : 0;
            if (viewDraft is { Parent: not null, RollDirection: < 0 } manual && !DrawingPlacements.Oblique(manual.Camera))
            {
                var parent = CurrentDrawing().Views.Single(v => v.Id == manual.Parent);
                alignment = Math.Abs(box.Center.X - parent.X * sheet!.PixelWidth) < Math.Abs(box.Y + (box.Height - (Options.Captions ? ViewCaption.Band : 0)) / 2 - parent.Y * sheet.PixelHeight) ? 1 : 2;
            }
            box = SnapCaptionBox(box, anchor, viewDraft?.Id ?? -1, factor, alignment == 1, alignment == 2,
                b => CanvasBounds.Safe(b) && (ghostValid?.Invoke(b) ?? true));
        }
        bool ghostReleased = false, ghostClick = false;
        if (viewDraft is { Parent: null })
        {
            if (ghostPress is { } press)
            {
                if (ghostCorner < 0)
                    box = ghostOpening with
                    {
                        X = ghostOpening.X + pointer.X - press.X,
                        Y = ghostOpening.Y + pointer.Y - press.Y
                    };
                else
                {
                    Vec[] corners = [new(ghostOpening.X, ghostOpening.Y), new(ghostOpening.Right, ghostOpening.Y), new(ghostOpening.Right, ghostOpening.Bottom), new(ghostOpening.X, ghostOpening.Bottom)];
                    var ghostAnchor = corners[(ghostCorner + 2) % 4];
                    var ray = press - ghostAnchor;
                    float scale = Math.Clamp(Vec.Dot(pointer - ghostAnchor, ray) / Math.Max(1, ray.LengthSquared()), .05f, 10);
                    box = new(ghostAnchor.X + (ghostOpening.X - ghostAnchor.X) * scale, ghostAnchor.Y + (ghostOpening.Y - ghostAnchor.Y) * scale, ghostOpening.Width * scale, ghostOpening.Height * scale);
                }
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    ghostReleased = true;
                    ghostClick = ghostCorner < 0 && Vec.Distance(pointer, press) * factor < 4;
                    ghostPin = box.Center;
                    ghostPress = null;
                    if (ghostCorner >= 0)
                    {
                        draftRatio *= ghostOpening.Width / box.Width;
                        int corner = ghostCorner;
                        var cornerAnchor = new Vec(corner is 0 or 3 ? ghostOpening.Right : ghostOpening.X,
                            corner is 0 or 1 ? ghostOpening.Bottom : ghostOpening.Y);
                        var keep = ghostPin;
                        PrepareView();
                        ghostPin = keep;
                        ghostResizeAnchor = (cornerAnchor, corner);
                        return true;
                    }
                }
            }
            else if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                Vec[] corners = [new(box.X, box.Y), new(box.Right, box.Y), new(box.Right, box.Bottom), new(box.X, box.Bottom)];
                ghostCorner = Array.FindIndex(corners, c => Vec.Distance(c, pointer) * factor < 12);
                if (ghostCorner >= 0 || box.Contains(pointer))
                {
                    ghostOpening = box;
                    ghostPress = pointer;
                }
            }
        }
        if (viewDraft != null && (!hovered || ghostTask != null) && ghostLastBox is { } last)
            box = last;
        if (viewDraft != null && ghostTask == null)
            ghostLastBox = box;
        bool inside = CanvasBounds.Safe(box);
        bool valid = (ghostValid?.Invoke(box) ?? true) && inside;
        if (hovered || viewDraft != null)
        {
            var a = origin + new float2(box.X * factor, box.Y * factor);
            var z = origin + new float2(box.Right * factor, box.Bottom * factor);
            var draw = ImGui.GetWindowDrawList();
            draw.AddImage(image, a, z);
            draw.AddRect(a, z, viewDraft != null ? 0xFF44DDFFu : valid ? 0xFF8EDD8Eu : 0xFF7777FFu, 0, ImDrawFlags.None, 2);
            if (viewDraft is { Parent: null })
                foreach (var corner in new[] { a, new float2(z.X, a.Y), z, new float2(a.X, z.Y) })
                    draw.AddRectFilled(corner - new float2(5), corner + new float2(5), 0xFF44DDFF);
            DrawAlignmentGuides(factor, origin);
            if (viewDraft is { } draft && (draft.RollDirection >= 0 || !DrawingPlacements.Oblique(draft.Camera)) && draft.Parent is { } parent && document?.Scene.Views.FirstOrDefault(v => v.Id == parent) is { } source)
            {
                var c = origin + new float2(source.Bounds.Center.X * factor, source.Bounds.Center.Y * factor);
                draw.AddLine(c, origin + new float2(box.Center.X * factor, (box.Y + (box.Height - (Options.Captions ? ViewCaption.Band : 0)) / 2) * factor), 0x889DDDFF, 1);
            }
            bool enter = viewDraft != null && annotationKeysActive && !ImGui.GetIO().WantTextInput
                && ImGui.IsKeyPressed(ImGuiKey.Enter);
            bool confirm = ghostConfirmRequested || enter;
            ghostConfirmRequested = false;
            if (valid && ghostTask == null && ghostPress == null && (confirm || hovered &&
                (viewDraft is { Parent: null } ? ghostReleased && ghostClick : ImGui.IsMouseClicked(ImGuiMouseButton.Left))))
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


    static DrawingPlacement CurrentDrawing() => document!.Scene.Drawing ?? DrawingPlacements.FromScene(document.Scene);


    static IEnumerable<Box> DrawingObstacles() => PanelNeighbors(int.MinValue).Concat(document!.Scene.Dimensions.Select(d => HeightDimensions.Place(document.Scene, d).Bounds));


    static void SelectCanvasView(int id)
    {
        SelectLabel(-1);
        SelectDetail(-1);
        selectedText = selectedKitten = selectedSpec = selectedTable = selectedHeight = -1;
        selectedView = selectedCanvasView = id;
    }


    static void DeleteDrawingView(int id)
    {
        if (!CanEditAnnotations || !CurrentDrawing().Views.Any(v => v.Id == id))
            return;
        if (DrawingPlacements.Dependents(CurrentDrawing(), id).Count > 1)
        {
            pendingPrimaryDelete = id;
            return;
        }
        CommitViewDeletion(id, false);
    }


    static void QueueDrawing(DrawingPlacement drawing, bool undo = false, int removed = -1, HashSet<int>? removedViews = null)
    {
        if (busy || sheet?.Scene == null || document == null)
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
        if (source == null)
        {
            if (drawing.Views.Length != 0)
                return;
            renderKeepsDocument = composingLabels = true;
            renderAdoptsHistory = undo;
            progress = "Updating object order";
            StartRender(token => AnnotationPainting.Compose(current with
            {
                Options = options,
                Scene = current.Scene!.WithOptions(options).WithDrawing(drawing)
            }, current.Labels, token));
            return;
        }
        if (removed >= 0 || removedViews is { Count: > 0 })
        {
            var deleted = removedViews ?? new HashSet<int> { removed };
            var remaining = document.Scene.WithDimensions(document.Scene.Dimensions.Where(d => !deleted.Contains(d.ViewId)).ToArray())
                .WithKittens(document.Scene.Kittens.Where(k => k.SourceView is not { } view || !deleted.Contains(view)).ToArray()).WithPoses(document.Scene.Poses.Where(p => !deleted.Contains(p.View)).ToArray()).WithSpecs(document.Scene.Specs.Select(b => deleted.Contains(b.ViewId) ? b with { Leader = false } : b).ToArray());
            current = current with
            {
                Scene = remaining,
                Labels = current.Labels.Where(l => !deleted.Contains(l.ViewId)).ToArray()
            };
        }
        var priorMain = sheet;
        var poses = current.Scene!.Poses;
        var obstacles = document.Details.Select(d => d.Bounds).Concat(PrintTables.Panels(document.Scene, current.Labels).Select(t => t.Bounds)).Concat(document.Scene.Specs.Select(b => b.Bounds)).ToArray();
        renderKeepsDocument = true;
        renderAdoptsHistory = undo;
        composingLabels = true;
        progress = "Composing drawing views";
        StartRender(token =>
        {
            long started = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var fresh = PosePainting.RenderMain(source, current.Title, options, current.Warnings, poses, token,
                composeAnnotations: false, previous: priorMain);
            long rendered = Stopwatch.GetTimestamp();
            DrawingPlacements.Validate(fresh.Scene!.Views, options);
            var changed = fresh.Scene.Views.Where(v => current.Scene!.Views.FirstOrDefault(p => p.Id == v.Id)?.Bounds != v.Bounds).ToArray();
            DrawingPlacements.Validate(changed, options, obstacles);
            var result = CompositionRegeneration.Rebuild(fresh, current, token, reuseDetailPixels: options.Page == current.Options.Page);
            long finished = Stopwatch.GetTimestamp();
            long workerBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            long tilePixels = fresh.MainInk?.Sum(tile => (long)tile.Width * tile.Height) ?? 0;
            int reused = fresh.MainInk?.Count(tile => priorMain.MainInk?.Any(old => ReferenceEquals(old.Pixels, tile.Pixels)) == true) ?? 0;
            PrintDiagnostics.Record($"drawing.edit views={drawing.Views.Length} rasterized_views={drawing.Views.Length - reused} reused_views={reused} details={current.Details.Length} page_pixels={(long)fresh.PixelWidth * fresh.PixelHeight} view_pixels={tilePixels} main_ms={(long)Stopwatch.GetElapsedTime(started, rendered).TotalMilliseconds} compose_ms={(long)Stopwatch.GetElapsedTime(rendered, finished).TotalMilliseconds} worker_allocated_bytes={workerBytes}");
            return result with
            {
                RegeneratedBase = null,
                PoseMain = fresh,
                Notice = "Drawing updated. Project from any view, or place an independent view. Ctrl+Z undoes this change."
            };
        });
    }


    static void BeginProjectionSource()
    {
        CancelPlacement();
        choosingProjectionSource = true;
        status = "Select a primary view in the drawing. Right-click cancels.";
    }

    static bool DrawProjectionSource(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (!choosingProjectionSource)
            return false;
        if (!editable || document == null || ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            choosingProjectionSource = false;
            return true;
        }
        var drawing = CurrentDrawing();
        var pick = CanvasObjects.Pick(sheet!, document.Scene, document.Labels, point, 5 / factor);
        var candidate = pick is { Kind: DrawingObjectKind.View, Id: < 1000 }
            ? drawing.Views.FirstOrDefault(v => v.Id == pick.Value.Id && v.Parent == null) : null;
        if (hovered && candidate != null)
        {
            var box = CanvasObjects.ContentBounds(document.Scene, document.Labels, new(DrawingObjectKind.View, candidate.Id));
            ImGui.GetWindowDrawList().AddRect(origin + new float2(box.X * factor, box.Y * factor),
                origin + new float2(box.Right * factor, box.Bottom * factor), 0xFF44DDFF, 0, ImDrawFlags.None, 2);
            PrintUiText.Tooltip("Project from " + DrawingPlacements.Name(candidate));
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                choosingProjectionSource = false;
                SelectCanvasView(candidate.Id);
                BeginView(candidate.Id);
            }
        }
        return true;
    }

    static void BeginView(int? parent = null) => BeginViewDraft(parent, null);


    static void BeginIsometricView() => BeginViewDraft(null, 4);


    static void BeginViewDraft(int? parent, int? cameraOverride)
    {
        requestedSettingsTab = "Place";
        if (document == null)
            return;
        if (sheet?.PoseSource == null)
        {
            if (KSA.Program.Editor is { } editor && !busy)
            {
                captureForView = true;
                captureViewCamera = cameraOverride;
                Generate(editor);
                if (renderTask == null)
                    captureForView = false;
            }
            return;
        }
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
        draftRatio = parent is { } linked ? ProjectionGroups.Ratio(drawing, linked) : DrawingPlacements.SuggestRatio(PartPoses.Exterior(sheet.PoseSource, sheet.PoseSource.Assembly), camera, Options);
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
                Parent = view.Parent,
                Ratio = view.Parent == null ? ratio : null
            };
        }
        DrawingPlacement Proposed(Box b) => drawing with
        {
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
        var curvature = sheet.MainCurvature;
        StartGhost("view", token => PlacementPreviews.View(source, view, ratio, options, poses, token, curvature),
            b => QueueDrawing(Proposed(b)), Valid, Position, retainPreview: ghostPixels != null);
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
                PrintUiText.Wrapped("Move along an axis for an aligned projection, or diagonally for a corner view. Return toward an axis to snap back. Click to place.");
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
            PrintUiText.Wrapped("Click or press Enter to place the yellow preview. Primary views can be dragged and resized; Shift+scroll also resizes. Right-click cancels.");
            ImGui.BeginDisabled(ghostTask != null || ghostPixels == null || ghostLastBox == null || ghostPress != null);
            if (PrintUiStyle.YellowButton("Confirm placement (Enter)"))
                ghostConfirmRequested = true;
            ImGui.EndDisabled();
            if (ImGui.Button("Cancel placement"u8))
                CancelGhost();
        }
        else
        {
            ImGui.BeginDisabled(ControlsLocked);
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
                            Views = views.Select(v => v.Id == selectedView ? v with { Alignment = 0 } : v).ToArray()
                        });
                }
                if (ImGui.CollapsingHeader("Selected group scale"u8))
                {
                    ImGui.InputFloat("1 :##SharedScale"u8, ref draftRatio);
                    if (ImGui.Button("Apply scale to group"u8))
                        QueueDrawing(ProjectionGroups.SetRatio(CurrentDrawing(), selectedView, draftRatio));
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
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                dragView = null;
                return true;
            }
            var centre = new Vec(dragging.X * sheet!.PixelWidth, dragging.Y * sheet.PixelHeight) + point - viewPress;
            var scene = document.Scene;
            var page = Options.Page;
            var moved = CanvasMotion.MoveViews(document.Scene, document.Labels, drawing, dragging.Id, centre, Options);
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
                delta => Valid(CanvasMotion.MoveViews(document.Scene, document.Labels, drawing, dragging.Id, centre + delta, Options)));
            moved = CanvasMotion.MoveViews(document.Scene, document.Labels, drawing, dragging.Id, centre + snap.Delta, Options);
            alignmentGuides = snap.Guides;
            DrawAlignmentGuides(factor, origin);
            bool valid = Valid(moved);
            var plans = Plans(moved);
            foreach (var plan in plans)
                if (drawing.Views.Single(v => v.Id == plan.Id) != moved.Views.Single(v => v.Id == plan.Id))
                    Outline(DrawingPlacements.Footprint(plan, Options.Captions), viewDraft != null ? 0xFF44DDFFu : valid ? 0xFF8EDD8Eu : 0xFF7777FFu);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                dragView = null;
                if (valid)
                {
                    if (Vec.Distance(point, viewPress) * factor >= 4 && (!moved.Views.SequenceEqual(drawing.Views) || moved.Ratio != drawing.Ratio))
                        QueueDrawing(moved);
                }
                else
                    ReportFailure("View unchanged: keep views and supporting blocks clear of each other.");
            }
            return true;
        }
        var pickedView = canvasTarget?.Kind == DrawingObjectKind.View ? canvasTarget?.Id : document.Scene.Pick(point)?.View;
        var hit = document.Scene.Views.LastOrDefault(v => v.Id < 1000 && (v.Id == pickedView || document.Scene.Captions.Any(c => c.ViewId == v.Id && c.Bounds.Contains(point))));
        if (selectedCanvasView >= 0 && document.Scene.Views.FirstOrDefault(v => v.Id == selectedCanvasView) is { } selected)
            Outline(DrawingPlacements.Footprint(selected, Options.Captions), 0xFF88D8FF);
        bool caption = hit != null && document.Scene.Captions.Any(c => c.ViewId == hit.Id && c.Bounds.Contains(point));
        if (!ControlsLocked && hovered && hit != null)
        {
            if (caption)
            {
                Outline(DrawingPlacements.Footprint(hit, Options.Captions), 0xFF88D8FF);
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            }
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragView = drawing.Views.Single(v => v.Id == hit.Id);
                viewPress = point;
                SelectCanvasView(hit.Id);
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
                    Views = drawing.Views.Select(v => v.Id == viewMenu ? v with { Alignment = 0 } : v).ToArray()
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
