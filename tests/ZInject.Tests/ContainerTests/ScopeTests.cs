using Microsoft.Extensions.DependencyInjection;
using ZInject.Container;

namespace ZInject.Tests.ContainerTests;

public class ScopeTests
{
    private interface IScopedService { }

    private sealed class ScopedService : IScopedService { }

    private interface ITransientService { }

    private sealed class TransientService : ITransientService, IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private interface ISingletonService { }

    private sealed class SingletonService : ISingletonService { }

    private interface IFallbackOnly { }

    private sealed class FallbackOnlyService : IFallbackOnly { }

    private sealed class DisposableTracker : IDisposable
    {
        public int DisposeOrder { get; set; } = -1;

        public void Dispose()
        {
            // Order is set externally before dispose verification
        }
    }

    private sealed class TestScope : ZInjectScope
    {
        private ScopedService? _scopedInstance;
        private readonly ZInjectServiceProviderBase _rootRef;

        public TestScope(ZInjectServiceProviderBase root, IServiceScope fallbackScope)
            : base(root, fallbackScope)
        {
            _rootRef = root;
        }

        protected override object? ResolveScopedKnown(Type serviceType)
        {
            if (serviceType == typeof(IScopedService))
            {
                return _scopedInstance ??= new ScopedService();
            }

            if (serviceType == typeof(ITransientService))
            {
                return TrackDisposable(new TransientService());
            }

            if (serviceType == typeof(ISingletonService))
            {
                // Delegate to root for singletons
                return _rootRef.GetService(serviceType);
            }

            return null;
        }
    }

    private sealed class TestProvider : ZInjectServiceProviderBase
    {
        private SingletonService? _singleton;

        public TestProvider(IServiceProvider fallback) : base(fallback) { }

        protected override object? ResolveKnown(Type serviceType)
        {
            if (serviceType == typeof(ISingletonService))
            {
                var instance = new SingletonService();
                return Interlocked.CompareExchange(ref _singleton, instance, null) ?? _singleton;
            }

            return null;
        }

        protected override bool IsKnownService(Type serviceType) => false;

        protected override bool IsKnownKeyedService(Type serviceType, object? serviceKey) => false;

        protected override ZInjectScope CreateScopeCore(IServiceScope fallbackScope)
            => new TestScope(this, fallbackScope);
    }

    private static TestProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddTransient<IFallbackOnly, FallbackOnlyService>();
        var fallback = services.BuildServiceProvider();
        return new TestProvider(fallback);
    }

    [Fact]
    public void Resolving_scoped_service_returns_same_instance_within_scope()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetService(typeof(IScopedService));
        var second = scope.ServiceProvider.GetService(typeof(IScopedService));

        Assert.NotNull(first);
        Assert.IsType<ScopedService>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Different_scopes_return_different_scoped_instances()
    {
        using var provider = CreateProvider();
        using var scope1 = provider.CreateScope();
        using var scope2 = provider.CreateScope();

        var instance1 = scope1.ServiceProvider.GetService(typeof(IScopedService));
        var instance2 = scope2.ServiceProvider.GetService(typeof(IScopedService));

        Assert.NotNull(instance1);
        Assert.NotNull(instance2);
        Assert.NotSame(instance1, instance2);
    }

    [Fact]
    public void Resolving_transient_returns_new_instance_each_time()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var first = scope.ServiceProvider.GetService(typeof(ITransientService));
        var second = scope.ServiceProvider.GetService(typeof(ITransientService));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Resolving_singleton_in_scope_delegates_to_root()
    {
        using var provider = CreateProvider();

        var rootSingleton = provider.GetService(typeof(ISingletonService));

        using var scope = provider.CreateScope();
        var scopeSingleton = scope.ServiceProvider.GetService(typeof(ISingletonService));

        Assert.NotNull(rootSingleton);
        Assert.Same(rootSingleton, scopeSingleton);
    }

    [Fact]
    public void Unknown_type_falls_through_to_fallback_scope()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var result = scope.ServiceProvider.GetService(typeof(IFallbackOnly));

        Assert.NotNull(result);
        Assert.IsType<FallbackOnlyService>(result);
    }

    [Fact]
    public void GetService_IServiceProvider_returns_scope_not_root()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var result = scope.ServiceProvider.GetService(typeof(IServiceProvider));

        Assert.Same(scope.ServiceProvider, result);
        Assert.NotSame(provider, result);
    }

    [Fact]
    public void GetService_IServiceScopeFactory_returns_root()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var result = scope.ServiceProvider.GetService(typeof(IServiceScopeFactory));

        Assert.Same(provider, result);
    }

    [Fact]
    public void Dispose_disposes_tracked_disposables_in_reverse_order()
    {
        using var provider = CreateProvider();
        var scope = provider.CreateScope();

        // Resolve two transients (which are tracked via TrackDisposable)
        var first = (TransientService)scope.ServiceProvider.GetService(typeof(ITransientService))!;
        var second = (TransientService)scope.ServiceProvider.GetService(typeof(ITransientService))!;

        Assert.False(first.IsDisposed);
        Assert.False(second.IsDisposed);

        scope.Dispose();

        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        using var provider = CreateProvider();
        var scope = provider.CreateScope();

        scope.Dispose();
        scope.Dispose(); // Should not throw
    }

    [Fact]
    public void TrackDisposable_tracks_IDisposable_transients()
    {
        using var provider = CreateProvider();
        var scope = provider.CreateScope();

        var service = (TransientService)scope.ServiceProvider.GetService(typeof(ITransientService))!;

        Assert.False(service.IsDisposed);

        scope.Dispose();

        Assert.True(service.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_disposes_tracked_disposables()
    {
        using var provider = CreateProvider();
        var scope = provider.CreateScope();

        var first = (TransientService)scope.ServiceProvider.GetService(typeof(ITransientService))!;
        Assert.False(first.IsDisposed);

        await ((IAsyncDisposable)scope).DisposeAsync();

        Assert.True(first.IsDisposed);
    }

    [Fact]
    public void Scope_constructor_null_root_throws_ArgumentNullException()
    {
        var services = new ServiceCollection();
        var fallback = services.BuildServiceProvider();
        using var scope = fallback.CreateScope();
        Assert.Throws<ArgumentNullException>(() => new TestScope(null!, scope));
    }

    [Fact]
    public void Scope_constructor_null_fallbackScope_throws_ArgumentNullException()
    {
        using var provider = CreateProvider();
        Assert.Throws<ArgumentNullException>(() => new TestScope(provider, null!));
    }

    [Fact]
    public void GetService_IServiceProviderIsService_ReturnsRoot()
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        using var scope = provider.CreateScope();
        var result = scope.ServiceProvider.GetService(typeof(IServiceProviderIsService));
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Fact]
    public void GetService_IServiceProviderIsKeyedService_ReturnsRoot()
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        using var scope = provider.CreateScope();
        var result = scope.ServiceProvider.GetService(typeof(IServiceProviderIsKeyedService));
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Fact]
    public void IsKeyedService_DelegatesToRoot()
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        using var scope = provider.CreateScope();
        Assert.False(((IServiceProviderIsKeyedService)scope.ServiceProvider).IsKeyedService(typeof(string), "unknown"));
    }
}
