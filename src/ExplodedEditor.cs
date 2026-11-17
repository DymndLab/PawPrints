using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;
using V3 = System.Numerics.Vector3;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static uint? partMenuNode;
    static bool detailScopeOpen, detailCommitQueued;
    static uint[] detailAncestors = [];
    static int detailScopeIndex, scopeEditingId = -1, selectedDetail = -1;
    static Box[] scopeHighlight = [];
    static ExplodedDetail? pendingDetail, dragDetail, detailEdit;
    static uint? dragGroup, selectedGroup;
    static V3 dragAxis;
    static bool resizeDetail;
    static Vec detailPress, detailOffset;
    static readonly ImInputString detailCaption = new(256);


    static bool UpdateDetailSelection(Vec point, float factor, bool hovered)
    {
        if (selectedDetail < 0 && dragDetail == null)
            return false;
        if (dragDetail == null && hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var selected = document?.Details.FirstOrDefault(d => d.Id == selectedDetail);
            var rendered = sheet?.Scene?.Details.FirstOrDefault(d => d.Definition.Id == selectedDetail);
            bool overHandle = selected != null && (rendered?.CaptionBounds.Contains(point) == true || Vec.Distance(point, new(selected.Bounds.Right, selected.Bounds.Bottom)) * factor < 18);
            bool overPart = selected != null && document?.Scene.Pick(point)?.View == selected.ViewId;
            if (!overHandle && !overPart)
                SelectDetail(-1);
        }
        return false;
    }
    static bool DetailGesture => pendingDetail != null || dragDetail != null || detailScopeOpen || dragTable >= 0;


    static void ResetDetailEditor()
    {
        viewSizeDialog = false;
        CancelDetailGesture();
        selectedDetail = -1;
        detailEdit = null;
        selectedGroup = null;
        selectedTable = -1;
    }


    static void CancelDetailGesture()
    {
        pendingDetail = null;
        dragDetail = null;
        dragGroup = null;
        detailScopeOpen = false;
        scopeHighlight = [];
        dragTable = -1;
    }


    static void SelectDetail(int id)
    {
        if (id >= 0)
            selectedTable = -1;
        selectedDetail = id;
        selectedLabel = -1;
        detailEdit = document?.Details.FirstOrDefault(d => d.Id == id);
        detailCaption.SetValue((detailEdit?.Caption ?? "").AsSpan());
        selectedGroup = null;
    }


    static void BeginDetail(uint? seed, int editing = -1)
    {
        var source = basePrint?.DetailSource;
        if (source == null || seed == null)
        {
            status = "Refresh the craft to capture its assembly hierarchy.";
            return;
        }
        if (editing < 0 && document!.Details.Length >= ExplodedPainting.Limit)
        {
            ReportFailure("Up to four exploded details per sheet.");
            return;
        }
        uint? retainedScope = editing >= 0 ? document!.Details.First(d => d.Id == editing).Scope : null;
        detailAncestors = AssemblyGraph.PartAncestors(source.Assembly, seed.Value, retainedScope);
        if (detailAncestors.Length == 0)
            detailAncestors = [seed.Value];
        detailScopeIndex = 0;
        scopeEditingId = editing;
        if (editing >= 0)
            detailScopeIndex = Math.Max(0, Array.IndexOf(detailAncestors, document!.Details.First(d => d.Id == editing).Scope));
        detailScopeOpen = true;
        UpdateScopeHighlight();
    }


    static void UpdateScopeHighlight()
    {
        var scene = sheet?.Scene;
        var source = basePrint?.DetailSource;
        if (scene?.MeshOwners == null || source == null)
        {
            scopeHighlight = [];
            return;
        }
        var included = AssemblyGraph.Subtree(source.Assembly, detailAncestors[detailScopeIndex]);
        var match = source.Assembly.MeshNodes.Select(included.Contains).ToArray();
        var runs = new List<Box>();
        for (int y = 0; y < scene.Height; y++)
            for (int x = 0; x < scene.Width; x++)
            {
                int mi = scene.MeshOwners[y * scene.Width + x];
                if (mi < 0 || !match[mi])
                    continue;
                int start = x;
                while (x + 1 < scene.Width)
                {
                    int next = scene.MeshOwners[y * scene.Width + x + 1];
                    if (next < 0 || !match[next])
                        break;
                    x++;
                }
                runs.Add(new(start, y, x - start + 1, 1));
            }
        scopeHighlight = runs.ToArray();
    }


    static void DrawDetailScopeDialog()
    {
        if (detailScopeOpen && !ImGui.IsPopupOpen("Exploded assembly scope"u8))
            ImGui.OpenPopup("Exploded assembly scope"u8);
        if (!ImGui.BeginPopupModal("Exploded assembly scope"u8, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            partMenuOpen = true;
            var source = basePrint?.DetailSource;
            if (!detailScopeOpen || source == null)
            {
                ImGui.CloseCurrentPopup();
                return;
            }
            ImGui.Text("Include this part and everything beneath it:");
            string[] names = detailAncestors.Select(id => source.Assembly.Nodes.First(n => n.Id == id).Name + $"  [#{id}]").ToArray();
            ImGui.SetNextItemWidth(520);
            if (ImGui.Combo("##Assembly scope"u8, ref detailScopeIndex, names, names.Length))
                UpdateScopeHighlight();
            var subtree = AssemblyGraph.Subtree(source.Assembly, detailAncestors[detailScopeIndex]);
            ImGui.Text($"{subtree.Count} hierarchy nodes included. Highlighted on the drawing.");
            ImGui.Text("First entry is the clicked part; last entry is the craft root.");
            if (ImGui.Button(scopeEditingId < 0 ? "Place detail frame" : "Apply scope"))
            {
                uint scope = detailAncestors[detailScopeIndex];
                var node = source.Assembly.Nodes.First(n => n.Id == scope);
                if (scopeEditingId >= 0)
                {
                    var old = document!.Details.First(d => d.Id == scopeEditingId);
                    QueueDetail(old with
                    {
                        Scope = scope,
                        IncludeInternal = false,
                        Expanded = [],
                        Groups = [],
                        Camera = null
                    });
                }
                else
                {
                    int id = Enumerable.Range(1, ExplodedPainting.Limit).First(i => !document!.Details.Any(d => d.Id == i));
                    var area = PageLayout.Drawing(Options);
                    pendingDetail = new(id, scope, ExplodedPainting.ClampFrame(new(area.X, area.Y, Math.Min(900, area.Width), Math.Min(800, area.Height)), Options), node.Name, Seed: detailAncestors[0]);
                    var draft = pendingDetail;
                    pendingDetail = null;
                    PreviewDetail(draft);
                }
                detailScopeOpen = false;
                scopeHighlight = [];
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel##Detail scope"u8))
            {
                CancelDetailGesture();
                ImGui.CloseCurrentPopup();
            }
        }
        finally { DrawWarningDialog(true); ImGui.EndPopup(); }
    }


    static void QueueDetails(ExplodedDetail[] details)
    {
        if (busy || basePrint?.DetailSource == null || sheet == null || document == null)
            return;
        var original = sheet.PoseMain ?? basePrint;
        original = original with
        {
            Options = sheet.Options,
            Scene = original.Scene!.WithOptions(sheet.Options)
        };
        var current = sheet;
        var labels = document.Labels;
        var tables = document.Tables;
        cancellation?.Dispose();
        cancellation = new();
        var token = cancellation.Token;
        composingLabels = true;
        renderKeepsDocument = true;
        detailCommitQueued = true;
        progress = "Drawing exploded detail";
        status = progress;
        renderTask = Task.Run(() => ExplodedPainting.Compose(original, current, details, labels, token, true, tables) with { Notice = "Exploded detail updated. Drag its caption to move; drag the lower-right corner to resize." }, token);
    }


    static void QueueDetail(ExplodedDetail detail)
    {
        selectedDetail = detail.Id;
        var old = document!.Details.FirstOrDefault(d => d.Id == detail.Id);
        if (old != null && detail.Sizing is { } sizing && old with
        {
            Bounds = detail.Bounds,
            Caption = detail.Caption,
            Camera = detail.Camera,
            Sizing = detail.Sizing,
            Guides = detail.Guides
        } != detail)
            detail = detail with
            {
                Sizing = sizing with
                {
                    Spread = null,
                    Center = null
                }
            };
        var existing = document!.Details;
        QueueDetails(existing.Any(d => d.Id == detail.Id) ? existing.Select(d => d.Id == detail.Id ? detail : d).ToArray() : [.. existing, detail]);
    }


    static void DeleteDetail()
    {
        if (!CanEditAnnotations || selectedDetail < 0)
            return;
        QueueDetails(document!.Details.Where(d => d.Id != selectedDetail).ToArray());
        selectedDetail = -1;
        detailEdit = null;
    }


    static void DrawDetailControls()
    {
        if (document == null || basePrint?.DetailSource == null)
            return;
        ImGui.Separator();
        ImGui.Text("Exploded details");
        if (document.Details.Length == 0)
        {
            PrintUiText.Wrapped("Choose Place > Exploded View, then click a part. Or right-click a part and choose Add exploded detail view.");
            return;
        }
        var names = new[] { "Select detail..." }.Concat(document.Details.Select(d => $"{SheetLayout.ViewName(d.ViewId)} / {d.Caption}")).ToArray();
        int index = Array.FindIndex(document.Details, d => d.Id == selectedDetail) + 1;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##Choose detail"u8, ref index, names, names.Length))
            SelectDetail(index == 0 ? -1 : document.Details[index - 1].Id);
        if (detailEdit == null)
            return;
        ImGui.BeginDisabled(ControlsLocked || sheet?.Options != Options);
        try
        {
            ImGui.Text("Detail caption");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##Detail caption"u8, detailCaption);
            detailCaption.EvaluateLength();
            int direction = detailEdit.View - 1;
            ImGui.Text("View");
            ImGui.SetNextItemWidth(-1);
            string[] directions = ["Front", "Side", "Top", "Isometric"];
            if (ImGui.Combo("##Detail view"u8, ref direction, directions, directions.Length))
                detailEdit = detailEdit with
                {
                    View = direction + 1,
                    Camera = null
                };
            var sizing = detailEdit.Sizing ?? sheet?.Scene?.Details.FirstOrDefault(d => d.Definition.Id == detailEdit.Id)?.Sizing;
            if (sizing != null)
            {
                float ratio = sizing.Ratio;
                ImGui.Text("Part scale 1 :");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.InputFloat("##Detail part scale"u8, ref ratio))
                    detailEdit = detailEdit with
                    {
                        Sizing = sizing with
                        {
                            Ratio = ratio
                        }
                    };
                if (ImGui.IsItemHovered())
                    PrintUiText.Tooltip("Smaller numbers make parts larger without moving their centres. Resize the box to change their spread.");
            }
            float separation = detailEdit.Separation;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat("##Separation"u8, ref separation, 0, 3, "Separation %.2f"u8))
                detailEdit = detailEdit with
                {
                    Separation = separation,
                    Camera = null
                };
            int axis = detailEdit.Axis;
            string[] axes = ["Auto X / Y / Z", "Root X axis", "Root Y axis", "Root Z axis"];
            ImGui.Text("Separation axis");
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Combo("##Spread axis"u8, ref axis, axes, axes.Length))
                detailEdit = detailEdit with
                {
                    Axis = axis,
                    Camera = null
                };
            bool guides = detailEdit.Guides;
            if (ImGui.Checkbox("Assembly guide lines", ref guides))
                detailEdit = detailEdit with
                {
                    Guides = guides
                };
            if (ImGui.Button("Apply detail settings"u8))
                QueueDetail(detailEdit with
                {
                    Caption = AnnotationText.Clean(detailCaption.ToString(), 60)
                });
            if (ImGui.Button("Change assembly scope"u8))
                BeginDetail(detailEdit.Seed ?? detailEdit.Scope, detailEdit.Id);
            if (ImGui.Button("Fit parts to box"u8))
                QueueDetail(detailEdit with
                {
                    Camera = null,
                    Sizing = null
                });
            var source = basePrint.DetailSource.Assembly;
            var groupIds = AssemblyGraph.Groups(source, detailEdit);
            if (groupIds.Length > 0)
            {
                ImGui.Text("Assembly group");
                int group = selectedGroup.HasValue ? Array.IndexOf(groupIds, selectedGroup.Value) : 0;
                if (group < 0)
                    group = 0;
                var groupNames = groupIds.Select(id => source.Nodes.First(n => n.Id == id).Name + $" [#{id}]").ToArray();
                ImGui.SetNextItemWidth(-1);
                ImGui.Combo("##Detail group"u8, ref group, groupNames, groupNames.Length);
                selectedGroup = groupIds[group];
                uint id = selectedGroup.Value;
                var setting = (detailEdit.Groups ?? []).FirstOrDefault(g => g.Node == id) ?? new(id, V3.Zero);
                bool anchored = id == (sheet?.Scene?.Details.FirstOrDefault(d => d.Definition.Id == detailEdit.Id)?.Anchor ?? detailEdit.Scope);
                ImGui.BeginDisabled(anchored);
                int groupAxis = setting.Axis;
                string[] groupAxes = ["Use separation axis", "Root X axis", "Root Y axis", "Root Z axis"];
                ImGui.Text("Group movement axis");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.Combo("##Group movement axis"u8, ref groupAxis, groupAxes, groupAxes.Length))
                    QueueDetail(WithGroup(detailEdit, setting with
                    {
                        Axis = groupAxis,
                        Offset = V3.Zero
                    }) with
                    {
                        Camera = null
                    });
                ImGui.EndDisabled();
                if (anchored)
                    PrintUiText.Wrapped(id == detailEdit.Scope ? "Assembly root stays fixed." : "Central body stays fixed as the base of this assembly.");
                bool hidden = setting.Hidden;
                if (ImGui.Checkbox("Hide this group", ref hidden))
                    QueueDetail(WithGroup(detailEdit, setting with
                    {
                        Hidden = hidden
                    }) with
                    {
                        Camera = null
                    });
                bool canExpand = source.Nodes.Any(n => n.Parent == id && n.Subpart) && !(detailEdit.Expanded ?? []).Contains(id);
                ImGui.BeginDisabled(!canExpand);
                if (ImGui.Button("Expand model components"u8))
                {
                    var expanded = detailEdit with
                    {
                        Expanded = [.. detailEdit.Expanded ?? [], id],
                        Camera = null
                    };
                    foreach (var child in source.Nodes.Where(n => n.Parent == id && n.Subpart))
                        expanded = WithGroup(expanded, new(child.Id, setting.Offset));
                    QueueDetail(expanded);
                }
                ImGui.EndDisabled();
                if (ImGui.Button("Reset group position"u8))
                    QueueDetail(WithGroup(detailEdit, setting with
                    {
                        Offset = V3.Zero
                    }) with
                    {
                        Camera = null
                    });
            }
            if (ImGui.Button("Restore whole parts"u8))
                QueueDetail(detailEdit with
                {
                    Groups = [],
                    Expanded = [],
                    Camera = null
                });
            if (ImGui.Button("Delete detail"u8))
                DeleteDetail();
            PrintUiText.Wrapped("Whole craft parts stay intact by default. Expand model components only to separate a part's skin, windows and fittings. Drag along the root's local X, Y or Z axis. The root stays fixed; change view for an end-on axis. Undo/Redo includes detail edits.");
        }
        finally { ImGui.EndDisabled(); }
    }


    static ExplodedDetail WithGroup(ExplodedDetail d, ExplodedGroup group) => d with { Groups = [.. (d.Groups ?? []).Where(g => g.Node != group.Node), group] };


    static bool DrawDetailCanvas(Vec point, Vec mouse, float factor, float2 origin, bool hovered, bool editable)
    {
        float2 Screen(Vec p) => origin + new float2(p.X * factor, p.Y * factor);
        void Outline(Box b, uint color) => ImGui.GetWindowDrawList().AddRect(Screen(new(b.X, b.Y)), Screen(new(b.Right, b.Bottom)), color, 0, ImDrawFlags.None, 2);
        foreach (var run in scopeHighlight)
            ImGui.GetWindowDrawList().AddLine(Screen(new(run.X, run.Y + .5f)), Screen(new(run.Right, run.Y + .5f)), 0x6699DDFF, Math.Max(1, factor));
        if (detailScopeOpen)
            return true;
        if (!editable)
            return false;
        if ((pendingDetail != null || dragDetail != null) && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CancelDetailGesture();
            return true;
        }
        if (pendingDetail is { } pending)
        {
            var b = ExplodedPainting.ClampFrame(FixedViewLayouts.EmptyFrame(Options, point) ?? pending.Bounds with
            {
                X = point.X,
                Y = point.Y
            }, Options);
            Box? clear = FrameLayout.Clear(b, PanelNeighbors(pending.Id), FrameLayout.Area(Options), false, 200, 180, snapPanels ? FrameLayout.Gap : 0, boundary: PageLayout.TitleBounds(Options));
            for (int i = 0; clear == null && i < 8; i++)
            {
                b = ExplodedPainting.ClampFrame(b with
                {
                    Width = Math.Max(200, b.Width * .85f),
                    Height = Math.Max(180, b.Height * .85f)
                }, Options);
                clear = FrameLayout.Clear(b, PanelNeighbors(pending.Id), FrameLayout.Area(Options), false, 200, 180, snapPanels ? FrameLayout.Gap : 0, boundary: PageLayout.TitleBounds(Options));
            }
            Outline(clear ?? b, clear != null ? 0xFF8EDD8E : 0xFF6666EE);
            if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && clear is { } placed)
            {
                pendingDetail = null;
                QueueDetail(pending with
                {
                    Bounds = placed
                });
            }
            return true;
        }
        var hit = document!.Details.LastOrDefault(d => d.Bounds.Contains(point));
        if (dragDetail is { } dragging)
        {
            if (dragGroup is { } group)
            {
                var delta = point - detailPress;
                var item = sheet!.Scene!.Details.First(d => d.Definition.Id == dragging.Id);
                var dragFrame = item.Sizing?.Spread is > 0 ? item.Frame with
                {
                    Scale = item.Sizing.Spread.Value
                } : item.Frame;
                var offset = ExplodedGeometry.DragOffset(dragFrame, dragAxis, delta);
                var screenDelta = new Vec(V3.Dot(offset, dragFrame.Right), -V3.Dot(offset, dragFrame.Up)) * dragFrame.Scale;
                int groupPart = basePrint!.Scene!.Parts.Length + Array.FindIndex(basePrint.DetailSource!.Assembly.Nodes, n => n.Id == group);
                foreach (var visual in sheet.Scene.Visible.Where(v => v.Key.View == dragging.ViewId && v.Key.Part == groupPart))
                    Outline(visual.Value.Bounds with
                    {
                        X = visual.Value.Bounds.X + screenDelta.X,
                        Y = visual.Value.Bounds.Y + screenDelta.Y
                    }, 0xFF8EDD8E);
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    if (delta.Length() * factor < 2)
                    {
                        CancelDetailGesture();
                        return true;
                    }
                    var setting = (dragging.Groups ?? []).FirstOrDefault(g => g.Node == group) ?? new(group, V3.Zero);
                    CancelDetailGesture();
                    QueueDetail(WithGroup(dragging, setting with
                    {
                        Offset = setting.Offset + offset
                    }) with
                    {
                        Camera = item.Frame
                    });
                }
            }
            else
            {
                var b = resizeDetail ? dragging.Bounds with
                {
                    Width = point.X - dragging.Bounds.X,
                    Height = point.Y - dragging.Bounds.Y
                } : dragging.Bounds with
                {
                    X = point.X - detailOffset.X,
                    Y = point.Y - detailOffset.Y
                };
                b = SnapPanel(b, dragging.Id, resizeDetail, factor);
                b = resizeDetail ? FrameLayout.Resize(b, Options, 200, 180) : ExplodedPainting.ClampFrame(b, Options);
                b = FrameLayout.Clear(b, PanelNeighbors(dragging.Id), FrameLayout.Area(Options), resizeDetail, 200, 180, snapPanels ? FrameLayout.Gap : 0, boundary: PageLayout.TitleBounds(Options)) ?? dragging.Bounds;
                if (!resizeDetail)
                {
                    var mark = sheet!.Scene!.Details.Single(d => d.Definition.Id == dragging.Id);
                    var anchor = ViewAlignment.Anchor(mark.CaptionBounds, (int)mark.CaptionBounds.Height - 23) - new Vec(dragging.Bounds.X, dragging.Bounds.Y);
                    var area = FrameLayout.Area(Options);
                    b = SnapCaptionBox(b, anchor, dragging.ViewId, factor, false, false,
                        proposed => proposed.X >= area.X && proposed.Y >= area.Y && proposed.Right <= area.Right && proposed.Bottom <= area.Bottom && PanelClear(proposed, dragging.Id));
                    DrawAlignmentGuides(factor, origin);
                }
                Outline(b, 0xFF8EDD8E);
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    var sizing = resizeDetail ? sheet!.Scene!.Details.Single(d => d.Definition.Id == dragging.Id).Sizing : dragging.Sizing;
                    CancelDetailGesture();
                    QueueDetail(dragging with
                    {
                        Bounds = b,
                        Camera = resizeDetail ? null : dragging.Camera,
                        Sizing = sizing
                    });
                }
            }
            return true;
        }
        if (selectedDetail >= 0 && document.Details.FirstOrDefault(d => d.Id == selectedDetail) is { } selected)
        {
            Outline(selected.Bounds, 0xFF88D8FF);
            PanelGrip(selected.Bounds, factor, origin);
        }
        if (hit != null && hovered && !ControlsLocked && ImGui.IsMouseClicked(ImGuiMouseButton.Right)
            && sheet!.Scene!.Details.First(d => d.Definition.Id == hit.Id).CaptionBounds.Contains(point))
        {
            BeginViewSize(hit.ViewId);
            return true;
        }
        if (hit == null || !hovered || !ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return false;
        bool corner = Vec.Distance(point, new(hit.Bounds.Right, hit.Bounds.Bottom)) * factor < 18, header = sheet!.Scene!.Details.First(d => d.Definition.Id == hit.Id).CaptionBounds.Contains(point);
        if (corner || header)
        {
            SelectDetail(hit.Id);
            dragDetail = hit;
            resizeDetail = corner;
            detailPress = point;
            detailOffset = point - new Vec(hit.Bounds.X, hit.Bounds.Y);
            return true;
        }
        if (placeKind == 2 && sheet!.Scene!.PickNode(point) is { } node)
        {
            SelectDetail(hit.Id);
            var source = basePrint!.DetailSource!.Assembly;
            selectedGroup = AssemblyGraph.GroupFor(source, hit, node);
            if (selectedGroup == ExplodedGeometry.AnchorGroup(source, hit))
            {
                ReportFailure("Assembly base stays fixed. Select another group to move it.");
                return true;
            }
            dragAxis = ExplodedGeometry.GroupDirection(source, hit, selectedGroup.Value);
            var frame = sheet.Scene.Details.First(d => d.Definition.Id == hit.Id).Frame;
            if (MathF.Pow(V3.Dot(dragAxis, frame.Right), 2) + MathF.Pow(V3.Dot(dragAxis, frame.Up), 2) < .01f)
            {
                ReportFailure("This axis points into the page. Change the detail view to move this group.");
                return true;
            }
            dragDetail = hit;
            dragGroup = selectedGroup;
            detailPress = point;
            return true;
        }
        return false;
    }
}
