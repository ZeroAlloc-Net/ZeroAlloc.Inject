using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectStandaloneScope : IServiceScope, IServiceProvider, IServiceProviderIsService, IDisposable, IAsyncDisposable
{
    private readonly ZeroInjectStandaloneProvider _root;
    private readonly object _trackLock = new object();
    private List<object>? _disposables;
    private int _disposed;
    private System.Collections.Generic.Dictionary<Type, object>? _openGenericScoped;

    protected ZeroInjectStandaloneScope(ZeroInjectStandaloneProvider root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    protected ZeroInjectStandaloneProvider Root => _root;

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

        return ResolveScopedKnown(serviceType);
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

    /// <summary>
    /// Returns the cached scoped instance for the given serviceType, creating it via the factory on first access.
    /// The created instance is tracked for disposal when the scope is disposed.
    /// </summary>
    protected object GetOrAddScopedOpenGeneric(Type serviceType, Func<object> factory)
    {
        lock (_trackLock)
        {
            _openGenericScoped ??= new System.Collections.Generic.Dictionary<Type, object>();
            if (_openGenericScoped.TryGetValue(serviceType, out var existing)) return existing;
            var instance = factory();
            _openGenericScoped[serviceType] = instance;
            TrackDisposable(instance);
            return instance;
        }
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
        }

        GC.SuppressFinalize(this);
    }
}
