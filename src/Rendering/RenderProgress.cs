namespace Blueprint.Rendering;

// Render jobs run synchronously on individual worker threads. Only immutable
// snapshots cross to the UI, and a cancelled worker owns its old reporter.
sealed class RenderProgress
{
    internal sealed record Snapshot(string Label, float? Fraction);
    [ThreadStatic] static RenderProgress? worker;
    Snapshot latest;
    public RenderProgress(string label) => latest = new(label, null);
    public Snapshot Latest => Volatile.Read(ref latest);

    public T Run<T>(Func<T> render)
    {
        var previous = worker;
        worker = this;
        try { return render(); }
        finally { worker = previous; }
    }

    public static void Report(string label, float? fraction = null)
    {
        if (worker is { } active)
            Volatile.Write(ref active.latest, new(label, fraction));
    }
}
