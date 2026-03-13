namespace ZeroInject.Tests.ContainerTests;

public class StandaloneProviderBaseTests
{
    // Minimal concrete subclass for testing the abstract base
    private sealed class TestProvider : ZeroInject.Container.ZeroInjectStandaloneProvider
    {
        protected override object? ResolveKnown(Type serviceType)
        {
            if (serviceType == typeof(string))
                return "hello";
            return null;
        }

        protected override ZeroInject.Container.ZeroInjectStandaloneScope CreateScopeCore()
        {
            return new TestScope(this);
        }
    }

    private sealed class TestScope : ZeroInject.Container.ZeroInjectStandaloneScope
    {
        public TestScope(ZeroInject.Container.ZeroInjectStandaloneProvider root) : base(root) { }

        protected override object? ResolveScopedKnown(Type serviceType)
        {
            if (serviceType == typeof(string))
                return "scoped-hello";
            return null;
        }
    }

    [Fact]
    public void GetService_KnownType_ReturnsInstance()
    {
        using var provider = new TestProvider();
        var result = provider.GetService(typeof(string));
        Assert.Equal("hello", result);
    }

    [Fact]
    public void GetService_UnknownType_ReturnsNull()
    {
        using var provider = new TestProvider();
        var result = provider.GetService(typeof(int));
        Assert.Null(result);
    }

    [Fact]
    public void GetService_IServiceProvider_ReturnsSelf()
    {
        using var provider = new TestProvider();
        var result = provider.GetService(typeof(IServiceProvider));
        Assert.Same(provider, result);
    }

    [Fact]
    public void GetService_IServiceScopeFactory_ReturnsSelf()
    {
        using var provider = new TestProvider();
        var result = provider.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory));
        Assert.Same(provider, result);
    }

    [Fact]
    public void CreateScope_ReturnsWorkingScope()
    {
        using var provider = new TestProvider();
        using var scope = ((Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider).CreateScope();
        var result = scope.ServiceProvider.GetService(typeof(string));
        Assert.Equal("scoped-hello", result);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var provider = new TestProvider();
        provider.Dispose();
        provider.Dispose(); // Should not throw
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var provider = new TestProvider();
        await provider.DisposeAsync();
        await provider.DisposeAsync(); // Should not throw
    }

    // Open generic resolution is now fully code-generated (inline delegate factories).
    // Behaviour is covered by the integration tests in IntegrationTests.cs.
    // Expose shared helper types for StandaloneScopeTests.
    public interface IGenericService<T> { }
    public class GenericService<T> : IGenericService<T> { }
    public interface ISingletonGeneric<T> { }
    public class SingletonGenericService<T> : ISingletonGeneric<T> { }
    public interface IScopedGeneric<T> { }
    public class ScopedGenericService<T> : IScopedGeneric<T> { }
}
