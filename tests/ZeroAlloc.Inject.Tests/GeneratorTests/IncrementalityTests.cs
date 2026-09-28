using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

/// <summary>
/// The generator's cached models carry source locations, so an edit that does not touch a service
/// must leave every step and output cached, and an edit that moves a service must move its diagnostic.
/// </summary>
public class IncrementalityTests
{
    private const string ServicesSource = """
        using ZeroAlloc.Inject;
        namespace TestApp;

        public interface IFoo { }
        public interface IRepository<T> { }

        [Transient]
        public class Foo : IFoo { }

        [Scoped]
        public class Repository<T> : IRepository<T> { }

        [Singleton]
        public class Consumer : IFoo
        {
            public Consumer(IRepository<string> repo) { }
        }

        [Decorator]
        public class LoggingFoo : IFoo
        {
            public LoggingFoo(IFoo inner) { }
        }

        [Transient]
        public class PlainService { }
        """;

    // The tracking names the generator gives its steps, in ZeroAlloc.Inject.Generator.TrackingNames.
    private static readonly string[] GeneratorStepNames =
    [
        "Transients", "Scopeds", "Singletons", "Decorators", "DecoratorOfs", "AllDecorators",
        "ClosedGenericUsages", "Inputs", "Diagnostics", "ReportedDiagnostics",
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedEdit_LeavesEveryTrackedStepAndOutputCached(bool includeContainer)
    {
        var services = CSharpSyntaxTree.ParseText(ServicesSource, path: "/src/Services.cs");
        var unrelated = CSharpSyntaxTree.ParseText(
            "namespace TestApp; public class Unrelated { public int M() => 1; }", path: "/src/Unrelated.cs");
        var compilation = GeneratorTestHelper.CreateCompilation([services, unrelated], includeContainer);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new Generator.ZeroAllocInjectGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult().Results[0];

        var edited = compilation.ReplaceSyntaxTree(
            unrelated,
            unrelated.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(
                "namespace TestApp; public class Unrelated { public int M() => 2; public int N() => 3; }")));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results[0];

        // Roslyn's own steps, such as the one that pairs each tree with the compilation, rerun on
        // every edit; the generator's named steps must not.
        foreach (var name in GeneratorStepNames)
        {
            Assert.True(second.TrackedSteps.TryGetValue(name, out var runSteps), $"Step '{name}' was not tracked.");
            AssertAllCachedOrUnchanged(name, runSteps);
        }

        Assert.NotEmpty(second.TrackedOutputSteps);
        foreach (var step in second.TrackedOutputSteps)
        {
            AssertAllCachedOrUnchanged(step.Key, step.Value);
        }

        // A cached output still reports its diagnostics, ZAI007 for PlainService, at the same place.
        Assert.Equal(Describe(first.Diagnostics), Describe(second.Diagnostics));
        Assert.Contains(second.Diagnostics, d => string.Equals(d.Id, "ZAI007", StringComparison.Ordinal));
    }

    [Fact]
    public void EditAboveAService_MovesItsDiagnostic()
    {
        var services = CSharpSyntaxTree.ParseText(ServicesSource, path: "/src/Services.cs");
        var compilation = GeneratorTestHelper.CreateCompilation([services]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new Generator.ZeroAllocInjectGenerator());
        driver = driver.RunGenerators(compilation);
        var before = AssertEx.One(
            driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZAI007", StringComparison.Ordinal));

        var moved = services.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(
            ServicesSource.Replace("namespace TestApp;", "namespace TestApp;\n\n// two\n// more lines", StringComparison.Ordinal)));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(services, moved));
        var after = AssertEx.One(
            driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZAI007", StringComparison.Ordinal));

        Assert.Equal(
            before.Location.GetLineSpan().StartLinePosition.Line + 3,
            after.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Same(moved, after.Location.SourceTree);
    }

    private static void AssertAllCachedOrUnchanged(string stepName, ImmutableArray<IncrementalGeneratorRunStep> runSteps)
    {
        foreach (var runStep in runSteps)
        {
            foreach (var (_, reason) in runStep.Outputs)
            {
                Assert.True(
                    reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged,
                    $"Step '{stepName}' produced output with reason {reason} after an unrelated edit.");
            }
        }
    }

    private static List<string> Describe(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Select(d => $"{d.Id} {d.Location.GetLineSpan()}").OrderBy(s => s, StringComparer.Ordinal).ToList();
}
