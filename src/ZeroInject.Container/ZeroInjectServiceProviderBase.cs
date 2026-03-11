using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public abstract class ZeroInjectServiceProviderBase : IServiceProvider, IServiceScopeFactory, IDisposable, IAsyncDisposable
{
    private readonly IServiceProvider _fallback;
    private int _disposed;

    protected ZeroInjectServiceProviderBase(IServiceProvider fallback)
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

        return ResolveKnown(serviceType) ?? _fallback.GetService(serviceType);
    }

    protected abstract object? ResolveKnown(Type serviceType);

    public IServiceScope CreateScope()
    {
        var fallbackScopeFactory = _fallback.GetRequiredService<IServiceScopeFactory>();
        var fallbackScope = fallbackScopeFactory.CreateScope();
        return CreateScopeCore(fallbackScope);
    }

    protected abstract ZeroInjectScope CreateScopeCore(IServiceScope fallbackScope);

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
