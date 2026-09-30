using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// A service with several interfaces resolves one instance per lifetime through every interface and
/// its concrete type, in every mode. On Microsoft DI each interface forwards to the concrete
/// registration, and Microsoft DI disposes the instance once per registration it resolved it
/// through, as documented. See ZeroAlloc-Net/ZeroAlloc.Inject#186.
/// </summary>
public class SharedInstanceTests
{
    private const string Source = """
        using System;
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface IReader { }
        public interface IWriter { }
        [LIFETIME]
        public class Store : IReader, IWriter, IDisposable
        {
            public int Disposals { get; private set; }
            public void Dispose() => Disposals++;
        }
        public interface IKeyedReader { }
        public interface IKeyedWriter { }
        [LIFETIME(Key = "k")]
        public class KeyedStore : IKeyedReader, IKeyedWriter { }
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

    private static IServiceProvider Build(GeneratedApp app, string mode) => mode switch
    {
        "MicrosoftDi" => app.BuildMicrosoftDi(),
        "Hybrid" => app.BuildHybrid(),
        "HybridWithoutFallback" => app.BuildHybrid(addServices: false),
        "Standalone" => app.BuildStandalone(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static int Disposals(object instance) => (int)instance.GetType().GetProperty("Disposals")!.GetValue(instance)!;

    [Theory]
    [MemberData(nameof(LifetimesAndModes))]
    public void EveryInterface_ResolvesTheSameInstance_PerLifetime(string lifetime, string mode)
    {
        var app = GeneratedApp.Compile(Source.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = Build(app, mode);
        bool shared = !string.Equals(lifetime, "Transient", StringComparison.Ordinal);

        object reader, other;
        using (var scope = provider.CreateScope())
        {
            var sp = scope.ServiceProvider;
            reader = sp.GetRequiredService(app.Type("IReader"));
            Assert.IsType(app.Type("Store"), reader);
            Assert.Equal(shared, ReferenceEquals(reader, sp.GetRequiredService(app.Type("IWriter"))));
            Assert.Equal(shared, ReferenceEquals(reader, sp.GetRequiredService(app.Type("Store"))));

            var keyed = sp.GetRequiredKeyedService(app.Type("IKeyedReader"), "k");
            Assert.IsType(app.Type("KeyedStore"), keyed);
            Assert.Equal(shared, ReferenceEquals(keyed, sp.GetRequiredKeyedService(app.Type("IKeyedWriter"), "k")));
            Assert.Equal(shared, ReferenceEquals(keyed, sp.GetRequiredKeyedService(app.Type("KeyedStore"), "k")));
        }
        using (var scope = provider.CreateScope())
        {
            other = scope.ServiceProvider.GetRequiredService(app.Type("IWriter"));
        }

        Assert.Equal(string.Equals(lifetime, "Singleton", StringComparison.Ordinal), ReferenceEquals(reader, other));
        (provider as IDisposable)?.Dispose();
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Scoped")]
    [InlineData("Singleton")]
    public void Containers_DisposeTheSharedInstance_Once(string lifetime)
    {
        var app = GeneratedApp.Compile(Source.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        foreach (var provider in new[] { app.BuildHybrid(), app.BuildHybrid(addServices: false), app.BuildStandalone() })
        {
            object reader;
            using (var scope = provider.CreateScope())
            {
                reader = scope.ServiceProvider.GetRequiredService(app.Type("IReader"));
                scope.ServiceProvider.GetRequiredService(app.Type("IWriter"));
                scope.ServiceProvider.GetRequiredService(app.Type("Store"));
            }
            ((IDisposable)provider).Dispose();
            Assert.Equal(1, Disposals(reader));
        }
    }

    [Theory]
    // A singleton or scoped instance is disposed once for its own registration, and once more for
    // each interface registration it was resolved through: here IReader and IWriter.
    [InlineData("Singleton", 3)]
    [InlineData("Scoped", 3)]
    // A transient resolved through IReader is tracked by the IReader registration and by the
    // concrete registration it forwards to.
    [InlineData("Transient", 2)]
    public void MicrosoftDi_DisposesTheSharedInstance_OncePerRegistration(string lifetime, int expected)
    {
        var app = GeneratedApp.Compile(Source.Replace("LIFETIME", lifetime, StringComparison.Ordinal));
        var provider = app.BuildMicrosoftDi();

        object reader;
        using (var scope = provider.CreateScope())
        {
            reader = scope.ServiceProvider.GetRequiredService(app.Type("IReader"));
            if (!string.Equals(lifetime, "Transient", StringComparison.Ordinal))
            {
                scope.ServiceProvider.GetRequiredService(app.Type("IWriter"));
                scope.ServiceProvider.GetRequiredService(app.Type("Store"));
            }
        }
        provider.Dispose();

        Assert.Equal(expected, Disposals(reader));
    }

    [Fact]
    public void MicrosoftDi_ForwardsEachInterface_ToTheConcreteRegistration()
    {
        var app = GeneratedApp.Compile(Source.Replace("LIFETIME", "Singleton", StringComparison.Ordinal));
        var services = app.AddServices(new ServiceCollection());

        Assert.NotNull(Descriptor(services, app.Type("IReader")).ImplementationFactory);
        Assert.NotNull(Descriptor(services, app.Type("IWriter")).ImplementationFactory);
        Assert.NotNull(Descriptor(services, app.Type("IKeyedReader")).KeyedImplementationFactory);
        Assert.NotNull(Descriptor(services, app.Type("IKeyedWriter")).KeyedImplementationFactory);

        static ServiceDescriptor Descriptor(IServiceCollection services, Type serviceType) =>
            AssertOne(services.Where(d => d.ServiceType == serviceType).ToList());
    }

    private static T AssertOne<T>(List<T> items)
    {
        Assert.True(items.Count == 1, $"Expected exactly one item, got {items.Count}.");
        return items[0];
    }

    [Fact]
    public void MicrosoftDi_ServiceWithOneInterface_IsNotForwarded()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IReader { }
            [Singleton]
            public class Store : IReader { }
            """;

        var app = GeneratedApp.Compile(source);
        using var provider = app.BuildMicrosoftDi();

        // Only a service with several interfaces forwards; one interface keeps its own factory.
        Assert.NotSame(provider.GetRequiredService(app.Type("IReader")), provider.GetRequiredService(app.Type("Store")));
    }
}
