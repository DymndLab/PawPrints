using Blueprint.Rendering;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Vec = System.Numerics.Vector2;
namespace Blueprint;

public sealed partial class BlueprintMod
{
    static AlignmentGuide[] alignmentGuides = [];

    static AlignmentSnap SnapCaptions(CaptionAnchor[] moving, HashSet<int> excluded, float factor, bool lockX, bool lockY, Func<Vec, bool> valid)
    {
        if (!snapPanels || document == null)
            return new(Vec.Zero, []);
        return ViewAlignment.Snap(moving, ViewAlignment.Anchors(document.Scene).Where(a => !excluded.Contains(a.ViewId)).ToArray(),
            10 / Math.Max(.05f, factor), lockX, lockY, valid);
    }

    static Box SnapCaptionBox(Box box, Vec localAnchor, int id, float factor, bool lockX, bool lockY, Func<Box, bool> valid)
    {
        Box Shift(Vec delta) => box with
        {
            X = MathF.Round(box.X + delta.X),
            Y = MathF.Round(box.Y + delta.Y)
        };
        var snap = SnapCaptions([new(id, localAnchor + new Vec(box.X, box.Y))], [id], factor, lockX, lockY, d => valid(Shift(d)));
        alignmentGuides = snap.Guides;
        return Shift(snap.Delta);
    }

    static void DrawAlignmentGuides(float factor, float2 origin)
    {
        var draw = ImGui.GetWindowDrawList();
        foreach (var guide in alignmentGuides)
        {
            var a = origin + new float2(guide.From.X * factor, guide.From.Y * factor);
            var b = origin + new float2(guide.To.X * factor, guide.To.Y * factor);
            draw.AddLine(a, b, 0xCC8EDD8E, 1);
            draw.AddCircle(b, 3, 0xCC8EDD8E);
        }
    }
}
