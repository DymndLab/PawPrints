using System.Numerics;
namespace Blueprint.Rendering;

readonly record struct CaptionAnchor(int ViewId, Vector2 Position);
readonly record struct AlignmentGuide(Vector2 From, Vector2 To);
sealed record AlignmentSnap(Vector2 Delta, AlignmentGuide[] Guides);
static class ViewAlignment
{

    public static Vector2 Anchor(Box caption, int font) => new(caption.Center.X, caption.Y + font);

    public static CaptionAnchor[] Anchors(RenderScene scene) => [
        ..scene.Captions.Where(c=>c.ViewId<1000).Select(c=>new CaptionAnchor(c.ViewId,Anchor(c.Bounds,c.FontSize))),
        ..scene.Details.Select(d=>new CaptionAnchor(d.Definition.ViewId,Anchor(d.CaptionBounds,(int)d.CaptionBounds.Height-23)))];


    // Tolerance is supplied in paper pixels from a fixed screen-pixel distance.
    // Try both axes, then each alone; never force a snap into an invalid layout.
    public static AlignmentSnap Snap(CaptionAnchor[] moving, CaptionAnchor[] targets, float tolerance,
        bool lockX = false, bool lockY = false, Func<Vector2, bool>? valid = null)
    {
        var pairs = moving.SelectMany(a => targets.Where(b => b.ViewId != a.ViewId).Select(b => (A: a.Position, B: b.Position))).ToArray();
        var xs = pairs.Where(p => !lockX && Math.Abs(p.B.X - p.A.X) <= tolerance).OrderBy(p => Math.Abs(p.B.X - p.A.X)).Take(1).ToArray();
        var ys = pairs.Where(p => !lockY && Math.Abs(p.B.Y - p.A.Y) <= tolerance).OrderBy(p => Math.Abs(p.B.Y - p.A.Y)).Take(1).ToArray();
        float dx = xs.Length > 0 ? xs[0].B.X - xs[0].A.X : 0, dy = ys.Length > 0 ? ys[0].B.Y - ys[0].A.Y : 0;
        foreach (var delta in new[] { new Vector2(dx, dy), new(dx, 0), new(0, dy) }.Distinct())
        {
            if (valid?.Invoke(delta) == false)
                continue;
            var guides = new List<AlignmentGuide>();
            if (xs.Length > 0 && delta.X == dx)
                guides.Add(new(xs[0].A + delta, xs[0].B));
            if (ys.Length > 0 && delta.Y == dy)
                guides.Add(new(ys[0].A + delta, ys[0].B));
            return new(delta, guides.ToArray());
        }
        return new(Vector2.Zero, []);
    }
}
