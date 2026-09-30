namespace ZeroAlloc.Inject.Container;

/// <summary>
/// The disposable instances a root provider created, in creation order. Disposing takes them all
/// and disposes them in reverse order, as Microsoft DI does, so an instance is disposed before the
/// ones it was built from. Tracking is safe from any thread.
/// </summary>
internal sealed class DisposableTracker
{
    private readonly object _lock = new object();
    private List<object>? _disposables;

    public void Track(object instance)
    {
        if (instance is not (IDisposable or IAsyncDisposable))
        {
            return;
        }

        lock (_lock)
        {
            _disposables ??= [];
            _disposables.Add(instance);
        }
    }

    /// <summary>Disposes every tracked <see cref="IDisposable"/>, last tracked first.</summary>
    public void DisposeAll()
    {
        var snapshot = TakeAll();
        if (snapshot is null)
        {
            return;
        }

        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            if (snapshot[i] is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>
    /// Disposes every tracked instance, last tracked first, through <see cref="IAsyncDisposable"/>
    /// when it implements it.
    /// </summary>
    public async ValueTask DisposeAllAsync()
    {
        var snapshot = TakeAll();
        if (snapshot is null)
        {
            return;
        }

        for (var i = snapshot.Count - 1; i >= 0; i--)
        {
            if (snapshot[i] is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (snapshot[i] is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private List<object>? TakeAll()
    {
        lock (_lock)
        {
            var snapshot = _disposables;
            _disposables = null;
            return snapshot;
        }
    }
}
