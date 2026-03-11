using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Tests.ContainerTests;

public class IntegrationTests
{
    /// <summary>
    /// Runs the source generator, compiles the user source together with the
    /// generated output, and returns an <see cref="IServiceProvider"/> backed
    /// by the generated container.  User-defined types live in the returned
    /// assembly and must be accessed via reflection.
    /// </summary>
    private static (Assembly assembly, IServiceProvider provider) BuildAndCreateProvider(
        string source,
        Action<IServiceCollection>? configureServices = null)
    {
        // 1. Run the generator to obtain the compilation with generated trees.
        var (outputCompilation, diagnostics) = RunGeneratorAndGetCompilation(source);

        var genErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (genErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Generator produced errors:\n" + string.Join("\n", genErrors));
        }

        // 2. Emit the compilation that already contains user + generated trees.
        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            throw new InvalidOperationException(
                "Compilation failed:\n" + string.Join("\n", errors));
        }

        ms.Seek(0, SeekOrigin.Begin);

        // Load into an isolated context so repeated test runs don't collide.
        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        // Locate the generated extension class and invoke BuildZeroInjectServiceProvider.
        var extensionClass = assembly.GetTypes()
            .First(t => t.Name == "ZeroInjectServiceCollectionExtensions");
        var buildMethod = extensionClass.GetMethod(
            "BuildZeroInjectServiceProvider",
            BindingFlags.Public | BindingFlags.Static);

        var services = new ServiceCollection();
        configureServices?.Invoke(services);

        var provider = (IServiceProvider)buildMethod!.Invoke(null, [services])!;
        return (assembly, provider);
    }

    /// <summary>
    /// Mirrors <c>GeneratorTestHelper.RunGeneratorCore</c> but returns the
    /// output <see cref="Compilation"/> directly (which contains user source
    /// plus every generated syntax tree as separate trees), avoiding the
    /// concat-into-one-string problem.
    /// </summary>
    private static (Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics) RunGeneratorAndGetCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        // Ensure key assemblies are present even if not yet loaded.
        var extraAssemblies = new[]
        {
            typeof(TransientAttribute).Assembly,                                    // ZeroInject (attributes)
            typeof(ZeroInject.Container.ZeroInjectServiceProviderBase).Assembly,     // ZeroInject.Container
            typeof(ServiceCollectionContainerBuilderExtensions).Assembly,            // M.E.DI (BuildServiceProvider)
            typeof(ServiceCollection).Assembly,                                     // M.E.DI.Abstractions
        };
        foreach (var asm in extraAssemblies)
        {
            if (!references.Any(r => string.Equals(r.Display, asm.Location, StringComparison.Ordinal)))
            {
                references.Add(MetadataReference.CreateFromFile(asm.Location));
            }
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.ZeroInjectGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation, diagnostics);
    }

    // ---------------------------------------------------------------
    // 1. Transient (no dependencies) - non-null, different instances
    // ---------------------------------------------------------------
    [Fact]
    public void Transient_NoDeps_ReturnsNonNull_DifferentInstances()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;

        var instance1 = provider.GetService(fooType);
        var instance2 = provider.GetService(fooType);

        Assert.NotNull(instance1);
        Assert.NotNull(instance2);
        Assert.NotSame(instance1, instance2);
    }

    // ---------------------------------------------------------------
    // 2. Transient with constructor dependency
    // ---------------------------------------------------------------
    [Fact]
    public void Transient_WithDependency_InjectsCorrectly()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface ILogger { }
            [Singleton]
            public class Logger : ILogger { }
            public interface IService { }
            [Transient]
            public class Service : IService
            {
                public Service(ILogger logger) { Logger = logger; }
                public ILogger Logger { get; }
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var serviceType = assembly.GetType("TestApp.IService")!;

        var instance = provider.GetService(serviceType);

        Assert.NotNull(instance);
        // Verify the dependency was injected by reading the Logger property.
        var loggerProp = instance!.GetType().GetProperty("Logger")!;
        var loggerValue = loggerProp.GetValue(instance);
        Assert.NotNull(loggerValue);
    }

    // ---------------------------------------------------------------
    // 3. Singleton returns same instance on repeated calls
    // ---------------------------------------------------------------
    [Fact]
    public void Singleton_ReturnsSameInstance()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var cacheType = assembly.GetType("TestApp.ICache")!;

        var first = provider.GetService(cacheType);
        var second = provider.GetService(cacheType);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // ---------------------------------------------------------------
    // 4. Scoped: same within scope, different across scopes
    // ---------------------------------------------------------------
    [Fact]
    public void Scoped_SameWithinScope_DifferentAcrossScopes()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IDbContext { }
            [Scoped]
            public class DbContext : IDbContext { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var dbContextType = assembly.GetType("TestApp.IDbContext")!;

        // The root provider is an IServiceScopeFactory via the generated base class.
        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;

        using var scope1 = scopeFactory.CreateScope();
        var a1 = scope1.ServiceProvider.GetService(dbContextType);
        var a2 = scope1.ServiceProvider.GetService(dbContextType);

        using var scope2 = scopeFactory.CreateScope();
        var b1 = scope2.ServiceProvider.GetService(dbContextType);

        Assert.NotNull(a1);
        Assert.Same(a1, a2);       // same within scope
        Assert.NotSame(a1, b1);    // different across scopes
    }

    // ---------------------------------------------------------------
    // 5. Unknown service falls through to fallback provider
    // ---------------------------------------------------------------
    [Fact]
    public void Unknown_Service_FallsThrough_ToFallback()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;

        // Register an extra service in the underlying ServiceCollection that
        // the generator knows nothing about.  It should be resolvable via
        // the fallback path.
        var (assembly, provider) = BuildAndCreateProvider(source, services =>
        {
            services.AddSingleton<IntegrationFallbackMarker>();
        });

        var marker = provider.GetService(typeof(IntegrationFallbackMarker));
        Assert.NotNull(marker);
        Assert.IsType<IntegrationFallbackMarker>(marker);
    }

    // ---------------------------------------------------------------
    // 6. IServiceScopeFactory and IServiceProvider resolve to self
    // ---------------------------------------------------------------
    [Fact]
    public void IServiceProvider_And_IServiceScopeFactory_ResolveToSelf()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;

        var (_, provider) = BuildAndCreateProvider(source);

        var resolvedProvider = provider.GetService(typeof(IServiceProvider));
        var resolvedFactory = provider.GetService(typeof(IServiceScopeFactory));

        Assert.Same(provider, resolvedProvider);
        Assert.Same(provider, resolvedFactory);
    }

    // ---------------------------------------------------------------
    // 7. Scope disposal disposes tracked disposable transients
    // ---------------------------------------------------------------
    [Fact]
    public void ScopeDisposal_DisposesTrackedTransients()
    {
        const string source = """
            using ZeroInject;
            using System;
            namespace TestApp;
            public interface IHandle { }
            [Transient]
            public class Handle : IHandle, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() { IsDisposed = true; }
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var handleType = assembly.GetType("TestApp.IHandle")!;

        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        var scope = scopeFactory.CreateScope();

        var instance = scope.ServiceProvider.GetService(handleType);
        Assert.NotNull(instance);

        // Read IsDisposed before and after scope disposal.
        var isDisposedProp = instance!.GetType().GetProperty("IsDisposed")!;
        Assert.False((bool)isDisposedProp.GetValue(instance)!);

        scope.Dispose();

        Assert.True((bool)isDisposedProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // 8. Keyed singleton returns same instance
    // ---------------------------------------------------------------
    [Fact]
    public void KeyedSingleton_ReturnsSameInstance()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;
        var (assembly, provider) = BuildAndCreateProvider(source);
        var keyedProvider = (IKeyedServiceProvider)provider;
        var cacheType = assembly.GetType("TestApp.ICache")!;

        var first = keyedProvider.GetKeyedService(cacheType, "redis");
        var second = keyedProvider.GetKeyedService(cacheType, "redis");

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // ---------------------------------------------------------------
    // 9. Keyed transient returns different instances
    // ---------------------------------------------------------------
    [Fact]
    public void KeyedTransient_ReturnsDifferentInstances()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface ICache { }
            [Transient(Key = "fast")]
            public class FastCache : ICache { }
            """;
        var (assembly, provider) = BuildAndCreateProvider(source);
        var keyedProvider = (IKeyedServiceProvider)provider;
        var cacheType = assembly.GetType("TestApp.ICache")!;

        var first = keyedProvider.GetKeyedService(cacheType, "fast");
        var second = keyedProvider.GetKeyedService(cacheType, "fast");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    // ---------------------------------------------------------------
    // 10. As property only resolves as specified type
    // ---------------------------------------------------------------
    [Fact]
    public void AsProperty_OnlyResolvesAsSpecifiedType()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IFoo { }
            public interface IBar { }
            [Transient(As = typeof(IFoo))]
            public class Svc : IFoo, IBar { }
            """;
        var (assembly, provider) = BuildAndCreateProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;
        var barType = assembly.GetType("TestApp.IBar")!;

        var asFoo = provider.GetService(fooType);
        var asBar = provider.GetService(barType);

        Assert.NotNull(asFoo);
        Assert.Null(asBar); // Not registered as IBar
    }

    // ---------------------------------------------------------------
    // 11. ZeroInjectServiceProviderFactory creates working provider
    // ---------------------------------------------------------------
    [Fact]
    public void ServiceProviderFactory_CreatesWorkingProvider()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;
        var (outputCompilation, diagnostics) = RunGeneratorAndGetCompilation(source);

        var genErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (genErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Generator produced errors:\n" + string.Join("\n", genErrors));
        }

        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString());
            throw new InvalidOperationException(
                "Compilation failed:\n" + string.Join("\n", errors));
        }

        ms.Seek(0, SeekOrigin.Begin);
        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        // Find the factory class and invoke it
        var factoryType = assembly.GetTypes().First(t => t.Name == "ZeroInjectServiceProviderFactory");
        var factory = Activator.CreateInstance(factoryType)!;

        var createBuilderMethod = factoryType.GetMethod("CreateBuilder")!;
        var createProviderMethod = factoryType.GetMethod("CreateServiceProvider")!;

        var services = new ServiceCollection();
        var builder = createBuilderMethod.Invoke(factory, [services]);
        var provider = (IServiceProvider)createProviderMethod.Invoke(factory, [builder])!;

        var fooType = assembly.GetType("TestApp.IFoo")!;
        var instance = provider.GetService(fooType);
        Assert.NotNull(instance);
    }

    // ---------------------------------------------------------------
    // 12. DisposeAsync on scope disposes tracked transients
    // ---------------------------------------------------------------
    [Fact]
    public async Task ScopeDisposeAsync_DisposesTrackedTransients()
    {
        const string source = """
            using ZeroInject;
            using System;
            namespace TestApp;
            public interface IHandle { }
            [Transient]
            public class Handle : IHandle, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() { IsDisposed = true; }
            }
            """;
        var (assembly, provider) = BuildAndCreateProvider(source);
        var handleType = assembly.GetType("TestApp.IHandle")!;
        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        var scope = scopeFactory.CreateScope();

        var instance = scope.ServiceProvider.GetService(handleType)!;
        var isDisposedProp = instance.GetType().GetProperty("IsDisposed")!;
        Assert.False((bool)isDisposedProp.GetValue(instance)!);

        await ((IAsyncDisposable)scope).DisposeAsync();

        Assert.True((bool)isDisposedProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // 13. Scoped service resolved from root returns null
    // ---------------------------------------------------------------
    [Fact]
    public void ScopedService_ResolvedFromRoot_ReturnsNull()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IRepo { }
            [Scoped]
            public class Repo : IRepo { }
            """;
        var (assembly, provider) = BuildAndCreateProvider(source);
        var repoType = assembly.GetType("TestApp.IRepo")!;

        // Scoped services are not in ResolveKnown (root), so should return null
        var result = provider.GetService(repoType);
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // 14. Scoped disposable services are disposed when scope is disposed
    // ---------------------------------------------------------------
    [Fact]
    public void ScopeDisposal_DisposesScopedDisposableServices()
    {
        const string source = """
            using ZeroInject;
            using System;
            namespace TestApp;
            public interface IRepo { }
            [Scoped]
            public class Repo : IRepo, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() { IsDisposed = true; }
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var repoType = assembly.GetType("TestApp.IRepo")!;
        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        var scope = scopeFactory.CreateScope();

        var instance = scope.ServiceProvider.GetService(repoType);
        Assert.NotNull(instance);

        var isDisposedProp = instance!.GetType().GetProperty("IsDisposed")!;
        Assert.False((bool)isDisposedProp.GetValue(instance)!);

        scope.Dispose();

        Assert.True((bool)isDisposedProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // 15. IKeyedServiceProvider resolvable via GetService
    // ---------------------------------------------------------------
    [Fact]
    public void IKeyedServiceProvider_ResolvableViaGetService()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton(Key = "redis")]
            public class RedisCache : ICache { }
            """;

        var (_, provider) = BuildAndCreateProvider(source);

        var keyedProvider = provider.GetService(typeof(IKeyedServiceProvider));

        Assert.NotNull(keyedProvider);
        Assert.Same(provider, keyedProvider);
    }

    // ---------------------------------------------------------------
    // 16. IKeyedServiceProvider resolvable in scope
    // ---------------------------------------------------------------
    [Fact]
    public void IKeyedServiceProvider_ResolvableInScope()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface ICache { }
            [Transient(Key = "fast")]
            public class FastCache : ICache { }
            """;

        var (_, provider) = BuildAndCreateProvider(source);
        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        using var scope = scopeFactory.CreateScope();

        var keyedProvider = scope.ServiceProvider.GetService(typeof(IKeyedServiceProvider));

        Assert.NotNull(keyedProvider);
        Assert.Same(scope.ServiceProvider, keyedProvider);
    }

    // ---------------------------------------------------------------
    // 17. GetService with multiple registrations returns last registered
    // ---------------------------------------------------------------
    [Fact]
    public void GetService_WithMultipleRegistrations_ReturnsLastRegistered()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IHandler { }
            [Transient(AllowMultiple = true)]
            public class HandlerA : IHandler { }
            [Transient(AllowMultiple = true)]
            public class HandlerB : IHandler { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var handlerType = assembly.GetType("TestApp.IHandler")!;

        var instance = provider.GetService(handlerType);
        Assert.NotNull(instance);
        // Last registered (HandlerB) should win
        Assert.Equal("HandlerB", instance!.GetType().Name);
    }

    // ---------------------------------------------------------------
    // 18. IEnumerable<T> returns all registered implementations
    // ---------------------------------------------------------------
    [Fact]
    public void IEnumerable_ReturnsAllRegistrations()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IHandler { }
            [Transient(AllowMultiple = true)]
            public class HandlerA : IHandler { }
            [Transient(AllowMultiple = true)]
            public class HandlerB : IHandler { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(assembly.GetType("TestApp.IHandler")!);

        var result = provider.GetService(enumerableType);
        Assert.NotNull(result);

        var array = ((System.Collections.IEnumerable)result!).Cast<object>().ToArray();
        Assert.Equal(2, array.Length);
    }

    // ---------------------------------------------------------------
    // 19. Singleton identity consistent between GetService and IEnumerable
    // ---------------------------------------------------------------
    [Fact]
    public void IEnumerable_SingletonIdentity_ConsistentWithGetService()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var cacheType = assembly.GetType("TestApp.ICache")!;
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(cacheType);

        var single = provider.GetService(cacheType);
        var enumerable = provider.GetService(enumerableType);
        var array = ((System.Collections.IEnumerable)enumerable!).Cast<object>().ToArray();

        Assert.Single(array);
        Assert.Same(single, array[0]);
    }

    // ---------------------------------------------------------------
    // 20. Scoped identity consistent between GetService and IEnumerable in scope
    // ---------------------------------------------------------------
    [Fact]
    public void IEnumerable_ScopedIdentity_ConsistentWithGetService()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IRepo { }
            [Scoped]
            public class Repo : IRepo { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var repoType = assembly.GetType("TestApp.IRepo")!;
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(repoType);
        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;

        using var scope = scopeFactory.CreateScope();
        var single = scope.ServiceProvider.GetService(repoType);
        var enumerable = scope.ServiceProvider.GetService(enumerableType);
        var array = ((System.Collections.IEnumerable)enumerable!).Cast<object>().ToArray();

        Assert.Single(array);
        Assert.Same(single, array[0]);
    }

    // ---------------------------------------------------------------
    // 21. IEnumerable at root excludes scoped services
    // ---------------------------------------------------------------
    [Fact]
    public void IEnumerable_AtRoot_ExcludesScopedServices()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IService { }
            [Scoped]
            public class ScopedOnly : IService { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(assembly.GetType("TestApp.IService")!);

        // At root, scoped-only IEnumerable should fall through to fallback
        var result = provider.GetService(enumerableType);
        if (result != null)
        {
            var array = ((System.Collections.IEnumerable)result).Cast<object>().ToArray();
            Assert.Empty(array);
        }
    }

    // ---------------------------------------------------------------
    // 22. IEnumerable in scope includes all lifetimes
    // ---------------------------------------------------------------
    [Fact]
    public void IEnumerable_InScope_IncludesAllLifetimes()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IHandler { }
            [Transient(AllowMultiple = true)]
            public class TransientHandler : IHandler { }
            [Singleton(AllowMultiple = true)]
            public class SingletonHandler : IHandler { }
            [Scoped(AllowMultiple = true)]
            public class ScopedHandler : IHandler { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var handlerType = assembly.GetType("TestApp.IHandler")!;
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);
        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;

        using var scope = scopeFactory.CreateScope();
        var result = scope.ServiceProvider.GetService(enumerableType);
        Assert.NotNull(result);

        var array = ((System.Collections.IEnumerable)result!).Cast<object>().ToArray();
        Assert.Equal(3, array.Length);
    }

    // ---------------------------------------------------------------
    // 23. IEnumerable with multiple singletons returns distinct instances
    // ---------------------------------------------------------------
    [Fact]
    public void IEnumerable_MultipleSingletons_ReturnsDistinctInstances()
    {
        const string source = """
            using ZeroInject;
            namespace TestApp;
            public interface IHandler { }
            [Singleton(AllowMultiple = true)]
            public class HandlerA : IHandler { }
            [Singleton(AllowMultiple = true)]
            public class HandlerB : IHandler { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var handlerType = assembly.GetType("TestApp.IHandler")!;
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);

        var result = provider.GetService(enumerableType);
        Assert.NotNull(result);

        var array = ((System.Collections.IEnumerable)result!).Cast<object>().ToArray();
        Assert.Equal(2, array.Length);
        Assert.NotSame(array[0], array[1]);
        Assert.Equal("HandlerA", array[0].GetType().Name);
        Assert.Equal("HandlerB", array[1].GetType().Name);
    }

    /// <summary>
    /// A simple marker type used to verify fallback resolution.
    /// Because this type is defined in the test assembly (not in the
    /// dynamically compiled source), it can only be resolved via the
    /// fallback <see cref="IServiceProvider"/>.
    /// </summary>
    public sealed class IntegrationFallbackMarker { }
}
