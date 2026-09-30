using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject.Container;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// TrackDisposable on the root providers: what the root creates is disposed with it, last created
/// first, as Microsoft DI does. See ZeroAlloc-Net/ZeroAlloc.Inject#177.
/// </summary>
public class RootDisposalTests
{
    private sealed class Recorder(string name, List<string> log) : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose()
        {
            DisposeCount++;
            lock (log) log.Add(name);
        }
    }

    private sealed class AsyncRecorder(string name, List<string> log) : IAsyncDisposable, IDisposable
    {
        public ValueTask DisposeAsync()
        {
            log.Add(name + ".async");
            return default;
        }

        public void Dispose() => log.Add(name + ".sync");
    }

    private sealed class HybridProvider(IServiceCollection services) : ZeroAllocInjectServiceProviderBase(services)
    {
        public T Track<T>(T instance) where T : notnull => TrackDisposable(instance);

        protected override object? ResolveKnown(Type serviceType) => null;

        protected override bool IsKnownService(Type serviceType) => false;

        protected override bool IsKnownKeyedService(Type serviceType, object? serviceKey) => false;

        protected override ZeroAllocInjectScope CreateScopeCore(IServiceScopeFactory fallbackScopeFactory) =>
            throw new NotSupportedException();
    }

    private sealed class StandaloneProvider : ZeroAllocInjectStandaloneProvider
    {
        public T Track<T>(T instance) where T : notnull => TrackDisposable(instance);

        protected override object? ResolveKnown(Type serviceType) => null;

        protected override bool IsKnownService(Type serviceType) => false;

        protected override bool IsKnownKeyedService(Type serviceType, object? serviceKey) => false;

        protected override ZeroAllocInjectStandaloneScope CreateScopeCore() => throw new NotSupportedException();
    }

    private interface ITracker : IDisposable, IAsyncDisposable
    {
        T Track<T>(T instance) where T : notnull;
    }

    private sealed class HybridTracker(HybridProvider provider) : ITracker
    {
        public T Track<T>(T instance) where T : notnull => provider.Track(instance);
        public void Dispose() => provider.Dispose();
        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private sealed class StandaloneTracker(StandaloneProvider provider) : ITracker
    {
        public T Track<T>(T instance) where T : notnull => provider.Track(instance);
        public void Dispose() => provider.Dispose();
        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }

    private static ITracker Create(string mode) => mode switch
    {
        "Hybrid" => new HybridTracker(new HybridProvider(new ServiceCollection())),
        "Standalone" => new StandaloneTracker(new StandaloneProvider()),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("Standalone")]
    public void Dispose_DisposesTrackedInstances_LastTrackedFirst(string mode)
    {
        var log = new List<string>();
        var provider = Create(mode);
        provider.Track(new Recorder("first", log));
        provider.Track(new Recorder("second", log));
        provider.Track(new object());

        provider.Dispose();

        Assert.Equal(["second", "first"], log);
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("Standalone")]
    public async Task DisposeAsync_PrefersIAsyncDisposable_LastTrackedFirst(string mode)
    {
        var log = new List<string>();
        var provider = Create(mode);
        provider.Track(new Recorder("sync-only", log));
        provider.Track(new AsyncRecorder("both", log));

        await provider.DisposeAsync();

        Assert.Equal(["both.async", "sync-only"], log);
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("Standalone")]
    public async Task Dispose_DisposesEachTrackedInstanceOnce(string mode)
    {
        var log = new List<string>();
        var provider = Create(mode);
        var recorder = provider.Track(new Recorder("once", log));

        provider.Dispose();
        provider.Dispose();
        await provider.DisposeAsync();

        Assert.Equal(1, recorder.DisposeCount);
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("Standalone")]
    public void TrackDisposable_FromManyThreads_TracksEveryInstance(string mode)
    {
        var log = new List<string>();
        var provider = Create(mode);
        var recorders = new Recorder[2000];

        Parallel.For(0, recorders.Length, i => recorders[i] = provider.Track(new Recorder("r", log)));
        provider.Dispose();

        Assert.All(recorders, r => Assert.Equal(1, r.DisposeCount));
        Assert.Equal(recorders.Length, log.Count);
    }

    [Fact]
    public void Hybrid_Dispose_DisposesTrackedInstancesBeforeTheFallback()
    {
        var log = new List<string>();
        var services = new ServiceCollection();
        services.AddSingleton(_ => new Recorder("fallback", log));
        var provider = new HybridProvider(services);
        provider.GetRequiredService<Recorder>();
        provider.Track(new Recorder("tracked", log));

        provider.Dispose();

        Assert.Equal(["tracked", "fallback"], log);
    }
}
