using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// A test source compiled together with the generator's output and loaded into its own context,
/// with the three ways to use it: the generated <c>Add...Services</c> extension on plain Microsoft DI,
/// the hybrid container, and the standalone container.
/// </summary>
internal sealed class GeneratedApp
{
    private const string DynamicCodeCheck = "global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported";

    private GeneratedApp(Assembly assembly) => Assembly = assembly;

    public Assembly Assembly { get; }

    /// <summary>
    /// Compiles <paramref name="source"/> with the generator's output. With
    /// <paramref name="withoutDynamicCode"/>, every check of
    /// <c>RuntimeFeature.IsDynamicCodeSupported</c> in the generated code reads <c>false</c>, which is
    /// what it reads in a NativeAOT binary. The test process itself keeps dynamic code, so Microsoft DI
    /// still closes open generics over value types here; the AOT smoke covers the refusal itself.
    /// </summary>
    public static GeneratedApp Compile(string source, bool withoutDynamicCode = false)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => (MetadataReference)MetadataReference.CreateFromFile(a.Location))
            .ToList();
        var locations = new HashSet<string>(references.Select(r => r.Display ?? ""), StringComparer.Ordinal);
        foreach (var assembly in new[]
        {
            typeof(TransientAttribute).Assembly,
            typeof(ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase).Assembly,
            typeof(ServiceCollectionContainerBuilderExtensions).Assembly,
            typeof(ServiceCollection).Assembly,
        })
        {
            if (locations.Add(assembly.Location))
                references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.ZeroAllocInjectGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var generatorErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (generatorErrors.Count > 0)
            throw new InvalidOperationException("Generator produced errors:\n" + string.Join("\n", generatorErrors.Select(d => d.ToString())));

        if (withoutDynamicCode)
        {
            var parseOptions = (CSharpParseOptions)compilation.SyntaxTrees[0].Options;
            var generated = driver.GetRunResult().GeneratedTrees;
            var rewritten = generated.Select(tree => CSharpSyntaxTree.ParseText(
                tree.GetText().ToString().Replace(DynamicCodeCheck, "false", StringComparison.Ordinal),
                parseOptions,
                tree.FilePath));
            output = compilation.AddSyntaxTrees(rewritten);
        }

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException("Compilation failed:\n" + string.Join(
                "\n", emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())));
        }

        ThrowOnGeneratedWarnings(compilation, emit);

        stream.Seek(0, SeekOrigin.Begin);
        var context = new AssemblyLoadContext(null, isCollectible: true);
        return new GeneratedApp(context.LoadFromStream(stream));
    }

    /// <summary>
    /// Throws when the generated code has warnings, which a consumer that builds with warnings as
    /// errors could not compile.
    /// </summary>
    private static void ThrowOnGeneratedWarnings(Compilation source, Microsoft.CodeAnalysis.Emit.EmitResult emit)
    {
        var generatedWarnings = emit.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning
                && d.Location.SourceTree is { } tree
                && !source.SyntaxTrees.Contains(tree))
            .ToList();
        if (generatedWarnings.Count > 0)
        {
            throw new InvalidOperationException("The generated code has warnings:\n" + string.Join("\n", generatedWarnings.Select(d => d.ToString())));
        }
    }

    /// <summary>The source's type <paramref name="name"/>, closed over <paramref name="typeArguments"/> when given.</summary>
    public Type Type(string name, params Type[] typeArguments)
    {
        var type = Assembly.GetType("TestApp." + name + (typeArguments.Length > 0 ? $"`{typeArguments.Length}" : ""), throwOnError: true)!;
        return typeArguments.Length > 0 ? type.MakeGenericType(typeArguments) : type;
    }

    /// <summary>Runs the generated <c>Add...Services</c> extension on <paramref name="services"/>.</summary>
    public IServiceCollection AddServices(IServiceCollection services)
    {
        var add = Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .First(m => m.Name.StartsWith("Add", StringComparison.Ordinal)
                && m.Name.EndsWith("Services", StringComparison.Ordinal));
        add.Invoke(null, [services]);
        return services;
    }

    /// <summary>Plain Microsoft DI over the generated registrations.</summary>
    public ServiceProvider BuildMicrosoftDi(Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        before?.Invoke(services);
        return AddServices(services).BuildServiceProvider();
    }

    /// <summary>
    /// The hybrid container. With <paramref name="addServices"/>, its Microsoft DI fallback holds the
    /// generated registrations, as in an application; without, only the generated type switch can
    /// resolve the source's services.
    /// </summary>
    public IServiceProvider BuildHybrid(bool addServices = true)
    {
        var services = new ServiceCollection();
        if (addServices) AddServices(services);
        var build = Assembly.GetTypes()
            .First(t => string.Equals(t.Name, "ZeroAllocInjectServiceCollectionExtensions", StringComparison.Ordinal))
            .GetMethod("BuildZeroAllocInjectServiceProvider", BindingFlags.Public | BindingFlags.Static)!;
        return (IServiceProvider)build.Invoke(null, [services])!;
    }

    /// <summary>The standalone container.</summary>
    public IServiceProvider BuildStandalone()
    {
        var type = Assembly.GetTypes().First(t => t.Name.EndsWith("StandaloneServiceProvider", StringComparison.Ordinal));
        return (IServiceProvider)Activator.CreateInstance(type)!;
    }
}
