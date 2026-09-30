using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// WhenRegistered is decided from the registrations in every mode. The Add...Services extension
/// looks the type up when it runs, the hybrid container in the full service collection it is built
/// from, and the standalone container among the generated services. A type the generator registers
/// itself is registered in every mode. See ZeroAlloc-Net/ZeroAlloc.Inject#180.
/// </summary>
public class WhenRegisteredModeTests
{
    private const string NonGeneric = """
        using System;
        using ZeroAlloc.Inject;
        namespace TestApp;
        public sealed class Marker { }
        [Singleton]
        public sealed class GeneratedMarker { }
        public interface IRepo { int Disposals { get; } }
        [LIFETIME]
        public class Repo : IRepo, IDisposable
        {
            public int Disposals { get; private set; }
            public void Dispose() => Disposals++;
        }
        [DecoratorOf(typeof(IRepo), Order = 1, WhenRegistered = typeof(Marker))]
        public class ByApplication : IRepo, IDisposable
        {
            public ByApplication(IRepo inner) { Inner = inner; }
            public IRepo Inner { get; }
            public int Disposals { get; private set; }
            public void Dispose() => Disposals++;
        }
        [DecoratorOf(typeof(IRepo), Order = 2, WhenRegistered = typeof(GeneratedMarker))]
        public class ByGenerator : IRepo
        {
            public ByGenerator(IRepo inner) { Inner = inner; }
            public IRepo Inner { get; }
            public int Disposals => Inner.Disposals;
        }
        """;

    private const string OpenGeneric = """
        using ZeroAlloc.Inject;
        namespace TestApp;
        public sealed class Marker { }
        [Singleton]
        public sealed class GeneratedMarker { }
        public interface IRepo<T> { }
        [LIFETIME]
        public class Repo<T> : IRepo<T> { }
        [DecoratorOf(typeof(IRepo<>), Order = 1, WhenRegistered = typeof(Marker))]
        public class ByApplication<T> : IRepo<T>
        {
            public ByApplication(IRepo<T> inner) { Inner = inner; }
            public IRepo<T> Inner { get; }
        }
        [DecoratorOf(typeof(IRepo<>), Order = 2, WhenRegistered = typeof(GeneratedMarker))]
        public class ByGenerator<T> : IRepo<T>
        {
            public ByGenerator(IRepo<T> inner) { Inner = inner; }
            public IRepo<T> Inner { get; }
        }
        [Transient]
        public class Consumer { public Consumer(IRepo<int> repo) { } }
        """;

    private static readonly string[] Lifetimes = ["Transient", "Scoped", "Singleton"];

    /// <summary>Where the application registers Marker: nowhere, before Add...Services, or after it.</summary>
    public static TheoryData<string, string, string, string> Cases()
    {
        // Source, lifetime, marker registration, mode, and the chain each resolves.
        var data = new TheoryData<string, string, string, string>();
        foreach (var source in new[] { "NonGeneric", "OpenGeneric" })
        {
            foreach (var lifetime in Lifetimes)
            {
                foreach (var marker in new[] { "None", "Before", "After" })
                {
                    foreach (var mode in new[] { "MicrosoftDi", "Hybrid", "HybridWithoutFallback", "Standalone" })
                    {
                        data.Add(source, lifetime, marker, mode);
                    }
                }
            }
        }
        return data;
    }

    /// <summary>Whether the decorator gated on the application's Marker applies.</summary>
    private static bool ApplicationMarkerApplies(string marker, string mode) => mode switch
    {
        // The extension decides when it runs, so a later registration is not seen.
        "MicrosoftDi" => string.Equals(marker, "Before", StringComparison.Ordinal),
        // The hybrid container checks the full collection it is built from.
        "Hybrid" or "HybridWithoutFallback" => !string.Equals(marker, "None", StringComparison.Ordinal),
        // The standalone container has only the generated services.
        _ => false,
    };

    private static IServiceProvider Build(GeneratedApp app, string marker, string mode)
    {
        Action<IServiceCollection> register = services => services.AddSingleton(app.Type("Marker"));
        var before = string.Equals(marker, "Before", StringComparison.Ordinal) ? register : null;
        var after = string.Equals(marker, "After", StringComparison.Ordinal) ? register : null;
        return mode switch
        {
            "MicrosoftDi" => app.BuildMicrosoftDi(before, after),
            "Hybrid" => app.BuildHybrid(before: before, after: after),
            "HybridWithoutFallback" => app.BuildHybrid(addServices: false, before: before, after: after),
            "Standalone" => app.BuildStandalone(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }

    private static string Chain(object? instance)
    {
        var names = new List<string>();
        while (instance != null)
        {
            var type = instance.GetType();
            names.Add(type.IsGenericType ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] : type.Name);
            instance = type.GetProperty("Inner")?.GetValue(instance);
        }
        return string.Join(",", names);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void WhenRegistered_IsDecidedFromTheRegistrations_InEveryMode(string source, string lifetime, string marker, string mode)
    {
        var generic = string.Equals(source, "OpenGeneric", StringComparison.Ordinal);
        var app = GeneratedApp.Compile((generic ? OpenGeneric : NonGeneric).Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, marker, mode);
        var serviceType = generic ? app.Type("IRepo", typeof(int)) : app.Type("IRepo");

        // GeneratedMarker is a generated service, so its decorator applies in every mode.
        var expected = ApplicationMarkerApplies(marker, mode) ? "ByGenerator,ByApplication,Repo" : "ByGenerator,Repo";
        using (var scope = provider.CreateScope())
        {
            var resolved = scope.ServiceProvider.GetRequiredService(serviceType);
            Assert.Equal(expected, Chain(resolved));
            var all = scope.ServiceProvider.GetServices(serviceType).ToList();
            Assert.Equal(expected, Chain(all[^1]));
            Assert.All(all, instance => Assert.Equal(expected, Chain(instance)));
        }
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void WhenRegistered_DisposesWhatItResolves_ExactlyOnce(string source, string lifetime, string marker, string mode)
    {
        if (string.Equals(source, "OpenGeneric", StringComparison.Ordinal)) return;

        var app = GeneratedApp.Compile(NonGeneric.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, marker, mode);

        object resolved;
        using (var scope = provider.CreateScope())
        {
            resolved = scope.ServiceProvider.GetRequiredService(app.Type("IRepo"));
        }
        ((IDisposable)provider).Dispose();

        // As in Microsoft DI, the concrete Repo is disposed by its registration, once, and a decorator
        // only when it is the outermost, what the factory returns. ByGenerator, the outermost here,
        // is not disposable, so a ByApplication under it is not disposed in any mode.
        var chain = new List<object>();
        for (object? instance = resolved; instance != null; instance = instance.GetType().GetProperty("Inner")?.GetValue(instance))
        {
            chain.Add(instance);
        }
        Assert.Equal(1, Disposals(chain[^1]));
        if (chain.Count == 3) Assert.Equal(0, Disposals(chain[1]));

        static int Disposals(object instance) => (int)instance.GetType().GetProperty("Disposals")!.GetValue(instance)!;
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Scoped")]
    [InlineData("Singleton")]
    public void WhenRegistered_AllSkipped_Hybrid_ResolvesAndDisposesTheConcreteOnce(string lifetime)
    {
        const string source = """
            using System;
            using ZeroAlloc.Inject;
            namespace TestApp;
            public sealed class Marker { }
            public interface IRepo { }
            [LIFETIME]
            public class Repo : IRepo, IDisposable
            {
                public int Disposals { get; private set; }
                public void Dispose() => Disposals++;
            }
            [DecoratorOf(typeof(IRepo), WhenRegistered = typeof(Marker))]
            public class Conditional : IRepo, IDisposable
            {
                public Conditional(IRepo inner) { }
                public void Dispose() { }
            }
            """;

        var app = GeneratedApp.Compile(source.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = app.BuildHybrid();
        object resolved;
        using (var scope = provider.CreateScope())
        {
            resolved = scope.ServiceProvider.GetRequiredService(app.Type("IRepo"));
            Assert.IsType(app.Type("Repo"), resolved);
        }
        ((IDisposable)provider).Dispose();
        Assert.Equal(1, (int)resolved.GetType().GetProperty("Disposals")!.GetValue(resolved)!);
    }
}
