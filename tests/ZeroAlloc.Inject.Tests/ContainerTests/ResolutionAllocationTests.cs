using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject.Container;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// The steady-state resolution paths are a field read: a singleton after its first resolve, and a
/// scoped service after its first resolve in the same scope. Neither may allocate.
/// </summary>
public class ResolutionAllocationTests
{
    private const int Iterations = 1000;

    private const string Source = """
        using ZeroAlloc.Inject;
        namespace TestApp;
        public interface ICache { }
        [Singleton]
        public class Cache : ICache { }
        public interface IDbContext { }
        [Scoped]
        public class DbContext : IDbContext { }
        """;

    private static readonly GeneratedApp App = GeneratedApp.Compile(Source);

    [Fact]
    public void Hybrid_Singleton_SteadyState_IsZeroAlloc()
    {
        var provider = App.BuildHybrid();
        var cache = App.Type("ICache");
        Assert.NotNull(provider.GetService(cache));

        AllocationGate.AssertBudget(0, Iterations, () => provider.GetService(cache), "hybrid singleton");
    }

    [Fact]
    public void Hybrid_CachedScoped_IsZeroAlloc()
    {
        var provider = App.BuildHybrid();
        var db = App.Type("IDbContext");
        using var scope = ((IServiceScopeFactory)provider).CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService(db));

        AllocationGate.AssertBudget(0, Iterations, () => scope.ServiceProvider.GetService(db), "hybrid cached scoped");
    }

    [Fact]
    public void Standalone_Singleton_SteadyState_IsZeroAlloc()
    {
        var provider = App.BuildStandalone();
        var cache = App.Type("ICache");
        Assert.NotNull(provider.GetService(cache));

        AllocationGate.AssertBudget(0, Iterations, () => provider.GetService(cache), "standalone singleton");
    }

    [Fact]
    public void Standalone_CachedScoped_IsZeroAlloc()
    {
        var provider = App.BuildStandalone();
        var db = App.Type("IDbContext");
        using var scope = ((IServiceScopeFactory)provider).CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService(db));

        AllocationGate.AssertBudget(0, Iterations, () => scope.ServiceProvider.GetService(db), "standalone cached scoped");
    }

    [Fact]
    public void CreateScope_WhenEveryRegistrationIsGenerated_DoesNotBuildFallback()
    {
        var provider = App.BuildHybrid();

        using var scope = ((IServiceScopeFactory)provider).CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetService(App.Type("IDbContext")));
        Assert.NotNull(scope.ServiceProvider.GetService(App.Type("ICache")));

        Assert.Null(FallbackOf(provider));
    }

    [Fact]
    public void CreateScope_WhenEveryRegistrationIsGenerated_StillFallsBackOnDemand()
    {
        var provider = App.BuildHybrid();

        using var scope = ((IServiceScopeFactory)provider).CreateScope();
        // Nothing registers it, so Microsoft DI answers null; asking builds the fallback lazily.
        Assert.Null(scope.ServiceProvider.GetService(typeof(Uri)));

        Assert.NotNull(FallbackOf(provider));
    }

    [Fact]
    public void CreateScope_WithAFallbackOnlyRegistration_BuildsFallback()
    {
        // An application registration the generated container does not know keeps the fallback
        // eager at CreateScope, so IsService keeps answering for it once a scope exists.
        var provider = App.BuildHybrid(after: s => s.AddSingleton<FallbackOnlyService>());

        using var scope = ((IServiceScopeFactory)provider).CreateScope();

        Assert.NotNull(FallbackOf(provider));
        Assert.True(((IServiceProviderIsService)provider).IsService(typeof(FallbackOnlyService)));
    }

    [Fact]
    public void CreateScope_AfterDispose_FallbackResolutionThrows()
    {
        var provider = App.BuildHybrid();
        var scope = ((IServiceScopeFactory)provider).CreateScope();
        ((IDisposable)provider).Dispose();

        Assert.Throws<ObjectDisposedException>(() => scope.ServiceProvider.GetService(typeof(Uri)));
        Assert.Null(FallbackOf(provider));
    }

    private static object? FallbackOf(IServiceProvider provider) =>
        typeof(ZeroAllocInjectServiceProviderBase)
            .GetField("_fallback", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(provider);

    public sealed class FallbackOnlyService { }
}
