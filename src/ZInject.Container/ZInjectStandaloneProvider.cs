using Microsoft.Extensions.DependencyInjection;

namespace ZInject.Container;

public abstract class ZInjectStandaloneProvider : IServiceProvider, IServiceScopeFactory, IServiceProviderIsService, IServiceProviderIsKeyedService, IDisposable, IAsyncDisposable
{
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

        return ResolveKnown(serviceType);
    }

    protected abstract object? ResolveKnown(Type serviceType);

    protected abstract bool IsKnownService(Type serviceType);

    protected abstract bool IsKnownKeyedService(Type serviceType, object? serviceKey);

    public bool IsService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScopeFactory)
            || serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService))
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

    protected abstract ZInjectStandaloneScope CreateScopeCore();

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // No resources to dispose in standalone base — subclasses override
        }
    }

    public virtual ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // No fallback to dispose — just mark as disposed
        }

        GC.SuppressFinalize(this);
        return default;
    }
}
