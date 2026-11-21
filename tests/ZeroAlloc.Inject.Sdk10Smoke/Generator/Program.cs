using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject;
using ZeroAlloc.Inject.Sdk10Smoke.Generator;

// AddZeroAllocInjectSdk10SmokeGeneratorServices exists only if the compiler loaded the generator.
var services = new ServiceCollection();
services.AddZeroAllocInjectSdk10SmokeGeneratorServices();
using var provider = services.BuildServiceProvider();

var clock = provider.GetRequiredService<IClock>();
if (clock is not FixedClock)
{
    Console.Error.WriteLine($"FAIL: IClock resolved to {clock.GetType()}");
    return 1;
}

Console.WriteLine("SDK 10 consumer: generator loaded and registrations resolved.");
return 0;

namespace ZeroAlloc.Inject.Sdk10Smoke.Generator
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
