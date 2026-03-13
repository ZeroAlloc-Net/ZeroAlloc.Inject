using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectScope : IServiceScope, IServiceProvider, IServiceProviderIsService, IDisposable, IAsyncDisposable
{
    private readonly ZeroInjectServiceProviderBase _root;
    private readonly IServiceScope _fallbackScope;
    private readonly object _trackLock = new object();
    private List<object>? _disposables;
    private int _disposed;

    protected ZeroInjectScope(ZeroInjectServiceProviderBase root, IServiceScope fallbackScope)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _fallbackScope = fallbackScope ?? throw new ArgumentNullException(nameof(fallbackScope));
    }

    protected ZeroInjectServiceProviderBase Root => _root;

    public IServiceProvider ServiceProvider => this;

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        if (serviceType == typeof(IServiceScopeFactory))
        {
            return _root;
        }

        if (serviceType == typeof(IServiceProviderIsService))
        {
            return _root;
        }

        return ResolveScopedKnown(serviceType) ?? _fallbackScope.ServiceProvider.GetService(serviceType);
    }

    protected abstract object? ResolveScopedKnown(Type serviceType);

    public bool IsService(Type serviceType) => ((IServiceProviderIsService)_root).IsService(serviceType);

    protected T TrackDisposable<T>(T instance)
        where T : notnull
    {
        if (instance is IDisposable or IAsyncDisposable)
        {
            lock (_trackLock)
            {
                _disposables ??= [];
                _disposables.Add(instance);
            }
        }

        return instance;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            List<object>? snapshot;
            lock (_trackLock)
            {
                snapshot = _disposables;
                _disposables = null;
            }

            if (snapshot is not null)
            {
                for (var i = snapshot.Count - 1; i >= 0; i--)
                {
                    if (snapshot[i] is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
            }

            _fallbackScope.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            List<object>? snapshot;
            lock (_trackLock)
            {
                snapshot = _disposables;
                _disposables = null;
            }

            if (snapshot is not null)
            {
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

            if (_fallbackScope is IAsyncDisposable asyncFallback)
            {
                await asyncFallback.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _fallbackScope.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }
}
