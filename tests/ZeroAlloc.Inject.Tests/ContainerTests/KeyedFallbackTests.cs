using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// Keyed resolution in the generated containers: the hybrid container falls back to Microsoft DI
/// for a keyed service it does not know, as it does for an unkeyed one, and both containers
/// resolve keyed services whether or not the generator knows any. See
/// ZeroAlloc-Net/ZeroAlloc.Inject#181.
/// </summary>
public class KeyedFallbackTests
{
    private const string WithKeyedService = """
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IFoo { }
        [Transient(Key = "generated")]
        public class GeneratedFoo : IFoo { }
        [Singleton]
        public class Plain : IFoo { }
        public class ManualFoo : IFoo { }
        """;

    private const string WithoutKeyedService = """
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IFoo { }
        [Singleton]
        public class Plain : IFoo { }
        public class ManualFoo : IFoo { }
        """;

    public static TheoryData<string> Sources() => new() { WithKeyedService, WithoutKeyedService };

    private static IServiceProvider BuildHybrid(GeneratedApp app, ServiceLifetime lifetime) =>
        app.BuildHybrid(before: services => services.Add(ServiceDescriptor.DescribeKeyed(
            app.Type("IFoo"), "manual", app.Type("ManualFoo"), lifetime)));

    [Theory]
    [MemberData(nameof(Sources))]
    public void Hybrid_FallsBackToMicrosoftDi_ForAKeyedServiceItDoesNotKnow(string source)
    {
        var app = GeneratedApp.Compile(source);
        var provider = BuildHybrid(app, ServiceLifetime.Transient);
        using var scope = provider.CreateScope();

        foreach (var sp in new[] { provider, scope.ServiceProvider })
        {
            Assert.IsType(app.Type("ManualFoo"), sp.GetKeyedService(app.Type("IFoo"), "manual"));
            Assert.IsType(app.Type("ManualFoo"), sp.GetRequiredKeyedService(app.Type("IFoo"), "manual"));
            Assert.Null(sp.GetKeyedService(app.Type("IFoo"), "unknown"));
            Assert.Throws<InvalidOperationException>(() => sp.GetRequiredKeyedService(app.Type("IFoo"), "unknown"));
            Assert.True(sp.GetRequiredService<IServiceProviderIsKeyedService>().IsKeyedService(app.Type("IFoo"), "manual"));
        }
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Scoped)]
    public void Hybrid_KeyedFallback_KeepsTheLifetimeOfTheRegistration(ServiceLifetime lifetime)
    {
        var app = GeneratedApp.Compile(WithKeyedService);
        var provider = BuildHybrid(app, lifetime);
        var serviceType = app.Type("IFoo");

        object first, other;
        using (var scope = provider.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredKeyedService(serviceType, "manual");
            Assert.Same(first, scope.ServiceProvider.GetRequiredKeyedService(serviceType, "manual"));
        }
        using (var scope = provider.CreateScope())
        {
            other = scope.ServiceProvider.GetRequiredKeyedService(serviceType, "manual");
        }

        Assert.Equal(lifetime == ServiceLifetime.Singleton, ReferenceEquals(first, other));
        if (lifetime == ServiceLifetime.Singleton)
        {
            Assert.Same(first, provider.GetRequiredKeyedService(serviceType, "manual"));
        }
        (provider as IDisposable)?.Dispose();
    }

    [Fact]
    public void Hybrid_ResolvesAKeyedServiceItKnows_ItselfFirst()
    {
        var app = GeneratedApp.Compile(WithKeyedService);
        var provider = app.BuildHybrid(before: services => services.AddKeyedTransient(
            app.Type("IFoo"), "generated", app.Type("ManualFoo")));
        using var scope = provider.CreateScope();

        Assert.IsType(app.Type("GeneratedFoo"), provider.GetRequiredKeyedService(app.Type("IFoo"), "generated"));
        Assert.IsType(app.Type("GeneratedFoo"), scope.ServiceProvider.GetRequiredKeyedService(app.Type("IFoo"), "generated"));
        (provider as IDisposable)?.Dispose();
    }

    public static TheoryData<string, string> SourcesAndContainers()
    {
        var data = new TheoryData<string, string>();
        foreach (var source in new[] { WithKeyedService, WithoutKeyedService })
        {
            data.Add(source, "Hybrid");
            data.Add(source, "Standalone");
        }
        return data;
    }

    private static IServiceProvider Build(GeneratedApp app, string container) =>
        string.Equals(container, "Hybrid", StringComparison.Ordinal) ? app.BuildHybrid() : app.BuildStandalone();

    [Theory]
    [MemberData(nameof(SourcesAndContainers))]
    public void Containers_AreKeyedServiceProviders_WhetherOrNotTheyKnowAKeyedService(string source, string container)
    {
        var app = GeneratedApp.Compile(source);
        var provider = Build(app, container);
        using var scope = provider.CreateScope();

        foreach (var sp in new[] { provider, scope.ServiceProvider })
        {
            Assert.Same(sp, sp.GetService(typeof(IKeyedServiceProvider)));
            Assert.True(sp.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IKeyedServiceProvider)));
            Assert.True(sp.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IServiceProviderIsKeyedService)));

            // As in Microsoft DI, an unknown key resolves null, and only the required form throws.
            Assert.Null(sp.GetKeyedService(app.Type("IFoo"), "unknown"));
            Assert.Throws<InvalidOperationException>(() => sp.GetRequiredKeyedService(app.Type("IFoo"), "unknown"));
        }
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(SourcesAndContainers))]
    public void Containers_ResolveANullKey_AsTheUnkeyedService(string source, string container)
    {
        var app = GeneratedApp.Compile(source);
        var provider = Build(app, container);
        using var scope = provider.CreateScope();

        var plain = provider.GetRequiredService(app.Type("Plain"));
        foreach (var sp in new[] { provider, scope.ServiceProvider })
        {
            var keyed = (IKeyedServiceProvider)sp.GetRequiredService(typeof(IKeyedServiceProvider));
            Assert.Same(plain, keyed.GetKeyedService(app.Type("Plain"), null));
            Assert.Same(plain, keyed.GetRequiredKeyedService(app.Type("Plain"), null));
        }
        (provider as IDisposable)?.Dispose();
    }

    [Fact]
    public void MicrosoftDi_ResolvesANullKey_AsTheUnkeyedService()
    {
        // The reference behaviour the containers follow.
        var app = GeneratedApp.Compile(WithoutKeyedService);
        using var provider = app.BuildMicrosoftDi();
        var keyed = (IKeyedServiceProvider)provider;

        Assert.Same(provider.GetRequiredService(app.Type("Plain")), keyed.GetKeyedService(app.Type("Plain"), null));
        Assert.Null(keyed.GetKeyedService(app.Type("IFoo"), "unknown"));
    }
}
