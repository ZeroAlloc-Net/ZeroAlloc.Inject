using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZeroAlloc.Inject.Container;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

#pragma warning disable MA0048 // multiple types in one file — co-located test fixtures

public sealed class ZAOwnedSentinel { }
public sealed class FallbackOnlyType { }
public sealed class LateBoundType { }

internal sealed class TestProvider : ZeroAllocInjectServiceProviderBase
{
    public TestProvider(IServiceCollection fallbackServices) : base(fallbackServices) { }

    /// <summary>Exposes the internal lazy field for assertion. Returns null until the fallback has materialized.</summary>
    public IServiceProvider? FallbackOrNull
    {
        get
        {
            var fieldInfo = typeof(ZeroAllocInjectServiceProviderBase)
                .GetField("_fallback", BindingFlags.NonPublic | BindingFlags.Instance);
            return (IServiceProvider?)fieldInfo!.GetValue(this);
        }
    }

    protected override object? ResolveKnown(Type serviceType)
        => serviceType == typeof(ZAOwnedSentinel) ? new ZAOwnedSentinel() : null;

    protected override bool IsKnownService(Type serviceType) => serviceType == typeof(ZAOwnedSentinel);

    protected override bool IsKnownKeyedService(Type serviceType, object? serviceKey) => false;

    protected override ZeroAllocInjectScope CreateScopeCore(IServiceScopeFactory fallbackScopeFactory)
        => throw new NotImplementedException("CreateScope is not exercised by these tests.");
}

#pragma warning restore MA0048

public class LazyFallbackProviderTests
{
    private static IServiceCollection BuildFallbackOnlyCollection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<FallbackOnlyType>();
        return services;
    }

    [Fact]
    public void Build_DoesNotMaterializeFallbackProvider()
    {
        var services = BuildFallbackOnlyCollection();
        var provider = new TestProvider(services);
        Assert.Null(provider.FallbackOrNull);
    }

    [Fact]
    public void Resolve_ZAOwnedService_NeverMaterializesFallback()
    {
        var services = BuildFallbackOnlyCollection();
        var provider = new TestProvider(services);
        var resolved = provider.GetService(typeof(ZAOwnedSentinel));
        Assert.NotNull(resolved);
        Assert.Null(provider.FallbackOrNull);
    }

    [Fact]
    public void Resolve_FallbackOnlyService_MaterializesFallbackOnce()
    {
        var services = BuildFallbackOnlyCollection();
        var provider = new TestProvider(services);

        var first = provider.GetService(typeof(FallbackOnlyType));
        var fallbackAfterFirst = provider.FallbackOrNull;
        var second = provider.GetService(typeof(FallbackOnlyType));
        var fallbackAfterSecond = provider.FallbackOrNull;

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(fallbackAfterFirst);
        Assert.Same(fallbackAfterFirst, fallbackAfterSecond);
    }

    [Fact]
    public void IsService_BeforeAnyResolution_ReturnsFalseForFallbackOnlyType()
    {
        var services = BuildFallbackOnlyCollection();
        var provider = new TestProvider(services);
        // Documented behavioral change — fallback isn't materialized yet, so IsService
        // can only answer truthfully for ZA-known types.
        Assert.False(provider.IsService(typeof(FallbackOnlyType)));
    }

    [Fact]
    public void IsService_AfterResolution_ReturnsTrueForFallbackOnlyType()
    {
        var services = BuildFallbackOnlyCollection();
        var provider = new TestProvider(services);
        _ = provider.GetService(typeof(FallbackOnlyType));  // forces materialization
        Assert.True(provider.IsService(typeof(FallbackOnlyType)));
    }

    [Fact]
    public void BuildSnapshots_MutationOfTestSnapshotDoesNotLeak()
    {
        // The snapshot is the responsibility of the EXTENSION METHOD, not the base class.
        // Test the contract by constructing the provider with a snapshot the test controls,
        // then mutating the ORIGINAL collection separately to confirm isolation.
        var original = BuildFallbackOnlyCollection();
        IServiceCollection snapshot = new ServiceCollection();
        foreach (var d in original) snapshot.Add(d);
        var provider = new TestProvider(snapshot);

        // Mutate the original AFTER snapshot was taken — should not affect the provider.
        original.AddSingleton<LateBoundType>();

        // Force fallback materialization
        _ = provider.GetService(typeof(FallbackOnlyType));

        // LateBoundType is in `original` but NOT in `snapshot` — provider can't resolve it.
        Assert.Null(provider.GetService(typeof(LateBoundType)));
    }

    [Fact]
    public void Dispose_WithoutMaterialization_DoesNotThrow()
    {
        var services = BuildFallbackOnlyCollection();
        var provider = new TestProvider(services);

        var exception = Record.Exception(() => provider.Dispose());

        Assert.Null(exception);
        Assert.Null(provider.FallbackOrNull);
    }

    [Fact]
    public async Task DisposeAsync_WithoutMaterialization_DoesNotThrow()
    {
        var services = BuildFallbackOnlyCollection();
        var provider = new TestProvider(services);

        var exception = await Record.ExceptionAsync(async () => await provider.DisposeAsync());

        Assert.Null(exception);
        Assert.Null(provider.FallbackOrNull);
    }

    [Fact]
    public void ParallelFirstResolve_CollapsesToSingleCachedProvider()
    {
        var services = BuildFallbackOnlyCollection();
        var provider = new TestProvider(services);

        Parallel.For(0, 100, _ => provider.GetService(typeof(FallbackOnlyType)));

        // Whichever thread won the race, the cached _fallback should now be exactly one provider.
        var first = provider.FallbackOrNull;
        var second = provider.FallbackOrNull;
        Assert.NotNull(first);
        Assert.Same(first, second);
    }
}
