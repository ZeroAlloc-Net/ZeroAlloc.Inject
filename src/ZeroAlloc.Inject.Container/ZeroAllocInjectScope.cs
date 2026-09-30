using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Container;

public abstract class ZeroAllocInjectScope : IServiceScope, IServiceProvider, IServiceProviderIsService, IServiceProviderIsKeyedService, IKeyedServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly ZeroAllocInjectServiceProviderBase _root;
    private readonly IServiceScopeFactory _fallbackScopeFactory;
    private IServiceScope? _fallbackScope;
    private readonly object _trackLock = new object();
    private List<object>? _disposables;
    private int _disposed;

    protected ZeroAllocInjectScope(ZeroAllocInjectServiceProviderBase root, IServiceScopeFactory fallbackScopeFactory)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _fallbackScopeFactory = fallbackScopeFactory ?? throw new ArgumentNullException(nameof(fallbackScopeFactory));
    }

    private IServiceScope GetOrCreateFallbackScope()
    {
        var existing = _fallbackScope;
        if (existing is not null) return existing;
        var fresh = _fallbackScopeFactory.CreateScope();
        var winner = Interlocked.CompareExchange(ref _fallbackScope, fresh, null);
        if (winner is not null)
        {
            // Lost the race — another thread materialized the scope first.
            // Dispose our loser (it never resolved anything) and use the winner.
            fresh.Dispose();
            return winner;
        }
        return fresh;
    }

    protected ZeroAllocInjectServiceProviderBase Root => _root;

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

        if (serviceType == typeof(IServiceProviderIsKeyedService))
        {
            return _root;
        }

        if (serviceType == typeof(IKeyedServiceProvider))
        {
            return this;
        }

        return ResolveScopedKnown(serviceType) ?? GetOrCreateFallbackScope().ServiceProvider.GetService(serviceType);
    }

    /// <summary>
    /// Resolves a keyed service: one this scope knows, or else one its Microsoft DI fallback scope
    /// resolves, as <see cref="GetService"/> does for an unkeyed service. A null key resolves the
    /// unkeyed service, as in Microsoft DI.
    /// </summary>
    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceKey is null)
        {
            return GetService(serviceType);
        }

        return ResolveScopedKnownKeyed(serviceType, serviceKey)
            ?? ZeroAllocInjectServiceProviderBase.KeyedFallback(GetOrCreateFallbackScope().ServiceProvider).GetKeyedService(serviceType, serviceKey);
    }

    /// <summary>
    /// Resolves a keyed service as <see cref="GetKeyedService"/> does, and throws the Microsoft DI
    /// fallback scope's exception when neither resolves it.
    /// </summary>
    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceKey is null)
        {
            return this.GetRequiredService(serviceType);
        }

        return ResolveScopedKnownKeyed(serviceType, serviceKey)
            ?? ZeroAllocInjectServiceProviderBase.KeyedFallback(GetOrCreateFallbackScope().ServiceProvider).GetRequiredKeyedService(serviceType, serviceKey);
    }

    protected abstract object? ResolveScopedKnown(Type serviceType);

    /// <summary>
    /// The keyed service of this type and key that the generated scope knows, or null. The base
    /// knows none.
    /// </summary>
    protected virtual object? ResolveScopedKnownKeyed(Type serviceType, object serviceKey) => null;

    public bool IsService(Type serviceType) => ((IServiceProviderIsService)_root).IsService(serviceType);

    public bool IsKeyedService(Type serviceType, object? serviceKey) => ((IServiceProviderIsKeyedService)_root).IsKeyedService(serviceType, serviceKey);

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

            _fallbackScope?.Dispose();
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
                _fallbackScope?.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }
}
