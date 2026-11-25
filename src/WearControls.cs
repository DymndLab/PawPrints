using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;

namespace Blueprint;

public sealed partial class BlueprintMod
{
    static int wearChoice, wearSize = 100, wearAngle;
    static bool wearSnap = true;
    static uint wearVariation = WearPlacement.NewSeed();
    static PreviewTexture? wearPalette, wearGhost;
    static (int Theme, uint Seed)? wearPaletteKey;
    static (int Theme, WearMark Shape)? wearGhostKey;
    static Box wearGhostBounds;

    static void DisposeWearTextures()
    {
        wearPalette?.Dispose();
        wearGhost?.Dispose();
        wearPalette = wearGhost = null;
        wearPaletteKey = null;
        wearGhostKey = null;
    }

    static void DrawWearControls()
    {
        var paletteKey = (theme, wearVariation);
        if (wearPaletteKey != paletteKey)
        {
            var p = WearPainting.Palette(theme, wearVariation);
            var next = new PreviewTexture(p.Width, p.Height, p.Rgba);
            wearPalette?.Dispose();
            wearPalette = next;
            wearPaletteKey = paletteKey;
        }
        if (wearPalette?.Image is { } image)
        {
            float width = Math.Clamp(ImGui.GetContentRegionAvail().X, 90, 320);
            var size = new float2(width, width / 2);
            var start = ImGui.GetCursorScreenPos();
            bool clicked = ImGui.InvisibleButton("##Wear thumbnails"u8, size);
            var draw = ImGui.GetWindowDrawList();
            draw.AddImage(image, start, start + size);
            if (ImGui.IsItemHovered())
            {
                var at = ImGui.GetMousePos() - start;
                int item = Math.Clamp((int)(3 * at.X / size.X), 0, 2) + 3 * Math.Clamp((int)(3 * at.Y / size.Y), 0, 2);
                if (clicked)
                    wearChoice = item;
                PrintUiText.Tooltip(WearPlacement.Names[item]);
            }
            var cell = new float2(size.X / 3, size.Y / 3);
            var corner = start + new float2(wearChoice % 3 * cell.X, wearChoice / 3 * cell.Y);
            draw.AddRect(corner, corner + cell, 0xff44ddff);
        }
        ImGui.Combo("Mark"u8, ref wearChoice, WearPlacement.Names, WearPlacement.Names.Length);
        ImGui.SliderInt("Size"u8, ref wearSize, 45, 190, "%d%%"u8);
        ImGui.SliderInt("Angle"u8, ref wearAngle, -180, 180, "%d deg"u8);
        if (WearPlacement.Tear((WearKind)wearChoice))
        {
            ImGui.Checkbox("Snap to sheet edge"u8, ref wearSnap);
            if (ImGui.Button("Try another tear variation"u8))
                wearVariation = WearPlacement.NewSeed();
        }
        PrintUiText.Wrapped("Click to place. Right-click finishes. Ctrl+Z undoes a placement.");
    }

    static bool DrawWearPlacement(Vec point, float factor, float2 origin, bool hovered, bool editable)
    {
        if (activePlaceTool != PlaceTool.Wear || !hovered || !editable || !CanEditAnnotationState(true) || sheet == null || document == null)
            return false;
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            activePlaceTool = PlaceTool.None;
            return true;
        }
        var kind = (WearKind)wearChoice;
        var mark = WearPlacement.Manual(kind, point, sheet.Options.Page, wearSize / 100f, wearAngle, wearSnap, wearVariation);
        var ghostKey = (theme, mark with
        {
            X = 0,
            Y = 0
        });
        if (wearGhostKey != ghostKey)
        {
            var p = WearPainting.Preview(mark, theme);
            var next = new PreviewTexture(p.Width, p.Height, p.Rgba);
            wearGhost?.Dispose();
            wearGhost = next;
            wearGhostBounds = p.Bounds;
            wearGhostKey = ghostKey;
        }
        if (wearGhost?.Image is { } image)
        {
            var draw = ImGui.GetWindowDrawList();
            var canvasSize = new float2(sheet.PixelWidth * factor, sheet.PixelHeight * factor);
            var anchor = origin + new float2(mark.X * canvasSize.X, mark.Y * canvasSize.Y);
            float scale = canvasSize.X / 1920f;
            draw.PushClipRect(origin, origin + canvasSize, true);
            draw.AddImage(image, anchor + new float2(wearGhostBounds.X, wearGhostBounds.Y) * scale,
                anchor + new float2(wearGhostBounds.Right, wearGhostBounds.Bottom) * scale);
            draw.PopClipRect();
        }
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var existing = document.Scene.WearMarks;
            if (existing.Length >= WearPlacement.Limit)
            {
                activePlaceTool = PlaceTool.None;
                status = $"The sheet has reached its {WearPlacement.Limit} placed wear marks.";
                return true;
            }
            var source = sheet with
            {
                Scene = document.Scene.WithWear([.. existing, mark])
            };
            var labels = document.Labels;
            composingLabels = true;
            renderKeepsDocument = true;
            renderAdoptsHistory = false;
            progress = "Placing wear";
            StartRender(token => AnnotationPainting.Compose(source, labels, token) with { Notice = "Wear placed. Ctrl+Z undoes this placement." });
            if (WearPlacement.Tear(kind))
                wearVariation = WearPlacement.NewSeed();
        }
        return true;
    }
}
