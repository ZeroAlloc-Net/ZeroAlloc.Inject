using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

internal static class GeneratorTestHelper
{
    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
    {
        return RunGeneratorCore(source, includeContainer: false, globalOptions: null, assemblyName: "TestAssembly");
    }

    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGeneratorWithContainer(string source)
    {
        return RunGeneratorCore(source, includeContainer: true, globalOptions: null, assemblyName: "TestAssembly");
    }

    /// <summary>
    /// Runs the generator with a "build_property.ZeroAllocGeneratedAccessibility" global MSBuild
    /// property set, mirroring how the CompilerVisibleProperty shipped in build/*.props surfaces
    /// the value to the generator's AnalyzerConfigOptionsProvider in a real consumer build.
    /// </summary>
    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGeneratorWithAccessibility(
        string source, string accessibilityValue, bool includeContainer = false, string assemblyName = "TestAssembly")
    {
        var globalOptions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["build_property.ZeroAllocGeneratedAccessibility"] = accessibilityValue,
        };
        return RunGeneratorCore(source, includeContainer, globalOptions, assemblyName);
    }

    private static (string output, ImmutableArray<Diagnostic> diagnostics) RunGeneratorCore(
        string source, bool includeContainer, IReadOnlyDictionary<string, string>? globalOptions, string assemblyName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var containerAssemblyName = typeof(ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase).Assembly.GetName().Name;

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Where(a => includeContainer || !string.Equals(a.GetName().Name, containerAssemblyName, StringComparison.Ordinal))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(TransientAttribute).Assembly.Location));

        if (includeContainer)
        {
            // Ensure it's present even if not yet loaded in AppDomain
            var containerLocation = typeof(ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase).Assembly.Location;
            if (!references.Any(r => string.Equals(r.Display, containerLocation, StringComparison.Ordinal)))
            {
                references.Add(MetadataReference.CreateFromFile(containerLocation));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.ZeroAllocInjectGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);

        if (globalOptions != null)
        {
            driver = driver.WithUpdatedAnalyzerConfigOptions(new TestAnalyzerConfigOptionsProvider(globalOptions));
        }

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("ZeroAlloc.Inject"))
            .ToList();

        var output = string.Join("\n", generatedTrees.Select(t => t.GetText().ToString()));
        return (output, diagnostics);
    }

    /// <summary>
    /// Minimal <see cref="AnalyzerConfigOptionsProvider"/> that exposes a fixed set of "global"
    /// MSBuild properties (i.e. those declared via CompilerVisibleProperty), matching how the real
    /// SDK surfaces build_property.* values to source generators.
    /// </summary>
    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestAnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(IReadOnlyDictionary<string, string> globalOptions)
        {
            _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _globalOptions;
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestAnalyzerConfigOptions(IReadOnlyDictionary<string, string> values) => _values = values;

        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}
