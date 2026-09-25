using System;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Inject.Tests.GeneratorTests;

public class GeneratedAccessibilityTests
{
    // Mirrors the issue's probe: a library assembly with only an internal [Singleton] class,
    // whose only intended public API is a hand-written wrapper that calls the generated method
    // from inside the assembly.
    private const string JevNetProbeSource = """
        using ZeroAlloc.Inject;
        namespace Jev.Net.Extensions.DependencyInjection;

        internal interface IJevClient { }

        [Singleton]
        internal class JevClient : IJevClient { }
        """;

    [Fact]
    public void Unset_GeneratesPublicExtensionClassAndMethod()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(JevNetProbeSource);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("public static class", output);
        Assert.Contains("public static IServiceCollection Add", output);
        Assert.DoesNotContain("internal static class", output);
    }

    [Fact]
    public void ExplicitPublic_IsByteIdenticalToUnset()
    {
        var (unsetOutput, _) = GeneratorTestHelper.RunGenerator(JevNetProbeSource);
        var (publicOutput, diagnostics) = GeneratorTestHelper.RunGeneratorWithAccessibility(JevNetProbeSource, "Public");

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Equal(unsetOutput, publicOutput);
    }

    [Fact]
    public void CaseInsensitive_Public_IsAccepted()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithAccessibility(JevNetProbeSource, "pUbLiC");

        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI020", StringComparison.Ordinal));
        Assert.Contains("public static class", output);
    }

    [Fact]
    public void Internal_GeneratesInternalExtensionClassAndMethod()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithAccessibility(
            JevNetProbeSource, "Internal", assemblyName: "Jev.Net.Extensions.DependencyInjection");

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("internal static class JevNetExtensionsDependencyInjectionServicesServiceCollectionExtensions", output);
        Assert.Contains("internal static IServiceCollection AddJevNetExtensionsDependencyInjectionServices(this IServiceCollection services)", output);
        Assert.DoesNotContain("public static class", output);
        Assert.DoesNotContain("public static IServiceCollection Add", output);
    }

    [Fact]
    public void CaseInsensitive_Internal_IsAccepted()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithAccessibility(JevNetProbeSource, "iNtErNaL");

        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI020", StringComparison.Ordinal));
        Assert.Contains("internal static class", output);
    }

    [Fact]
    public void Internal_HybridContainer_GeneratesInternalExtensionAndFactory()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithAccessibility(
            JevNetProbeSource, "Internal", includeContainer: true, assemblyName: "Jev.Net.Extensions.DependencyInjection");

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);

        // MS DI extension mode
        Assert.Contains("internal static class JevNetExtensionsDependencyInjectionServicesServiceCollectionExtensions", output);

        // Hybrid mode's extension + factory in Microsoft.Extensions.DependencyInjection
        Assert.Contains("internal static class ZeroAllocInjectServiceCollectionExtensions", output);
        Assert.Contains("internal static IServiceProvider BuildZeroAllocInjectServiceProvider(this IServiceCollection services)", output);
        Assert.Contains("internal sealed class ZeroAllocInjectServiceProviderFactory : IServiceProviderFactory<IServiceCollection>", output);

        // The interface implementation members stay public — implicit interface implementation
        // requires it — even though the containing factory class is internal.
        Assert.Contains("public IServiceCollection CreateBuilder(IServiceCollection services)", output);
        Assert.Contains("public IServiceProvider CreateServiceProvider(IServiceCollection containerBuilder)", output);

        // The standalone/hybrid resolver class was already internal before this feature and stays that way.
        Assert.Contains("internal sealed class JevNetExtensionsDependencyInjectionServiceProvider", output);
    }

    [Fact]
    public void InvalidValue_ReportsZAI020AndFallsBackToPublic()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithAccessibility(JevNetProbeSource, "Protected");

        Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZAI020", StringComparison.Ordinal));
        Assert.Equal(1, diagnostics.AsEnumerable().Count(static d => string.Equals(d.Id, "ZAI020", StringComparison.Ordinal)));

        var diagnostic = diagnostics.AsEnumerable().First(static d => string.Equals(d.Id, "ZAI020", StringComparison.Ordinal));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("Protected", diagnostic.GetMessage());
        Assert.Contains("ZeroAllocGeneratedAccessibility", diagnostic.GetMessage());

        // Falls back to the safe default (Public) rather than emitting broken or unexpectedly-internal code.
        Assert.Contains("public static class", output);
    }

    [Fact]
    public void EmptyValue_IsTreatedAsPublic_NoDiagnostic()
    {
        var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithAccessibility(JevNetProbeSource, "");

        Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZAI020", StringComparison.Ordinal));
        Assert.Contains("public static class", output);
    }
}
