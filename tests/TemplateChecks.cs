using Blueprint.Rendering;
using System.Numerics;

static class TemplateChecks
{

    public static void Run(Action<bool, string> check)
    {
        var all = PrintTemplates.All;
        check(all.Length == 14 && all.Select(t => t.Name).Distinct().Count() == all.Length, "Visual template names are unique and all 14 are selectable");
        check(all.Select(t => t.Layout.Template).Distinct().Order().SequenceEqual(Enumerable.Range(0, 12)), "Visual catalog exposes every supported box topology");
        check(all.All(t => t.Layout.AutoRows && PrintTemplates.Name(t.Layout with { TopShare = .2f }) == t.Name), "Automatic fitting does not lose selected-template identity");
        check(PrintTemplates.Name(new(Template: 1, A: 0)) == "Custom layout" && PrintTemplates.Matches(new(Template: 1, A: 4, B: 1), new(Template: 1, A: 4, B: 2)), "Custom camera assignments are identified; unused slots do not affect template identity");
        int cases = 0;
        foreach (var t in all)
            for (int shape = 0; shape < 3; shape++)
                for (int ratio = 0; ratio < 6; ratio++)
                    for (int tables = 0; tables < 4; tables++)
                    {
                        var options = new PrintOptions(0, 0, true, false, 10, 2, 15, true, (tables & 1) != 0, (tables & 2) != 0, 100, shape, ratio, Boxes: t.Layout);
                        var area = PageLayout.Drawing(options);
                        var cells = FixedViewLayouts.Cells(t.Layout, area);
                        if (cells.Any(c => c.Width < 96 || c.Height < 120 || c.X < area.X || c.Y < area.Y || c.Right > area.Right + 1 || c.Bottom > area.Bottom + 1))
                            throw new Exception("Template outside printable area: " + t.Name);
                        var expected = t.Layout.Assignments.Take(cells.Length).Where(v => v >= 0);
                        if (!FixedViewLayouts.Cameras(options).SequenceEqual(expected))
                            throw new Exception("Camera mapping changed: " + t.Name);
                        cases++;
                    }
        check(cases == 1008, "All 1,008 template / paper / table combinations fit and retain exact camera assignments");
        check(all.Single(t => t.Name == "Room for a detail").Layout.Assignments.Take(4).SequenceEqual(new[] { 3, -1, 2, 4 }), "Detail template leaves upper-right slot empty without substituting a view");
        check(GenerationAvailability.Reason(false, false, false, true)?.Contains("Load a craft") == true && GenerationAvailability.Reason(true, false, false, true) == null, "Empty editor explains generation prerequisite; loading a craft enables generation");
        check(GenerationAvailability.Reason(true, true, false, true) != null && GenerationAvailability.Reason(true, false, true, true) != null && GenerationAvailability.Reason(true, false, false, false)?.Contains("assign a view") == true, "Busy, unfinished placement and blank layouts have explicit generation explanations");
        check(TemplateRocket.Views.All(v => v.Length > 0 && v.SelectMany(l => new[] { l.A, l.B }).All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && Math.Abs(p.X) <= 1.001f && Math.Abs(p.Y) <= 1.001f)) && !TemplateRocket.Views[4].SequenceEqual(TemplateRocket.Views[2]), "Projected template rockets stay inside icon bounds and isometric differs from side elevation");
    }
}
