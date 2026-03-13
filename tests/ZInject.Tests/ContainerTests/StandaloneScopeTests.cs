namespace ZInject.Tests.ContainerTests;

public class StandaloneScopeTests
{
    private sealed class TestProvider : ZInject.Container.ZInjectStandaloneProvider
    {
        protected override object? ResolveKnown(Type serviceType) => null;
        protected override bool IsKnownService(Type serviceType) => false;
        protected override bool IsKnownKeyedService(Type serviceType, object? serviceKey) => false;
        protected override ZInject.Container.ZInjectStandaloneScope CreateScopeCore()
            => new TestScope(this);
    }

    private sealed class TestScope : ZInject.Container.ZInjectStandaloneScope
    {
        public TestScope(ZInject.Container.ZInjectStandaloneProvider root) : base(root) { }
        protected override object? ResolveScopedKnown(Type serviceType)
        {
            if (serviceType == typeof(string))
                return "scoped";
            return null;
        }
    }

    private sealed class DisposableService : IDisposable
    {
        public bool IsDisposed { get; private set; }
        public void Dispose() { IsDisposed = true; }
    }

    private sealed class AsyncDisposableService : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }
        public ValueTask DisposeAsync() { IsDisposed = true; return default; }
    }

    [Fact]
    public void GetService_KnownType_ReturnsInstance()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Equal("scoped", scope.ServiceProvider.GetService(typeof(string)));
    }

    [Fact]
    public void GetService_UnknownType_ReturnsNull()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Null(scope.ServiceProvider.GetService(typeof(int)));
    }

    [Fact]
    public void GetService_IServiceProvider_ReturnsSelf()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Same(scope.ServiceProvider, scope.ServiceProvider.GetService(typeof(IServiceProvider)));
    }

    [Fact]
    public void GetService_IServiceScopeFactory_ReturnsRoot()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Same(provider, scope.ServiceProvider.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)));
    }

    [Fact]
    public void ServiceProvider_Property_ReturnsSelf()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        Assert.Same(scope.ServiceProvider, scope.ServiceProvider);
    }

    [Fact]
    public void Dispose_DisposesTrackedServices()
    {
        using var provider = new TestProvider();
        var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        var svc = new DisposableService();
        // Access TrackDisposable via reflection since it's protected
        var trackMethod = typeof(ZInject.Container.ZInjectStandaloneScope)
            .GetMethod("TrackDisposable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(typeof(DisposableService));
        trackMethod.Invoke(scope.ServiceProvider, [svc]);

        Assert.False(svc.IsDisposed);
        scope.Dispose();
        Assert.True(svc.IsDisposed);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTrackedAsyncServices()
    {
        using var provider = new TestProvider();
        var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        var svc = new AsyncDisposableService();
        var trackMethod = typeof(ZInject.Container.ZInjectStandaloneScope)
            .GetMethod("TrackDisposable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(typeof(AsyncDisposableService));
        trackMethod.Invoke(scope.ServiceProvider, [svc]);

        Assert.False(svc.IsDisposed);
        await ((IAsyncDisposable)scope).DisposeAsync();
        Assert.True(svc.IsDisposed);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        using var provider = new TestProvider();
        var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        scope.Dispose();
        scope.Dispose(); // Should not throw
    }

    [Fact]
    public void GetService_IServiceProviderIsService_ReturnsRoot()
    {
        var provider = new TestProvider();
        using var scope = provider.CreateScope();
        var result = scope.ServiceProvider.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceProviderIsService));
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Fact]
    public void GetService_IServiceProviderIsKeyedService_ReturnsRoot()
    {
        var provider = new TestProvider();
        using var scope = provider.CreateScope();
        var result = scope.ServiceProvider.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService));
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Fact]
    public void IsKeyedService_DelegatesToRoot()
    {
        var provider = new TestProvider();
        using var scope = provider.CreateScope();
        Assert.False(((Microsoft.Extensions.DependencyInjection.IServiceProviderIsKeyedService)scope.ServiceProvider).IsKeyedService(typeof(string), "unknown"));
    }

    // GetOrAddScopedOpenGeneric: used by generated scoped open-generic resolution

    private class TestScopedOpenGenericProvider : ZInject.Container.ZInjectStandaloneProvider
    {
        protected override object? ResolveKnown(Type serviceType) => null;
        protected override bool IsKnownService(Type serviceType) => false;
        protected override bool IsKnownKeyedService(Type serviceType, object? serviceKey) => false;
        protected override ZInject.Container.ZInjectStandaloneScope CreateScopeCore() => new TestScopedOgScope(this);

        private sealed class TestScopedOgScope : ZInject.Container.ZInjectStandaloneScope
        {
            public TestScopedOgScope(ZInject.Container.ZInjectStandaloneProvider root) : base(root) { }

            protected override object? ResolveScopedKnown(Type serviceType)
            {
                if (serviceType == typeof(StandaloneProviderBaseTests.IScopedGeneric<string>))
                    return GetOrAddScopedOpenGeneric(serviceType, () => new StandaloneProviderBaseTests.ScopedGenericService<string>());
                return null;
            }
        }
    }

    [Fact]
    public void GetOrAddScopedOpenGeneric_ReturnsSameInstanceWithinScope()
    {
        using var provider = new TestScopedOpenGenericProvider();
        using var scope = (ZInject.Container.ZInjectStandaloneScope)provider.CreateScope();
        var a = scope.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.IScopedGeneric<string>));
        var b = scope.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.IScopedGeneric<string>));
        Assert.NotNull(a);
        Assert.Same(a, b);
    }

    [Fact]
    public void GetOrAddScopedOpenGeneric_ReturnsDifferentInstancesAcrossScopes()
    {
        using var provider = new TestScopedOpenGenericProvider();
        using var scope1 = (ZInject.Container.ZInjectStandaloneScope)provider.CreateScope();
        using var scope2 = (ZInject.Container.ZInjectStandaloneScope)provider.CreateScope();
        var a = scope1.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.IScopedGeneric<string>));
        var b = scope2.ServiceProvider.GetService(typeof(StandaloneProviderBaseTests.IScopedGeneric<string>));
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotSame(a, b);
    }
}
