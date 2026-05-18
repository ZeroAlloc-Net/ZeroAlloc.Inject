using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// Locks in the generator's IEnumerable&lt;T&gt; caching behavior:
/// <list type="bullet">
///   <item>All-singleton groups: cache the resolved array as an instance field;
///         repeated resolutions return the SAME reference.</item>
///   <item>Mixed-lifetime groups: keep the existing per-call <c>new T[] { ... }</c>
///         emit; arrays differ across resolutions.</item>
/// </list>
/// </summary>
public class EnumerableCacheTests
{
    // ---------------------------------------------------------------
    // Helper: same source-build pattern as IntegrationTests.
    // Helper is copied (option B) rather than shared via a refactor
    // because lifting it cleanly would touch both files and exceed
    // the 30-line refactor budget the plan calls out.
    // ---------------------------------------------------------------
    private static (Assembly assembly, IServiceProvider provider) BuildAndCreateProvider(
        string source,
        Action<IServiceCollection>? configureServices = null)
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

        var extensionClass = assembly.GetTypes()
            .First(static t => string.Equals(t.Name, "ZeroAllocInjectServiceCollectionExtensions", StringComparison.Ordinal));
        var buildMethod = extensionClass.GetMethod(
            "BuildZeroAllocInjectServiceProvider",
            BindingFlags.Public | BindingFlags.Static);

        var services = new ServiceCollection();
        configureServices?.Invoke(services);

        var provider = (IServiceProvider)buildMethod!.Invoke(null, [services])!;
        return (assembly, provider);
    }

    private static (Compilation outputCompilation, ImmutableArray<Diagnostic> diagnostics) RunGeneratorAndGetCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        var extraAssemblies = new[]
        {
            typeof(TransientAttribute).Assembly,
            typeof(ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase).Assembly,
            typeof(ServiceCollectionContainerBuilderExtensions).Assembly,
            typeof(ServiceCollection).Assembly,
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

        var generator = new Generator.ZeroAllocInjectGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation, diagnostics);
    }

    // ---------------------------------------------------------------
    // 1. All-singleton IEnumerable<T> returns the SAME cached array reference.
    // ---------------------------------------------------------------
    [Fact]
    public void Enumerable_AllSingleton_ReturnsSameArrayInstance()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IPlugin { string Name { get; } }
            [Singleton(AllowMultiple = true)]
            public class PluginA : IPlugin { public string Name => "A"; }
            [Singleton(AllowMultiple = true)]
            public class PluginB : IPlugin { public string Name => "B"; }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var pluginType = assembly.GetType("TestApp.IPlugin")!;
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(pluginType);

        var first = provider.GetService(enumerableType);
        var second = provider.GetService(enumerableType);

        Assert.NotNull(first);
        Assert.NotNull(second);
        // The cached array is the SAME reference across resolutions for all-singleton groups.
        Assert.Same(first, second);
    }

    // ---------------------------------------------------------------
    // 2. All-singleton IEnumerable<T> contains every registered singleton.
    // ---------------------------------------------------------------
    [Fact]
    public void Enumerable_AllSingleton_ContainsAllRegisteredSingletons()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IPlugin { string Name { get; } }
            [Singleton(AllowMultiple = true)]
            public class PluginA : IPlugin { public string Name => "A"; }
            [Singleton(AllowMultiple = true)]
            public class PluginB : IPlugin { public string Name => "B"; }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var pluginType = assembly.GetType("TestApp.IPlugin")!;
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(pluginType);

        var arr = (System.Collections.IEnumerable)provider.GetService(enumerableType)!;
        var names = new List<string>();
        foreach (var p in arr)
        {
            var name = (string)pluginType.GetProperty("Name")!.GetValue(p)!;
            names.Add(name);
        }

        Assert.Equal(2, names.Count);
        Assert.Contains("A", names);
        Assert.Contains("B", names);
    }

    // ---------------------------------------------------------------
    // 3. Mixed-lifetime IEnumerable<T> keeps the per-call emit:
    //    each resolution returns a FRESH array. Regression guard.
    // ---------------------------------------------------------------
    [Fact]
    public void Enumerable_MixedLifetime_ReturnsFreshArrayEachCall()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IMixedSvc { }
            [Singleton(AllowMultiple = true)]
            public class MixedSingleton : IMixedSvc { }
            [Transient(AllowMultiple = true)]
            public class MixedTransient : IMixedSvc { }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var svcType = assembly.GetType("TestApp.IMixedSvc")!;
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(svcType);

        var first = provider.GetService(enumerableType);
        var second = provider.GetService(enumerableType);

        Assert.NotNull(first);
        Assert.NotNull(second);
        // Mixed-lifetime stays on per-call emit -- arrays differ between resolutions.
        Assert.NotSame(first, second);
    }

    // ---------------------------------------------------------------
    // 4. Cached all-singleton array is safe under concurrent resolution
    //    and always contains the expected singleton instances.
    // ---------------------------------------------------------------
    [Fact]
    public void Enumerable_AllSingleton_ParallelResolve_AlwaysContainsCachedSingletons()
    {
        const string source = """
            using ZeroAlloc.Inject;
            namespace TestApp;
            public interface IPlugin { string Name { get; } }
            [Singleton(AllowMultiple = true)]
            public class PluginA : IPlugin { public string Name => "A"; }
            [Singleton(AllowMultiple = true)]
            public class PluginB : IPlugin { public string Name => "B"; }
            """;

        var (assembly, provider) = BuildAndCreateProvider(source);
        var pluginType = assembly.GetType("TestApp.IPlugin")!;
        var pluginAType = assembly.GetType("TestApp.PluginA")!;
        var pluginBType = assembly.GetType("TestApp.PluginB")!;
        var enumerableType = typeof(IEnumerable<>).MakeGenericType(pluginType);

        var expectedA = provider.GetService(pluginAType);
        var expectedB = provider.GetService(pluginBType);

        System.Threading.Tasks.Parallel.For(0, 1000, _ =>
        {
            var arr = (System.Collections.IEnumerable)provider.GetService(enumerableType)!;
            var list = new List<object>();
            foreach (var p in arr) list.Add(p);
            Assert.Equal(2, list.Count);
            Assert.Contains(expectedA, list);
            Assert.Contains(expectedB, list);
        });
    }
}
