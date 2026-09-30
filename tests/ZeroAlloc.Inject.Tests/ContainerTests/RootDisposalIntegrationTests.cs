using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Inject.Tests.ContainerTests;

/// <summary>
/// The generated containers dispose the disposable transients they resolve from the root, and the
/// singletons, in the order Microsoft DI does. See ZeroAlloc-Net/ZeroAlloc.Inject#177.
/// </summary>
public class RootDisposalIntegrationTests
{
    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;
        using ZeroAlloc.Inject;
        namespace TestApp;

        public static class DisposalLog
        {
            public static List<string> Entries { get; } = new();
            public static void Add(string entry) { lock (Entries) Entries.Add(entry); }
        }

        public interface IClock { }
        [Singleton]
        public class Clock : IClock, IDisposable
        {
            public void Dispose() => DisposalLog.Add("Clock");
        }

        public interface IWorker { }
        [Transient]
        public class Worker : IWorker, IDisposable
        {
            public Worker(IClock clock) { }
            public void Dispose() => DisposalLog.Add("Worker");
        }

        public interface IAsyncWorker { }
        [Transient]
        public class AsyncWorker : IAsyncWorker, IAsyncDisposable, IDisposable
        {
            public ValueTask DisposeAsync() { DisposalLog.Add("AsyncWorker.async"); return default; }
            public void Dispose() => DisposalLog.Add("AsyncWorker.sync");
        }

        public interface IKeyedWorker { }
        [Transient(Key = "k")]
        public class KeyedWorker : IKeyedWorker, IDisposable
        {
            public void Dispose() => DisposalLog.Add("KeyedWorker");
        }

        public interface IRepo<T> { }
        [Transient]
        public class Repo<T> : IRepo<T>, IDisposable
        {
            public void Dispose() => DisposalLog.Add("Repo<" + typeof(T).Name + ">");
        }

        [Transient]
        public class Consumer { public Consumer(IRepo<int> repo) { } }
        """;

    private static IServiceProvider Build(GeneratedApp app, string mode) => mode switch
    {
        "MicrosoftDi" => app.BuildMicrosoftDi(),
        "Hybrid" => app.BuildHybrid(),
        "HybridWithoutFallback" => app.BuildHybrid(addServices: false),
        "Standalone" => app.BuildStandalone(),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static List<string> Log(GeneratedApp app) =>
        (List<string>)app.Type("DisposalLog").GetProperty("Entries")!.GetValue(null)!;

    /// <summary>Resolves one of each root transient, then IEnumerable of the worker.</summary>
    private static void ResolveFromTheRoot(GeneratedApp app, IServiceProvider provider)
    {
        provider.GetRequiredService(app.Type("IWorker"));
        provider.GetRequiredService(app.Type("IRepo", typeof(int)));
        provider.GetRequiredKeyedService(app.Type("IKeyedWorker"), "k");
        provider.GetRequiredService(app.Type("IAsyncWorker"));
        foreach (var _ in provider.GetServices(app.Type("IWorker"))) { }
    }

    private static List<string> DisposedByMicrosoftDi(bool async)
    {
        var app = GeneratedApp.Compile(Source);
        var provider = app.BuildMicrosoftDi();
        ResolveFromTheRoot(app, provider);
        if (async) provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        else provider.Dispose();
        return Log(app);
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("HybridWithoutFallback")]
    [InlineData("Standalone")]
    public void Dispose_DisposesRootTransientsAndSingletons_AsMicrosoftDiDoes(string mode)
    {
        var app = GeneratedApp.Compile(Source);
        var provider = Build(app, mode);
        ResolveFromTheRoot(app, provider);
        Assert.Empty(Log(app));

        ((IDisposable)provider).Dispose();

        var expected = DisposedByMicrosoftDi(async: false);
        Assert.Equal(["Worker", "AsyncWorker.sync", "KeyedWorker", "Repo<Int32>", "Worker", "Clock"], expected);
        Assert.Equal(expected, Log(app));
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("HybridWithoutFallback")]
    [InlineData("Standalone")]
    public async Task DisposeAsync_DisposesRootTransientsAndSingletons_AsMicrosoftDiDoes(string mode)
    {
        var app = GeneratedApp.Compile(Source);
        var provider = Build(app, mode);
        ResolveFromTheRoot(app, provider);

        await ((IAsyncDisposable)provider).DisposeAsync();

        var expected = DisposedByMicrosoftDi(async: true);
        Assert.Equal(["Worker", "AsyncWorker.async", "KeyedWorker", "Repo<Int32>", "Worker", "Clock"], expected);
        Assert.Equal(expected, Log(app));
    }

    [Theory]
    [InlineData("HybridWithoutFallback")]
    [InlineData("Standalone")]
    public void Dispose_TracksRootTransientsResolvedConcurrently(string mode)
    {
        var app = GeneratedApp.Compile(Source);
        var provider = Build(app, mode);
        var workerType = app.Type("IWorker");
        var repoType = app.Type("IRepo", typeof(int));

        // Resolve the singleton first, so a race to create it does not dispose a loser early.
        provider.GetRequiredService(app.Type("IClock"));
        Parallel.For(0, 1000, i => provider.GetRequiredService(i % 2 == 0 ? workerType : repoType));
        ((IDisposable)provider).Dispose();

        var log = Log(app);
        Assert.Equal(500, log.Count(e => string.Equals(e, "Worker", StringComparison.Ordinal)));
        Assert.Equal(500, log.Count(e => string.Equals(e, "Repo<Int32>", StringComparison.Ordinal)));
        Assert.Equal("Clock", log[^1]);
    }

    [Theory]
    [InlineData("Hybrid")]
    [InlineData("HybridWithoutFallback")]
    [InlineData("Standalone")]
    public void ScopeDisposal_DoesNotDisposeRootTransients(string mode)
    {
        var app = GeneratedApp.Compile(Source);
        var provider = Build(app, mode);
        provider.GetRequiredService(app.Type("IWorker"));

        using (var scope = provider.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService(app.Type("IRepo", typeof(int)));
        }

        // The scope disposes only what it resolved; the root transient waits for the root.
        Assert.Equal(["Repo<Int32>"], Log(app));
        ((IDisposable)provider).Dispose();
        Assert.Equal(["Repo<Int32>", "Worker", "Clock"], Log(app));
    }
}
