using Blueprint.Rendering;
using System.Numerics;
static class PoseChecks
{

    public static void Run(Action<bool, string> check)
    {
        double[][] vertices = [[0, 0, 0], [1, 0, 0], [0, 1, 0], [0, 0, 1]];
        var mesh = new Mesh("Door", vertices, [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3], vertices, vertices.Select(_ => new double[] { 1, 0, 0 }).ToArray(), vertices.Select(_ => new double[] { 0, 0 }).ToArray(), "structure", null, null);
        var a = new AssemblyCapture(new([mesh]), [new(1, null, "Bay", "bay", false, new(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ)), new(2, 1, "Door", "door", true)], [2], [false]);
        var translations = Enumerable.Range(0, 101).Select(i => Matrix4x4.CreateTranslation(i * .02f, 0, i * .01f)).ToArray();
        var control = new PoseControl(1, "door", "Bay", false, 0, [new(2, 2, [2], translations)]);
        var source = new PoseSource(a, [control], 1, [new(0, "Bay", "bay", [0], NodeId: 1)], new Dictionary<string, Texture>());
        var shared = PartPoses.Set([], control, -1, 100);
        var one = PartPoses.Set(shared, control, 2, 25);
        check(PartPoses.Value(one, control, 1) == 100 && PartPoses.Value(one, control, 2) == 25 && PartPoses.Value(one, control, 1001) == 100, "Per-view pose overrides shared pose; details inherit shared pose");
        var global = PartPoses.Set(one, control, -1, 50);
        check(global.Length == 1 && PartPoses.Value(global, control, 2) == 50, "All views removes this part's individual pose overrides");
        check(PartPoses.Set(one, control, 2, null).SequenceEqual(shared) && PartPoses.Set(one, control, -1, null).Length == 0, "Reset shared and use-shared operations remove the intended overrides");
        var capturedOverride = PartPoses.Set(shared, control, 2, -1);
        check(ReferenceEquals(PartPoses.Apply(source, capturedOverride, 2), a), "Per-view captured reset overrides a shared pose without rounding the original animation state");
        var partner = control with
        {
            Part = 3,
            SymmetryParts = [1, 3]
        };
        control = control with
        {
            SymmetryParts = [1, 3]
        };
        var symmetrical = source with
        {
            Controls = [control, partner, control with { Part = 4, SymmetryParts = [4] }]
        };
        var linked = PartPoses.SetLinked(symmetrical, [], control, -1, 100, true);
        check(linked.Length == 2 && linked.All(p => p.Part is 1 or 3), "Keep symmetry changes the linked group, not unrelated identical parts");
        var individual = PartPoses.SetLinked(symmetrical, linked, control, -1, 30, false);
        check(individual.Single(p => p.Part == 1).Percent == 30 && individual.Single(p => p.Part == 3).Percent == 100, "Unchecking Keep symmetry changes only the selected part and retains partners' poses");
        var posed = PartPoses.Apply(source, one, 2);
        check(posed.Geometry.Meshes[0].Vertices[0].SequenceEqual(new double[] { .25, .5, 0 }) && a.Geometry.Meshes[0].Vertices[0].SequenceEqual(new double[] { 0, 0, 0 }), "Posing transforms detached geometry in the right axes without mutating captured vertices");
        var parent = new PoseControl(1, "parent", "Parent", false, 0, [new(1, 1, [1, 2], Enumerable.Repeat(Matrix4x4.CreateRotationZ(MathF.PI / 2), 101).ToArray())]);
        var nested = source with
        {
            Controls = [parent, control]
        };
        var combined = PartPoses.Apply(nested, [new(1, "parent", -1, 100), new(1, "door", -1, 100)], 1);
        check(Vector3.Distance(ExplodedGeometry.World(combined.Geometry.Meshes[0].Vertices[0]), new(0, 2, 1)) < .0001f, "Nested actuator transforms compose child before parent independent of control order");
        try
        {
            PartPoses.Validate(source, [new(99, "missing", -1, 0)], [1, 2]);
            throw new Exception("missing control accepted");
        }
        catch (InvalidOperationException) { check(true, "Regeneration rejects missing actuator before publishing a partial print"); }
        var options = new PrintOptions(Layout: 2, DrawingSize: 100, TitleBlock: new(Title: "Pose test"), Boxes: new(A: 1, B: 2, C: -1, D: -1));
        var original = PosePainting.RenderMain(source, "Pose test", options, [], [], default);
        int first = original.Scene!.Views[0].Id, second = original.Scene.Views[1].Id;
        var viewPoses = PartPoses.Set(shared, control, second, 0);
        var changed = PosePainting.Rebuild(original, viewPoses, default);
        check(changed.Scene!.Poses.SequenceEqual(viewPoses) && changed.Scene.Views.Select(v => v.Frame!.Scale).Distinct().Count() == 1, "Posed main views keep one global scale with independently rendered geometry");
        check(changed.Scene.ViewHeights[first].Top == 2 && changed.Scene.ViewHeights[second].Top == 1, "Each view's height dimension measures that view's pose");
        var doc = new AnnotationDocument(original.Scene);
        doc.Rebind(changed.Scene, [], []);
        doc.Undo();
        check(doc.Scene.Poses.Length == 0, "Undo restores captured pose choices");
        doc.Redo();
        check(doc.Scene.Poses.SequenceEqual(viewPoses), "Redo restores per-view and shared pose choices");
        var again = PosePainting.Rebuild(changed, [], default);
        check(again.Rgb.SequenceEqual(original.Rgb), "Resetting all poses regenerates the exact original drawing");
        var detail = new ExplodedDetail(1, 1, new(1400, 900, 700, 800), "Bay", Separation: 0);
        var withDetail = ExplodedPainting.Compose(changed.PoseMain!, changed, [detail], [], default);
        var detailPoses = PartPoses.Set(viewPoses, control, detail.ViewId, 0);
        var detailed = PosePainting.Rebuild(withDetail, detailPoses, default);
        check(!detailed.Scene!.Details[0].Pixels.Ink.SequenceEqual(withDetail.Scene!.Details[0].Pixels.Ink) || detailed.Scene.Details[0].Frame.Center != withDetail.Scene.Details[0].Frame.Center, "Detail-only pose invalidates cached detail geometry");
        check(detailed.Scene.Poses.SequenceEqual(detailPoses) && detailed.Details.Length == 1, "Detail posing preserves the detail frame and pose overrides");
    }
}
