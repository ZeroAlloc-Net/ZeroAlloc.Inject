using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// A non-generic [Decorator] resolves the same way in every mode: the generated Add...Services
/// extension on Microsoft DI, which is the reference, the hybrid container with and without the
/// generated registrations in its fallback, and the standalone container.
/// See ZeroAlloc-Net/ZeroAlloc.Inject#180.
/// </summary>
public class DecoratorModeParityTests
{
    private const string DecoratedRepo = """
        using System;
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IRepo { }
        [LIFETIME]
        public class Repo : IRepo { }
        [Decorator]
        public class LoggingRepo : IRepo
        {
            public LoggingRepo(IRepo inner) { Inner = inner; }
            public IRepo Inner { get; }
        }
        """;

    private static readonly string[] Modes = ["MicrosoftDi", "Hybrid", "HybridWithoutFallback", "Standalone"];

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

    private static IServiceProvider Build(GeneratedApp app, string mode) => mode switch
    {
        "MicrosoftDi" => app.BuildMicrosoftDi(),
        "Hybrid" => app.BuildHybrid(),
        "HybridWithoutFallback" => app.BuildHybrid(addServices: false),
        "Standalone" => app.BuildStandalone(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static bool IsTransient(string lifetime) => string.Equals(lifetime, "Transient", StringComparison.Ordinal);

    private static bool IsSingleton(string lifetime) => string.Equals(lifetime, "Singleton", StringComparison.Ordinal);

    private static T Only<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        Assert.True(list.Count == 1, $"Expected exactly one item, got {list.Count}.");
        return list[0];
    }

    private static object? Property(object instance, string name) => instance.GetType().GetProperty(name)!.GetValue(instance);

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Decorator_WrapsTheConcreteRegistration_WithItsLifetime(string lifetime, string mode)
    {
        var app = GeneratedApp.Compile(DecoratedRepo.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, mode);
        var serviceType = app.Type("IRepo");

        object first, other;
        using (var scope = provider.CreateScope())
        {
            var sp = scope.ServiceProvider;
            first = sp.GetRequiredService(serviceType);
            Assert.IsType(app.Type("LoggingRepo"), first);
            Assert.IsType(app.Type("Repo"), Property(first, "Inner"));

            // The inner is the concrete registration of Repo, as Microsoft DI resolves it.
            var concrete = sp.GetRequiredService(app.Type("Repo"));
            Assert.IsType(app.Type("Repo"), concrete);
            Assert.Equal(!IsTransient(lifetime), ReferenceEquals(Property(first, "Inner"), concrete));
            Assert.Equal(!IsTransient(lifetime), ReferenceEquals(first, sp.GetRequiredService(serviceType)));

            var all = sp.GetServices(serviceType).ToList();
            var only = Only(all);
            Assert.IsType(app.Type("LoggingRepo"), only);
            Assert.Equal(!IsTransient(lifetime), ReferenceEquals(first, only));
        }
        using (var scope = provider.CreateScope())
        {
            other = scope.ServiceProvider.GetRequiredService(serviceType);
        }

        Assert.Equal(IsSingleton(lifetime), ReferenceEquals(first, other));
        if (IsSingleton(lifetime))
        {
            Assert.Same(first, provider.GetRequiredService(serviceType));
            Assert.Same(first, Only(provider.GetServices(serviceType)));
        }
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Decorator_WithAs_WrapsANewInner(string lifetime, string mode)
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IWide { }
            public interface INarrow { }
            [LIFETIME(As = typeof(INarrow))]
            public class Repo : INarrow, IWide
            {
                public Repo(IDependency dependency) { Dependency = dependency; }
                public IDependency Dependency { get; }
            }
            public interface IDependency { }
            [Singleton]
            public class Dependency : IDependency { }
            [Decorator]
            public class LoggingRepo : INarrow
            {
                public LoggingRepo(INarrow inner) { Inner = inner; }
                public INarrow Inner { get; }
            }
            """;

        var app = GeneratedApp.Compile(source.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, mode);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var resolved = sp.GetRequiredService(app.Type("INarrow"));
        Assert.IsType(app.Type("LoggingRepo"), resolved);
        var inner = Property(resolved, "Inner")!;
        Assert.IsType(app.Type("Repo"), inner);
        Assert.Same(sp.GetRequiredService(app.Type("IDependency")), Property(inner, "Dependency"));
        Assert.Equal(!IsTransient(lifetime), ReferenceEquals(resolved, sp.GetRequiredService(app.Type("INarrow"))));
        Assert.IsType(app.Type("LoggingRepo"), Only(sp.GetServices(app.Type("INarrow"))));

        // As narrows the registration, so neither the concrete type nor its other interface resolves.
        Assert.Null(sp.GetService(app.Type("Repo")));
        Assert.Null(sp.GetService(app.Type("IWide")));
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Decorator_ChainsInOrder_AndResolvesItsOtherDependencies(string lifetime, string mode)
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo { }
            [LIFETIME]
            public class Repo : IRepo { }
            public interface IClock { }
            [Singleton]
            public class Clock : IClock { }
            [DecoratorOf(typeof(IRepo), Order = 2)]
            public class Outer : IRepo
            {
                public Outer(IClock clock, IRepo inner) { Clock = clock; Inner = inner; }
                public IClock Clock { get; }
                public IRepo Inner { get; }
            }
            [DecoratorOf(typeof(IRepo), Order = 1)]
            public class Middle : IRepo
            {
                public Middle(IRepo inner) { Inner = inner; }
                public IRepo Inner { get; }
            }
            """;

        var app = GeneratedApp.Compile(source.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, mode);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var outer = sp.GetRequiredService(app.Type("IRepo"));
        Assert.IsType(app.Type("Outer"), outer);
        Assert.Same(sp.GetRequiredService(app.Type("IClock")), Property(outer, "Clock"));
        var middle = Property(outer, "Inner")!;
        Assert.IsType(app.Type("Middle"), middle);
        Assert.IsType(app.Type("Repo"), Property(middle, "Inner"));
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Decorator_AllowMultiple_DecoratesEachRegistration(string mode)
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IRepo { }
            [Transient(AllowMultiple = true)]
            public class Repo : IRepo { }
            [Singleton(AllowMultiple = true)]
            public class OtherRepo : IRepo { }
            [Decorator]
            public class LoggingRepo : IRepo
            {
                public LoggingRepo(IRepo inner) { Inner = inner; }
                public IRepo Inner { get; }
            }
            """;

        var app = GeneratedApp.Compile(source);
        var provider = Build(app, mode);
        var serviceType = app.Type("IRepo");

        // Transients are registered before singletons, so the singleton OtherRepo is the last one.
        var resolved = provider.GetRequiredService(serviceType);
        Assert.IsType(app.Type("LoggingRepo"), resolved);
        Assert.IsType(app.Type("OtherRepo"), Property(resolved, "Inner"));

        AssertAll(provider);
        using (var scope = provider.CreateScope())
        {
            AssertAll(scope.ServiceProvider);
        }
        (provider as IDisposable)?.Dispose();

        void AssertAll(IServiceProvider sp)
        {
            var all = sp.GetServices(serviceType).ToList();
            Assert.Equal(2, all.Count);
            Assert.All(all, s => Assert.IsType(app.Type("LoggingRepo"), s));
            Assert.IsType(app.Type("Repo"), Property(all[0]!, "Inner"));
            Assert.Same(resolved, all[1]);
        }
    }

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Decorator_OfTwoInterfaces_WrapsTheOneConcreteRegistration(string lifetime, string mode)
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IReader { }
            public interface IWriter { }
            [LIFETIME]
            public class Store : IReader, IWriter { }
            [Decorator]
            public class LoggingReader : IReader
            {
                public LoggingReader(IReader inner) { Inner = inner; }
                public IReader Inner { get; }
            }
            [Decorator]
            public class LoggingWriter : IWriter
            {
                public LoggingWriter(IWriter inner) { Inner = inner; }
                public IWriter Inner { get; }
            }
            """;

        var app = GeneratedApp.Compile(source.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, mode);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var reader = sp.GetRequiredService(app.Type("IReader"));
        var writer = sp.GetRequiredService(app.Type("IWriter"));
        Assert.IsType(app.Type("LoggingReader"), reader);
        Assert.IsType(app.Type("LoggingWriter"), writer);
        Assert.Equal(!IsTransient(lifetime), ReferenceEquals(Property(reader, "Inner"), Property(writer, "Inner")));
        Assert.Equal(!IsTransient(lifetime), ReferenceEquals(Property(reader, "Inner"), sp.GetRequiredService(app.Type("Store"))));
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Decorator_OfAServiceWithPropertyInjection_InjectsTheInner(string lifetime, string mode)
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IClock { }
            [Singleton]
            public class Clock : IClock { }
            public interface IRepo { IClock? Clock { get; } }
            [LIFETIME]
            public class Repo : IRepo
            {
                [Inject] public IClock? Clock { get; set; }
            }
            [Decorator]
            public class LoggingRepo : IRepo
            {
                public LoggingRepo(IRepo inner) { Inner = inner; }
                public IRepo Inner { get; }
                public IClock? Clock => Inner.Clock;
            }
            public interface INarrow { IClock? Clock { get; } }
            [LIFETIME(As = typeof(INarrow))]
            public class Narrow : INarrow
            {
                [Inject] public IClock? Clock { get; set; }
            }
            [Decorator]
            public class LoggingNarrow : INarrow
            {
                public LoggingNarrow(INarrow inner) { Inner = inner; }
                public INarrow Inner { get; }
                public IClock? Clock => Inner.Clock;
            }
            """;

        var app = GeneratedApp.Compile(source.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, mode);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;
        var clock = sp.GetRequiredService(app.Type("IClock"));

        var repo = sp.GetRequiredService(app.Type("IRepo"));
        Assert.IsType(app.Type("LoggingRepo"), repo);
        Assert.Same(clock, Property(Property(repo, "Inner")!, "Clock"));

        var narrow = sp.GetRequiredService(app.Type("INarrow"));
        Assert.IsType(app.Type("LoggingNarrow"), narrow);
        Assert.Same(clock, Property(Property(narrow, "Inner")!, "Clock"));
        (provider as IDisposable)?.Dispose();
    }

    // ---------------------------------------------------------------
    // Disposal: the container disposes what Microsoft DI disposes
    // ---------------------------------------------------------------

    private const string DisposableRepo = """
        using System;
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IRepo { int Disposals { get; } }
        [LIFETIME]
        public class Repo : IRepo, IDisposable
        {
            public int Disposals { get; private set; }
            public void Dispose() => Disposals++;
        }
        [Decorator]
        public class LoggingRepo : IRepo, IDisposable
        {
            public LoggingRepo(IRepo inner) { Inner = inner; }
            public IRepo Inner { get; }
            public int Disposals { get; private set; }
            public void Dispose() => Disposals++;
        }
        public interface IUnit { int Disposals { get; } }
        [LIFETIME]
        public class Unit : IUnit, IDisposable
        {
            public int Disposals { get; private set; }
            public void Dispose() => Disposals++;
        }
        [Decorator]
        public class LoggingUnit : IUnit
        {
            public LoggingUnit(IUnit inner) { Inner = inner; }
            public IUnit Inner { get; }
            public int Disposals => Inner.Disposals;
        }
        """;

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void Decorator_DisposesTheDecoratorAndTheInner_ExactlyOnce(string lifetime, string mode)
    {
        var app = GeneratedApp.Compile(DisposableRepo.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, mode);

        object repo, unit;
        using (var scope = provider.CreateScope())
        {
            repo = scope.ServiceProvider.GetRequiredService(app.Type("IRepo"));
            unit = scope.ServiceProvider.GetRequiredService(app.Type("IUnit"));
            Assert.IsType(app.Type("LoggingRepo"), repo);
            Assert.IsType(app.Type("LoggingUnit"), unit);
        }

        // A scope disposes its transients and scoped services, the root its singletons.
        if (IsSingleton(lifetime))
        {
            AssertDisposals(0);
            ((IDisposable)provider).Dispose();
        }
        AssertDisposals(1);

        if (!IsSingleton(lifetime))
        {
            ((IDisposable)provider).Dispose();
            AssertDisposals(1);
        }

        void AssertDisposals(int expected)
        {
            Assert.Equal(expected, (int)Property(repo, "Disposals")!);
            Assert.Equal(expected, (int)Property(Property(repo, "Inner")!, "Disposals")!);
            Assert.Equal(expected, (int)Property(Property(unit, "Inner")!, "Disposals")!);
        }
    }

    [Theory]
    [MemberData(nameof(AllModes))]
    public void Decorator_TransientResolvedFromTheRoot_IsDisposedWithTheRoot(string mode)
    {
        var app = GeneratedApp.Compile(DisposableRepo.Replace("LIFETIME", "Transient", StringComparison.Ordinal));
        var provider = Build(app, mode);
        var repo = provider.GetRequiredService(app.Type("IRepo"));
        var unit = provider.GetRequiredService(app.Type("IUnit"));

        ((IDisposable)provider).Dispose();

        Assert.Equal(1, (int)Property(repo, "Disposals")!);
        Assert.Equal(1, (int)Property(Property(repo, "Inner")!, "Disposals")!);
        Assert.Equal(1, (int)Property(Property(unit, "Inner")!, "Disposals")!);
    }

    // ---------------------------------------------------------------
    // WhenRegistered in the Add...Services extension
    // ---------------------------------------------------------------

    private const string ConditionalRepo = """
        using ZeroAlloc.Inject;
        namespace TestApp;
        public sealed class Marker { }
        public sealed class OtherMarker { }
        public interface IRepo { }
        [Transient]
        public class Repo : IRepo { }
        [DecoratorOf(typeof(IRepo), Order = 1)]
        public class First : IRepo
        {
            public First(IRepo inner) { Inner = inner; }
            public IRepo Inner { get; }
        }
        [DecoratorOf(typeof(IRepo), Order = 2, WhenRegistered = typeof(Marker))]
        public class Second : IRepo
        {
            public Second(IRepo inner) { Inner = inner; }
            public IRepo Inner { get; }
        }
        [DecoratorOf(typeof(IRepo), Order = 3, WhenRegistered = typeof(OtherMarker))]
        public class Third : IRepo
        {
            public Third(IRepo inner) { Inner = inner; }
            public IRepo Inner { get; }
        }
        [DecoratorOf(typeof(IRepo), Order = 4)]
        public class Fourth : IRepo
        {
            public Fourth(IRepo inner) { Inner = inner; }
            public IRepo Inner { get; }
        }
        """;

    [Theory]
    [InlineData(false, false, "Fourth,First,Repo")]
    [InlineData(true, false, "Fourth,Second,First,Repo")]
    [InlineData(false, true, "Fourth,Third,First,Repo")]
    [InlineData(true, true, "Fourth,Third,Second,First,Repo")]
    public void WhenRegistered_MicrosoftDi_SkipsOnlyTheDecoratorsWhoseTypeIsNotRegistered(bool marker, bool otherMarker, string expected)
    {
        var app = GeneratedApp.Compile(ConditionalRepo);
        using var provider = app.BuildMicrosoftDi(services =>
        {
            if (marker) services.AddSingleton(app.Type("Marker"));
            if (otherMarker) services.AddSingleton(app.Type("OtherMarker"));
        });

        var chain = new List<string>();
        object? current = provider.GetRequiredService(app.Type("IRepo"));
        while (current != null)
        {
            chain.Add(current.GetType().Name);
            current = current.GetType().GetProperty("Inner")?.GetValue(current);
        }

        Assert.Equal(expected, string.Join(",", chain));
        Only(provider.GetServices(app.Type("IRepo")));
    }

    [Fact]
    public void WhenRegistered_MicrosoftDi_KeepsTheServiceRegistered_WhenItsOnlyDecoratorIsSkipped()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public sealed class Marker { }
            public interface ICond { }
            [Scoped]
            public class Cond : ICond { }
            [DecoratorOf(typeof(ICond), WhenRegistered = typeof(Marker))]
            public class LoggingCond : ICond
            {
                public LoggingCond(ICond inner) { Inner = inner; }
                public ICond Inner { get; }
            }
            """;

        var app = GeneratedApp.Compile(source);
        using var provider = app.BuildMicrosoftDi();
        using var scope = provider.CreateScope();

        var resolved = scope.ServiceProvider.GetRequiredService(app.Type("ICond"));
        Assert.IsType(app.Type("Cond"), resolved);
        Only(scope.ServiceProvider.GetServices(app.Type("ICond")));
    }
}
