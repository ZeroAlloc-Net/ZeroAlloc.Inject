using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// [Decorator] and Key on an open generic service resolve the same way in every mode: the generated
/// Add...Services extension on Microsoft DI, with and without dynamic code, the hybrid container and
/// the standalone container. See ZeroAlloc-Net/ZeroAlloc.Inject#176.
/// </summary>
public class OpenGenericDecoratorAndKeyTests
{
    private const string DecoratedRepo = """
        using System;
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IRepo<T> { }
        [LIFETIME]
        public class Repo<T> : IRepo<T> { }
        [Decorator]
        public class LoggingRepo<T> : IRepo<T>
        {
            public LoggingRepo(IRepo<T> inner) { Inner = inner; }
            public IRepo<T> Inner { get; }
        }
        [Transient]
        public class Consumer
        {
            public Consumer(IRepo<int> numbers, IRepo<string> names) { Numbers = numbers; Names = names; }
            public IRepo<int> Numbers { get; }
            public IRepo<string> Names { get; }
        }
        """;

    private const string KeyedStore = """
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IStore<T> { }
        [LIFETIME(Key = "k")]
        public class Store<T> : IStore<T> { }
        [Transient]
        public class Consumer { public Consumer(IStore<int> numbers, IStore<string> names) { } }
        """;

    public static TheoryData<string, string> LifetimesAndModes()
    {
        var data = new TheoryData<string, string>();
        foreach (var lifetime in new[] { "Transient", "Scoped", "Singleton" })
        {
            foreach (var mode in Modes)
            {
                data.Add(lifetime, mode);
            }
        }
        return data;
    }

    public static TheoryData<string> AllModes()
    {
        var data = new TheoryData<string>();
        foreach (var mode in Modes)
        {
            data.Add(mode);
        }
        return data;
    }

    private static readonly string[] Modes =
    [
        "MicrosoftDi", "MicrosoftDiWithoutDynamicCode", "Hybrid", "HybridWithoutFallback", "Standalone",
    ];

    private static GeneratedApp Compile(string source, string mode) =>
        GeneratedApp.Compile(source, withoutDynamicCode: string.Equals(mode, "MicrosoftDiWithoutDynamicCode", StringComparison.Ordinal));

    private static IServiceProvider Build(GeneratedApp app, string mode) => mode switch
    {
        "MicrosoftDi" or "MicrosoftDiWithoutDynamicCode" => app.BuildMicrosoftDi(),
        "Hybrid" => app.BuildHybrid(),
        "HybridWithoutFallback" => app.BuildHybrid(addServices: false),
        "Standalone" => app.BuildStandalone(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static T Only<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        Assert.True(list.Count == 1, $"Expected exactly one item, got {list.Count}.");
        return list[0];
    }

    private static ServiceDescriptor DescriptorFor(IServiceCollection services, Type serviceType) =>
        Only(services.Where(d => d.ServiceType == serviceType));

    private static object? Property(object instance, string name) => instance.GetType().GetProperty(name)!.GetValue(instance);

    // ---------------------------------------------------------------
    // [Decorator] on an open generic interface
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Decorator_WrapsEveryClosedForm_InEveryMode(string lifetime, string mode)
    {
        var app = Compile(DecoratedRepo.Replace("LIFETIME", lifetime, StringComparison.Ordinal), mode);
        var provider = Build(app, mode);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        AssertDecorated(typeof(int));
        AssertDecorated(typeof(string));

        var consumer = sp.GetRequiredService(app.Type("Consumer"));
        Assert.IsType(app.Type("LoggingRepo", typeof(int)), Property(consumer, "Numbers"));
        Assert.IsType(app.Type("LoggingRepo", typeof(string)), Property(consumer, "Names"));
        (provider as IDisposable)?.Dispose();

        void AssertDecorated(Type argument)
        {
            var serviceType = app.Type("IRepo", argument);
            var resolved = sp.GetRequiredService(serviceType);
            Assert.IsType(app.Type("LoggingRepo", argument), resolved);
            Assert.IsType(app.Type("Repo", argument), Property(resolved, "Inner"));

            var only = Only(sp.GetServices(serviceType));
            Assert.IsType(app.Type("LoggingRepo", argument), only);
            if (!string.Equals(lifetime, "Transient", StringComparison.Ordinal))
            {
                Assert.Same(resolved, only);
            }
        }
    }

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Decorator_Lifetime_IsTheLifetimeOfTheDecoratedService(string lifetime, string mode)
    {
        var app = Compile(DecoratedRepo.Replace("LIFETIME", lifetime, StringComparison.Ordinal), mode);
        var provider = Build(app, mode);
        var serviceType = app.Type("IRepo", typeof(int));

        object first, second, other;
        using (var scope = provider.CreateScope())
        {
            first = scope.ServiceProvider.GetRequiredService(serviceType);
            second = scope.ServiceProvider.GetRequiredService(serviceType);

            // The decorated inner is the concrete registration of Repo<int>, as Microsoft DI resolves it.
            var concrete = scope.ServiceProvider.GetRequiredService(app.Type("Repo", typeof(int)));
            Assert.Equal(!string.Equals(lifetime, "Transient", StringComparison.Ordinal), ReferenceEquals(Property(first, "Inner"), concrete));
        }
        using (var scope = provider.CreateScope())
        {
            other = scope.ServiceProvider.GetRequiredService(serviceType);
        }

        Assert.Equal(!string.Equals(lifetime, "Transient", StringComparison.Ordinal), ReferenceEquals(first, second));
        Assert.Equal(string.Equals(lifetime, "Singleton", StringComparison.Ordinal), ReferenceEquals(first, other));
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decorator_MicrosoftDi_RegistersTheDecoratedClosedForms_InsteadOfTheOpenInterface(bool withoutDynamicCode)
    {
        var app = GeneratedApp.Compile(DecoratedRepo.Replace("LIFETIME", "Scoped", StringComparison.Ordinal), withoutDynamicCode);
        var services = app.AddServices(new ServiceCollection());

        // An open IRepo<> registration would add an undecorated entry to IEnumerable<IRepo<int>>.
        Assert.DoesNotContain(services, d => d.ServiceType == app.Type("IRepo`1"));
        AssertDecoratedClosedForm(app.Type("IRepo", typeof(int)));
        AssertDecoratedClosedForm(app.Type("IRepo", typeof(string)));

        // The concrete type stays registered open, and is what the decorator wraps.
        DescriptorFor(services, app.Type("Repo`1"));

        void AssertDecoratedClosedForm(Type serviceType)
        {
            var closed = DescriptorFor(services, serviceType);
            Assert.NotNull(closed.ImplementationFactory);
            Assert.Equal(ServiceLifetime.Scoped, closed.Lifetime);
        }
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Decorator_AllowMultiple_DecoratesEachRegistrationInOrder(string mode)
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Transient(AllowMultiple = true)]
            public class Repo<T> : IRepo<T> { }
            [Singleton(AllowMultiple = true)]
            public class OtherRepo<T> : IRepo<T> { }
            [Decorator]
            public class LoggingRepo<T> : IRepo<T>
            {
                public LoggingRepo(IRepo<T> inner) { Inner = inner; }
                public IRepo<T> Inner { get; }
            }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = Compile(source, mode);
        var provider = Build(app, mode);
        var serviceType = app.Type("IRepo", typeof(int));

        // Transients are registered before singletons, so the singleton OtherRepo<T> resolves.
        var resolved = provider.GetRequiredService(serviceType);
        Assert.IsType(app.Type("OtherRepo", typeof(int)), Property(resolved, "Inner"));

        var all = provider.GetServices(serviceType).ToList();
        Assert.Equal(2, all.Count);
        Assert.All(all, s => Assert.IsType(app.Type("LoggingRepo", typeof(int)), s));
        Assert.IsType(app.Type("Repo", typeof(int)), Property(all[0]!, "Inner"));
        Assert.Same(resolved, all[1]);
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Decorator_DisposesTheDecoratorAndTheInner_ItOwns(string mode)
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
            [Decorator]
            public class LoggingRepo<T> : IRepo<T>, IDisposable
            {
                public LoggingRepo(IRepo<T> inner) { Inner = inner; }
                public IRepo<T> Inner { get; }
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
            [Decorator]
            public class LoggingUnit<T> : IUnit<T>
            {
                public LoggingUnit(IUnit<T> inner) { Inner = inner; }
                public IUnit<T> Inner { get; }
                public bool IsDisposed => Inner.IsDisposed;
            }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo, IUnit<int> unit) { } }
            """;

        var app = Compile(source, mode);
        var provider = Build(app, mode);

        object unit;
        using (var scope = provider.CreateScope())
        {
            unit = scope.ServiceProvider.GetRequiredService(app.Type("IUnit", typeof(int)));
        }
        Assert.True((bool)Property(Property(unit, "Inner")!, "IsDisposed")!);

        var repo = provider.GetRequiredService(app.Type("IRepo", typeof(int)));
        ((IDisposable)provider).Dispose();
        Assert.True((bool)Property(repo, "IsDisposed")!);
        Assert.True((bool)Property(Property(repo, "Inner")!, "IsDisposed")!);
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Decorator_TransientResolvedFromTheRoot_IsDisposedWithTheRoot(string mode)
    {
        // The root tracks the decorator it returns and the concrete inner it resolved, as Microsoft
        // DI does, see ZeroAlloc-Net/ZeroAlloc.Inject#177.
        const string source = """
            using System;
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { bool IsDisposed { get; } }
            [Transient]
            public class Repo<T> : IRepo<T>, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() => IsDisposed = true;
            }
            [Decorator]
            public class LoggingRepo<T> : IRepo<T>, IDisposable
            {
                public LoggingRepo(IRepo<T> inner) { Inner = inner; }
                public IRepo<T> Inner { get; }
                public bool IsDisposed { get; private set; }
                public void Dispose() => IsDisposed = true;
            }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = Compile(source, mode);
        var provider = Build(app, mode);
        var repo = provider.GetRequiredService(app.Type("IRepo", typeof(int)));
        var inner = Property(repo, "Inner")!;
        Assert.IsType(app.Type("LoggingRepo", typeof(int)), repo);
        Assert.False((bool)Property(repo, "IsDisposed")!);

        ((IDisposable)provider).Dispose();

        Assert.True((bool)Property(repo, "IsDisposed")!);
        Assert.True((bool)Property(inner, "IsDisposed")!);
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Decorator_WithAs_WrapsANewInner(string mode)
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Scoped(As = typeof(IRepo<>))]
            public class Repo<T> : IRepo<T> { }
            [Decorator]
            public class LoggingRepo<T> : IRepo<T>
            {
                public LoggingRepo(IRepo<T> inner) { Inner = inner; }
                public IRepo<T> Inner { get; }
            }
            [Transient]
            public class Consumer { public Consumer(IRepo<int> repo) { } }
            """;

        var app = Compile(source, mode);
        var provider = Build(app, mode);
        using var scope = provider.CreateScope();

        // As narrows the registration to IRepo<>, so the concrete Repo<int> is not a service of its own.
        var resolved = scope.ServiceProvider.GetRequiredService(app.Type("IRepo", typeof(int)));
        Assert.IsType(app.Type("LoggingRepo", typeof(int)), resolved);
        Assert.IsType(app.Type("Repo", typeof(int)), Property(resolved, "Inner"));
        Assert.Same(resolved, scope.ServiceProvider.GetRequiredService(app.Type("IRepo", typeof(int))));
        Assert.Null(scope.ServiceProvider.GetService(app.Type("Repo", typeof(int))));
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Decorator_DoesNotApplyToAKeyedService_InAnyMode(string mode)
    {
        // The containers never decorated a keyed service. The extension registered the decorator
        // unkeyed around an unkeyed inner that did not exist, and dropped the keyed interface.
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IFoo { }
            [Transient(Key = "k")]
            public class Foo : IFoo { }
            [Decorator]
            public class LoggingFoo : IFoo { public LoggingFoo(IFoo inner) { } }
            """;

        var app = Compile(source, mode);
        var provider = Build(app, mode);

        Assert.IsType(app.Type("Foo"), provider.GetRequiredKeyedService(app.Type("IFoo"), "k"));
        Assert.Null(provider.GetService(app.Type("IFoo")));
        (provider as IDisposable)?.Dispose();
    }

    // ---------------------------------------------------------------
    // Key on an open generic service
    // ---------------------------------------------------------------

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Key_ResolvesTheClosedFormsKeyed_InEveryMode(string lifetime, string mode)
    {
        var app = Compile(KeyedStore.Replace("LIFETIME", lifetime, StringComparison.Ordinal), mode);
        var provider = Build(app, mode);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        foreach (var argument in new[] { typeof(int), typeof(string) })
        {
            var serviceType = app.Type("IStore", argument);
            var first = sp.GetRequiredKeyedService(serviceType, "k");
            var second = sp.GetRequiredKeyedService(serviceType, "k");
            Assert.IsType(app.Type("Store", argument), first);
            Assert.Equal(!string.Equals(lifetime, "Transient", StringComparison.Ordinal), ReferenceEquals(first, second));
            Assert.True(provider.GetRequiredService<IServiceProviderIsKeyedService>().IsKeyedService(serviceType, "k"));

            // Keyed only: neither unkeyed nor under another key.
            Assert.Null(sp.GetService(serviceType));
            Assert.Null(sp.GetKeyedService(serviceType, "other"));
        }
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData("Transient", ServiceLifetime.Transient)]
    [InlineData("Scoped", ServiceLifetime.Scoped)]
    [InlineData("Singleton", ServiceLifetime.Singleton)]
    public void Key_MicrosoftDi_RegistersTheOpenGenericKeyed(string lifetime, ServiceLifetime expected)
    {
        var app = GeneratedApp.Compile(KeyedStore.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var services = app.AddServices(new ServiceCollection());

        AssertKeyedOpen(app.Type("IStore`1"));
        AssertKeyedOpen(app.Type("Store`1"));
        Assert.DoesNotContain(services, d => d.ServiceType == app.Type("IStore", typeof(int)));

        void AssertKeyedOpen(Type serviceType)
        {
            var open = DescriptorFor(services, serviceType);
            Assert.True(open.IsKeyedService);
            Assert.Equal("k", open.ServiceKey);
            Assert.Equal(app.Type("Store`1"), open.KeyedImplementationType);
            Assert.Equal(expected, open.Lifetime);
        }
    }

    [Fact]
    public void Key_MicrosoftDi_WithoutDynamicCode_RegistersTheValueTypeClosedFormKeyed()
    {
        var app = GeneratedApp.Compile(KeyedStore.Replace("LIFETIME", "Singleton", StringComparison.Ordinal), withoutDynamicCode: true);
        var services = app.AddServices(new ServiceCollection());

        var closed = DescriptorFor(services, app.Type("IStore", typeof(int)));
        Assert.True(closed.IsKeyedService);
        Assert.Equal("k", closed.ServiceKey);
        Assert.Equal(app.Type("Store", typeof(int)), closed.KeyedImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, closed.Lifetime);
        Assert.DoesNotContain(services, d => d.ServiceType == app.Type("IStore", typeof(string)));
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Key_KeyedAndUnkeyedOpenGenericsOfOneInterface_ResolveApart(string mode)
    {
        // Both use TryAdd, which compares the key too, so neither registration hides the other.
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IStore<T> { }
            [Transient]
            public class Store<T> : IStore<T> { }
            [Singleton(Key = "k")]
            public class KeyedStore<T> : IStore<T> { }
            [Transient]
            public class Consumer { public Consumer(IStore<int> numbers) { } }
            """;

        var app = Compile(source, mode);
        var provider = Build(app, mode);
        var serviceType = app.Type("IStore", typeof(int));

        Assert.IsType(app.Type("Store", typeof(int)), provider.GetRequiredService(serviceType));
        if (!string.Equals(mode, "MicrosoftDiWithoutDynamicCode", StringComparison.Ordinal))
        {
            // This test process has dynamic code, so Microsoft DI still closes the open registration
            // next to the closed one; under NativeAOT it refuses to, see docs/native-aot.md.
            Assert.IsType(app.Type("Store", typeof(int)), Only(provider.GetServices(serviceType)));
        }
        var keyed = provider.GetRequiredKeyedService(serviceType, "k");
        Assert.IsType(app.Type("KeyedStore", typeof(int)), keyed);
        Assert.Same(keyed, provider.GetRequiredKeyedService(serviceType, "k"));
        Assert.NotNull(provider.GetRequiredService(app.Type("Consumer")));
        (provider as IDisposable)?.Dispose();
    }
}
