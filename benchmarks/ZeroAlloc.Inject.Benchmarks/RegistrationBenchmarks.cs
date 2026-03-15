using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net90)]
public class RegistrationBenchmarks
{
    [Benchmark(Baseline = true, Description = "MS DI (reflection)")]
    public IServiceCollection MsDi_Reflection()
    {
        var services = new ServiceCollection();

        // Transient
        services.AddTransient<ISimpleService, SimpleService>();
        services.AddTransient<IServiceWithDep, ServiceWithDep>();
        services.AddTransient<IServiceWithMultipleDeps, ServiceWithMultipleDeps>();

        // Singleton
        services.AddSingleton<ISingletonService, SingletonService>();

        // Scoped
        services.AddScoped<IScopedService, ScopedService>();

        return services;
    }

    [Benchmark(Description = "ZeroAlloc.Inject Container: Build provider")]
    public IServiceProvider ZeroAllocInject_ContainerBuild()
    {
        var services = new ServiceCollection();
        services.AddZeroAllocInjectBenchmarksServices();
        return services.BuildZeroAllocInjectServiceProvider();
    }

    [Benchmark(Description = "Standalone: Direct instantiation")]
    public IServiceProvider Standalone_DirectInstantiation()
    {
        return new ZeroAlloc.Inject.Generated.ZeroAllocInjectBenchmarksStandaloneServiceProvider();
    }
}
