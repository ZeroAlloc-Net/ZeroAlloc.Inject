using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

internal static class GeneratorTestHelper
{
    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
    {
        return RunGeneratorCore(source, includeContainer: false);
    }

    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGeneratorWithContainer(string source)
    {
        return RunGeneratorCore(source, includeContainer: true);
    }

    private static (string output, ImmutableArray<Diagnostic> diagnostics) RunGeneratorCore(string source, bool includeContainer)
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
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.ZeroAllocInjectGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("ZeroAlloc.Inject"))
            .ToList();

        var output = string.Join("\n", generatedTrees.Select(t => t.GetText().ToString()));
        return (output, diagnostics);
    }
}
