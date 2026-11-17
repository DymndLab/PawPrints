using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static bool viewSizeDialog, viewSizeUpdating;
    static int sizeView;
    static float sizeStartRatio, sizePercent = 100;
    static string sizeMessage = "";
    static Task<ScaleFit?>? sizeFitTask;
    static ScaleFit? sizeFit;

    static float? ViewSizeRatio(int view) => view < 1000 ? document?.Scene.Drawing?.Ratio :
        sheet?.Scene?.Details.FirstOrDefault(d => d.Definition.ViewId == view)?.Sizing?.Ratio;

    static void BeginViewSize(int view)
    {
        if (busy || ViewSizeRatio(view) is not { } ratio)
            return;
        sizeView = view;
        sizeStartRatio = ratio;
        sizePercent = 100;
        sizeMessage = "";
        sizeFit = null;
        var scene = document!.Scene;
        var source = sheet!.PoseSource;
        var detailSource = sheet.DetailSource;
        var drawing = CurrentDrawing();
        var detail = scene.Details.FirstOrDefault(d => d.Definition.ViewId == view);
        var obstacles = document.Details.Select(d => d.Bounds).Concat(PrintTables.Panels(scene, document.Labels).Select(t => t.Bounds)).Concat(scene.Specs.Select(b => b.Bounds)).ToArray();
        sizeFitTask = Task.Run(() =>
        {
            try
            {
                if (view < 1000 && source != null)
                    return ScaleAdjustment.ForMain(source, scene, drawing, obstacles);
                if (detail?.Sizing is { } sizing && detailSource != null)
                {
                    var assembly = source == null ? detailSource.Assembly : PartPoses.Apply(source, scene.Poses, view);
                    return ScaleAdjustment.Detail(ExplodedGeometry.Build(assembly, detail.Definition), sizing, detail.Definition.View, scene.Options);
                }
            }
            catch (Exception e) { Console.WriteLine("[Paw Prints] Scale fit: " + e.Message); }
            return null;
        });
        viewSizeUpdating = false;
        viewSizeDialog = true;
    }

    // Own the popup outside the canvas so it survives asynchronous preview work.
    static void DrawViewSizeDialog()
    {
        if (viewSizeDialog && !ImGui.IsPopupOpen("Adjust Scale"u8))
            ImGui.OpenPopup("Adjust Scale"u8);
        if (!ImGui.BeginPopupModal("Adjust Scale"u8, ImGuiWindowFlags.AlwaysAutoResize))
            return;
        try
        {
            partMenuOpen = true;
            if (!viewSizeDialog || document == null || sheet?.Options != Options || ViewSizeRatio(sizeView) is not { } accepted)
            {
                viewSizeDialog = false;
                ImGui.CloseCurrentPopup();
                return;
            }
            if (viewSizeUpdating && !busy)
            {
                viewSizeUpdating = false;
                sizePercent = 100 * sizeStartRatio / accepted;
                sizeMessage = status.StartsWith("Preview failed:", StringComparison.Ordinal) ? status : "";
            }
            bool measuring = sizeFitTask is { IsCompleted: false };
            if (sizeFitTask is { IsCompletedSuccessfully: true })
            {
                sizeFit = sizeFitTask.Result;
                sizeFitTask = null;
            }
            ImGui.Text(sizeView < 1000 ? "Main views - shared scale" : "Exploded detail - part size");
            ImGui.BeginDisabled(busy || measuring);
            bool update;
            try
            {
                ImGui.SetNextItemWidth(360);
                ImGui.SliderFloat("##View size percent"u8, ref sizePercent, 10, 400, "%.1f%%"u8);
                update = ImGui.IsItemDeactivatedAfterEdit();
                var a = ImGui.GetItemRectMin();
                var z = ImGui.GetItemRectMax();
                if (sizeFit is { Maximum: < 400 } fit)
                {
                    // Match ImGui's slider grab travel, including the end inset.
                    float inset = 2 + Math.Max(ImGui.GetStyle().GrabMinSize, 1) * .5f;
                    float x = a.X + inset + (z.X - a.X - 2 * inset) * (fit.Maximum - 10) / 390;
                    ImGui.GetWindowDrawList().AddLine(new float2(x, a.Y), new float2(x, z.Y), 0xFF55DDEE, 2);
                }
                sizePercent = Math.Clamp(sizePercent, 10, 400);
                ImGui.Text($"Scale 1:{Math.Clamp(sizeStartRatio * 100 / sizePercent, .01f, 1000000):0.##}");
                if (sizeFit is { } limit)
                {
                    PrintUiStyle.Warning(limit.Maximum < 400 ? $"Max fit here: {limit.Maximum:0.0}%" : "Fits through 400%");
                    if (limit.Maximum < 400 && ImGui.Button("Use max fit"u8))
                    {
                        sizePercent = limit.Maximum;
                        update = true;
                    }
                }
                if (ImGui.Button("Reset to opening size"u8))
                {
                    sizePercent = 100;
                    update = true;
                }
            }
            finally { ImGui.EndDisabled(); }
            ImGui.Text(sizeView < 1000 ? "Changes all main views. Positions stay fixed." : "Changes this detail's parts. Frame and group centres stay fixed.");
            bool tooLarge = sizeFit is { } max && sizePercent > max.Maximum;
            if (update && !busy && !measuring && !tooLarge)
            {
                float ratio = Math.Clamp(sizeStartRatio * 100 / sizePercent, .01f, 1000000);
                if (Math.Abs(ratio - accepted) > Math.Max(.00001f, accepted * .000001f))
                {
                    sizeMessage = "";
                    if (sizeView < 1000)
                        QueueDrawing(CurrentDrawing() with
                        {
                            Ratio = ratio
                        });
                    else
                    {
                        var detail = sheet!.Scene!.Details.Single(d => d.Definition.ViewId == sizeView);
                        QueueDetail(detail.Definition with
                        {
                            Sizing = detail.Sizing! with
                            {
                                Ratio = ratio
                            }
                        });
                    }
                    viewSizeUpdating = true;
                }
            }
            ImGui.Text(measuring ? "Checking available space..." : busy ? "Updating preview..." : "Release to update. Close, then Ctrl+Z to undo.");
            if (tooLarge)
                PrintUiStyle.Warning("Too large for this layout. The preview is unchanged.");
            if (sizeMessage.Length > 0)
                PrintUiStyle.Warning(sizeMessage);
            if (ImGui.Button("Done"u8))
            {
                viewSizeDialog = false;
                ImGui.CloseCurrentPopup();
            }
        }
        finally { DrawWarningDialog(true); ImGui.EndPopup(); }
    }
}
