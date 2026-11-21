using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject;
using ZeroAlloc.Inject.Sdk10Smoke.Container;

// AddZeroAllocInjectSdk10SmokeContainerServices and BuildZeroAllocInjectServiceProvider exist
// only if the compiler loaded the generator bundled in ZeroAlloc.Inject.Container.
var services = new ServiceCollection();
services.AddZeroAllocInjectSdk10SmokeContainerServices();
var provider = services.BuildZeroAllocInjectServiceProvider();

if (!string.Equals(provider.GetType().Namespace, "ZeroAlloc.Inject.Generated", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"FAIL: provider is {provider.GetType()}, not the generated hybrid provider");
    return 1;
}

var clock = provider.GetRequiredService<IClock>();
if (clock is not FixedClock)
{
    Console.Error.WriteLine($"FAIL: IClock resolved to {clock.GetType()}");
    return 1;
}

if (!ReferenceEquals(clock, provider.GetRequiredService<IClock>()))
{
    Console.Error.WriteLine("FAIL: the singleton IClock resolved to two instances");
    return 1;
}

(provider as IDisposable)?.Dispose();
Console.WriteLine("SDK 10 Container consumer: bundled generator loaded and the hybrid provider resolved.");
return 0;

namespace ZeroAlloc.Inject.Sdk10Smoke.Container
{
    public interface IClock
    {
        DateTimeOffset Now { get; }
    }

    [Singleton]
    public sealed class FixedClock : IClock
    {
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch;
    }
}
