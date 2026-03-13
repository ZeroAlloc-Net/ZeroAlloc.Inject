using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectStandaloneProvider : IServiceProvider, IServiceScopeFactory, IDisposable, IAsyncDisposable
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

        return ResolveKnown(serviceType);
    }

    protected abstract object? ResolveKnown(Type serviceType);

    public IServiceScope CreateScope()
    {
        return CreateScopeCore();
    }

    protected abstract ZeroInjectStandaloneScope CreateScopeCore();

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
