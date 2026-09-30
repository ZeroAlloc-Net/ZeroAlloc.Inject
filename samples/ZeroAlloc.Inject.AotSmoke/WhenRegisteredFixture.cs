using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

// WhenRegistered, decided from the registrations in every mode. GeneratedFeature is a generated
// service, so its decorator applies everywhere. TracingFeature is registered only by Program.cs,
// before Add...Services, for Microsoft DI and the hybrid container; the standalone container
// never has it.
public sealed class TracingFeature
{
}

[Singleton]
public sealed class GeneratedFeature
{
}

public interface IPriceFeed
{
    string Describe();
}

[Transient]
public sealed class PriceFeed : IPriceFeed
{
    public string Describe() => "feed";
}

[DecoratorOf(typeof(IPriceFeed), Order = 1, WhenRegistered = typeof(TracingFeature))]
public sealed class TracedPriceFeed : IPriceFeed
{
    private readonly IPriceFeed _inner;
    public TracedPriceFeed(IPriceFeed inner) => _inner = inner;
    public string Describe() => $"traced({_inner.Describe()})";
}

[DecoratorOf(typeof(IPriceFeed), Order = 2, WhenRegistered = typeof(GeneratedFeature))]
public sealed class CachedPriceFeed : IPriceFeed
{
    private readonly IPriceFeed _inner;
    public CachedPriceFeed(IPriceFeed inner) => _inner = inner;
    public string Describe() => $"cached({_inner.Describe()})";
}
