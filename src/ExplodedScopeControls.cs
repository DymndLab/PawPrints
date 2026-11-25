using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    sealed record ScopeRun(int X, int Y, int Length, uint Part);
    sealed record ScopePreview(ViewPixels Pixels, uint[] Groups, byte[] Rgba, ScopeRun[] Runs);
    static readonly Stack<ExplodedDetail> scopeUndo = new(), scopeRedo = new();
    static ExplodedDetail? scopeDraft;
    static ExplodedSource? scopeSource;
    static Task<ScopePreview>? scopeTask;
    static ScopePreview? scopePreview;
    static CancellationTokenSource? scopeCancellation;
    static HashSet<uint> scopeIncluded = [];
    static PreviewTexture? scopeTexture;
    static string? scopeHint;
    static uint? scopeRootTarget;
    static int scopeDeleteId = -1;

    static void ClearScope()
    {
        if (scopeCancellation is { } cancellation && scopeTask is { } task)
        {
            cancellation.Cancel();
            _ = task.ContinueWith(t =>
            {
                _ = t.Exception;
                cancellation.Dispose();
            }, TaskScheduler.Default);
        }
        scopeCancellation = null;
        scopeTask = null;
        scopePreview = null;
        scopeDraft = null;
        scopeSource = null;
        scopeRootTarget = null;
        scopeDeleteId = -1;
        scopeIncluded = [];
        if (scopeTexture != null)
        {
            retiredGhosts.Add(scopeTexture);
            scopeTexture = null;
        }
        scopeUndo.Clear();
        scopeRedo.Clear();
    }

    static void BeginDetail(uint? seed, int editing = -1) => BeginDetailScope(seed, editing, replaceRoot: false);

    static void BeginDetailScope(uint? seed, int editing, bool replaceRoot)
    {
        var source = basePrint?.DetailSource;
        if (source?.Assembly.Topology == null || seed == null)
        {
            ReportFailure("Refresh the craft to capture its attachment connections.");
            return;
        }
        if (editing < 0 && document!.Details.Length >= ExplodedPainting.Limit)
        {
            ReportFailure($"Up to {ExplodedPainting.Limit} exploded details per sheet.");
            return;
        }
        var old = document!.Details.FirstOrDefault(d => d.Id == editing);
        if (!replaceRoot && old?.SourceIssue is DetailSourceIssue.MissingRoot or DetailSourceIssue.ChangedScope)
        {
            ShowActivityInPreferences();
            return;
        }
        CancelPlacement();
        uint root = AssemblyGraph.WholePart(source.Assembly, seed.Value);
        var draft = old ?? NewDetail(root, root, source.Assembly.Nodes.First(n => n.Id == root).Name);
        if (replaceRoot)
            draft = draft with { Scope = root, Seed = root, Selection = new(root, [], []),
                Expanded = null, Groups = null, Camera = null, Sizing = null, SourceIssue = DetailSourceIssue.None };
        scopeSource = source;
        try { SetScope(draft with { Selection = draft.Selection ?? (old != null
            ? AssemblyGraph.LegacyScope(source.Assembly, draft.Scope) : new(draft.Scope, [], [])) }); }
        catch (InvalidDataException e)
        {
            ClearScope();
            ReportFailure(e.Message);
            return;
        }
        detailScopeOpen = true;
        scopeCancellation = new();
        var token = scopeCancellation.Token;
        var previewDetail = scopeDraft! with
        {
            // Keep omitted branches visible as dimmed context in the editor.
            Selection = new(scopeDraft!.Scope, [], []),
            Bounds = new(0, 0, 904, 704),
            Camera = null,
            Sizing = null,
            Guides = false,
            Separation = Math.Max(1, scopeDraft.Separation),
            Groups = scopeDraft.Groups?.Select(group => group with { Hidden = false }).ToArray()
        };
        scopeTask = Task.Run(() => RenderMemory.InSlot(() => BuildScopePreview(source, previewDetail, token), token), token);
    }

    static ScopePreview BuildScopePreview(ExplodedSource source, ExplodedDetail detail, CancellationToken token)
    {
        var rendered = ExplodedGeometry.Render(source, detail, token);
        var pixels = rendered.Pixels;
        int width = pixels.Width, height = pixels.Height;
        var owners = rendered.Groups.Distinct().ToDictionary(id => id, id => AssemblyGraph.WholePart(source.Assembly, id));
        var groups = rendered.Groups.Select(id => owners[id]).ToArray();
        var rgba = new byte[checked(width * height * 4)];
        var runs = new List<ScopeRun>();
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < width;)
            {
                int start = x;
                int owner = pixels.MeshOwners[y * width + x];
                do
                {
                    int index = y * width + x++;
                    rgba[index * 4] = 255;
                    rgba[index * 4 + 1] = 226;
                    rgba[index * 4 + 2] = 149;
                    rgba[index * 4 + 3] = pixels.Ink[index];
                }
                while (x < width && pixels.MeshOwners[y * width + x] == owner);
                if (owner >= 0) runs.Add(new(start, y, x - start, groups[owner]));
            }
        }
        return new(pixels, groups, rgba, runs.ToArray());
    }

    static void SetScope(ExplodedDetail detail)
    {
        scopeHint = null;
        scopeIncluded = AssemblyGraph.Included(scopeSource!.Assembly, detail);
        scopeDraft = detail;
    }

    static void ChangeScope(ExplodedDetail detail)
    {
        scopeUndo.Push(scopeDraft!);
        scopeRedo.Clear();
        SetScope(detail);
    }

    static void UndoScope(bool redo)
    {
        var from = redo ? scopeRedo : scopeUndo;
        if (!from.TryPop(out var detail)) return;
        (redo ? scopeUndo : scopeRedo).Push(scopeDraft!);
        SetScope(detail);
    }

    static void DrawExplodedScopePopup()
    {
        const string popup = "Exploded view editor";
        if (detailScopeOpen && !ImGui.IsPopupOpen(popup))
            ImGui.OpenPopup(popup);
        if (!ImGui.IsPopupOpen(popup)) return;
        var size = ImGui.GetWindowSize();
        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + size * .5f, ImGuiCond.Always, new float2(.5f, .5f));
        ImGui.SetNextWindowSize(size * .6f, ImGuiCond.Always);
        if (!ImGui.BeginPopupModal(popup, ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize)) return;
        try
        {
            if (!detailScopeOpen)
            {
                ImGui.CloseCurrentPopup();
                return;
            }
            DrawScopeCanvas();
        }
        finally { ImGui.EndPopup(); }
    }

    static void DrawScopeCanvas()
    {
        var source = scopeSource!;
        var draft = scopeDraft!;
        bool editing = document?.Details.Any(d => d.Id == draft.Id) == true;
        if (ImGui.GetIO().KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Z)) UndoScope(false);
            else if (ImGui.IsKeyPressed(ImGuiKey.Y)) UndoScope(true);
            draft = scopeDraft!;
        }
        if (ImGui.Button("Cancel"u8) || editing && ImGui.IsWindowHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            CancelDetailGesture();
            ImGui.CloseCurrentPopup();
            return;
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(scopeUndo.Count == 0);
        if (ImGui.Button("Undo"u8)) UndoScope(false);
        PrintUiStyle.ShowUnavailableCursor(scopeUndo.Count == 0);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(scopeRedo.Count == 0);
        if (ImGui.Button("Redo"u8)) UndoScope(true);
        PrintUiStyle.ShowUnavailableCursor(scopeRedo.Count == 0);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(scopePreview == null);
        if (ImGui.Button(editing ? "Apply" : "Place exploded view"))
        {
            var accepted = scopeDraft!;
            detailScopeOpen = false;
            ClearScope();
            ImGui.CloseCurrentPopup();
            if (editing) QueueDetail(accepted);
            else PreviewDetail(accepted);
            ImGui.EndDisabled();
            return;
        }
        PrintUiStyle.ShowUnavailableCursor(scopePreview == null);
        ImGui.EndDisabled();

        draft = scopeDraft!;
        var selection = draft.Selection!;
        bool canIncludeAll = !selection.Cuts.IsEmpty || !selection.Removed.IsEmpty ||
            selection.RemovedBranches is { IsEmpty: false };
        ImGui.SameLine();
        ImGui.BeginDisabled(!canIncludeAll);
        if (ImGui.Button("Include all"u8))
        {
            ChangeScope(draft with { Selection = new(draft.Scope, [], []), Camera = null });
            draft = scopeDraft!;
        }
        PrintUiStyle.ShowUnavailableCursor(!canIncludeAll);
        ImGui.EndDisabled();

        if (scopeTask?.IsCompleted == true && scopePreview == null)
        {
            try
            {
                var result = scopeTask.GetAwaiter().GetResult();
                scopeTexture = new PreviewTexture(result.Pixels.Width, result.Pixels.Height, result.Rgba);
                scopePreview = result;
            }
            catch (Exception e)
            {
                CancelDetailGesture();
                ImGui.CloseCurrentPopup();
                PrintDiagnostics.Error("exploded.scope.preview", e);
                ReportFailure("Could not prepare the exploded assembly preview.");
                return;
            }
        }
        if (scopePreview is not { } scene || scopeTexture?.Image is not { } image)
        {
            ImGui.Text(editing ? "Loading exploded view editor..." : "Loading exploded view creator...");
            return;
        }

        var root = source.Assembly.Nodes.First(n => n.Id == draft.Scope);
        string help = editing ? "Click a part to toggle its branch." :
            "Click a part to toggle its branch. Right-click a part to set a new root.";
        PrintUiText.Wrapped(scopeHint ?? $"Root: {root.Name}. {help}");

        int width = scene.Pixels.Width, height = scene.Pixels.Height;
        var available = ImGui.GetContentRegionAvail();
        float factor = Math.Min(available.X / width, available.Y / height);
        if (factor <= 0) return;
        var size = new float2(width * factor, height * factor);
        var origin = ImGui.GetCursorScreenPos() + new float2((available.X - size.X) / 2, 0);
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##scope canvas"u8, in size);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xFF292522);
        draw.AddImage(image, origin, origin + size);
        foreach (var run in scene.Runs)
        {
            uint color = run.Part == draft.Scope ? 0x306DE1FF :
                scopeIncluded.Contains(run.Part) ? 0 : 0xCC292522;
            if (color == 0) continue;
            draw.AddRectFilled(origin + new float2(run.X * factor, run.Y * factor),
                origin + new float2((run.X + run.Length) * factor, (run.Y + 1) * factor), color);
        }
        if (ImGui.IsItemHovered())
            HandleScopePart(scene, source.Assembly, draft, (ImGui.GetMousePos() - origin) / factor, editing);
        if (!editing)
            DrawScopeRootActions(source.Assembly);
    }

    static void HandleScopePart(ScopePreview scene, AssemblyCapture assembly, ExplodedDetail draft, float2 point, bool editing)
    {
        int x = (int)point.X, y = (int)point.Y;
        int width = scene.Pixels.Width, height = scene.Pixels.Height;
        if (point.X < 0 || point.Y < 0 || x >= width || y >= height) return;
        int mesh = scene.Pixels.MeshOwners[y * width + x];
        if (mesh < 0) return;
        uint part = scene.Groups[mesh];
        var node = assembly.Nodes.First(n => n.Id == part);
        if (!editing && part != draft.Scope && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            scopeRootTarget = part;
            ImGui.OpenPopup("##scope root actions"u8);
            return;
        }

        string action = part == draft.Scope ? "Root" : scopeIncluded.Contains(part) ? "Exclude branch" : "Restore branch";
        PrintUiText.Tooltip($"{node.Name} ? {action}");
        if (part == draft.Scope || !ImGui.IsMouseClicked(ImGuiMouseButton.Left)) return;
        var next = AssemblyGraph.ToggleBranch(assembly, draft, part);
        if (next != draft) ChangeScope(next);
        else scopeHint = "Restore a connecting branch or choose Include all.";
    }

    static void DrawScopeRootActions(AssemblyCapture assembly)
    {
        if (!ImGui.BeginPopup("##scope root actions"u8)) return;
        try
        {
            if (scopeRootTarget is { } part && ImGui.MenuItem("Set as root"u8))
            {
                var next = AssemblyGraph.RootSubassembly(assembly, scopeDraft!, part);
                if (next != scopeDraft) ChangeScope(next);
            }
        }
        finally { ImGui.EndPopup(); }
    }

    static (ExplodedDetail Detail, uint Part, bool Branch)? removalPreview;
    static HashSet<uint>? removalAffected;

    static ExplodedDetail WithoutPart(AssemblyCapture source, ExplodedDetail detail, uint part, bool downstream)
    {
        var selection = detail.Selection ?? AssemblyGraph.LegacyScope(source, detail.Scope);
        return detail with { Camera = null, Selection = downstream
            ? selection with { RemovedBranches = (selection.RemovedBranches ?? []).Add(part) }
            : selection with { Removed = selection.Removed.Add(part) } };
    }

    static void DrawDetailRemovalMenu(int id, uint hit, float2 origin, float factor, ImDrawListPtr canvas)
    {
        var detail = document!.Details.Single(d => d.Id == id);
        var source = basePrint!.DetailSource!.Assembly;
        if (source.Topology == null) return;
        uint part = AssemblyGraph.WholePart(source, hit);
        bool removePart = ImGui.MenuItem("Remove part"u8);
        bool previewPart = ImGui.IsItemHovered();
        bool removeBranch = ImGui.MenuItem("Remove part and downstream parts"u8);
        bool previewBranch = ImGui.IsItemHovered();
        if (removePart || removeBranch)
        {
            if (part == detail.Scope) scopeDeleteId = id;
            else QueueDetail(WithoutPart(source, detail, part, removeBranch));
            ImGui.CloseCurrentPopup();
            return;
        }
        if (!previewPart && !previewBranch) return;
        if (removalPreview != (detail, part, previewBranch))
        {
            removalPreview = (detail, part, previewBranch);
            removalAffected = AssemblyGraph.Included(source, detail);
            if (part != detail.Scope)
                removalAffected.ExceptWith(AssemblyGraph.Included(source, WithoutPart(source, detail, part, previewBranch)));
        }
        foreach (var label in document.Scene.Parts)
        {
            if (label.NodeId is not { } node || !removalAffected!.Contains(node) ||
                !document.Scene.Visible.TryGetValue((detail.ViewId, label.Id), out var visual)) continue;
            var b = visual.Bounds;
            canvas.AddRectFilled(origin + new float2(b.X * factor, b.Y * factor),
                origin + new float2(b.Right * factor, b.Bottom * factor), 0x66807DFF);
        }
    }

    static void DrawScopeDeleteConfirmation()
    {
        if (scopeDeleteId < 0) return;
        if (!ImGui.IsPopupOpen("Delete exploded view?"u8)) ImGui.OpenPopup("Delete exploded view?"u8);
        if (!ImGui.BeginPopupModal("Delete exploded view?"u8, ImGuiWindowFlags.AlwaysAutoResize)) return;
        PrintUiText.Wrapped("Deleting the exploded view?s root part will remove the view. Continue?");
        if (ImGui.Button("Delete view"u8))
        {
            DeleteDetail(scopeDeleteId);
            scopeDeleteId = -1;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel"u8))
        {
            scopeDeleteId = -1;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
