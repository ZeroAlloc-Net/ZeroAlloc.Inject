using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace ZInject.Tests.ContainerTests;

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

        var genErrors = new List<Diagnostic>();
        foreach (var d in diagnostics) { if (d.Severity == DiagnosticSeverity.Error) genErrors.Add(d); }
        if (genErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Generator produced errors:\n" + string.Join("\n", genErrors.Select(e => e.ToString())));
        }

        // 2. Emit the compilation that already contains user + generated trees.
        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = new List<string>();
            foreach (var d in emitResult.Diagnostics)
                { if (d.Severity == DiagnosticSeverity.Error) errors.Add(d.ToString()); }
            throw new InvalidOperationException(
                "Compilation failed:\n" + string.Join("\n", errors));
        }

        ms.Seek(0, SeekOrigin.Begin);

        // Load into an isolated context so repeated test runs don't collide.
        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        // Locate the generated extension class and invoke BuildZInjectServiceProvider.
        var extensionClass = assembly.GetTypes()
            .First(static t => string.Equals(t.Name, "ZInjectServiceCollectionExtensions", StringComparison.Ordinal));
        var buildMethod = extensionClass.GetMethod(
            "BuildZInjectServiceProvider",
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
            typeof(TransientAttribute).Assembly,                                    // ZInject (attributes)
            typeof(ZInject.Container.ZInjectServiceProviderBase).Assembly,     // ZInject.Container
            typeof(ServiceCollectionContainerBuilderExtensions).Assembly,            // M.E.DI (BuildServiceProvider)
            typeof(ServiceCollection).Assembly,                                     // M.E.DI.Abstractions
        };
        var existingLocations = new HashSet<string>(
            references.Select(static r => r.Display ?? ""), StringComparer.Ordinal);
        foreach (var asm in extraAssemblies)
        {
            if (existingLocations.Add(asm.Location))
            {
                references.Add(MetadataReference.CreateFromFile(asm.Location));
            }
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.ZInjectGenerator();
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
    // 11. ZInjectServiceProviderFactory creates working provider
    // ---------------------------------------------------------------
    [Fact]
    public void ServiceProviderFactory_CreatesWorkingProvider()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;
        var (outputCompilation, diagnostics) = RunGeneratorAndGetCompilation(source);

        var genErrors = new List<Diagnostic>();
        foreach (var d in diagnostics) { if (d.Severity == DiagnosticSeverity.Error) genErrors.Add(d); }
        if (genErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Generator produced errors:\n" + string.Join("\n", genErrors.Select(e => e.ToString())));
        }

        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = new List<string>();
            foreach (var d in emitResult.Diagnostics)
                { if (d.Severity == DiagnosticSeverity.Error) errors.Add(d.ToString()); }
            throw new InvalidOperationException(
                "Compilation failed:\n" + string.Join("\n", errors));
        }

        ms.Seek(0, SeekOrigin.Begin);
        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        // Find the factory class and invoke it
        var factoryType = assembly.GetTypes().First(static t => string.Equals(t.Name, "ZInjectServiceProviderFactory", StringComparison.Ordinal));
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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

        Assert.Collection(array, item => Assert.Same(single, item));
    }

    // ---------------------------------------------------------------
    // 20. Scoped identity consistent between GetService and IEnumerable in scope
    // ---------------------------------------------------------------
    [Fact]
    public void IEnumerable_ScopedIdentity_ConsistentWithGetService()
    {
        const string source = """
            using ZInject;
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

        Assert.Collection(array, item => Assert.Same(single, item));
    }

    // ---------------------------------------------------------------
    // 21. IEnumerable at root excludes scoped services
    // ---------------------------------------------------------------
    [Fact]
    public void IEnumerable_AtRoot_ExcludesScopedServices()
    {
        const string source = """
            using ZInject;
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
            using ZInject;
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
            using ZInject;
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

    // =============================================================
    // Standalone provider helper + integration tests
    // =============================================================

    /// <summary>
    /// Runs the source generator, compiles, and instantiates the generated
    /// standalone provider (no fallback <see cref="IServiceCollection"/>).
    /// </summary>
    private static (Assembly assembly, IServiceProvider provider) BuildAndCreateStandaloneProvider(string source)
    {
        var (outputCompilation, diagnostics) = RunGeneratorAndGetCompilation(source);

        var genErrors = new List<Diagnostic>();
        foreach (var d in diagnostics) { if (d.Severity == DiagnosticSeverity.Error) genErrors.Add(d); }
        if (genErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Generator produced errors:\n" + string.Join("\n", genErrors.Select(e => e.ToString())));
        }

        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = new List<string>();
            foreach (var d in emitResult.Diagnostics)
                { if (d.Severity == DiagnosticSeverity.Error) errors.Add(d.ToString()); }
            throw new InvalidOperationException(
                "Compilation failed:\n" + string.Join("\n", errors));
        }

        ms.Seek(0, SeekOrigin.Begin);

        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        var providerType = assembly.GetTypes()
            .First(t => t.Name.EndsWith("StandaloneServiceProvider"));
        var provider = (IServiceProvider)Activator.CreateInstance(providerType)!;
        return (assembly, provider);
    }

    // ---------------------------------------------------------------
    // Standalone 1. Transient returns non-null, different instances
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_Transient_ReturnsNonNull_DifferentInstances()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;

        var instance1 = provider.GetService(fooType);
        var instance2 = provider.GetService(fooType);

        Assert.NotNull(instance1);
        Assert.NotNull(instance2);
        Assert.NotSame(instance1, instance2);
    }

    // ---------------------------------------------------------------
    // Standalone 2. Singleton returns same instance
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_Singleton_ReturnsSameInstance()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache { }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var cacheType = assembly.GetType("TestApp.ICache")!;

        var first = provider.GetService(cacheType);
        var second = provider.GetService(cacheType);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    // ---------------------------------------------------------------
    // Standalone 3. Scoped: same within scope, different across scopes
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_Scoped_SameWithinScope_DifferentAcrossScopes()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IRepo { }
            [Scoped]
            public class Repo : IRepo { }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var repoType = assembly.GetType("TestApp.IRepo")!;

        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;

        using var scope1 = scopeFactory.CreateScope();
        var a1 = scope1.ServiceProvider.GetService(repoType);
        var a2 = scope1.ServiceProvider.GetService(repoType);

        using var scope2 = scopeFactory.CreateScope();
        var b1 = scope2.ServiceProvider.GetService(repoType);

        Assert.NotNull(a1);
        Assert.Same(a1, a2);       // same within scope
        Assert.NotSame(a1, b1);    // different across scopes
    }

    // ---------------------------------------------------------------
    // Standalone 4. Unknown type returns null (no fallback)
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_UnknownType_ReturnsNull()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;

        var (_, provider) = BuildAndCreateStandaloneProvider(source);

        var result = provider.GetService(typeof(IntegrationFallbackMarker));
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Standalone 5. IServiceProvider and IServiceScopeFactory resolve to self
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_IServiceProvider_And_IServiceScopeFactory_ResolveToSelf()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { }
            [Transient]
            public class Foo : IFoo { }
            """;

        var (_, provider) = BuildAndCreateStandaloneProvider(source);

        var resolvedProvider = provider.GetService(typeof(IServiceProvider));
        var resolvedFactory = provider.GetService(typeof(IServiceScopeFactory));

        Assert.Same(provider, resolvedProvider);
        Assert.Same(provider, resolvedFactory);
    }

    // ---------------------------------------------------------------
    // Standalone 6. Scope disposal disposes tracked transients
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_ScopeDisposal_DisposesTrackedTransients()
    {
        const string source = """
            using ZInject;
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

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var handleType = assembly.GetType("TestApp.IHandle")!;

        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        var scope = scopeFactory.CreateScope();

        var instance = scope.ServiceProvider.GetService(handleType);
        Assert.NotNull(instance);

        var isDisposedProp = instance!.GetType().GetProperty("IsDisposed")!;
        Assert.False((bool)isDisposedProp.GetValue(instance)!);

        scope.Dispose();

        Assert.True((bool)isDisposedProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // Standalone 7. IEnumerable<T> returns all registrations
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_IEnumerable_ReturnsAllRegistrations()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IHandler { }
            [Transient(AllowMultiple = true)]
            public class HandlerA : IHandler { }
            [Transient(AllowMultiple = true)]
            public class HandlerB : IHandler { }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(assembly.GetType("TestApp.IHandler")!);

        var result = provider.GetService(enumerableType);
        Assert.NotNull(result);

        var array = ((System.Collections.IEnumerable)result!).Cast<object>().ToArray();
        Assert.Equal(2, array.Length);
    }

    // ---------------------------------------------------------------
    // Standalone 8. Singleton consistent between root and scope
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_Singleton_ConsistentBetweenRootAndScope()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache { }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var cacheType = assembly.GetType("TestApp.ICache")!;

        var rootInstance = provider.GetService(cacheType);

        var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;
        using var scope = scopeFactory.CreateScope();
        var scopeInstance = scope.ServiceProvider.GetService(cacheType);

        Assert.NotNull(rootInstance);
        Assert.Same(rootInstance, scopeInstance);
    }

    // ---------------------------------------------------------------
    // Standalone 9. Transient with dependency injects correctly
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_TransientWithDependency_InjectsCorrectly()
    {
        const string source = """
            using ZInject;
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

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var serviceType = assembly.GetType("TestApp.IService")!;

        var instance = provider.GetService(serviceType);

        Assert.NotNull(instance);
        var loggerProp = instance!.GetType().GetProperty("Logger")!;
        var loggerValue = loggerProp.GetValue(instance);
        Assert.NotNull(loggerValue);
    }

    // ---------------------------------------------------------------
    // Standalone 10. Provider disposal disposes singleton instances
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_ProviderDisposal_DisposesSingletons()
    {
        const string source = """
            using ZInject;
            using System;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public void Dispose() { IsDisposed = true; }
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var cacheType = assembly.GetType("TestApp.ICache")!;

        var instance = provider.GetService(cacheType);
        Assert.NotNull(instance);

        var isDisposedProp = instance!.GetType().GetProperty("IsDisposed")!;
        Assert.False((bool)isDisposedProp.GetValue(instance)!);

        ((IDisposable)provider).Dispose();

        Assert.True((bool)isDisposedProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // Standalone 11. Provider DisposeAsync disposes singleton instances
    // ---------------------------------------------------------------
    [Fact]
    public async Task Standalone_ProviderDisposeAsync_DisposesSingletons()
    {
        const string source = """
            using ZInject;
            using System;
            using System.Threading.Tasks;
            namespace TestApp;
            public interface ICache { }
            [Singleton]
            public class Cache : ICache, IAsyncDisposable
            {
                public bool IsDisposed { get; private set; }
                public ValueTask DisposeAsync() { IsDisposed = true; return default; }
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var cacheType = assembly.GetType("TestApp.ICache")!;

        var instance = provider.GetService(cacheType);
        Assert.NotNull(instance);

        var isDisposedProp = instance!.GetType().GetProperty("IsDisposed")!;
        Assert.False((bool)isDisposedProp.GetValue(instance)!);

        await ((IAsyncDisposable)provider).DisposeAsync();

        Assert.True((bool)isDisposedProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // Standalone 12. Open generic transient: new instance each call
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_OpenGeneric_Transient_ResolvesNewInstanceEachTime()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Transient]
            public class Repo<T> : IRepo<T> { }
            [Transient]
            public class Consumer
            {
                public Consumer(IRepo<string> repo) { }
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var repoOpenType = assembly.GetType("TestApp.IRepo`1")!;
        var repoType = repoOpenType.MakeGenericType(typeof(string));

        var a = provider.GetService(repoType);
        var b = provider.GetService(repoType);

        Assert.NotNull(a);
        Assert.NotSame(a, b);
    }

    // ---------------------------------------------------------------
    // Standalone 13. Open generic scoped: same within scope, different across scopes
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_OpenGeneric_Scoped_SameWithinScope_DifferentAcrossScopes()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Scoped]
            public class Repo<T> : IRepo<T> { }
            [Scoped]
            public class Consumer
            {
                public Consumer(IRepo<string> repo) { }
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var repoOpenType = assembly.GetType("TestApp.IRepo`1")!;
        var repoType = repoOpenType.MakeGenericType(typeof(string));

        var scopeFactory = (Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider
            .GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory))!;

        object? instanceScope1A, instanceScope1B, instanceScope2A;
        using (var scope1 = scopeFactory.CreateScope())
        {
            instanceScope1A = scope1.ServiceProvider.GetService(repoType);
            instanceScope1B = scope1.ServiceProvider.GetService(repoType);
        }

        using (var scope2 = scopeFactory.CreateScope())
        {
            instanceScope2A = scope2.ServiceProvider.GetService(repoType);
        }

        Assert.NotNull(instanceScope1A);
        Assert.Same(instanceScope1A, instanceScope1B);
        Assert.NotSame(instanceScope1A, instanceScope2A);
    }

    // ---------------------------------------------------------------
    // Standalone 14. Open generic singleton: same across root and scope
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_OpenGeneric_Singleton_ReturnsSameInstanceAcrossRootAndScope()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Singleton]
            public class Repo<T> : IRepo<T> { }
            [Singleton]
            public class Consumer
            {
                public Consumer(IRepo<int> repo) { }
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var repoOpenType = assembly.GetType("TestApp.IRepo`1")!;
        var repoType = repoOpenType.MakeGenericType(typeof(int));

        var fromRoot = provider.GetService(repoType);

        var scopeFactory = (Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider
            .GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory))!;

        object? fromScope;
        using (var scope = scopeFactory.CreateScope())
        {
            fromScope = scope.ServiceProvider.GetService(repoType);
        }

        Assert.NotNull(fromRoot);
        Assert.Same(fromRoot, fromScope);
    }

    // ---------------------------------------------------------------
    // Standalone 15. Open generic unknown closed type returns null
    // ---------------------------------------------------------------
    [Fact]
    public void Standalone_OpenGeneric_UnknownClosedType_ReturnsNull()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IRepo<T> { }
            [Transient]
            public class Repo<T> : IRepo<T> { }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);

        // IList<T> is not registered — should return null
        var result = provider.GetService(typeof(System.Collections.Generic.IList<string>));
        Assert.Null(result);
    }

    // ---------------------------------------------------------------
    // Open generic chained dependency (fixed-point AOT discovery)
    // ---------------------------------------------------------------
    [Fact]
    public void OpenGeneric_ChainedDependency_Standalone_ResolvesCorrectly()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IRepository<T> { string Name { get; } }
            public interface IContext<T> { string Tag { get; } }
            [Transient]
            public class Repository<T> : IRepository<T>
            {
                private readonly IContext<T> _ctx;
                public Repository(IContext<T> ctx) { _ctx = ctx; }
                public string Name => "repo:" + _ctx.Tag;
            }
            [Transient]
            public class Context<T> : IContext<T>
            {
                public string Tag => typeof(T).Name;
            }
            [Transient]
            public class OrderService
            {
                public IRepository<Order> Repo { get; }
                public OrderService(IRepository<Order> repo) { Repo = repo; }
            }
            public class Order { }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var svcType = assembly.GetType("TestApp.OrderService")!;
        var svc = provider.GetService(svcType)!;
        var repoProp = svcType.GetProperty("Repo")!;
        var repo = repoProp.GetValue(svc)!;
        var nameProp = repo.GetType().GetProperty("Name")!;
        Assert.Equal("repo:Order", (string)nameProp.GetValue(repo)!);
    }

    // ---------------------------------------------------------------
    // Open generic narrowing (As = typeof(IReadRepo<>))
    // ---------------------------------------------------------------
    [Fact]
    public void OpenGeneric_Narrowed_Standalone_ResolvesNarrowedInterface_NotOther()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IReadRepo<T> { string Read(); }
            public interface IWriteRepo<T> { }
            [Transient(As = typeof(IReadRepo<>))]
            public class Repo<T> : IReadRepo<T>, IWriteRepo<T>
            {
                public string Read() => typeof(T).Name;
            }
            [Transient]
            public class Consumer
            {
                public IReadRepo<string> Repo { get; }
                public Consumer(IReadRepo<string> repo) { Repo = repo; }
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var consumerType = assembly.GetType("TestApp.Consumer")!;
        var consumer = provider.GetService(consumerType)!;
        var repoProp = consumerType.GetProperty("Repo")!;
        var repo = repoProp.GetValue(consumer)!;
        var readMethod = repo.GetType().GetMethod("Read")!;
        Assert.Equal("String", (string)readMethod.Invoke(repo, null)!);

        // IWriteRepo<string> should not be registered (narrowed away)
        var writeRepoType = assembly.GetType("TestApp.IWriteRepo`1")!
            .MakeGenericType(typeof(string));
        Assert.Null(provider.GetService(writeRepoType));
    }

    // ---------------------------------------------------------------
    // Open generic + decorator via standalone container
    // ---------------------------------------------------------------
    [Fact]
    public void OpenGeneric_WithDecorator_Standalone_WrapsInnerWithDecorator()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IRepo<T> { string Tag { get; } }
            [Transient]
            public class Repo<T> : IRepo<T>
            {
                public string Tag => "impl:" + typeof(T).Name;
            }
            [Decorator]
            public class LoggingRepo<T> : IRepo<T>
            {
                private readonly IRepo<T> _inner;
                public LoggingRepo(IRepo<T> inner) { _inner = inner; }
                public string Tag => "logging:" + _inner.Tag;
            }
            [Transient]
            public class Consumer
            {
                public IRepo<string> Repo { get; }
                public Consumer(IRepo<string> repo) { Repo = repo; }
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var consumerType = assembly.GetType("TestApp.Consumer")!;
        var consumer = provider.GetService(consumerType)!;
        var repoProp = consumerType.GetProperty("Repo")!;
        var repo = repoProp.GetValue(consumer)!;
        var tagProp = repo.GetType().GetProperty("Tag")!;
        Assert.Equal("logging:impl:String", (string)tagProp.GetValue(repo)!);
    }

    // ---------------------------------------------------------------
    // Decorator 1. Non-generic decorator via hybrid container
    // ---------------------------------------------------------------
    [Fact]
    public void Decorator_NonGeneric_HybridContainer_ReturnsDecoratedInstance()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { string Tag { get; } }
            [Transient]
            public class FooImpl : IFoo { public string Tag => "impl"; }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                private readonly IFoo _inner;
                public LoggingFoo(IFoo inner) { _inner = inner; }
                public string Tag => "logging:" + _inner.Tag;
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;
        var tagProp = fooType.GetProperty("Tag")!;

        var instance = provider.GetService(fooType);

        Assert.NotNull(instance);
        Assert.Equal("logging:impl", (string)tagProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // Decorator 2. Non-generic decorator via standalone container
    // ---------------------------------------------------------------
    [Fact]
    public void Decorator_NonGeneric_Standalone_ReturnsDecoratedInstance()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { string Tag { get; } }
            [Transient]
            public class FooImpl : IFoo { public string Tag => "impl"; }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                private readonly IFoo _inner;
                public LoggingFoo(IFoo inner) { _inner = inner; }
                public string Tag => "logging:" + _inner.Tag;
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;
        var tagProp = fooType.GetProperty("Tag")!;

        var instance = provider.GetService(fooType);

        Assert.NotNull(instance);
        Assert.Equal("logging:impl", (string)tagProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // DecoratorOf 1. Non-generic [DecoratorOf] via hybrid container
    // ---------------------------------------------------------------
    [Fact]
    public void DecoratorOf_NonGeneric_HybridContainer_ReturnsDecoratedInstance()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { string Tag { get; } }
            [Transient]
            public class FooImpl : IFoo { public string Tag => "impl"; }
            [DecoratorOf(typeof(IFoo))]
            public class LoggingFoo : IFoo
            {
                private readonly IFoo _inner;
                public LoggingFoo(IFoo inner) { _inner = inner; }
                public string Tag => "logging:" + _inner.Tag;
            }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;
        var tagProp = fooType.GetProperty("Tag")!;

        var instance = provider.GetService(fooType);

        Assert.NotNull(instance);
        Assert.Equal("logging:impl", (string)tagProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // DecoratorOf 2. Non-generic [DecoratorOf] via standalone container
    // ---------------------------------------------------------------
    [Fact]
    public void DecoratorOf_NonGeneric_Standalone_ReturnsDecoratedInstance()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { string Tag { get; } }
            [Transient]
            public class FooImpl : IFoo { public string Tag => "impl"; }
            [DecoratorOf(typeof(IFoo))]
            public class LoggingFoo : IFoo
            {
                private readonly IFoo _inner;
                public LoggingFoo(IFoo inner) { _inner = inner; }
                public string Tag => "logging:" + _inner.Tag;
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;
        var tagProp = fooType.GetProperty("Tag")!;

        var instance = provider.GetService(fooType);

        Assert.NotNull(instance);
        Assert.Equal("logging:impl", (string)tagProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // Decorator 3. Non-generic decorator via MS DI extension method
    // ---------------------------------------------------------------
    [Fact]
    public void Decorator_NonGeneric_MsDiPath_ReturnsDecoratedInstance()
    {
        const string source = """
            using ZInject;
            namespace TestApp;
            public interface IFoo { string Tag { get; } }
            [Transient]
            public class FooImpl : IFoo { public string Tag => "impl"; }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                private readonly IFoo _inner;
                public LoggingFoo(IFoo inner) { _inner = inner; }
                public string Tag => "logging:" + _inner.Tag;
            }
            """;

        var (assembly, provider) = BuildAndCreateMsDiProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;
        var tagProp = fooType.GetProperty("Tag")!;

        var instance = provider.GetService(fooType);

        Assert.NotNull(instance);
        Assert.Equal("logging:impl", (string)tagProp.GetValue(instance)!);
    }

    // ---------------------------------------------------------------
    // Decorator 4. Scoped decorator is disposed with scope
    // ---------------------------------------------------------------
    [Fact]
    public void Decorator_Scoped_IsDisposed_WithScope()
    {
        const string source = """
            using ZInject;
            using System;
            namespace TestApp;
            public interface IFoo { }
            [Scoped]
            public class FooImpl : IFoo { }
            [Decorator]
            public class LoggingFoo : IFoo, IDisposable
            {
                public bool IsDisposed { get; private set; }
                public LoggingFoo(IFoo inner) { }
                public void Dispose() { IsDisposed = true; }
            }
            """;

        var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
        var fooType = assembly.GetType("TestApp.IFoo")!;

        var scopeFactory = (Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)provider
            .GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory))!;

        object? instance;
        using (var scope = scopeFactory.CreateScope())
        {
            instance = scope.ServiceProvider.GetService(fooType);
            Assert.NotNull(instance);
        }

        var isDisposedProp = instance!.GetType().GetProperty("IsDisposed")!;
        Assert.True((bool)isDisposedProp.GetValue(instance)!);
    }

    /// <summary>
    /// Builds an <see cref="IServiceProvider"/> using the generated MS DI extension method
    /// (<c>AddZInjectServices</c>) on top of a standard <see cref="ServiceCollection"/>.
    /// </summary>
    private static (Assembly assembly, IServiceProvider provider) BuildAndCreateMsDiProvider(string source)
    {
        var (outputCompilation, diagnostics) = RunGeneratorAndGetCompilation(source);

        var genErrors = new List<Diagnostic>();
        foreach (var d in diagnostics) { if (d.Severity == DiagnosticSeverity.Error) genErrors.Add(d); }
        if (genErrors.Count > 0)
        {
            throw new InvalidOperationException(
                "Generator produced errors:\n" + string.Join("\n", genErrors.Select(e => e.ToString())));
        }

        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = new List<string>();
            foreach (var d in emitResult.Diagnostics)
                { if (d.Severity == DiagnosticSeverity.Error) errors.Add(d.ToString()); }
            throw new InvalidOperationException(
                "Compilation failed:\n" + string.Join("\n", errors));
        }

        ms.Seek(0, SeekOrigin.Begin);
        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        // Invoke the generated Add*Services extension on a ServiceCollection.
        // The class name is dynamic (e.g. TestAssemblyServicesServiceCollectionExtensions),
        // so search all public static methods across all types.
        var addMethod = assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .First(m => m.Name.StartsWith("Add", StringComparison.Ordinal) && m.Name.EndsWith("Services", StringComparison.Ordinal));

        var services = new ServiceCollection();
        addMethod!.Invoke(null, [services]);
        var provider = services.BuildServiceProvider();

        return (assembly, provider);
    }

    /// <summary>
    /// A simple marker type used to verify fallback resolution.
    /// Because this type is defined in the test assembly (not in the
    /// dynamically compiled source), it can only be resolved via the
    /// fallback <see cref="IServiceProvider"/>.
    /// </summary>
    public sealed class IntegrationFallbackMarker { }
}
