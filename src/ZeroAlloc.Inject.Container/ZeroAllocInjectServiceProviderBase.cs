using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Container;

public abstract class ZeroAllocInjectServiceProviderBase : IServiceProvider, IServiceScopeFactory, IServiceProviderIsService, IServiceProviderIsKeyedService, IDisposable, IAsyncDisposable
{
    private readonly IServiceCollection _fallbackServices;
    private IServiceProvider? _fallback;
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

        return ResolveKnown(serviceType) ?? Fallback.GetService(serviceType);
    }

    protected abstract object? ResolveKnown(Type serviceType);

    protected abstract bool IsKnownService(Type serviceType);

    protected abstract bool IsKnownKeyedService(Type serviceType, object? serviceKey);

    public bool IsService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScopeFactory)
            || serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService))
            return true;
        if (IsKnownService(serviceType)) return true;
        // Don't materialize the fallback just to answer IsService — only consult if it's already built.
        var existing = _fallback;
        return (existing as IServiceProviderIsService)?.IsService(serviceType) == true;
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        if (IsKnownKeyedService(serviceType, serviceKey)) return true;
        // Same short-circuit as IsService — don't build the fallback to answer a query.
        var existing = _fallback;
        return (existing as IServiceProviderIsKeyedService)?.IsKeyedService(serviceType, serviceKey) == true;
    }

    public IServiceScope CreateScope()
    {
        var fallbackScopeFactory = Fallback.GetRequiredService<IServiceScopeFactory>();
        return CreateScopeCore(fallbackScopeFactory);
    }

    protected abstract ZeroAllocInjectScope CreateScopeCore(IServiceScopeFactory fallbackScopeFactory);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            (_fallback as IDisposable)?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
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
