using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Container;

public abstract class ZeroAllocInjectServiceProviderBase : IServiceProvider, IServiceScopeFactory, IServiceProviderIsService, IServiceProviderIsKeyedService, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _fallback;
    private int _disposed;

    protected ZeroAllocInjectServiceProviderBase(IServiceProvider fallback)
    {
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    protected IServiceProvider Fallback => _fallback;

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

        return ResolveKnown(serviceType) ?? _fallback.GetService(serviceType);
    }

    protected abstract object? ResolveKnown(Type serviceType);

    protected abstract bool IsKnownService(Type serviceType);

    protected abstract bool IsKnownKeyedService(Type serviceType, object? serviceKey);

    public bool IsService(Type serviceType)
    {
        if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScopeFactory)
            || serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService))
            return true;
        return IsKnownService(serviceType)
            || (_fallback as IServiceProviderIsService)?.IsService(serviceType) == true;
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        return IsKnownKeyedService(serviceType, serviceKey)
            || (_fallback as IServiceProviderIsKeyedService)?.IsKeyedService(serviceType, serviceKey) == true;
    }

    public IServiceScope CreateScope()
    {
        var fallbackScopeFactory = _fallback.GetRequiredService<IServiceScopeFactory>();
        var fallbackScope = fallbackScopeFactory.CreateScope();
        return CreateScopeCore(fallbackScope);
    }

    protected abstract ZeroAllocInjectScope CreateScopeCore(IServiceScope fallbackScope);

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
