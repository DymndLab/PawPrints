namespace Blueprint.Rendering;

static class LabelQuantities
{

    public static int Count(RenderScene scene, int viewId, int partId)
    {
        var part = scene.Parts.Single(p => p.Id == partId);
        if (!part.DetailOnly)
            return scene.Parts.Count(p => !p.DetailOnly && p.TemplateId == part.TemplateId && p.Name == part.Name);
        var detail = scene.Details.SingleOrDefault(d => d.Definition.ViewId == viewId);
        if (detail == null)
            return 1;
        var groups = detail.Groups.ToHashSet();
        return scene.Parts.Count(p => p.DetailOnly && p.NodeId is { } node && groups.Contains(node) && p.TemplateId == part.TemplateId && p.Name == part.Name);
    }

    public static PartLabel Refresh(RenderScene scene, PartLabel label) => label with { Quantity = label.FlagNote ? 1 : Count(scene, label.ViewId, label.PartId) };

    public static PartLabel Set(RenderScene scene, PartLabel label, bool shown)
    {
        var changed = Refresh(scene, label) with
        {
            ShowQuantity = shown,
            ManualPlacement = true
        };
        var b = AnnotationText.Bounds(changed);
        return changed with
        {
            X = Math.Clamp(changed.X, 0, scene.Width - b.Width),
            Y = Math.Clamp(changed.Y, 0, scene.Height - b.Height)
        };
    }
}
