namespace Blueprint.Rendering;

sealed record PrintTemplate(string Name, string Hint, FixedViewLayout Layout);
static class PrintTemplates
{
    public static readonly PrintTemplate[] All = [
        new("Engineering","Four complementary views",new()),
        new("Room for a detail","Top, blank, side, isometric",new(A:3,B:-1,C:2,D:4)),
        new("Side + isometric","Two views side by side",new(Template:2,A:2,B:4)),
        new("Isometric","One large three-dimensional view",new(Template:1,A:4)),
        new("Side elevation","One large side view",new(Template:1,A:2)),
        new("Three across","Front, side and top",new(Template:4,A:1,B:2,C:3)),
        new("Two above, one below","Front and side over isometric",new(Template:8,A:1,B:2,C:4)),
        new("Top above two views","Top over side and isometric",new(Template:9,A:3,B:2,C:4)),
        new("Two stacked","Front over isometric",new(Template:3,A:1,B:4)),
        new("Three stacked","Top, front and side",new(Template:5,A:3,B:1,C:2)),
        new("Three + wide view","Three elevations over isometric",new(Template:6,A:1,B:2,C:3,D:4)),
        new("Three + centered view","Space beside the lower view",new(Template:7,A:1,B:2,C:3,D:4)),
        new("Six / three across","Five views and a blank space",new(Template:10,A:1,B:2,C:3,D:4,E:0,F:-1)),
        new("Six / two across","Five views and a blank space",new(Template:11,A:1,B:2,C:3,D:4,E:0,F:-1))
    ];

    public static bool Matches(FixedViewLayout a, FixedViewLayout b) => a.Template == b.Template &&
        a.Assignments.Take(Count(a)).SequenceEqual(b.Assignments.Take(Count(b)));

    static int Count(FixedViewLayout layout) => FixedViewLayouts.Cells(layout.Template, new(0, 0, 1000, 1000)).Length;

    public static string Name(FixedViewLayout layout) => All.FirstOrDefault(t => Matches(t.Layout, layout))?.Name ?? "Custom layout";
}
