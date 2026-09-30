using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Container;

public abstract class ZeroAllocInjectServiceProviderBase : IServiceProvider, IServiceScopeFactory, IServiceProviderIsService, IServiceProviderIsKeyedService, IKeyedServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly IServiceCollection _fallbackServices;
    private IServiceProvider? _fallback;
    private readonly DisposableTracker _tracked = new DisposableTracker();
    private int _disposed;

    protected ZeroAllocInjectServiceProviderBase(IServiceCollection fallbackServices)
    {
        _fallbackServices = fallbackServices ?? throw new ArgumentNullException(nameof(fallbackServices));
    }

    /// <summary>The MS DI fallback provider. Materializes on first access via Interlocked.CompareExchange — applications whose registrations are fully ZA-owned never pay the BuildServiceProvider cost.</summary>
    protected IServiceProvider Fallback => GetOrCreateFallbackProvider();

    private IServiceProvider GetOrCreateFallbackProvider()
    {
        var existing = _fallback;
        if (existing is not null) return existing;
        var fresh = _fallbackServices.BuildServiceProvider();
        var winner = Interlocked.CompareExchange(ref _fallback, fresh, null);
        if (winner is not null)
        {
            // Lost the race — another thread materialized the provider first.
            // Dispose our loser (it never resolved anything) and use the winner.
            (fresh as IDisposable)?.Dispose();
            return winner;
        }
        return fresh;
    }

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

        return ResolveKnown(serviceType) ?? Fallback.GetService(serviceType);
    }

    /// <summary>
    /// Resolves a keyed service: one this provider knows, or else one its Microsoft DI fallback
    /// resolves, as <see cref="GetService"/> does for an unkeyed service. A null key resolves the
    /// unkeyed service, as in Microsoft DI.
    /// </summary>
    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceKey is null)
        {
            return GetService(serviceType);
        }

        return ResolveKnownKeyed(serviceType, serviceKey) ?? KeyedFallback(Fallback).GetKeyedService(serviceType, serviceKey);
    }

    /// <summary>
    /// Resolves a keyed service as <see cref="GetKeyedService"/> does, and throws the Microsoft DI
    /// fallback's exception when neither resolves it.
    /// </summary>
    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        if (serviceKey is null)
        {
            return this.GetRequiredService(serviceType);
        }

        return ResolveKnownKeyed(serviceType, serviceKey) ?? KeyedFallback(Fallback).GetRequiredKeyedService(serviceType, serviceKey);
    }

    /// <summary>The keyed view of the Microsoft DI provider or scope that this container falls back to.</summary>
    internal static IKeyedServiceProvider KeyedFallback(IServiceProvider fallback) =>
        fallback as IKeyedServiceProvider ?? fallback.GetRequiredService<IKeyedServiceProvider>();

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
        if (IsKnownService(serviceType)) return true;
        // Don't materialize the fallback just to answer IsService — only consult if it's already built.
        var existing = _fallback;
        if (existing is null) return false;
        var iss = existing as IServiceProviderIsService ?? (IServiceProviderIsService?)existing.GetService(typeof(IServiceProviderIsService));
        return iss?.IsService(serviceType) == true;
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        if (IsKnownKeyedService(serviceType, serviceKey)) return true;
        // Same short-circuit as IsService — don't build the fallback to answer a query.
        var existing = _fallback;
        if (existing is null) return false;
        var iks = existing as IServiceProviderIsKeyedService ?? (IServiceProviderIsKeyedService?)existing.GetService(typeof(IServiceProviderIsKeyedService));
        return iks?.IsKeyedService(serviceType, serviceKey) == true;
    }

    public IServiceScope CreateScope()
    {
        var fallbackScopeFactory = Fallback.GetRequiredService<IServiceScopeFactory>();
        return CreateScopeCore(fallbackScopeFactory);
    }

    protected abstract ZeroAllocInjectScope CreateScopeCore(IServiceScopeFactory fallbackScopeFactory);

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
            (_fallback as IDisposable)?.Dispose();
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            await _tracked.DisposeAllAsync().ConfigureAwait(false);

            if (_fallback is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                (_fallback as IDisposable)?.Dispose();
            }
        }

        GC.SuppressFinalize(this);
    }
}
