namespace Blueprint.Rendering;

sealed record CraftRefreshReview(int Labels, int Poses, int Specs)
{
    public int Removed => Labels + Poses + Specs;
}

// All references are checked against the preceding accepted capture. The new
// drawing is built in isolation.
sealed class CraftRefresh
{
    readonly SourceTopology previous, current;
    readonly Dictionary<uint, string> templates;
    readonly HashSet<uint> surviving;
    int labels, poses, specs;

    public CraftRefresh(SourceTopology previous, SourceTopology current)
    {
        this.previous = previous;
        this.current = current;
        templates = previous.Parts.ToDictionary(p => p.Id, p => p.Template);
        surviving = current.Parts.Where(p => templates.TryGetValue(p.Id, out var id) && id == p.Template)
            .Select(p => p.Id).ToHashSet();
    }

    public bool Keeps(LabelPart part) => part.NodeId is { } id && surviving.Contains(id) && templates[id] == part.TemplateId;

    bool Survives(uint id) => surviving.Contains(id);

    public void RemovedLabel() => labels++;

    public PartPose[] Poses(PartPose[] items, PoseSource target)
    {
        var controls = target.Controls.Select(c => (c.Part, c.Key)).ToHashSet();
        var kept = items.Where(p => Survives(p.Part) && controls.Contains((p.Part, p.Key))).ToArray();
        poses = items.Length - kept.Length;
        return kept;
    }

    public SpecBlock[] Specs(SpecBlock[] items, PerformanceData? data)
    {
        var engines = data?.Engines.Where(e => Survives(e.Part)).ToArray() ?? [];
        var ids = engines.Select(e => e.Id).ToHashSet();
        var parts = engines.Select(e => e.Part).ToHashSet();
        var stages = data?.Stages.Select(s => s.Number).ToHashSet() ?? [];
        var kept = items.Where(b => data != null && (b.EnginePart == null || parts.Contains(b.EnginePart.Value)) &&
            (b.Stage < 0 || stages.Contains(b.Stage)) && (b.SelectedEngines?.All(ids.Contains) ?? true)).ToArray();
        specs = items.Length - kept.Length;
        return kept;
    }

    public ExplodedDetail Detail(ExplodedDetail detail)
    {
        if (detail.SourceIssue != DetailSourceIssue.None) return detail;
        if (!Survives(detail.Scope)) return detail with { SourceIssue = DetailSourceIssue.MissingRoot };

        var selection = detail.Selection;
        if (selection != null)
        {
            // Removing a cut could unexpectedly restore other branches.
            // Require explicit review if the connection can no longer be bound.
            if (selection.Cuts.Any(j => !Survives(j.A.Part) || !Survives(j.B.Part) ||
                current.ResolveJoint(previous.Binding, j) != BindingStatus.Exact))
                return detail with { SourceIssue = DetailSourceIssue.ChangedScope };

            selection = selection with
            {
                Removed = [.. selection.Removed.Where(Survives)],
                RemovedBranches = selection.RemovedBranches is { } branches ? [.. branches.Where(Survives)] : null,
                VisibleParts = selection.VisibleParts is { } visible ? [.. visible.Where(Survives)] : null
            };
        }
        return detail with
        {
            Selection = selection,
            Seed = detail.Seed is { } seed && Survives(seed) ? seed : detail.Scope,
            Expanded = detail.Expanded?.Where(Survives).ToArray(),
            Groups = detail.Groups?.Where(g => Survives(g.Node)).ToArray()
        };
    }

    public CraftRefreshReview Review() => new(labels, poses, specs);
}
