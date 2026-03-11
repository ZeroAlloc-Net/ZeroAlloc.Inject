using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ZeroInject.Benchmarks;

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

    [Benchmark(Description = "ZeroInject (factory lambda)")]
    public IServiceCollection ZeroInject_FactoryLambda()
    {
        var services = new ServiceCollection();
        services.AddZeroInjectBenchmarksServices();
        return services;
    }

    [Benchmark(Description = "ZeroInject Container: Build provider")]
    public IServiceProvider ZeroInject_ContainerBuild()
    {
        var services = new ServiceCollection();
        services.AddZeroInjectBenchmarksServices();
        return services.BuildZeroInjectServiceProvider();
    }
}
