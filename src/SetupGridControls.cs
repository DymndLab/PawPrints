using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using KSA;
using Vector3 = System.Numerics.Vector3;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static readonly string[] setupAngles = ["Third-angle", "First-angle"];
    static PreviewTexture? setupPaperTexture;
    static (PageSize Page, int Theme, int Wear, uint Seed)? setupPaperKey;

    static void ReleaseSetupPaper()
    {
        setupPaperTexture?.Dispose();
        setupPaperTexture = null;
        setupPaperKey = null;
    }

    static ImTextureRef? SetupPaperImage(PrintOptions options)
    {
        if (newDrawing == null || options.WearLevel == 0) return null;
        var key = (options.Page, options.Theme, options.WearLevel, options.WearSeed);
        if (setupPaperKey != key)
        {
            ReleaseSetupPaper();
            setupPaperKey = key;
            try
            {
                float scale = 420f / Math.Max(key.Page.Width, key.Page.Height);
                int width = Math.Max(1, (int)MathF.Round(key.Page.Width * scale));
                int height = Math.Max(1, (int)MathF.Round(key.Page.Height * scale));
                var paper = SheetLayout.Colors(options.Theme).Paper;
                var rgb = new byte[width * height * 3];
                for (int i = 0; i < rgb.Length; i += 3)
                {
                    rgb[i] = (byte)paper.X;
                    rgb[i + 1] = (byte)paper.Y;
                    rgb[i + 2] = (byte)paper.Z;
                }
                var marks = WearPlacement.Resolve(options, "", [], []);
                WearPainting.Apply(rgb, width, height, options.Theme, marks, default);
                setupPaperTexture = new PreviewTexture(width, height, Sheet.MakePreview(rgb));
            }
            catch (Exception error) { PrintDiagnostics.Error("setup.paper", error); }
        }
        return setupPaperTexture?.Image;
    }

    static void DrawSelectViews(VehicleEditor editor)
    {
        var draft = newDrawing!;
        float available = ImGui.GetContentRegionAvail().X;
        bool sideBySide = available >= 680;
        float panel = Math.Min(available, Math.Max(252, ImGui.GetFontSize() * 17));
        if (sideBySide)
            ImGui.BeginChild("Setup view choices"u8, new float2(panel, Math.Max(160, ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeight() * 3)), ImGuiChildFlags.None);
        else ImGui.BeginGroup();
        ImGui.PushItemWidth(Math.Max(100, panel - ImGui.CalcTextSize("Orientation"u8).X - 12));
        try
        {
            DrawOutputChoices(draft.Output);
            int choice = setupGrid;
            if (ImGui.Combo("Layout grid", ref choice, setupGridNames, setupGridNames.Length))
                TrySetupLayout(choice, setupPrimary, setupWorkingGrid.Angle);
            DrawSetupProjection(draft);
            DrawDraftPaper(draft);
            ImGui.Separator();
            DrawSetupSelection();
        }
        finally
        {
            ImGui.PopItemWidth();
            if (sideBySide) ImGui.EndChild(); else ImGui.EndGroup();
        }
        if (sideBySide) ImGui.SameLine();
        ImGui.BeginGroup();
        DrawSetupPage(editor, interactive: true);
        ImGui.EndGroup();
        if (ImGui.Button("Back")) { ClearSetupGesture(); setupStage = SetupStage.Choose; }
        ImGui.SameLine();
        if (PrintUiStyle.PrimaryButton("Start editing"))
        {
            ClearSetupGesture();
            setupLayout = setupWorkingGrid.ToTemplate();
            setupFitStarter = true;
            StartDrawing(editor);
        }
        DrawSetupUnlink();
        DrawSetupGridWarning();
    }

    static void ReleaseSetupSketch()
    {
        setupSketch = null;
        setupSketchRevision = null;
    }

    static void DrawSetupPage(VehicleEditor editor, bool interactive)
    {
        var revision = ObserveCraft(editor);
        if (setupSketchRevision != revision)
        {
            setupSketchRevision = revision;
            setupSketch = null;
            if (revision is { Pending: false } craft)
            {
                try { setupSketch = SetupCraftSketch.Read(craft.Tree); }
                catch (Exception e) { PrintDiagnostics.Error("setup.sketch", e); }
            }
        }
        var draft = newDrawing!;
        var options = draft.Options with { PartsTable = setupParts, FlagTable = setupFlags };
        DrawSetupBoard(options, draft.DrawingTitle, interactive && !busy);
    }

    static void AcceptSetupMove(SetupViewGrid moved)
    {
        setupWorkingGrid = moved;
        ClearSetupGesture();
        RefreshSetupViewIssues(setupGrid, SelectedCameras(), setupPrimary);
    }

    static void DropSetupView(int camera, int cell)
    {
        var proposal = setupWorkingGrid.Move(camera, cell, out bool unlink);
        if (proposal == null)
        {
            setupMoveHint = "That move needs a free cell for each linked view.";
            return;
        }
        if (unlink) setupUnlink = proposal;
        else AcceptSetupMove(proposal);
    }

    static void DrawSetupUnlink()
    {
        if (setupUnlink == null) return;
        if (!ImGui.IsPopupOpen("Break projection link?"u8)) ImGui.OpenPopup("Break projection link?"u8);
        CenterDrawingDialog();
        if (!ImGui.BeginPopupModal("Break projection link?"u8, ImGuiWindowFlags.AlwaysAutoResize)) return;
        try
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetFontSize() * 30);
            PrintUiText.Wrapped("You are attempting to break the projection link for this secondary view. If you do so, the two views will have independent scales during editing.");
            ImGui.PopTextWrapPos();
            bool cancel = ImGui.Button("Cancel"u8) || ImGui.IsKeyPressed(ImGuiKey.Escape);
            ImGui.SetItemDefaultFocus();
            ImGui.SameLine();
            if (ImGui.Button("Confirm"u8)) { AcceptSetupMove(setupUnlink); ImGui.CloseCurrentPopup(); }
            else if (cancel) { ClearSetupGesture(); ImGui.CloseCurrentPopup(); }
        }
        finally { ImGui.EndPopup(); }
    }

    static void DrawSetupBoard(PrintOptions options, string craftName, bool interactive)
    {
        var layout = setupWorkingGrid;
        var area = ImGui.GetContentRegionAvail();
        var page = options.Page;
        float factor = Math.Min(Math.Max(1, area.X) / page.Width, Math.Max(190, area.Y - ImGui.GetFrameHeight() * 4) / page.Height);
        var size = new float2(page.Width * factor, page.Height * factor);
        var paper = ImGui.GetCursorScreenPos();
        var work = PageLayout.Drawing(options);
        var origin = paper + new float2(work.X, work.Y) * factor;
        var cellSize = new float2(work.Width / layout.Columns, work.Height / layout.Rows) * factor;
        float2 Corner(int cell) => origin + new float2(cell % layout.Columns * cellSize.X, cell / layout.Columns * cellSize.Y);
        ImGui.InvisibleButton("##snap-grid-page"u8, size);
        var mouse = ImGui.GetMousePos() - origin;
        int col = (int)MathF.Floor(mouse.X / cellSize.X), row = (int)MathF.Floor(mouse.Y / cellSize.Y);
        bool hover = interactive && ImGui.IsItemHovered() && setupUnlink == null && setupGridWarning == null;
        int target = hover && col >= 0 && col < layout.Columns && row >= 0 && row < layout.Rows ? col + row * layout.Columns : -1;
        if (setupUnlink == null && (!interactive || !ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) ||
            ImGui.IsKeyPressed(ImGuiKey.Escape))) ClearSetupGesture();
        if (target >= 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            int index = layout.IndexAt(target);
            if (setupPicked is int selected && (index < 0 || layout.Slots[index].Camera != selected) &&
                (index < 0 || layout.Move(selected, target, out _) != null))
                DropSetupView(selected, target);
            else if (index >= 0)
            {
                setupPicked = layout.Slots[index].Camera;
                setupPointerDown = true;
                setupMoveHint = null;
            }
        }
        if (setupPointerDown)
        {
            setupDragging |= ImGui.IsMouseDragging(ImGuiMouseButton.Left);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if (setupDragging && target >= 0 && setupPicked is int pick) DropSetupView(pick, target);
                else if (setupDragging) ClearSetupGesture();
                setupPointerDown = setupDragging = false;
            }
        }
        layout = setupWorkingGrid;
        var ghost = setupDragging && target >= 0 && setupPicked is int drag ? layout.Move(drag, target, out _) : null;
        if (hover && (setupDragging || target >= 0 && layout.IndexAt(target) >= 0))
            ImGui.SetMouseCursor(setupDragging && ghost == null ? ImGuiMouseCursor.NotAllowed : ImGuiMouseCursor.ResizeAll);

        var draw = ImGui.GetWindowDrawList();
        var colors = SheetLayout.Colors(options.Theme);
        static uint Color(Vector3 rgb, uint alpha = 255) => (uint)rgb.X | (uint)rgb.Y << 8 | (uint)rgb.Z << 16 | alpha << 24;
        uint ink = Color(colors.Ink), paperColor = Color(colors.Paper), gridInk = Color(colors.Ink, 96);
        var edge = paper + size;
        if (SetupPaperImage(options) is { } worn) draw.AddImage(worn, paper, edge);
        else draw.AddRectFilled(paper, edge, paperColor);
        draw.AddRect(paper, edge, gridInk);
        if (options.Grid)
        {
            uint ruled = Color(colors.Ink, 26);
            const float spacing = 20;
            for (float x = paper.X + spacing; x < edge.X; x += spacing)
                draw.AddLine(new(x, paper.Y), new(x, edge.Y), ruled);
            for (float y = paper.Y + spacing; y < edge.Y; y += spacing)
                draw.AddLine(new(paper.X, y), new(edge.X, y), ruled);
        }
        if (options.Zones)
        {
            var inset = new float2(PageLayout.Margin * factor);
            draw.AddRect(paper + inset, edge - inset, gridInk);
            for (int i = 1; i < 6; i++)
            {
                float x = paper.X + size.X * i / 6, y = paper.Y + size.Y * i / 6;
                draw.AddLine(new(x, paper.Y), new(x, paper.Y + inset.Y), gridInk);
                draw.AddLine(new(x, edge.Y - inset.Y), new(x, edge.Y), gridInk);
                draw.AddLine(new(paper.X, y), new(paper.X + inset.X, y), gridInk);
                draw.AddLine(new(edge.X - inset.X, y), new(edge.X, y), gridInk);
            }
        }
        void Region(Box box, string text)
        {
            if (box.Width <= 0 || box.Height <= 0) return;
            var a = paper + new float2(box.X, box.Y) * factor;
            var b = a + new float2(box.Width, box.Height) * factor;
            draw.AddRect(a, b, gridInk);
            draw.PushClipRect(a, b, true);
            draw.AddText(a + new float2(4, 2), ink, text);
            draw.PopClipRect();
        }
        if (options.TitleBlockVisible) Region(PageLayout.TitleBounds(options), craftName);
        if (options.PartsTable || options.FlagTable)
            Region(PageLayout.Table(options), options.PartsTable && options.FlagTable ? "Parts / Flagnotes" : options.PartsTable ? "Parts" : "Flagnotes");
        for (int k = 0; k < layout.Columns * layout.Rows; k++)
        {
            var a = Corner(k);
            if (setupPicked is int pick && layout.Move(pick, k, out bool breaks) is { } plan && plan != layout)
                draw.AddRectFilled(a, a + cellSize, breaks ? Color(colors.Ink, 12) : Color(colors.Ink, 34));
            draw.AddRect(a, a + cellSize, gridInk);
            if (cellSize.X > 45 && cellSize.Y > 40)
                draw.AddText(a + new float2(4, 2), gridInk, $"{(char)('A' + k / layout.Columns)}{k % layout.Columns + 1}");
            if (k == target && setupDragging)
                draw.AddRect(a, a + cellSize, Color(colors.Ink, ghost == null ? 100u : 225u), 0, ImDrawFlags.None, 2);
        }
        var visible = ghost ?? layout;
        float Fit(SetupCraftSketch.Outline outline) => Math.Min(cellSize.X * .78f / Math.Max(.0001f, outline.Size.X),
            Math.Max(2, cellSize.Y - 2 * ImGui.GetFontSize() - 8) / Math.Max(.0001f, outline.Size.Y));
        SetupCraftSketch.Outline? Sketch(SetupSlot slot)
        {
            int cam = slot.Camera == 8 ? 4 : slot.Camera;
            var axes = visible.Axes(slot) ?? DrawingPlacements.Axes(new(0, cam, 0, 0));
            return setupSketch?.Project(axes, slot.Camera == 8);
        }
        var sketches = visible.Slots.ToDictionary(s => s.Camera, Sketch);
        bool SharesScale(SetupSlot slot) => !slot.Detached && StarterDrawings.Ortho(slot.Camera);
        float linkedScale = visible.Slots.Where(SharesScale).Select(s => sketches[s.Camera])
            .OfType<SetupCraftSketch.Outline>().Where(o => o.Faces.Length > 0)
            .Select(Fit).DefaultIfEmpty(1).Min();
        float Scale(SetupSlot slot, SetupCraftSketch.Outline outline) => SharesScale(slot) ? linkedScale : Fit(outline);
        float radius = Math.Max(2, Math.Min(cellSize.X, cellSize.Y - 26) * .32f);
        float2 Center(SetupSlot slot) => Corner(slot.Cell) + cellSize * .5f;
        float2 HalfExtent(SetupSlot slot) =>
            sketches[slot.Camera] is { Faces.Length: > 0 } outline
                ? new float2(outline.Size.X, outline.Size.Y) * (Scale(slot, outline) * .5f)
                : new float2(radius);

        foreach (var (from, to) in visible.ProjectionGuides())
        {
            bool emphasized = setupPicked is int picked && (picked == from.Camera || picked == to.Camera || picked == visible.Primary);
            uint guide = Color(colors.Ink, emphasized ? 216u : 146u);
            DrawProjectionGuide(Center(from), HalfExtent(from), Center(to), HalfExtent(to), guide);
        }

        foreach (var s in visible.Slots)
        {
            var a = Corner(s.Cell);
            var center = Center(s);
            var outline = sketches[s.Camera];
            draw.PushClipRect(a, a + cellSize, true);
            if (outline is { Faces.Length: > 0 })
            {
                float scale = Scale(s, outline);
                foreach (var face in outline.Faces)
                {
                    float2 Point(int i) => center + new float2(face[i].X - outline.Center.X, face[i].Y - outline.Center.Y) * scale;
                    for (int i = 0; i < face.Length; i++) draw.PathLineTo(Point(i));
                    draw.PathFillConvex(paperColor);
                    for (int i = 0; i < face.Length; i++) draw.PathLineTo(Point(i));
                    draw.PathStroke(ink, ImDrawFlags.Closed, 1);
                }
            }
            else
            {
                int cam = s.Camera == 8 ? 4 : s.Camera;
                DrawTemplateView(center, radius, cam, ink, visible.Axes(s));
            }
            string caption = setupViewNames[s.Camera];
            float font = Math.Min(ImGui.GetFontSize(), ImGui.GetFontSize() * (cellSize.X - 4) / Math.Max(1, ImGui.CalcTextSize(caption).X));
            draw.AddText(ImGui.GetFont(), Math.Max(1, font), new float2(center.X - ImGui.CalcTextSize(caption).X * font / ImGui.GetFontSize() / 2, a.Y + cellSize.Y - font - 2), ink, caption);
            if (s.Camera == setupPicked) draw.AddRect(a + new float2(1), a + cellSize - new float2(1), ink, 0, ImDrawFlags.None, 2);
            draw.PopClipRect();
        }
        string guidance = "Drag a view or click a view and then its destination.";
        if (setupPicked is int id)
        {
            var picked = layout.Slots[layout.IndexAt(id, byCamera: true)];
            guidance = !SharesScale(picked) ? $"{setupViewNames[id]} selected. Choose a destination cell." :
                id == layout.Primary ? "Moving the primary moves its linked views together." :
                "Brighter cells keep the projection linked. Dimmer cells ask you to unlink.";
        }
        PrintUiText.Wrapped(setupMoveHint ?? guidance);
    }

    static void DrawProjectionGuide(float2 from, float2 fromExtent, float2 to, float2 toExtent, uint ink)
    {
        bool horizontal = from.Y == to.Y;
        float span = horizontal ? to.X - from.X : to.Y - from.Y;
        var axis = (horizontal ? new float2(1, 0) : new float2(0, 1)) * MathF.Sign(span);
        var across = horizontal ? new float2(0, 1) : new float2(1, 0);
        float start = (horizontal ? fromExtent.X : fromExtent.Y) + 6;
        float end = MathF.Abs(span) - (horizontal ? toExtent.X : toExtent.Y) - 6;
        if (end - start < 6) return;

        float fromWidth = horizontal ? fromExtent.Y : fromExtent.X;
        float toWidth = horizontal ? toExtent.Y : toExtent.X;
        float wide = Math.Min(toWidth * .38f, 50);
        float narrow = Math.Min(fromWidth * .22f, wide * .48f);
        var startCenter = from + axis * start;
        var endCenter = from + axis * end;
        var a = startCenter - across * narrow;
        var b = startCenter + across * narrow;
        var c = endCenter - across * wide;
        var d = endCenter + across * wide;
        var draw = ImGui.GetWindowDrawList();
        DrawGuideDashes(draw, a, c, ink);
        DrawGuideDashes(draw, b, d, ink);
        DrawGuideDashes(draw, c, d, ink);
    }

    static void DrawGuideDashes(ImDrawListPtr draw, float2 start, float2 end, uint ink)
    {
        var delta = end - start;
        float length = MathF.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (length < 1) return;
        var direction = delta / length;
        const float dash = 6, step = 10;
        for (float at = 0; at < length; at += step)
            draw.AddLine(start + direction * at, start + direction * Math.Min(at + dash, length), ink);
    }
}
