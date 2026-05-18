using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject.Container;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

public class LazyFallbackScopeTests
{
    private sealed class ZAOwnedSentinel { }

    private sealed class CountingFallback : IServiceProvider, IServiceScopeFactory, IDisposable
    {
        public int CreateScopeCount;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            return null;
        }

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref CreateScopeCount);
            return new CountingFallbackScope(this);
        }

        public void Dispose() { }

        private sealed class CountingFallbackScope : IServiceScope, IAsyncDisposable
        {
            private readonly CountingFallback _parent;

            public CountingFallbackScope(CountingFallback parent) => _parent = parent;

            public IServiceProvider ServiceProvider => _parent;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class TestScope : ZeroAllocInjectScope
    {
        public TestScope(ZeroAllocInjectServiceProviderBase root, IServiceScopeFactory fallbackScopeFactory)
            : base(root, fallbackScopeFactory) { }

        protected override object? ResolveScopedKnown(Type serviceType)
        {
            if (serviceType == typeof(ZAOwnedSentinel))
            {
                return new ZAOwnedSentinel();
            }

            return null;
        }
    }

    private sealed class TestProvider : ZeroAllocInjectServiceProviderBase
    {
        public TestProvider(IServiceProvider fallback) : base(fallback) { }

        protected override object? ResolveKnown(Type serviceType) => null;

        protected override bool IsKnownService(Type serviceType) => false;

        protected override bool IsKnownKeyedService(Type serviceType, object? serviceKey) => false;

        protected override ZeroAllocInjectScope CreateScopeCore(IServiceScopeFactory fallbackScopeFactory)
            => new TestScope(this, fallbackScopeFactory);
    }

    [Fact]
    public void Hybrid_CreateScope_DoesNotCreateFallbackScope()
    {
        var fallback = new CountingFallback();
        var provider = new TestProvider(fallback);

        using var scope = provider.CreateScope();

        Assert.Equal(0, fallback.CreateScopeCount);
    }

    [Fact]
    public void Hybrid_Resolve_ZAOwnedService_NeverCreatesFallback()
    {
        var fallback = new CountingFallback();
        var provider = new TestProvider(fallback);

        using var scope = provider.CreateScope();
        var resolved = scope.ServiceProvider.GetService(typeof(ZAOwnedSentinel));

        Assert.NotNull(resolved);
        Assert.Equal(0, fallback.CreateScopeCount);
    }

    [Fact]
    public void Hybrid_Resolve_FallbackOnly_CreatesFallbackOnce()
    {
        var fallback = new CountingFallback();
        var provider = new TestProvider(fallback);

        using var scope = provider.CreateScope();
        _ = scope.ServiceProvider.GetService(typeof(string));
        _ = scope.ServiceProvider.GetService(typeof(string));

        Assert.Equal(1, fallback.CreateScopeCount);
    }

    [Fact]
    public void Hybrid_Dispose_WithNoFallback_DoesNotThrow()
    {
        var fallback = new CountingFallback();
        var provider = new TestProvider(fallback);
        var scope = provider.CreateScope();

        var exception = Record.Exception(() => scope.Dispose());

        Assert.Null(exception);
        Assert.Equal(0, fallback.CreateScopeCount);
    }

    [Fact]
    public async Task Hybrid_DisposeAsync_WithNoFallback_DoesNotThrow()
    {
        var fallback = new CountingFallback();
        var provider = new TestProvider(fallback);
        var scope = (IAsyncDisposable)provider.CreateScope();

        var exception = await Record.ExceptionAsync(async () => await scope.DisposeAsync());

        Assert.Null(exception);
        Assert.Equal(0, fallback.CreateScopeCount);
    }
}
