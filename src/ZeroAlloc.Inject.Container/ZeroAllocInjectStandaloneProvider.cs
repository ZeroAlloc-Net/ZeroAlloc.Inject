using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Container;

public abstract class ZeroAllocInjectStandaloneProvider : IServiceProvider, IServiceScopeFactory, IServiceProviderIsService, IServiceProviderIsKeyedService, IKeyedServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly DisposableTracker _tracked = new DisposableTracker();
    private int _disposed;

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider))
        {
            return this;
        }

        if (serviceType == typeof(IServiceScopeFactory))
        {
            return this;
        }

        if (serviceType == typeof(IServiceProviderIsService))
        {
            return this;
        }

        if (serviceType == typeof(IServiceProviderIsKeyedService))
        {
            return this;
        }

        if (serviceType == typeof(IKeyedServiceProvider))
        {
            return this;
        }

        return ResolveKnown(serviceType);
    }

    /// <summary>
    /// Resolves a keyed service this provider knows, or null. A null key resolves the unkeyed
    /// service, as in Microsoft DI.
    /// </summary>
    public object? GetKeyedService(Type serviceType, object? serviceKey) =>
        serviceKey is null ? GetService(serviceType) : ResolveKnownKeyed(serviceType, serviceKey);

    /// <summary>Resolves a keyed service as <see cref="GetKeyedService"/> does, and throws when it resolves none.</summary>
    public object GetRequiredKeyedService(Type serviceType, object? serviceKey) =>
        GetKeyedService(serviceType, serviceKey) ?? throw NoKeyedService(serviceType, serviceKey);

    internal static InvalidOperationException NoKeyedService(Type serviceType, object? serviceKey) =>
        new InvalidOperationException(serviceKey is null
            ? $"No service for type '{serviceType}' has been registered."
            : $"No keyed service of type '{serviceType}' with key '{serviceKey}' has been registered.");

    protected abstract object? ResolveKnown(Type serviceType);

    /// <summary>
    /// The keyed service of this type and key that the generated provider knows, or null. The base
    /// knows none.
    /// </summary>
    protected virtual object? ResolveKnownKeyed(Type serviceType, object serviceKey) => null;

    protected abstract bool IsKnownService(Type serviceType);

    protected abstract bool IsKnownKeyedService(Type serviceType, object? serviceKey);

    public bool IsService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScopeFactory)
            || serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService)
            || serviceType == typeof(IKeyedServiceProvider))
            return true;
        return IsKnownService(serviceType);
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        return IsKnownKeyedService(serviceType, serviceKey);
    }

    public IServiceScope CreateScope()
    {
        return CreateScopeCore();
    }

    protected abstract ZeroAllocInjectStandaloneScope CreateScopeCore();

    /// <summary>
    /// Tracks an instance this provider created, when it is <see cref="IDisposable"/> or
    /// <see cref="IAsyncDisposable"/>, so disposing the provider disposes it, in reverse creation
    /// order, as Microsoft DI does for the transients and singletons of its root.
    /// </summary>
    protected T TrackDisposable<T>(T instance)
        where T : notnull
    {
        _tracked.Track(instance);
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
            _tracked.DisposeAll();
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _tracked.DisposeAllAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
    }
}
