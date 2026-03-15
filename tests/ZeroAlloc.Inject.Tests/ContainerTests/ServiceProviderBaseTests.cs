using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject.Container;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

public class ServiceProviderBaseTests
{
    private interface ITestService { }

    private sealed class TestService : ITestService { }

    private interface ITestCache { }

    private sealed class TestCache : ITestCache { }

    private interface IFallbackOnly { }

    private sealed class FallbackOnlyService : IFallbackOnly { }

    private sealed class TestScope : ZeroAllocInjectScope
    {
        public TestScope(ZeroAllocInjectServiceProviderBase root, IServiceScope fallbackScope)
            : base(root, fallbackScope) { }

        protected override object? ResolveScopedKnown(Type serviceType) => null;
    }


    private sealed class TestProvider : ZeroAllocInjectServiceProviderBase
    {
        private TestCache? _singleton;

        public TestProvider(IServiceProvider fallback) : base(fallback) { }

        protected override object? ResolveKnown(Type serviceType)
        {
            if (serviceType == typeof(ITestService))
            {
                return new TestService();
            }

            if (serviceType == typeof(ITestCache))
            {
                var instance = new TestCache();
                return Interlocked.CompareExchange(ref _singleton, instance, null) ?? _singleton;
            }

            return null;
        }

        protected override bool IsKnownService(Type serviceType) => false;

        protected override bool IsKnownKeyedService(Type serviceType, object? serviceKey) => false;

        protected override ZeroAllocInjectScope CreateScopeCore(IServiceScope fallbackScope)
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
    public void Resolving_known_transient_returns_new_instance_each_time()
    {
        using var provider = CreateProvider();

        var first = provider.GetService(typeof(ITestService));
        var second = provider.GetService(typeof(ITestService));

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.IsType<TestService>(first);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Resolving_known_singleton_returns_same_instance()
    {
        using var provider = CreateProvider();

        var first = provider.GetService(typeof(ITestCache));
        var second = provider.GetService(typeof(ITestCache));

        Assert.NotNull(first);
        Assert.IsType<TestCache>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void Resolving_unknown_type_falls_through_to_fallback()
    {
        using var provider = CreateProvider();

        var result = provider.GetService(typeof(IFallbackOnly));

        Assert.NotNull(result);
        Assert.IsType<FallbackOnlyService>(result);
    }

    [Fact]
    public void GetService_IServiceProvider_returns_self()
    {
        using var provider = CreateProvider();

        var result = provider.GetService(typeof(IServiceProvider));

        Assert.Same(provider, result);
    }

    [Fact]
    public void GetService_IServiceScopeFactory_returns_self()
    {
        using var provider = CreateProvider();

        var result = provider.GetService(typeof(IServiceScopeFactory));

        Assert.Same(provider, result);
    }

    [Fact]
    public void CreateScope_returns_a_scope()
    {
        using var provider = CreateProvider();

        using var scope = provider.CreateScope();

        Assert.NotNull(scope);
        Assert.IsAssignableFrom<IServiceScope>(scope);
    }

    [Fact]
    public void Dispose_disposes_fallback_provider()
    {
        var services = new ServiceCollection();
        var fallback = services.BuildServiceProvider();
        var provider = new TestProvider(fallback);

        provider.Dispose();

        // After disposing the fallback, resolving from it should throw
        Assert.Throws<ObjectDisposedException>(() => fallback.GetRequiredService<IServiceProvider>());
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        using var provider = CreateProvider();

        provider.Dispose();
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_disposes_fallback_provider()
    {
        var services = new ServiceCollection();
        var fallback = services.BuildServiceProvider();
        var provider = new TestProvider(fallback);

        await provider.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => fallback.GetRequiredService<IServiceProvider>());
    }

    [Fact]
    public void Constructor_null_fallback_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TestProvider(null!));
    }

    [Fact]
    public void GetService_IServiceProviderIsService_ReturnsSelf()
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        var result = provider.GetService(typeof(IServiceProviderIsService));
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Theory]
    [InlineData(typeof(IServiceProvider))]
    [InlineData(typeof(IServiceScopeFactory))]
    [InlineData(typeof(IServiceProviderIsService))]
    public void IsService_BuiltInTypes_ReturnsTrue(Type serviceType)
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        Assert.True(((IServiceProviderIsService)provider).IsService(serviceType));
    }

    [Fact]
    public void IsService_UnknownType_ReturnsFalse()
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        Assert.False(((IServiceProviderIsService)provider).IsService(typeof(string)));
    }

    [Fact]
    public void GetService_IServiceProviderIsKeyedService_ReturnsSelf()
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        var result = provider.GetService(typeof(IServiceProviderIsKeyedService));
        Assert.NotNull(result);
        Assert.Same(provider, result);
    }

    [Fact]
    public void IsKeyedService_UnknownKeyedService_ReturnsFalse()
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        Assert.False(((IServiceProviderIsKeyedService)provider).IsKeyedService(typeof(string), "unknown"));
    }

    [Fact]
    public void IsService_IServiceProviderIsKeyedService_ReturnsTrue()
    {
        var fallback = new ServiceCollection().BuildServiceProvider();
        var provider = new TestProvider(fallback);
        Assert.True(((IServiceProviderIsService)provider).IsService(typeof(IServiceProviderIsKeyedService)));
    }
}
