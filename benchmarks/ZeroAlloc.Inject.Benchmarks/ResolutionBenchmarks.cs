using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class ResolutionBenchmarks
{
    private ServiceProvider _msDiProvider = null!;
    private IServiceProvider _containerProvider = null!;
    private IServiceProvider _standaloneProvider = null!;
    private JabContainer _jabProvider = null!;

    [GlobalSetup]
    public void Setup()
    {
        // MS DI (reflection-based)
        var msDiServices = new ServiceCollection();
        msDiServices.AddTransient<ISimpleService, SimpleService>();
        msDiServices.AddTransient<IServiceWithDep, ServiceWithDep>();
        msDiServices.AddTransient<IServiceWithMultipleDeps, ServiceWithMultipleDeps>();
        msDiServices.AddSingleton<ISingletonService, SingletonService>();
        msDiServices.AddScoped<IScopedService, ScopedService>();
        msDiServices.AddTransient<IMultiService, MultiServiceA>();
        msDiServices.AddTransient<IMultiService, MultiServiceB>();
        msDiServices.AddTransient<IMultiService, MultiServiceC>();
        msDiServices.AddTransient<IServiceWithPropertyDep>(sp =>
        {
            var svc = new ServiceWithPropertyDep();
            svc.Simple = sp.GetRequiredService<ISimpleService>();
            return svc;
        });
        msDiServices.AddTransient<DecoratedServiceImpl>();
        msDiServices.AddTransient<IDecoratedService>(sp => new LoggingDecoratedService(sp.GetRequiredService<DecoratedServiceImpl>()));
        msDiServices.AddTransient(typeof(IGenericRepo<>), typeof(GenericRepo<>));
        _msDiProvider = msDiServices.BuildServiceProvider();

        // ZeroAlloc.Inject Container (generated container)
        var containerServices = new ServiceCollection();
        containerServices.AddZeroAllocInjectBenchmarksServices();
        _containerProvider = containerServices.BuildZeroAllocInjectServiceProvider();

        // ZeroAlloc.Inject Standalone provider
        _standaloneProvider = new ZeroAlloc.Inject.Generated.ZeroAllocInjectBenchmarksStandaloneServiceProvider();

        // Jab (source-gen competitor)
        _jabProvider = new JabContainer();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _msDiProvider.Dispose();
        (_containerProvider as IDisposable)?.Dispose();
        (_standaloneProvider as IDisposable)?.Dispose();
        _jabProvider.Dispose();
    }

    // --- Transient resolution (no dependencies) ---

    [Benchmark(Baseline = true, Description = "MS DI: Resolve transient (no deps)")]
    [BenchmarkCategory("Transient")]
    public ISimpleService MsDi_ResolveTransient()
        => _msDiProvider.GetRequiredService<ISimpleService>();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve transient (no deps)")]
    [BenchmarkCategory("Transient")]
    public ISimpleService Container_ResolveTransient()
        => _containerProvider.GetRequiredService<ISimpleService>();

    [Benchmark(Description = "Standalone: Resolve transient (no deps)")]
    [BenchmarkCategory("Transient")]
    public ISimpleService Standalone_ResolveTransient()
        => _standaloneProvider.GetRequiredService<ISimpleService>();

    [Benchmark(Description = "Jab: Resolve transient (no deps)")]
    [BenchmarkCategory("Transient")]
    public ISimpleService Jab_ResolveTransient()
        => _jabProvider.GetService<ISimpleService>();

    // --- Transient resolution (1 dependency) ---

    [Benchmark(Description = "MS DI: Resolve transient (1 dep)")]
    [BenchmarkCategory("TransientWithDep")]
    public IServiceWithDep MsDi_ResolveWithDep()
        => _msDiProvider.GetRequiredService<IServiceWithDep>();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve transient (1 dep)")]
    [BenchmarkCategory("TransientWithDep")]
    public IServiceWithDep Container_ResolveWithDep()
        => _containerProvider.GetRequiredService<IServiceWithDep>();

    [Benchmark(Description = "Standalone: Resolve transient (1 dep)")]
    [BenchmarkCategory("TransientWithDep")]
    public IServiceWithDep Standalone_ResolveWithDep()
        => _standaloneProvider.GetRequiredService<IServiceWithDep>();

    [Benchmark(Description = "Jab: Resolve transient (1 dep)")]
    [BenchmarkCategory("TransientWithDep")]
    public IServiceWithDep Jab_ResolveWithDep()
        => _jabProvider.GetService<IServiceWithDep>();

    // --- Transient resolution (1 property-injected dependency) ---

    [Benchmark(Description = "MS DI: Resolve transient (1 property dep)")]
    [BenchmarkCategory("PropertyInjection")]
    public IServiceWithPropertyDep MsDi_ResolvePropertyDep()
        => _msDiProvider.GetRequiredService<IServiceWithPropertyDep>();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve transient (1 property dep)")]
    [BenchmarkCategory("PropertyInjection")]
    public IServiceWithPropertyDep Container_ResolvePropertyDep()
        => _containerProvider.GetRequiredService<IServiceWithPropertyDep>();

    [Benchmark(Description = "Standalone: Resolve transient (1 property dep)")]
    [BenchmarkCategory("PropertyInjection")]
    public IServiceWithPropertyDep Standalone_ResolvePropertyDep()
        => _standaloneProvider.GetRequiredService<IServiceWithPropertyDep>();

    // --- Transient resolution (2 dependencies) ---

    [Benchmark(Description = "MS DI: Resolve transient (2 deps)")]
    [BenchmarkCategory("TransientMultiDep")]
    public IServiceWithMultipleDeps MsDi_ResolveMultipleDeps()
        => _msDiProvider.GetRequiredService<IServiceWithMultipleDeps>();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve transient (2 deps)")]
    [BenchmarkCategory("TransientMultiDep")]
    public IServiceWithMultipleDeps Container_ResolveMultipleDeps()
        => _containerProvider.GetRequiredService<IServiceWithMultipleDeps>();

    [Benchmark(Description = "Standalone: Resolve transient (2 deps)")]
    [BenchmarkCategory("TransientMultiDep")]
    public IServiceWithMultipleDeps Standalone_ResolveMultipleDeps()
        => _standaloneProvider.GetRequiredService<IServiceWithMultipleDeps>();

    [Benchmark(Description = "Jab: Resolve transient (2 deps)")]
    [BenchmarkCategory("TransientMultiDep")]
    public IServiceWithMultipleDeps Jab_ResolveMultipleDeps()
        => _jabProvider.GetService<IServiceWithMultipleDeps>();

    // --- Singleton resolution ---

    [Benchmark(Description = "MS DI: Resolve singleton")]
    [BenchmarkCategory("Singleton")]
    public ISingletonService MsDi_ResolveSingleton()
        => _msDiProvider.GetRequiredService<ISingletonService>();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve singleton")]
    [BenchmarkCategory("Singleton")]
    public ISingletonService Container_ResolveSingleton()
        => _containerProvider.GetRequiredService<ISingletonService>();

    [Benchmark(Description = "Standalone: Resolve singleton")]
    [BenchmarkCategory("Singleton")]
    public ISingletonService Standalone_ResolveSingleton()
        => _standaloneProvider.GetRequiredService<ISingletonService>();

    [Benchmark(Description = "Jab: Resolve singleton")]
    [BenchmarkCategory("Singleton")]
    public ISingletonService Jab_ResolveSingleton()
        => _jabProvider.GetService<ISingletonService>();

    // --- Scoped resolution, first resolve in a fresh scope ---
    // [IterationSetup] makes BenchmarkDotNet run one invocation per iteration, so each one measures
    // the cold path: the first resolve in a new scope, which creates and caches the instance.

    private IServiceScope _msDiScope = null!;
    private IServiceScope _containerScope = null!;
    private IServiceScope _standaloneScope = null!;
    private JabContainer.Scope _jabScope = null!;

    [IterationSetup(Targets = [nameof(MsDi_ResolveScoped), nameof(Container_ResolveScoped), nameof(Standalone_ResolveScoped), nameof(Jab_ResolveScoped)])]
    public void ScopeSetup()
    {
        _msDiScope = _msDiProvider.CreateScope();
        _containerScope = (_containerProvider as IServiceScopeFactory)!.CreateScope();
        _standaloneScope = (_standaloneProvider as IServiceScopeFactory)!.CreateScope();
        _jabScope = _jabProvider.CreateScope();
    }

    [IterationCleanup(Targets = [nameof(MsDi_ResolveScoped), nameof(Container_ResolveScoped), nameof(Standalone_ResolveScoped), nameof(Jab_ResolveScoped)])]
    public void ScopeCleanup()
    {
        _msDiScope.Dispose();
        _containerScope.Dispose();
        _standaloneScope.Dispose();
        _jabScope.Dispose();
    }

    [Benchmark(Description = "MS DI: Resolve scoped (first in scope)")]
    [BenchmarkCategory("Scoped")]
    public IScopedService MsDi_ResolveScoped()
        => _msDiScope.ServiceProvider.GetRequiredService<IScopedService>();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve scoped (first in scope)")]
    [BenchmarkCategory("Scoped")]
    public IScopedService Container_ResolveScoped()
        => _containerScope.ServiceProvider.GetRequiredService<IScopedService>();

    [Benchmark(Description = "Standalone: Resolve scoped (first in scope)")]
    [BenchmarkCategory("Scoped")]
    public IScopedService Standalone_ResolveScoped()
        => _standaloneScope.ServiceProvider.GetRequiredService<IScopedService>();

    [Benchmark(Description = "Jab: Resolve scoped (first in scope)")]
    [BenchmarkCategory("Scoped")]
    public IScopedService Jab_ResolveScoped()
        => _jabScope.GetService<IScopedService>();

    // --- Scoped resolution, cached ---
    // One scope per container for the whole run, created and primed in [GlobalSetup], so every
    // invocation measures the steady state: a scoped service already resolved in this scope.

    private IServiceScope _msDiCachedScope = null!;
    private IServiceScope _containerCachedScope = null!;
    private IServiceScope _standaloneCachedScope = null!;
    private JabContainer.Scope _jabCachedScope = null!;

    [GlobalSetup(Targets = [nameof(MsDi_ResolveScopedCached), nameof(Container_ResolveScopedCached), nameof(Standalone_ResolveScopedCached), nameof(Jab_ResolveScopedCached)])]
    public void CachedScopeSetup()
    {
        Setup();
        _msDiCachedScope = _msDiProvider.CreateScope();
        _containerCachedScope = (_containerProvider as IServiceScopeFactory)!.CreateScope();
        _standaloneCachedScope = (_standaloneProvider as IServiceScopeFactory)!.CreateScope();
        _jabCachedScope = _jabProvider.CreateScope();
        _ = _msDiCachedScope.ServiceProvider.GetRequiredService<IScopedService>();
        _ = _containerCachedScope.ServiceProvider.GetRequiredService<IScopedService>();
        _ = _standaloneCachedScope.ServiceProvider.GetRequiredService<IScopedService>();
        _ = _jabCachedScope.GetService<IScopedService>();
    }

    [GlobalCleanup(Targets = [nameof(MsDi_ResolveScopedCached), nameof(Container_ResolveScopedCached), nameof(Standalone_ResolveScopedCached), nameof(Jab_ResolveScopedCached)])]
    public void CachedScopeCleanup()
    {
        _msDiCachedScope.Dispose();
        _containerCachedScope.Dispose();
        _standaloneCachedScope.Dispose();
        _jabCachedScope.Dispose();
        Cleanup();
    }

    [Benchmark(Description = "MS DI: Resolve scoped (cached)")]
    [BenchmarkCategory("ScopedCached")]
    public IScopedService MsDi_ResolveScopedCached()
        => _msDiCachedScope.ServiceProvider.GetRequiredService<IScopedService>();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve scoped (cached)")]
    [BenchmarkCategory("ScopedCached")]
    public IScopedService Container_ResolveScopedCached()
        => _containerCachedScope.ServiceProvider.GetRequiredService<IScopedService>();

    [Benchmark(Description = "Standalone: Resolve scoped (cached)")]
    [BenchmarkCategory("ScopedCached")]
    public IScopedService Standalone_ResolveScopedCached()
        => _standaloneCachedScope.ServiceProvider.GetRequiredService<IScopedService>();

    [Benchmark(Description = "Jab: Resolve scoped (cached)")]
    [BenchmarkCategory("ScopedCached")]
    public IScopedService Jab_ResolveScopedCached()
        => _jabCachedScope.GetService<IScopedService>();

    // --- IEnumerable<T> resolution ---

    [Benchmark(Description = "MS DI: Resolve IEnumerable<T>")]
    [BenchmarkCategory("Enumerable")]
    public IMultiService[] MsDi_ResolveEnumerable()
        => _msDiProvider.GetRequiredService<IEnumerable<IMultiService>>().ToArray();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve IEnumerable<T>")]
    [BenchmarkCategory("Enumerable")]
    public IMultiService[] Container_ResolveEnumerable()
        => _containerProvider.GetRequiredService<IEnumerable<IMultiService>>().ToArray();

    [Benchmark(Description = "Standalone: Resolve IEnumerable<T>")]
    [BenchmarkCategory("Enumerable")]
    public IMultiService[] Standalone_ResolveEnumerable()
        => _standaloneProvider.GetRequiredService<IEnumerable<IMultiService>>().ToArray();

    [Benchmark(Description = "Jab: Resolve IEnumerable<T>")]
    [BenchmarkCategory("Enumerable")]
    public IMultiService[] Jab_ResolveEnumerable()
        => _jabProvider.GetService<IEnumerable<IMultiService>>().ToArray();

    // --- Decorator resolution ---

    [Benchmark(Description = "MS DI: Resolve decorated transient")]
    [BenchmarkCategory("Decorator")]
    public IDecoratedService MsDi_ResolveDecorated()
        => _msDiProvider.GetRequiredService<IDecoratedService>();

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve decorated transient")]
    [BenchmarkCategory("Decorator")]
    public IDecoratedService Container_ResolveDecorated()
        => _containerProvider.GetRequiredService<IDecoratedService>();

    [Benchmark(Description = "Standalone: Resolve decorated transient")]
    [BenchmarkCategory("Decorator")]
    public IDecoratedService Standalone_ResolveDecorated()
        => _standaloneProvider.GetRequiredService<IDecoratedService>();

    [Benchmark(Description = "Jab: Resolve decorated transient (via factory)")]
    [BenchmarkCategory("Decorator")]
    public IDecoratedService Jab_ResolveDecorated()
        => _jabProvider.GetService<IDecoratedService>();

    // --- Open generic resolution ---

    [Benchmark(Description = "MS DI: Resolve open generic (string)")]
    [BenchmarkCategory("OpenGeneric")]
    public object? MsDi_ResolveOpenGeneric()
        => _msDiProvider.GetService(typeof(IGenericRepo<string>));

    [Benchmark(Description = "ZeroAlloc.Inject Container: Resolve open generic (string) - compile-time closed type")]
    [BenchmarkCategory("OpenGeneric")]
    public object? Container_ResolveOpenGeneric()
        => _containerProvider.GetService(typeof(IGenericRepo<string>));

    [Benchmark(Description = "Standalone: Resolve open generic (string) - compile-time closed type")]
    [BenchmarkCategory("OpenGeneric")]
    public object? Standalone_ResolveOpenGeneric()
        => _standaloneProvider.GetService(typeof(IGenericRepo<string>));

    // --- Scope creation ---

    [Benchmark(Description = "MS DI: Create scope")]
    [BenchmarkCategory("ScopeCreation")]
    public IServiceScope MsDi_CreateScope()
    {
        var scope = _msDiProvider.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "ZeroAlloc.Inject Container: Create scope")]
    [BenchmarkCategory("ScopeCreation")]
    public IServiceScope Container_CreateScope()
    {
        var scope = (_containerProvider as IServiceScopeFactory)!.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "Standalone: Create scope")]
    [BenchmarkCategory("ScopeCreation")]
    public IServiceScope Standalone_CreateScope()
    {
        var scope = (_standaloneProvider as IServiceScopeFactory)!.CreateScope();
        scope.Dispose();
        return scope;
    }

    [Benchmark(Description = "Jab: Create scope")]
    [BenchmarkCategory("ScopeCreation")]
    public JabContainer.Scope Jab_CreateScope()
    {
        var scope = _jabProvider.CreateScope();
        scope.Dispose();
        return scope;
    }
}
