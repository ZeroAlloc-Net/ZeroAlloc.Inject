using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// The closed forms of open generics that the generator finds in constructors, registered for
/// Microsoft DI and resolved by the hybrid container. Under NativeAOT Microsoft DI refuses to
/// close an open generic over a value type, see ZeroAlloc-Net/ZeroAlloc.Inject#173.
/// </summary>
public class ClosedGenericRegistrationTests
{
    private const string Repo = """
        using System;
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IRepo<T> { }
        [LIFETIME]
        public class Repo<T> : IRepo<T> { }
        [Transient]
        public class Consumer
        {
            public Consumer(IRepo<int> numbers, IRepo<string> names) { Numbers = numbers; }
            public IRepo<int> Numbers { get; }
        }
        """;

    private static string RepoWith(string lifetime) => Repo.Replace("LIFETIME", lifetime, StringComparison.Ordinal);

    private static T Only<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        Assert.True(list.Count == 1, $"Expected exactly one item, got {list.Count}.");
        return list[0];
    }

    private static List<ServiceDescriptor> DescriptorsFor(IServiceCollection services, Type serviceType) =>
        services.Where(d => d.ServiceType == serviceType).ToList();

    // ---------------------------------------------------------------
    // Microsoft DI: the generated Add...Services extension
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("Transient")]
    [InlineData("Scoped")]
    [InlineData("Singleton")]
    public void MicrosoftDi_WithDynamicCode_RegistrationsAndCountsAreUnchanged(string lifetime)
    {
        var app = GeneratedApp.Compile(RepoWith(lifetime));
        var services = app.AddServices(new ServiceCollection());

        // Only the open registrations: no closed IRepo<int> descriptor under the JIT.
        Assert.Empty(DescriptorsFor(services, app.Type("IRepo", typeof(int))));
        Only(DescriptorsFor(services, app.Type("IRepo`1")));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var repoOfInt = app.Type("IRepo", typeof(int));
        Assert.IsType(app.Type("Repo", typeof(int)), scope.ServiceProvider.GetRequiredService(repoOfInt));
        Only(scope.ServiceProvider.GetServices(repoOfInt));
        Only(scope.ServiceProvider.GetServices(app.Type("IRepo", typeof(string))));
    }

    [Theory]
    [InlineData("Transient", ServiceLifetime.Transient)]
    [InlineData("Scoped", ServiceLifetime.Scoped)]
    [InlineData("Singleton", ServiceLifetime.Singleton)]
    public void MicrosoftDi_WithoutDynamicCode_RegistersEachValueTypeClosedFormOnce(string lifetime, ServiceLifetime expected)
    {
        var app = GeneratedApp.Compile(RepoWith(lifetime), withoutDynamicCode: true);
        var services = app.AddServices(new ServiceCollection());

        var closed = Only(DescriptorsFor(services, app.Type("IRepo", typeof(int))));
        Assert.Equal(app.Type("Repo", typeof(int)), closed.ImplementationType);
        Assert.Equal(expected, closed.Lifetime);
        Assert.False(closed.IsKeyedService);

        // A reference-type argument closes fine under NativeAOT, so it keeps only the open registration.
        Assert.Empty(DescriptorsFor(services, app.Type("IRepo", typeof(string))));
        Only(DescriptorsFor(services, app.Type("IRepo`1")));
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Scoped")]
    [InlineData("Singleton")]
    public void MicrosoftDi_WithoutDynamicCode_ResolvesWhatTheOpenRegistrationResolves(string lifetime)
    {
        var withoutDynamicCode = GeneratedApp.Compile(RepoWith(lifetime), withoutDynamicCode: true);
        var withDynamicCode = GeneratedApp.Compile(RepoWith(lifetime));

        foreach (var app in new[] { withDynamicCode, withoutDynamicCode })
        {
            using var provider = app.BuildMicrosoftDi();
            using var scope = provider.CreateScope();
            var repoOfInt = app.Type("IRepo", typeof(int));
            var first = scope.ServiceProvider.GetRequiredService(repoOfInt);
            var second = scope.ServiceProvider.GetRequiredService(repoOfInt);
            var consumer = scope.ServiceProvider.GetRequiredService(app.Type("Consumer"));
            var viaConsumer = app.Type("Consumer").GetProperty("Numbers")!.GetValue(consumer);

            Assert.IsType(app.Type("Repo", typeof(int)), first);
            Assert.Equal(!string.Equals(lifetime, "Transient", StringComparison.Ordinal), ReferenceEquals(first, second));
            Assert.Equal(!string.Equals(lifetime, "Transient", StringComparison.Ordinal), ReferenceEquals(first, viaConsumer));
        }
    }

    [Fact]
    public void MicrosoftDi_WithoutDynamicCode_AllowMultiple_ClosedFormIsTheLastRegistration()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Transient(AllowMultiple = true)]
            public class Repo<T> : IRepo<T> { }
            [Transient(AllowMultiple = true)]
            public class OtherRepo<T> : IRepo<T> { }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = GeneratedApp.Compile(source, withoutDynamicCode: true);
        var services = app.AddServices(new ServiceCollection());

        // Microsoft DI resolves the last of the two open registrations; the closed one has to agree.
        var closed = Only(DescriptorsFor(services, app.Type("IRepo", typeof(int))));
        Assert.Equal(app.Type("OtherRepo", typeof(int)), closed.ImplementationType);
        using var provider = services.BuildServiceProvider();
        Assert.IsType(app.Type("OtherRepo", typeof(int)), provider.GetRequiredService(app.Type("IRepo", typeof(int))));
    }

    [Fact]
    public void MicrosoftDi_WithoutDynamicCode_TryAdd_ClosedFormIsTheFirstRegistration()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Transient]
            public class Repo<T> : IRepo<T> { }
            [Transient]
            public class OtherRepo<T> : IRepo<T> { }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = GeneratedApp.Compile(source, withoutDynamicCode: true);
        var services = app.AddServices(new ServiceCollection());

        // TryAdd keeps only the first open registration, so the closed one follows it.
        var closed = Only(DescriptorsFor(services, app.Type("IRepo", typeof(int))));
        Assert.Equal(app.Type("Repo", typeof(int)), closed.ImplementationType);
    }

    [Fact]
    public void MicrosoftDi_WithoutDynamicCode_AnEarlierClosedRegistrationStillWins()
    {
        var app = GeneratedApp.Compile(RepoWith("Transient"), withoutDynamicCode: true);
        var repoOfInt = app.Type("IRepo", typeof(int));
        var custom = new object();

        // Under the JIT an application's own closed registration beats the open one; the generated
        // closed registration must not take that over.
        using var provider = app.BuildMicrosoftDi(services => services.AddSingleton(repoOfInt, _ => custom));

        Assert.Same(custom, provider.GetRequiredService(repoOfInt));
    }

    // ---------------------------------------------------------------
    // Hybrid container
    // ---------------------------------------------------------------

    [Theory]
    [InlineData("Transient")]
    [InlineData("Singleton")]
    public void Hybrid_ResolvesValueTypeClosedForm_FromTheGeneratedTypeSwitch(string lifetime)
    {
        var app = GeneratedApp.Compile(RepoWith(lifetime));

        // No generated registrations in the fallback: only the type switch can resolve IRepo<int>.
        var provider = app.BuildHybrid(addServices: false);

        Assert.IsType(app.Type("Repo", typeof(int)), provider.GetService(app.Type("IRepo", typeof(int))));
        Assert.IsType(app.Type("Repo", typeof(string)), provider.GetService(app.Type("IRepo", typeof(string))));
        Assert.True(((IServiceProviderIsService)provider).IsService(app.Type("IRepo", typeof(int))));
    }

    [Fact]
    public void Hybrid_ResolvesScopedValueTypeClosedForm_FromTheGeneratedTypeSwitch()
    {
        var app = GeneratedApp.Compile(RepoWith("Scoped"));
        var provider = app.BuildHybrid(addServices: false);
        var repoOfInt = app.Type("IRepo", typeof(int));

        object first, second, other;
        using (var scope = provider.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService(repoOfInt);
            second = scope.ServiceProvider.GetRequiredService(repoOfInt);
        }
        using (var scope = provider.CreateScope())
        {
            other = scope.ServiceProvider.GetRequiredService(repoOfInt);
        }

        Assert.IsType(app.Type("Repo", typeof(int)), first);
        Assert.Same(first, second);
        Assert.NotSame(first, other);
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Scoped")]
    [InlineData("Singleton")]
    public void Hybrid_GetServices_CountsMatchMicrosoftDi(string lifetime)
    {
        var app = GeneratedApp.Compile(RepoWith(lifetime));
        using var microsoftDi = app.BuildMicrosoftDi();
        var hybrid = app.BuildHybrid();

        using var microsoftDiScope = microsoftDi.CreateScope();
        using var hybridScope = hybrid.CreateScope();
        var repoOfInt = app.Type("IRepo", typeof(int));
        var repoOfString = app.Type("IRepo", typeof(string));
        var expected = new[]
        {
            microsoftDiScope.ServiceProvider.GetServices(repoOfInt).ToList().Count,
            microsoftDiScope.ServiceProvider.GetServices(repoOfString).ToList().Count,
        };
        var actual = new[]
        {
            hybridScope.ServiceProvider.GetServices(repoOfInt).ToList().Count,
            hybridScope.ServiceProvider.GetServices(repoOfString).ToList().Count,
        };
        Assert.Equal(expected, actual);

        if (!string.Equals(lifetime, "Scoped", StringComparison.Ordinal))
        {
            var serviceType = app.Type("IRepo", typeof(int));
            Assert.Equal(microsoftDi.GetServices(serviceType).Count(), hybrid.GetServices(serviceType).Count());
        }
    }

    [Fact]
    public void Hybrid_AllowMultiple_ResolvesTheLastRegistration_AndEnumeratesBoth()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Singleton(AllowMultiple = true)]
            public class Repo<T> : IRepo<T> { }
            [Transient(AllowMultiple = true)]
            public class OtherRepo<T> : IRepo<T> { }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = GeneratedApp.Compile(source);
        var hybrid = app.BuildHybrid(addServices: false);
        var repoOfInt = app.Type("IRepo", typeof(int));

        // Transients are registered before singletons, so the singleton Repo<T> is the last one.
        Assert.IsType(app.Type("Repo", typeof(int)), hybrid.GetRequiredService(repoOfInt));
        var all = hybrid.GetServices(repoOfInt).ToList();
        Assert.Equal(2, all.Count);
        Assert.IsType(app.Type("OtherRepo", typeof(int)), all[0]);
        Assert.IsType(app.Type("Repo", typeof(int)), all[1]);

        using var microsoftDi = app.BuildMicrosoftDi();
        Assert.Equal(
            microsoftDi.GetServices(repoOfInt).Select(s => s!.GetType()),
            all.Select(s => s!.GetType()));
    }

    [Fact]
    public void Hybrid_Singleton_OneInstanceAcrossGetServiceGetServicesConsumersAndScopes()
    {
        var app = GeneratedApp.Compile(RepoWith("Singleton"));
        var hybrid = app.BuildHybrid();
        var repoOfInt = app.Type("IRepo", typeof(int));

        var root = hybrid.GetRequiredService(repoOfInt);
        var consumer = hybrid.GetRequiredService(app.Type("Consumer"));
        using var scope = hybrid.CreateScope();

        Assert.Same(root, app.Type("Consumer").GetProperty("Numbers")!.GetValue(consumer));
        Assert.Same(root, Only(hybrid.GetServices(repoOfInt)));
        Assert.Same(root, scope.ServiceProvider.GetRequiredService(repoOfInt));
        Assert.Same(root, Only(scope.ServiceProvider.GetServices(repoOfInt)));
    }

    [Fact]
    public void Hybrid_DisposesTheValueTypeClosedForms_ItOwns()
    {
        const string source = """
            using System;
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { bool IsDisposed { get; } }
            [Singleton]
            public class Repo<T> : IRepo<T>, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() => IsDisposed = true;
            }
            public interface IUnit<T> { bool IsDisposed { get; } }
            [Scoped]
            public class Unit<T> : IUnit<T>, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() => IsDisposed = true;
            }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo, IUnit<int> unit) { } }
            """;

        var app = GeneratedApp.Compile(source);
        var hybrid = app.BuildHybrid();

        object unit;
        using (var scope = hybrid.CreateScope())
        {
            unit = scope.ServiceProvider.GetRequiredService(app.Type("IUnit", typeof(int)));
        }
        var repo = hybrid.GetRequiredService(app.Type("IRepo", typeof(int)));
        ((IDisposable)hybrid).Dispose();

        Assert.True((bool)unit.GetType().GetProperty("IsDisposed")!.GetValue(unit)!);
        Assert.True((bool)repo.GetType().GetProperty("IsDisposed")!.GetValue(repo)!);
    }

    [Fact]
    public async Task Hybrid_DisposesTheSingletonsItOwns_OnDisposeAsync()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface ICache { bool IsDisposed { get; } }
            [Singleton]
            public class Cache : ICache, IAsyncDisposable
            {
                public bool IsDisposed { get; private set; }
                public ValueTask DisposeAsync() { IsDisposed = true; return default; }
            }
            public interface IRepo<T> { bool IsDisposed { get; } }
            [Singleton]
            public class Repo<T> : IRepo<T>, IAsyncDisposable
            {
                public bool IsDisposed { get; private set; }
                public ValueTask DisposeAsync() { IsDisposed = true; return default; }
            }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = GeneratedApp.Compile(source);
        var hybrid = app.BuildHybrid();
        var cache = hybrid.GetRequiredService(app.Type("ICache"));
        var repo = hybrid.GetRequiredService(app.Type("IRepo", typeof(int)));

        await ((IAsyncDisposable)hybrid).DisposeAsync();

        Assert.True((bool)cache.GetType().GetProperty("IsDisposed")!.GetValue(cache)!);
        Assert.True((bool)repo.GetType().GetProperty("IsDisposed")!.GetValue(repo)!);
    }

    [Fact]
    public void Hybrid_DisposesTheNonGenericSingletonsItOwns()
    {
        const string source = """
            using System;
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface ICache { bool IsDisposed { get; } }
            [Singleton]
            public class Cache : ICache, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() => IsDisposed = true;
            }
            [Singleton(Key = "fast")]
            public class FastCache : ICache, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() => IsDisposed = true;
            }
            """;

        var app = GeneratedApp.Compile(source);
        var hybrid = app.BuildHybrid();
        var cache = hybrid.GetRequiredService(app.Type("ICache"));
        var fast = ((IKeyedServiceProvider)hybrid).GetRequiredKeyedService(app.Type("ICache"), "fast");

        ((IDisposable)hybrid).Dispose();

        Assert.True((bool)cache.GetType().GetProperty("IsDisposed")!.GetValue(cache)!);
        Assert.True((bool)fast.GetType().GetProperty("IsDisposed")!.GetValue(fast)!);
    }

    [Fact]
    public void Hybrid_OpenGenericDecorator_ResolvesWhatMicrosoftDiResolves()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Transient]
            public class Repo<T> : IRepo<T> { }
            [Decorator]
            public class LoggingRepo<T> : IRepo<T> { public LoggingRepo(IRepo<T> inner) { } }
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            [Decorator]
            public class LoggingFoo : IFoo { public LoggingFoo(IFoo inner) { } }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo, IFoo foo) { } }
            """;

        var app = GeneratedApp.Compile(source);
        using var microsoftDi = app.BuildMicrosoftDi();
        var hybrid = app.BuildHybrid();

        foreach (var serviceType in new[] { app.Type("IRepo", typeof(int)), app.Type("IFoo") })
        {
            Assert.Equal(
                microsoftDi.GetRequiredService(serviceType).GetType(),
                hybrid.GetRequiredService(serviceType).GetType());
        }
        Assert.IsType(app.Type("LoggingFoo"), hybrid.GetRequiredService(app.Type("IFoo")));
        Assert.IsType(app.Type("LoggingRepo", typeof(int)), hybrid.GetRequiredService(app.Type("IRepo", typeof(int))));
    }

    // ---------------------------------------------------------------
    // Standalone container
    // ---------------------------------------------------------------

    [Fact]
    public void Standalone_AllowMultiple_ResolvesTheLastRegistration()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Transient(AllowMultiple = true)]
            public class Repo<T> : IRepo<T> { }
            [Transient(AllowMultiple = true)]
            public class OtherRepo<T> : IRepo<T> { }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = GeneratedApp.Compile(source);
        using var microsoftDi = app.BuildMicrosoftDi();
        var repoOfInt = app.Type("IRepo", typeof(int));

        Assert.IsType(app.Type("OtherRepo", typeof(int)), microsoftDi.GetRequiredService(repoOfInt));
        Assert.IsType(app.Type("OtherRepo", typeof(int)), app.BuildStandalone().GetRequiredService(repoOfInt));
    }

    [Fact]
    public void Standalone_IsService_KnowsTheClosedForms()
    {
        var app = GeneratedApp.Compile(RepoWith("Transient"));
        var standalone = (IServiceProviderIsService)app.BuildStandalone();

        Assert.True(standalone.IsService(app.Type("IRepo", typeof(int))));
        Assert.True(standalone.IsService(app.Type("IRepo", typeof(string))));
        Assert.False(standalone.IsService(app.Type("IRepo", typeof(long))));
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Scoped")]
    [InlineData("Singleton")]
    public void Standalone_GetServices_CountsMatchMicrosoftDi(string lifetime)
    {
        var app = GeneratedApp.Compile(RepoWith(lifetime));
        using var microsoftDi = app.BuildMicrosoftDi();
        var standalone = app.BuildStandalone();

        using var microsoftDiScope = microsoftDi.CreateScope();
        using var standaloneScope = standalone.CreateScope();
        AssertSameServices(app.Type("IRepo", typeof(int)));
        AssertSameServices(app.Type("IRepo", typeof(string)));

        if (!string.Equals(lifetime, "Scoped", StringComparison.Ordinal))
        {
            var serviceType = app.Type("IRepo", typeof(int));
            Assert.Equal(microsoftDi.GetServices(serviceType).Count(), standalone.GetServices(serviceType).Count());
        }

        void AssertSameServices(Type serviceType)
        {
            var expected = microsoftDiScope.ServiceProvider.GetServices(serviceType).ToList();
            var actual = standaloneScope.ServiceProvider.GetServices(serviceType).ToList();
            Assert.Equal(expected.Select(s => s!.GetType()), actual.Select(s => s!.GetType()));
            if (!string.Equals(lifetime, "Transient", StringComparison.Ordinal))
            {
                Assert.Same(standaloneScope.ServiceProvider.GetRequiredService(serviceType), Only(actual));
            }
        }
    }

    [Fact]
    public void Standalone_AllowMultiple_EnumeratesEveryRegistrationInOrder()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Singleton(AllowMultiple = true)]
            public class Repo<T> : IRepo<T> { }
            [Transient(AllowMultiple = true)]
            public class OtherRepo<T> : IRepo<T> { }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = GeneratedApp.Compile(source);
        var standalone = app.BuildStandalone();
        var repoOfInt = app.Type("IRepo", typeof(int));

        var all = standalone.GetServices(repoOfInt).ToList();
        using var microsoftDi = app.BuildMicrosoftDi();
        Assert.Equal(
            microsoftDi.GetServices(repoOfInt).Select(s => s!.GetType()),
            all.Select(s => s!.GetType()));
        Assert.Same(standalone.GetRequiredService(repoOfInt), all[1]);

        using var scope = standalone.CreateScope();
        var inScope = scope.ServiceProvider.GetServices(repoOfInt).ToList();
        Assert.Equal(2, inScope.Count);
        Assert.Same(all[1], inScope[1]);
    }

    [Fact]
    public void Standalone_Singleton_OneInstanceAcrossGetServiceGetServicesConsumersAndScopes()
    {
        var app = GeneratedApp.Compile(RepoWith("Singleton"));
        var standalone = app.BuildStandalone();
        var repoOfInt = app.Type("IRepo", typeof(int));

        var root = standalone.GetRequiredService(repoOfInt);
        var consumer = standalone.GetRequiredService(app.Type("Consumer"));
        using var scope = standalone.CreateScope();

        Assert.Same(root, app.Type("Consumer").GetProperty("Numbers")!.GetValue(consumer));
        Assert.Same(root, Only(standalone.GetServices(repoOfInt)));
        Assert.Same(root, scope.ServiceProvider.GetRequiredService(repoOfInt));
        Assert.Same(root, Only(scope.ServiceProvider.GetServices(repoOfInt)));
    }

    // ---------------------------------------------------------------
    // A constructor parameter of the concrete closed type
    // ---------------------------------------------------------------

    private const string ConcreteRegistry = """
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IRegistry<T> { }
        [LIFETIME]
        public class Registry<T> : IRegistry<T> { }
        [Transient]
        public class Consumer
        {
            public Consumer(Registry<int> numbers, Registry<string> names) { Numbers = numbers; }
            public Registry<int> Numbers { get; }
        }
        """;

    [Theory]
    [InlineData("Transient", ServiceLifetime.Transient)]
    [InlineData("Scoped", ServiceLifetime.Scoped)]
    [InlineData("Singleton", ServiceLifetime.Singleton)]
    public void ConcreteClosedUsage_MicrosoftDi_WithoutDynamicCode_RegistersTheConcreteClosedForm(string lifetime, ServiceLifetime expected)
    {
        var app = GeneratedApp.Compile(ConcreteRegistry.Replace("LIFETIME", lifetime, StringComparison.Ordinal), withoutDynamicCode: true);
        var services = app.AddServices(new ServiceCollection());

        var closed = Only(DescriptorsFor(services, app.Type("Registry", typeof(int))));
        Assert.Equal(app.Type("Registry", typeof(int)), closed.ImplementationType);
        Assert.Equal(expected, closed.Lifetime);

        // Nothing asks for the interface closed over int, and a reference type closes fine.
        Assert.Empty(DescriptorsFor(services, app.Type("IRegistry", typeof(int))));
        Assert.Empty(DescriptorsFor(services, app.Type("Registry", typeof(string))));
    }

    [Theory]
    [InlineData("Transient", "HybridWithoutFallback")]
    [InlineData("Scoped", "HybridWithoutFallback")]
    [InlineData("Singleton", "HybridWithoutFallback")]
    [InlineData("Transient", "Standalone")]
    [InlineData("Scoped", "Standalone")]
    [InlineData("Singleton", "Standalone")]
    public void ConcreteClosedUsage_Containers_ResolveTheConcreteClosedForm(string lifetime, string mode)
    {
        var app = GeneratedApp.Compile(ConcreteRegistry.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = string.Equals(mode, "Standalone", StringComparison.Ordinal) ? app.BuildStandalone() : app.BuildHybrid(addServices: false);
        var registryOfInt = app.Type("Registry", typeof(int));

        using var scope = provider.CreateScope();
        var direct = scope.ServiceProvider.GetRequiredService(registryOfInt);
        var consumer = scope.ServiceProvider.GetRequiredService(app.Type("Consumer"));

        Assert.IsType(registryOfInt, direct);
        Assert.IsType(app.Type("Registry", typeof(string)), scope.ServiceProvider.GetRequiredService(app.Type("Registry", typeof(string))));
        Assert.Equal(
            !string.Equals(lifetime, "Transient", StringComparison.Ordinal),
            ReferenceEquals(direct, app.Type("Consumer").GetProperty("Numbers")!.GetValue(consumer)));
        Assert.True(((IServiceProviderIsService)provider).IsService(registryOfInt));
        Only(scope.ServiceProvider.GetServices(registryOfInt));
        (provider as IDisposable)?.Dispose();
    }

    [Fact]
    public void ConcreteClosedUsage_WithAs_IsNotAServiceInAnyMode()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRegistry<T> { }
            [Singleton(As = typeof(IRegistry<>))]
            public class Registry<T> : IRegistry<T> { }
            [Transient]
            public class Consumer { public Consumer(Registry<int> numbers) { } }
            """;

        var app = GeneratedApp.Compile(source, withoutDynamicCode: true);
        var registryOfInt = app.Type("Registry", typeof(int));

        Assert.Empty(DescriptorsFor(app.AddServices(new ServiceCollection()), registryOfInt));
        Assert.False(((IServiceProviderIsService)app.BuildStandalone()).IsService(registryOfInt));
        Assert.Null(app.BuildHybrid(addServices: false).GetService(registryOfInt));
    }
}
