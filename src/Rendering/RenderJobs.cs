namespace Blueprint.Rendering;

// A revision identifies accepted/draft document state and its native source lifetime.
// Request sequence is separate: two requests for the same revision still supersede.
readonly record struct RenderRevision(Guid Document, long Revision, long SourceEpoch);

sealed class RenderJob<T> : IDisposable
{
    public RenderRevision Revision { get; }
    public long Sequence { get; }
    public CancellationTokenSource Cancellation { get; } = new();
    public Task<T> Task { get; }
    bool disposed;

    internal RenderJob(long sequence, RenderRevision revision, Func<CancellationToken, T> render)
    {
        Sequence = sequence;
        Revision = revision;
        var token = Cancellation.Token;
        Task = System.Threading.Tasks.Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            var result = render(token);
            token.ThrowIfCancellationRequested();
            return result;
        }, token);
        // Observe failures even after the editor has closed or a job was replaced.
        _ = Task.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Cancellation.Cancel();
        // Workers can still register cancellation callbacks until they finish.
        _ = Task.ContinueWith(_ => Cancellation.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}

// Owner-thread only. Workers receive detached inputs and a token, never this queue.
sealed class RenderJobs<T>
{
    long sequence;
    public RenderJob<T>? Current { get; private set; }

    public RenderJob<T> Start(RenderRevision revision, Func<CancellationToken, T> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        Current?.Dispose();
        return Current = new(checked(++sequence), revision, render);
    }

    public bool Matches(RenderJob<T> job, RenderRevision revision) => ReferenceEquals(Current, job) && job.Revision == revision;

    public bool CanAccept(RenderJob<T> job, RenderRevision revision) =>
        Matches(job, revision) &&
        !job.Cancellation.IsCancellationRequested && job.Task.Status == TaskStatus.RanToCompletion;

    public void Finish(RenderJob<T> job)
    {
        if (ReferenceEquals(Current, job)) Current = null;
        job.Dispose();
    }

    public void Clear()
    {
        Current?.Dispose();
        Current = null;
    }
}

// All retained/transient allocations must be assigned to an owner before S09
// implements a measured planner. A declaration is not a claim of available RAM.
enum DrawingResourceOwner { Source, AcceptedPreview, PendingRender, Export, History, ReviewGhost }
readonly record struct ResourceEstimate(long GeometryBytes, long MaterialBytes, long PixelBytes)
{
    public long Total => checked(GeometryBytes + MaterialBytes + PixelBytes);
    public void Validate()
    {
        if (GeometryBytes < 0 || MaterialBytes < 0 || PixelBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(ResourceEstimate));
        _ = Total;
    }
}
interface IRenderReservation : IDisposable
{
    DrawingResourceOwner Owner { get; }
    long ReservedBytes { get; }
}
interface IRenderBudget
{
    IRenderReservation Reserve(DrawingResourceOwner owner, ResourceEstimate estimate, CancellationToken token);
}
readonly record struct ViewCacheKey(SourceBinding Source, Guid Document, long Revision, int View, string Purpose);
interface IViewRenderCache<T>
{
    bool TryGet(ViewCacheKey key, out T? value);
    // Cache takes ownership of the reservation, including disposal on replacement.
    void Store(ViewCacheKey key, T value, IRenderReservation reservation);
    void Clear();
}
