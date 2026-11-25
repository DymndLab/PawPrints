using Blueprint.Rendering;
using System.Collections.Immutable;
using System.Text;

static class FoundationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var revision = new RenderRevision(Guid.NewGuid(), 1, 1);
        var jobs = new RenderJobs<int>();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var old = jobs.Start(revision, token =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            return 1; // Deliberately ignores cancellation until the queue's final check.
        });
        try
        {
            check(entered.Wait(TimeSpan.FromSeconds(5)), "First render has started before replacement");
            var latest = jobs.Start(revision, _ => 2);
            check(latest.Task.GetAwaiter().GetResult() == 2 && jobs.CanAccept(latest, revision),
                "Newest completed request for the same revision can be accepted");
            release.Set();
            try { _ = old.Task.GetAwaiter().GetResult(); check(false, "Superseded worker must cancel"); }
            catch (OperationCanceledException) { check(true, "Superseded worker cannot return a late success even if its body ignores cancellation"); }
            check(!jobs.CanAccept(old, revision), "Out-of-order old completion cannot replace the latest result");
            check(!jobs.CanAccept(latest, revision with { Revision = 2 }), "Document edits invalidate an earlier render revision");
            check(!jobs.CanAccept(latest, revision with { SourceEpoch = 2 }), "Source changes invalidate completed-but-unaccepted results");
            latest.Cancellation.Cancel();
            check(!jobs.CanAccept(latest, revision), "Cancel after completion still prevents acceptance");
            jobs.Finish(latest);
            var failed = jobs.Start(revision, _ => throw new InvalidOperationException("fixture failure"));
            try { _ = failed.Task.GetAwaiter().GetResult(); }
            catch (InvalidOperationException) { }
            check(!jobs.CanAccept(failed, revision), "Failed render cannot become the accepted drawing");
            jobs.Clear();
            check(jobs.Current == null, "Closing a source retires its render job");
        }
        finally { release.Set(); jobs.Clear(); }

        var session = Guid.NewGuid();
        var binding = new SourceBinding(session, 1);
        SourcePart[] parts = [new(1, "cargo", null, false), new(2, "tank", null, false), new(3, "engine", 2, false)];
        var joint = JointKey.Between(new(1, "bottom"), new(2, "top"));
        SourceLink[] links = [new(SourceLinkKind.Structural, joint), new(SourceLinkKind.Ownership, JointKey.Between(new(2, ""), new(3, ""))),
            new(SourceLinkKind.Fuel, JointKey.Between(new(1, ""), new(3, "")))];
        var topology = new SourceTopology(binding, parts, links);
        check(topology.ResolveJoint(binding, new(joint.B, joint.A)) == BindingStatus.Exact,
            "A structural joint resolves by both connector endpoints independent of traversal direction");
        check(topology.ResolveJoint(binding, links[2].Key) == BindingStatus.Missing,
            "Fuel links do not become structural snip connections");
        check(topology.ResolveJoint(binding, links[1].Key) == BindingStatus.Missing,
            "Owned subparts do not become structural snip connections");
        var edited = new SourceTopology(binding with { Revision = 2 }, parts, links);
        check(edited.ResolvePart(binding, 2, "tank") == BindingStatus.Exact,
            "Surviving runtime part identities resolve across recaptures in one source session");
        check(edited.ResolvePart(binding, 2, "replacement") == BindingStatus.Missing,
            "Template mismatch cannot silently rebind a replaced part");
        check(edited.ResolvePart(binding with { Session = Guid.NewGuid() }, 2, "tank") == BindingStatus.RebindRequired,
            "Matching numbers and templates after restart require explicit rebinding");
        var ambiguous = new SourceTopology(binding, parts, [links[0], links[0]]);
        check(ambiguous.ResolveJoint(binding, joint) == BindingStatus.Ambiguous,
            "Indistinguishable parallel joints are reported as ambiguous");
        parts[0] = new(9, "changed input", null, false);
        check(topology.Parts[0].Id == 1, "Source topology owns immutable copies of captured definitions");
        Expect<InvalidDataException>(() => new SourceTopology(binding, [parts[1], parts[1]], []), check,
            "Duplicate runtime identities are rejected");

        var options = new PrintOptions(Orientation: 3, CustomWidth: 1280, CustomHeight: 720,
            TitleBlock: new(Title: "Kept appearance", CreatorInitialsSize: 175),
            Drawing: new(100, [new(1, 1, 250, 250), new(2, 2, 500, 250, Parent: 1)]));
        int pixels = checked(options.Page.Width * options.Page.Height);
        var scene = new RenderScene(new byte[pixels * 3], Enumerable.Repeat(-1, pixels).ToArray(), [],
            [new(1, "Front", new(100, 100, 100, 100), null), new(2, "Side", new(400, 100, 100, 100), null)], options: options);
        scene = scene.WithDrawing(options.Drawing);
        var sheet = new Sheet("Synthetic", options, [1, 2, 3], [1, 2, 3, 255], []) { Scene = scene };
        var document = new AnnotationDocument(scene);
        var accepted = document.Fork();
        document.Rebind(scene.WithOptions(options with { Theme = 2 }), [], []);
        long editedRevision = document.Revision;
        check(document.CanUndo && !accepted.CanUndo, "Candidate history does not mutate the accepted checkpoint");
        document.Undo();
        check(document.Scene.Options.Theme == 0 && document.Revision > editedRevision, "Undo restores definitions and advances the render revision");
        document.Redo();
        check(document.Scene.Options.Theme == 2 && document.CanUndo, "Redo reapplies a compact definition edit");
        document.Restore(accepted);
        check(document.Scene.Options.Theme == 0 && !document.CanUndo && !document.CanRedo,
            "Failed-edit recovery restores accepted definitions and both history stacks");
        var candidate = DrawingAcceptance.Prepare(sheet with { Options = options with { Theme = 1 }, Scene = scene.WithOptions(options with { Theme = 1 }) }, document, true, false);
        check(document.Scene.Options.Theme == 0 && candidate.Document.Scene.Options.Theme == 1 && candidate.Document.CanUndo,
            "Preparing a render commit leaves the current document untouched");
        check(ReferenceEquals(candidate.Sheet.Rgb, sheet.Rgb) && ReferenceEquals(candidate.Sheet.PreviewRgba, sheet.PreviewRgba),
            "Document acceptance preserves the renderer's exact pixel buffers");
        Expect<InvalidDataException>(() => DrawingAcceptance.Prepare(sheet with { Scene = null }, document, true, false), check,
            "Invalid candidate cannot replace the accepted document");
        check(document.Scene.Options.Theme == 0 && !document.CanUndo, "Failed preparation leaves history usable");
        var boundSource = new PoseSource(new(new Capture([]), [], [], []) { Topology = topology }, [], 0, [], new Dictionary<string, Texture>());
        var reloadedSource = boundSource with { Assembly = boundSource.Assembly with
        { Topology = new SourceTopology(binding with { Session = Guid.NewGuid() }, topology.Parts, topology.Links) } };
        Expect<InvalidOperationException>(() => PrintGeneration.Render(reloadedSource, new Capture([]), sheet.Title, options, [],
            sheet with { PoseSource = boundSource }, false, default), check,
            "Production generation rejects a foreign source session before rendering or rebinding labels");

        var definition = DrawingDefinitions.FromAccepted(sheet, document);
        check(definition.Layout.Groups.Single().Members.SequenceEqual([1, 2]), "Legacy parent links become an explicit projection group");
        var roundTrip = DrawingSchemas.ReadDrawing(DrawingSchemas.WriteDrawing(definition));
        check(roundTrip.Id == definition.Id && roundTrip.Output == definition.Output && roundTrip.Appearance == definition.Appearance &&
            roundTrip.Layout.Groups.Single().Members.SequenceEqual([1, 2]), "Draft drawing schema round-trips output, appearance and group identity");
        var scoped = definition with
        {
            Source = binding,
            Layout = definition.Layout with { Views = definition.Layout.Views.Add(new(1001, 4, new(0, 0, 200, 200), 100,
                Exploded: true, Scope: new(1, [joint], [3], [1, 2]))) }
        };
        var scopedAgain = DrawingSchemas.ReadDrawing(DrawingSchemas.WriteDrawing(scoped));
        check(scopedAgain.Layout.Views[^1].Scope!.Cuts.Single() == joint && scopedAgain.Source == binding,
            "Scoped drawing draft retains its root, endpoint cuts and source session");
        var template = DrawingDefinitions.ToTemplate(scoped);
        var bytes = DrawingSchemas.WriteTemplate(template);
        var text = Encoding.UTF8.GetString(bytes);
        var templateAgain = DrawingSchemas.ReadTemplate(bytes);
        check(templateAgain.Layout.Views[^1].Scope == null && templateAgain.Layout.Views[^1].NeedsScopeBinding,
            "Layout template strips craft-specific cuts and requests assembly rebinding");
        check(!text.Contains("Kept appearance") && !text.Contains(session.ToString()) && !text.Contains("CreatorInitials") && !text.Contains("Output"),
            "Templates contain no session appearance, output settings or source identity");
        check(Math.Abs(templateAgain.Layout.Views[0].Bounds.X - 100f / options.Page.Width) < .00001,
            "Template layout uses output-relative coordinates");
        Expect<InvalidDataException>(() => DrawingSchemas.ReadDrawing(bytes), check, "Template payload cannot masquerade as a drawing");
        var future = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(DrawingSchemas.WriteDrawing(definition)).Replace("\"Version\":1", "\"Version\":2"));
        Expect<InvalidDataException>(() => DrawingSchemas.ReadDrawing(future), check, "Unknown future drawing schema is rejected");
        Expect<InvalidDataException>(() => DrawingSchemas.ReadDrawing(new byte[DrawingSchemas.MaxBytes + 1]), check,
            "Drawing byte budget is enforced before JSON parsing");
        Expect<InvalidDataException>(() => DrawingSchemas.WriteTemplate(template with { Layout = template.Layout with
        { Views = template.Layout.Views.SetItem(0, template.Layout.Views[0] with { Parent = 2 }) } }), check,
            "Cyclic projection links are rejected at the schema boundary");
        Expect<OverflowException>(() => new ResourceEstimate(long.MaxValue, 1, 0).Validate(), check, "Resource estimates use checked arithmetic");
        Expect<ArgumentOutOfRangeException>(() => new ResourceEstimate(-1, 0, 0).Validate(), check, "Negative reservations cannot bypass a future budget");
        var notices = new DrawingNotices();
        for (int i = 0; i < 80; i++) notices.Add(new("fixture", i.ToString()));
        notices.Add(new("fixture", "79"));
        check(notices.Entries.Count == 64 && notices.Entries[0].Message == "16", "Activity notices are bounded and deduplicate adjacent repeats");
    }

    static void Expect<T>(Action run, Action<bool, string> check, string message) where T : Exception
    {
        try { run(); check(false, message); }
        catch (T) { check(true, message); }
    }
}
