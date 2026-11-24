using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using Vec = System.Numerics.Vector2;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static readonly string[] LayoutNames = ["Single view", "Orthographic", "Engineering sheet"];
    static readonly string[] TipNames = ["Dot", "Filled triangle", "Outline triangle", "None"];
    static readonly string[] LineNames = ["Dotted", "Dashed", "Solid"];
    static readonly string[] FitNames = ["Fit page", "Fit width", "Actual pixels"];
    static int defaultLine, defaultTip, placeKind, previewFit, pageOrientation, pageAspect, brand = 2;
    static readonly ImInputString noteText = new(512);
    static int selectedLabel = -1;
    static readonly ImInputString labelText = new(512);
    static bool composingLabels;
    static PartLabel? dragLabel, pendingLabel;
    static Vec dragOffset;
    static bool draggingAttachment;
    static Vec pressMouse;
    static LabelPlacementGesture? placementGesture;
    static bool draftHeld => placementGesture?.Held == true;


    static void CancelPlacement()
    {
        selectedCanvasView = selectedHeight = -1;
        placingText = false;
        textBoxStart = null;
        warningNotice = null;
        newDrawingDialog = false;
        CancelTextPreview();
        textDraft = null;
        dragText = null;
        selectedText = -1;
        CancelGhost();
        specDraft = null;
        dragSpec = null;
        poseDialog = false;
        CancelDetailGesture();
        dragKitten = null;
        selectedKitten = hoveredKitten = -1;
        dragHeight = null;
        dragLabel = null;
        pendingLabel = null;
        placementGesture = null;
        partMenuScene = null;
        partMenuOpen = false;
        focusFlagnoteText = false;
        flagnotePlacement = null;
        flagnoteScene = null;
        flagnoteError = "";
    }
    static float zoom = 1;


    static void SelectLabel(int id)
    {
        if (id >= 0)
            selectedHeight = -1;
        selectedSpec = -1;
        selectedLabel = id;
        labelText.SetValue((document?.Labels.FirstOrDefault(l => l.Id == id)?.Text ?? "").AsSpan());
    }


    static void Repaint()
    {
        if (busy || sheet == null || document == null)
            return;
        bool changedPose = !sheet.Scene!.Poses.SequenceEqual(document.Scene.Poses) || sheet.Scene.Drawing != document.Scene.Drawing || sheet.Options.OutputPage != document.Scene.Options.OutputPage;
        var historyOptions = document.Scene.Options;
        var source = sheet with { Scene = document.Scene, Details = document.Details, Labels = document.Labels };
        var main = sheet.PoseMain ?? basePrint ?? sheet;
        var acceptedOptions = sheet.Options;
        bool hadDetails = sheet.Details.Length > 0;
        var historyTables = document.Tables;
        composingLabels = true;
        renderKeepsDocument = true;
        renderAdoptsHistory = true;
        progress = "Updating print";
        StartRender(token => changedPose
            ? PosePainting.Rebuild(source with { Options = historyOptions }, source.Scene.Poses, token, true)
            : acceptedOptions != historyOptions
                ? PreviewUpdates.Render(source, main, historyOptions, token)
                : (main.DetailSource != null && (source.Details.Length > 0 || hadDetails)
                    ? ExplodedPainting.Compose(main, source, source.Details, source.Labels, token, tables: historyTables, captions: source.Scene.Captions)
                    : AnnotationPainting.Compose(source, source.Labels, token)) with { Notice = "Print updated." });
    }

    static void ApplyPrintStyle(int line, int tip)
    {
        if (busy)
            return;
        defaultLine = line;
        defaultTip = tip;
        printStyleDirty = sheet != null;
    }


    static void ChangeLabel(PartLabel label)
    {
        if (document == null)
            return;
        if (document.Change(label with
        {
            ManualPlacement = true
        }, out var reason))
        {
            status = "Label updated.";
            Repaint();
        }
        else
            ReportFailure(reason);
    }
    static bool ControlsLocked => newDrawingDialog || placingText || exportSetupOpen || warningNotice != null || viewSizeDialog || textDraft != null || dragText != null || GhostActive || viewDraft != null || dragView != null || specDraft != null || dragSpec != null || poseDialog || busy || dragKitten != null || dragHeight != null || dragLabel != null || pendingLabel != null || flagnotePlacement != null || DetailGesture;


    static void DrawControls(VehicleEditor editor)
    {
        // Reserve a small status area; primary actions stay in the workspace header.
        float footer = Math.Min(175, ImGui.GetContentRegionAvail().Y * .22f);
        float content = Math.Max(80, ImGui.GetContentRegionAvail().Y - footer);
        ImGui.BeginChild("Settings region"u8, new float2(0, content), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        try
        {
            PrintUiStyle.PushTabs();
            try
            {
                if (ImGui.BeginTabBar("Print settings"u8))
                {
                    try
                    {
                        DrawSettingsTab("Layout", DrawSheetControls);
                        DrawSettingsTab("Place", DrawPlaceControls);
                        DrawSettingsTab("Title Block", DrawTitleBlockControls);
                        DrawSettingsTab("Preferences", DrawPreferencesControls);
                    }
                    finally { ImGui.EndTabBar(); }
                }
            }
            finally { PrintUiStyle.PopTabs(); }
        }
        finally { ImGui.EndChild(); }
        ImGui.Separator();
        DrawPrintActions(editor);
    }


    static string? requestedSettingsTab;

    static void DrawSettingsTab(string name, Action draw)
    {
        bool requested = requestedSettingsTab == name;
        if (requested)
            requestedSettingsTab = null;
        if (!ImGui.BeginTabItem(name, requested ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None))
            return;
        PrintDiagnostics.State("settings.tab", name);
        try
        {
            ImGui.BeginChild("Settings " + name, new float2(0, 0));
            try
            {
                draw();
            }
            finally { ImGui.EndChild(); }
        }
        finally { ImGui.EndTabItem(); }
    }


    static void DrawSheetControls()
    {
        ImGui.BeginDisabled(ControlsLocked);
        ImGui.Text("Layout");
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##Shape"u8, ref pageOrientation, PageSize.Shapes, PageSize.Shapes.Length);
        if (pageOrientation == 3)
            DrawDesktopSize();
        else if (pageOrientation != 1)
        {
            ImGui.Text("Aspect ratio");
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##Aspect"u8, ref pageAspect, pageOrientation == 2 ? PageSize.WideRatios : PageSize.Ratios, PageSize.Ratios.Length);
        }
        if (ImGui.Button("Use Template"u8))
            OpenTemplates();
        ImGui.EndDisabled();

        ImGui.Separator();
        DrawTablePlacementControls();
        ImGui.BeginDisabled(ControlsLocked);
        ImGui.Checkbox("Zoned sheet"u8, ref zones);
        if (ImGui.IsItemHovered())
        {
            var count = SheetZones.Count(Options);
            PrintUiText.Tooltip($"{count.Columns} columns x {count.Rows}. Each zone spans two blocks of five grid squares.");
        }

        ImGui.Separator();
        ImGui.Text("Background");
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##Paper"u8, ref theme, Themes, Themes.Length);
        ImGui.Checkbox("Graph paper"u8, ref grid);
        ImGui.Text("Archival wear");
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("##ArchivalWear"u8, ref archivalWear, WearNames, WearNames.Length);
        ImGui.EndDisabled();
    }


    static void DrawMarksControls()
    {
        bool current = sheet != null && PrintIsCurrent;
        if (document != null)
        {
            ImGui.Separator();
            ImGui.Text("Annotations");
            PrintUiText.Wrapped("Hover to highlight; drag to place the chosen annotation. Right-click a part for actions, or to cancel an active placement. Manual crossings and overlaps are allowed.");
            if ((pendingLabel != null || flagnotePlacement != null) && ImGui.Button("Cancel placement"u8))
            {
                CancelPlacement();
                status = "Placement cancelled.";
            }
            ImGui.BeginDisabled(ControlsLocked || !current);
            var repeated = document.Labels.Where(l => !l.FlagNote && l.Key == null && LabelQuantities.Count(document.Scene, l.ViewId, l.PartId) > 1).ToArray();
            if (repeated.Length > 0)
            {
                bool allQuantities = repeated.All(l => l.ShowQuantity);
                if (ImGui.Checkbox("QTY on repeated parts"u8, ref allQuantities))
                {
                    var ids = repeated.Select(l => l.Id).ToHashSet();
                    var changed = document.Labels.Select(l => ids.Contains(l.Id) ? LabelQuantities.Set(document.Scene, l, allQuantities) : l with { ManualPlacement = true }).ToArray();
                    if (document.Apply(changed, out var reason))
                        Repaint();
                    else
                        ReportFailure(reason);
                }
            }
            ImGui.BeginDisabled(!document.CanUndo);
            if (ImGui.Button("Undo"u8))
            {
                document.Undo();
                SelectLabel(-1);
                Repaint();
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!document.CanRedo);
            if (ImGui.Button("Redo"u8))
            {
                document.Redo();
                SelectLabel(-1);
                Repaint();
            }
            ImGui.EndDisabled();
            if (document.Labels.Length > 0)
            {
                var items = document.Labels.Select(l => $"{SheetLayout.ViewName(l.ViewId)} / {l.Text}" + (l.Visible ? "" : " (hidden)")).ToArray();
                int index = Array.FindIndex(document.Labels, l => l.Id == selectedLabel);
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##Select label"u8, ref index, items, items.Length) && index >= 0)
                    SelectLabel(document.Labels[index].Id);
            }
            var selected = document.Labels.FirstOrDefault(l => l.Id == selectedLabel);
            if (selected != null)
            {
                PrintUiText.Wrapped("Part: " + document.Scene.Parts.Single(p => p.Id == selected.PartId).Name);
                if (!selected.FlagNote && selected.Key == null && LabelQuantities.Count(document.Scene, selected.ViewId, selected.PartId) > 1)
                {
                    bool qty = selected.ShowQuantity;
                    if (ImGui.Checkbox("Show QTY on this label"u8, ref qty))
                        ChangeLabel(LabelQuantities.Set(document.Scene, selected, qty));
                }
                if (selected.Key != null && !selected.FlagNote)
                    PrintUiText.Wrapped("Key " + selected.Key + " uses the catalog part name.");
                else
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText("##Label text"u8, labelText);
                    if (ImGui.Button("Apply text"u8))
                    {
                        string clean = AnnotationText.Clean(labelText.ToString());
                        if (clean.Length == 0)
                            status = "Enter a label name.";
                        else
                            ChangeLabel(selected with
                            {
                                Text = clean
                            });
                    }
                }
                bool shown = selected.Visible;
                if (ImGui.Checkbox("Visible", ref shown))
                    ChangeLabel(selected with
                    {
                        Visible = shown
                    });
                if (ImGui.CollapsingHeader("This label's style"u8))
                {
                    int tip = (int)selected.Tip;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.Combo("##Endpoint"u8, ref tip, TipNames, TipNames.Length))
                        ChangeLabel(selected with
                        {
                            Tip = (LeaderTip)tip
                        });
                    int line = (int)selected.Line;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.Combo("##Leader line"u8, ref line, LineNames, LineNames.Length))
                        ChangeLabel(selected with
                        {
                            Line = (LeaderLine)line
                        });
                }
                if ((selected.Attachment != null || selected.Socket != null) && ImGui.Button("Automatic leader"u8))
                    ChangeLabel(selected with
                    {
                        Attachment = null,
                        Socket = null,
                        AttachmentPinned = false
                    });
                if (ImGui.Button("Delete label"u8) && document.Delete(selected.Id, out _))
                {
                    SelectLabel(-1);
                    Repaint();
                }
            }
            ImGui.EndDisabled();
        }
    }


    static void DrawPrintActions(VehicleEditor editor)
    {
        bool current = sheet != null && PrintIsCurrent;
        ImGui.BeginChild("Print status"u8, new float2(0, 0));
        try
        {
            if (warningNotice == null && GenerationBlock(editor) is { } generationBlock)
                PrintUiStyle.Warning(generationBlock);
            if (renderTask != null)
            {
                PrintUiText.Wrapped(progress);
                if (!composingLabels && ImGui.Button("Cancel"u8))
                    cancellation?.Cancel();
            }
            if (!FailureStatus(status))
                PrintUiText.Wrapped(status.StartsWith("Saved: ", StringComparison.Ordinal) ? "PNG saved. Select View exported drawings to open its folder." : status);
            if (sheet != null && !current)
                PrintUiText.Wrapped(livePreviewUpdates ? "Live preview will apply settled settings." : "Generate PawPrint to apply the changed settings.");
            if (sheet != null && sheet.Warnings.Length > 0 && ImGui.CollapsingHeader("Capture warnings"u8))
                foreach (var warning in sheet.Warnings)
                    PrintUiStyle.Warning(warning);

        }
        finally { ImGui.EndChild(); }
    }


    static void DrawPreview()
    {
        alignmentGuides = [];
        hoveredKitten = -1;
        hoveredText = -1;
        hoveredSpec = -1;
        hoveredLabel = -1;
        canvasHovered = ImGui.IsWindowHovered();
        if (preview?.Image is not { } texture || sheet == null)
        {
            PrintUiText.Wrapped("Generate PawPrint to arrange part labels on the sheet.");
            return;
        }
        // Use the fixed viewport, not the scrollable content extent: otherwise
        // scrolling can itself change the fit scale and make the image jump.
        var viewportSize = ImGui.GetWindowSize();
        var style = ImGui.GetStyle();
        float width = viewportSize.X - style.WindowPadding.X * 2 - style.ScrollbarSize - 16;
        float height = viewportSize.Y - style.WindowPadding.Y * 2 - style.ScrollbarSize - 16;
        float fit = previewFit == 2 ? 1 : Math.Max(.08f, previewFit == 1 ? width / sheet.PixelWidth : Math.Min(width / sheet.PixelWidth, height / sheet.PixelHeight));
        var mouse = ImGui.GetMousePos();
        Vec? zoomScroll = null;
        bool navigating = spaceHeld || (!ImGui.GetIO().WantTextInput && ImGui.IsKeyDown(ImGuiKey.Space));
        if (!(viewDraft != null && ImGui.GetIO().KeyShift) && canvasHovered && dragText == null && dragView == null && dragSpec == null && dragKitten == null && dragHeight == null && dragLabel == null && !DetailGesture && !draftHeld && !panning && ImGui.GetIO().MouseWheel != 0)
        {
            float next = PreviewNavigation.Zoom(zoom, ImGui.GetIO().MouseWheel);
            var before = ImGui.GetCursorScreenPos();
            zoomScroll = PreviewNavigation.ZoomScroll(new(ImGui.GetScrollX(), ImGui.GetScrollY()), new(mouse.X - before.X, mouse.Y - before.Y), next / zoom);
            zoom = next;
        }
        float factor = fit * zoom;
        var size = new float2(sheet.PixelWidth * factor, sheet.PixelHeight * factor);
        ImGui.Image(texture, in size);
        var origin = ImGui.GetItemRectMin();
        // An Image is not an input target. Claim the canvas press so ImGui cannot
        // start dragging the root window while our Space gesture pans the paper.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##Paper input"u8, in size);
        if (zoomScroll is { } scroll)
        {
            ImGui.SetScrollX(scroll.X);
            ImGui.SetScrollY(scroll.Y);
        }
        if (canvasHovered && navigating && dragText == null && dragView == null && dragSpec == null && dragKitten == null && dragHeight == null && dragLabel == null && !draftHeld)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                panning = true;
                panMouse = new(mouse.X, mouse.Y);
                panScroll = new(ImGui.GetScrollX(), ImGui.GetScrollY());
            }
        }
        if (panning)
        {
            if (!navigating || !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                panning = false;
            else
            {
                var panTarget = PreviewNavigation.PanScroll(panScroll, panMouse, new(mouse.X, mouse.Y));
                ImGui.SetScrollX(panTarget.X);
                ImGui.SetScrollY(panTarget.Y);
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }
        }
        if ((navigating || panning) && !partMenuOpen && dragText == null && dragView == null && dragSpec == null && dragHeight == null && dragKitten == null)
            return; // Keep any pending draft intact while navigating.
        var point = new Vec((mouse.X - origin.X) / factor, (mouse.Y - origin.Y) / factor);
        bool editable = !busy && PrintIsCurrent && document != null;
        bool hovered = ImGui.IsItemHovered();
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            selectedHeight = -1;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && document != null
            && !document.Scene.Captions.Any(c => c.ViewId < 1000 && c.Bounds.Contains(point)))
            selectedCanvasView = -1;
        if (DrawTextPlacement(point, factor, origin, hovered))
            return;
        UpdateReferenceSelection(point, hovered);
        if (UpdateTableSelection(point, factor, hovered))
            return;
        if (UpdateDetailSelection(point, factor, hovered))
            return;
        if (DrawGhost(point, factor, origin, hovered))
            return;
        if (DrawReferenceMenu(editable))
            return;
        if (DrawTextCanvas(point, factor, origin, hovered, editable))
            return;
        if (DrawSpecCanvas(point, factor, origin, hovered, editable))
            return;
        if (DrawKittenCanvas(point, factor, origin, hovered, editable))
            return;
        if (DrawHeightCanvas(point, factor, origin, hovered, editable))
            return;
        if (DrawViewCanvas(point, factor, origin, hovered, editable))
            return;
        if (DrawCaptionMenu(point, hovered, editable))
            return;
        if (dragLabel == null && pendingLabel == null && flagnotePlacement == null)
        {
            if (dragDetail == null && pendingDetail == null && !detailScopeOpen && DrawTableCanvas(point, factor, origin, hovered, editable))
                return;
            if (DrawDetailCanvas(point, new(mouse.X, mouse.Y), factor, origin, hovered, editable))
                return;
        }
        if ((dragLabel != null || pendingLabel != null || flagnotePlacement?.Stage is FlagnoteStage.Start or FlagnoteStage.End) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CancelPlacement();
            status = "Placement cancelled.";
            return;
        }
        if (DrawPartMenu(hovered, point, editable))
            return;
        if (flagnotePlacement is { } flag)
        {
            if (!editable || !ReferenceEquals(flagnoteScene, document?.Scene))
            {
                CancelPlacement();
                return;
            }
            if (flag.Stage == FlagnoteStage.Start)
            {
                var anchor = LeaderRouting.SnapAttachment(document!.Scene, flag.Label, point, 12 / factor)?.Vector ?? point;
                if (hovered)
                    Handle(anchor, 0xFF8EDD8E);
                if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    flag.PickStart(anchor);
                return; // The start click cannot also select the end.
            }
            var proposed = flag.Stage == FlagnoteStage.End ? flag.PreviewEnd(point) : flag.Label;
            bool valid = LeaderRouting.TryLayout(document!.Scene, [.. document.Labels, proposed], out var flagRoutes, out _);
            uint color = valid ? 0xFF8EDD8E : 0xFF7777FF;
            Outline(AnnotationText.Bounds(proposed), color);
            var flagRoute = flagRoutes.FirstOrDefault(r => r.Label.Id == proposed.Id);
            if (flagRoute != null)
            {
                ImGui.GetWindowDrawList().AddLine(ToScreen(flagRoute.Start), ToScreen(flagRoute.End), color, 1);
                Handle(flagRoute.Start, color);
                Handle(flagRoute.End, color);
            }
            var flagLines = AnnotationText.Lines(proposed);
            for (int i = 0; i < flagLines.Length; i++)
                ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), AnnotationText.Font * factor, ToScreen(new(proposed.X, proposed.Y + i * AnnotationText.Leading)), color, flagLines[i]);
            if (flag.Stage == FlagnoteStage.End && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                flag.PickEnd(point);
                if (!flag.Custom && !CommitFlagnote())
                    flag.RetryEnd();
            }
            return;
        }
        LabelGeometry[] currentRoutes = document?.Routes ?? [];
        if (editable && hovered)
        {
            var hoverLabel = currentRoutes.LastOrDefault(r => r.TextBox.Contains(point) || DistanceToLine(point, r.Start, r.End) * factor < 6);
            hoveredLabel = hoverLabel?.Label.Id ?? -1;
            if (hoverLabel != null)
                Outline(hoverLabel.TextBox, 0xFFFFDD99);
        }
        if (editable && hovered && dragLabel == null && pendingLabel == null && document!.Scene.Pick(point) is { } hover)
        {
            foreach (var run in document.Scene.Highlight(hover.View, hover.Part))
                ImGui.GetWindowDrawList().AddLine(ToScreen(new(run.X, run.Y + .5f)), ToScreen(new(run.Right, run.Y + .5f)), 0x6699DDFF, Math.Max(factor, 1));
            var name = document.Scene.Parts.Single(p => p.Id == hover.Part).Name;
            PrintUiText.Tooltip(name);
        }
        if (editable && hovered && pendingLabel == null && dragLabel == null && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            pressMouse = new(mouse.X, mouse.Y);
            var handle = currentRoutes.OrderByDescending(r => r.Label.Id == selectedLabel).FirstOrDefault(r => Vec.Distance(point, r.Start) * factor < 10 || Vec.Distance(point, r.End) * factor < 10);
            var label = document!.Labels.LastOrDefault(l => l.Visible && AnnotationText.Bounds(l).Contains(point));
            if (handle != null)
            {
                SelectLabel(handle.Label.Id);
                dragLabel = handle.Label;
                draggingAttachment = Vec.Distance(point, handle.End) < Vec.Distance(point, handle.Start);
                if (!draggingAttachment)
                {
                    dragOffset = point - new Vec(dragLabel.X, dragLabel.Y);
                }
            }
            else if (label != null)
            {
                SelectLabel(label.Id);
                dragLabel = label;
                draggingAttachment = false;
                dragOffset = point - new Vec(label.X, label.Y);
            }
            else if (document.Scene.Pick(point) is { } target)
            {
                if (placeKind == 2)
                    BeginDetail(document.Scene.PickNode(point));
                else if (placeKind == 3)
                {
                    if (EngineOwner(target.Part) != null)
                        BeginSpec(target.Part, target.View);
                    else
                        status = "Select an engine to add its specification sheet.";
                }
                else
                    BeginPartPlacement(target.View, target.Part, placeKind == 1, true, point);
                return;
            }
            else
            {
                var nearest = currentRoutes.FirstOrDefault(r => DistanceToLine(point, r.Start, r.End) * factor < 6);
                SelectLabel(nearest?.Label.Id ?? -1);
            }
        }
        var selected = document?.Labels.FirstOrDefault(l => l.Id == selectedLabel && l.Visible);
        if (selected != null && dragLabel == null && pendingLabel == null)
        {
            Outline(AnnotationText.Bounds(selected), 0xFF88D8FF);
            var route = currentRoutes.FirstOrDefault(r => r.Label.Id == selected.Id);
            if (route != null)
            {
                Handle(route.Start, 0xFF88D8FF);
                Handle(route.End, 0xFF88D8FF);
            }
        }
        if ((dragLabel != null || pendingLabel != null) && document != null)
        {
            if (!editable)
            {
                CancelPlacement();
                return;
            }
            bool placing = pendingLabel != null;
            var original = pendingLabel ?? dragLabel!;
            var position = placing ? point + new Vec(16, 16) : point - dragOffset;
            var proposed = original.MoveTo(position.X, position.Y);
            var proposedBox = AnnotationText.Bounds(proposed);
            proposed = proposed with
            {
                X = Math.Clamp(proposed.X, 0, sheet.PixelWidth - proposedBox.Width),
                Y = Math.Clamp(proposed.Y, 0, sheet.PixelHeight - proposedBox.Height)
            };
            if (!placing && draggingAttachment)
            {
                var anchor = LeaderRouting.SnapAttachment(document.Scene, original, point, 12 / factor);
                proposed = original with
                {
                    ManualPlacement = true,
                    AttachmentPinned = true,
                    Attachment = anchor ?? new(Math.Clamp(point.X, 0, sheet.PixelWidth - 1), Math.Clamp(point.Y, 0, sheet.PixelHeight - 1))
                };
            }
            var labels = placing ? [.. document.Labels, proposed] : document.Labels.Select(l => l.Id == proposed.Id ? proposed : l).ToArray();
            bool valid = LeaderRouting.TryLayout(document.Scene, labels, out var routes, out var reason);
            uint color = valid ? 0xFF8EDD8E : 0xFF7777FF;
            Outline(AnnotationText.Bounds(proposed), color);
            var route = routes.FirstOrDefault(r => r.Label.Id == proposed.Id);
            if (route != null)
            {
                ImGui.GetWindowDrawList().AddLine(ToScreen(route.Start), ToScreen(route.End), color, 1);
                Handle(route.Start, color);
                Handle(route.End, color);
            }
            else if (draggingAttachment && !placing)
                Handle(proposed.Attachment!.Vector, color);
            var lines = AnnotationText.Lines(proposed);
            for (int i = 0; i < lines.Length; i++)
                ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), AnnotationText.Font * factor, ToScreen(new(proposed.X, proposed.Y + i * AnnotationText.Leading)), color, lines[i]);
            var screenMouse = new Vec(mouse.X, mouse.Y);
            bool commit = placing ? placementGesture?.Update(screenMouse, ImGui.IsMouseDown(ImGuiMouseButton.Left), ImGui.IsMouseClicked(ImGuiMouseButton.Left), hovered) == true : !ImGui.IsMouseDown(ImGuiMouseButton.Left);
            if (!placing && commit && Vec.Distance(screenMouse, pressMouse) < 2)
            {
                dragLabel = null;
                return;
            }
            if (commit)
            {
                if (valid && document.Apply(labels, out reason))
                {
                    CancelPlacement();
                    SelectLabel(proposed.Id);
                    status = "Label placed. Drag either handle to adjust its leader.";
                    Repaint();
                }
                else
                {
                    if (!placing)
                        dragLabel = null;
                    ReportFailure(reason);
                }
            }
        }
        float2 ToScreen(Vec p) => origin + new float2(p.X * factor, p.Y * factor);
        void Outline(Box box, uint color) => ImGui.GetWindowDrawList().AddRect(ToScreen(new(box.X, box.Y)), ToScreen(new(box.Right, box.Bottom)), color, 0, ImDrawFlags.None, 1.5f);
        void Handle(Vec p, uint color)
        {
            ImGui.GetWindowDrawList().AddCircleFilled(ToScreen(p), 5, 0xFF332211);
            ImGui.GetWindowDrawList().AddCircle(ToScreen(p), 5, color, 16, 1.5f);
        }
    }


    static float DistanceToLine(Vec p, Vec a, Vec b)
    {
        var d = b - a;
        return Vec.Distance(p, a + d * Math.Clamp(Vec.Dot(p - a, d) / Math.Max(d.LengthSquared(), .001f), 0, 1));
    }
}
