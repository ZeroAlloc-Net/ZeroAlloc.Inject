using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Collections;

namespace ZeroAlloc.Inject.Container;

public abstract class ZeroAllocInjectStandaloneScope : IServiceScope, IServiceProvider, IServiceProviderIsService, IServiceProviderIsKeyedService, IKeyedServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly ZeroAllocInjectStandaloneProvider _root;
    private readonly object _trackLock = new object();
    private List<object>? _disposables;
    private int _disposed;
    private HeapSpanDictionary<Type, object>? _openGenericScoped;

    protected ZeroAllocInjectStandaloneScope(ZeroAllocInjectStandaloneProvider root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    protected ZeroAllocInjectStandaloneProvider Root => _root;

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

        return ResolveScopedKnown(serviceType);
    }

    /// <summary>
    /// Resolves a keyed service this scope knows, or null. A null key resolves the unkeyed service,
    /// as in Microsoft DI.
    /// </summary>
    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        serviceKey is null ? GetService(serviceType) : ResolveScopedKnownKeyed(serviceType, serviceKey);

    /// <summary>Resolves a keyed service as <see cref="GetKeyedService"/> does, and throws when it resolves none.</summary>
    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        GetKeyedService(serviceType, serviceKey) ?? throw ZeroAllocInjectStandaloneProvider.NoKeyedService(serviceType, serviceKey);

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

    /// <summary>
    /// Returns the cached scoped instance for the given serviceType, creating it via the factory on first access.
    /// The created instance is tracked for disposal when the scope is disposed.
    /// </summary>
    protected object GetOrAddScopedOpenGeneric(Type serviceType, Func<object> factory)
    {
        lock (_trackLock)
        {
            _openGenericScoped ??= new HeapSpanDictionary<Type, object>();
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
            HeapSpanDictionary<Type, object>? openGenericScoped;
            lock (_trackLock)
            {
                snapshot = _disposables;
                _disposables = null;
                openGenericScoped = _openGenericScoped;
                _openGenericScoped = null;
            }

            openGenericScoped?.Dispose();

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
            HeapSpanDictionary<Type, object>? openGenericScoped;
            lock (_trackLock)
            {
                snapshot = _disposables;
                _disposables = null;
                openGenericScoped = _openGenericScoped;
                _openGenericScoped = null;
            }

            openGenericScoped?.Dispose();

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
