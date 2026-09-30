using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject.AotSmoke;

// Exercise the generator-emitted AddZeroAllocInjectAotSmokeServices() extension
// under PublishAot=true. The DI container must:
//   1. Instantiate Greeter (Singleton, no deps) once and share it
//   2. Instantiate WelcomeService (Transient) and wire IGreeter into its ctor
//   3. Return the expected composed greeting

var services = new ServiceCollection();
services.AddZeroAllocInjectAotSmokeServices();
// A keyed registration the generator does not know: the hybrid container falls back to Microsoft DI for it.
services.AddKeyedSingleton<IGreeter>("manual", (_, _) => new ManualGreeter());
using var provider = services.BuildServiceProvider();

var welcome = provider.GetRequiredService<IWelcomeService>();
var greeting = welcome.WelcomeUser("AOT");
if (!string.Equals(greeting, "Hello, AOT!", StringComparison.Ordinal))
    return Fail($"WelcomeUser expected 'Hello, AOT!', got '{greeting}'");

// Singleton identity — two resolutions of IGreeter share the same instance
var g1 = provider.GetRequiredService<IGreeter>();
var g2 = provider.GetRequiredService<IGreeter>();
if (!ReferenceEquals(g1, g2))
    return Fail("IGreeter Singleton: two resolutions should share one instance");

// Transient identity — two resolutions of IWelcomeService are distinct
var w1 = provider.GetRequiredService<IWelcomeService>();
var w2 = provider.GetRequiredService<IWelcomeService>();
if (ReferenceEquals(w1, w2))
    return Fail("IWelcomeService Transient: two resolutions should be distinct");

// Scoped lifetime — same instance within a scope, distinct across scopes.
using (var scope1 = provider.CreateScope())
{
    var c1a = scope1.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    var c1b = scope1.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    if (!ReferenceEquals(c1a, c1b))
        return Fail($"IHttpRequestContext Scoped: two resolutions in same scope should share instance (got RequestId {c1a.RequestId} vs {c1b.RequestId})");

    using var scope2 = provider.CreateScope();
    var c2 = scope2.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    if (ReferenceEquals(c1a, c2))
        return Fail($"IHttpRequestContext Scoped: resolutions in different scopes should be distinct (both got RequestId {c1a.RequestId})");
}

// Decorator pattern — resolving IFoo returns LoggingFoo wrapping Foo.
var foo = provider.GetRequiredService<IFoo>();
if (foo is not LoggingFoo)
    return Fail($"IFoo Decorator: expected LoggingFoo, got {foo.GetType().Name}");
var fooResult = foo.DoStuff("x");
if (!string.Equals(fooResult, "[logged] base:x", StringComparison.Ordinal))
    return Fail($"IFoo Decorator: expected '[logged] base:x', got '{fooResult}'");

// Open-generic closed-usage — IInventory<Product> resolves to the closed form.
var inv = provider.GetRequiredService<IInventory<Product>>();
var invDesc = inv.Describe();
if (!string.Equals(invDesc, "InMemoryInventory<Product>", StringComparison.Ordinal))
    return Fail($"IInventory<Product> closed-generic: expected 'InMemoryInventory<Product>', got '{invDesc}'");

// Open generics closed over value types, in all three modes. Each closed form is its own native
// instantiation, and Microsoft DI refuses to close an open generic over a value type under
// NativeAOT, so the generated registrations and the hybrid type switch have to supply them.
if (CheckValueTypeGenerics(provider, "Microsoft DI") is { } microsoftDiError) return Fail(microsoftDiError);
if (CheckOpenGenericFeatures(provider, "Microsoft DI") is { } microsoftDiFeatureError) return Fail(microsoftDiFeatureError);

var hybrid = (ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase)services.BuildZeroAllocInjectServiceProvider();
using (hybrid)
{
    if (CheckValueTypeGenerics(hybrid, "hybrid container") is { } hybridError) return Fail(hybridError);
    if (CheckValueTypeEnumerables(hybrid, "hybrid container") is { } hybridEnumerableError) return Fail(hybridEnumerableError);
    if (CheckOpenGenericFeatures(hybrid, "hybrid container") is { } hybridFeatureError) return Fail(hybridFeatureError);
    if (CheckKeyedFallback(hybrid) is { } hybridKeyedError) return Fail(hybridKeyedError);
}

using (var standalone = new ZeroAlloc.Inject.Generated.ZeroAllocInjectAotSmokeStandaloneServiceProvider())
{
    if (CheckValueTypeGenerics(standalone, "standalone container") is { } standaloneError) return Fail(standaloneError);
    if (CheckValueTypeEnumerables(standalone, "standalone container") is { } standaloneEnumerableError) return Fail(standaloneEnumerableError);
    if (CheckOpenGenericFeatures(standalone, "standalone container") is { } standaloneFeatureError) return Fail(standaloneFeatureError);
}

// Disposable transients resolved from the root are disposed with the provider in every mode.
if (CheckRootDisposal(() => new ServiceCollection().AddZeroAllocInjectAotSmokeServices().BuildServiceProvider(), "Microsoft DI") is { } microsoftDiDisposalError)
    return Fail(microsoftDiDisposalError);
if (CheckRootDisposal(() => new ServiceCollection().AddZeroAllocInjectAotSmokeServices().BuildZeroAllocInjectServiceProvider(), "hybrid container") is { } hybridDisposalError)
    return Fail(hybridDisposalError);
if (CheckRootDisposal(() => new ZeroAlloc.Inject.Generated.ZeroAllocInjectAotSmokeStandaloneServiceProvider(), "standalone container") is { } standaloneDisposalError)
    return Fail(standaloneDisposalError);

// Non-generic decorators of a singleton, a scoped service and an As service, in every mode.
if (CheckNonGenericDecorators(() => new ServiceCollection().AddZeroAllocInjectAotSmokeServices().BuildServiceProvider(), "Microsoft DI") is { } microsoftDiDecoratorError)
    return Fail(microsoftDiDecoratorError);
if (CheckNonGenericDecorators(() => new ServiceCollection().AddZeroAllocInjectAotSmokeServices().BuildZeroAllocInjectServiceProvider(), "hybrid container") is { } hybridDecoratorError)
    return Fail(hybridDecoratorError);
if (CheckNonGenericDecorators(() => new ZeroAlloc.Inject.Generated.ZeroAllocInjectAotSmokeStandaloneServiceProvider(), "standalone container") is { } standaloneDecoratorError)
    return Fail(standaloneDecoratorError);

Console.WriteLine("AOT smoke: PASS");
return 0;

// Resolves every value-type closed form through the consumer's constructor and directly, and
// checks the exact implementation type, the values each one holds, and its lifetime.
static string? CheckValueTypeGenerics(IServiceProvider provider, string mode)
{
    var first = provider.GetRequiredService<ValueTypeConsumer>();
    var second = provider.GetRequiredService<ValueTypeConsumer>();

    if (first.Count is not ValueSlot<int> count)
        return $"{mode}: IValueSlot<int> expected ValueSlot<int>, got {first.Count.GetType()}";
    if (first.Limit is not ValueSlot<int?> limit)
        return $"{mode}: IValueSlot<int?> expected ValueSlot<int?>, got {first.Limit.GetType()}";
    if (first.Color is not ValueSlot<SmokeColor> color)
        return $"{mode}: IValueSlot<SmokeColor> expected ValueSlot<SmokeColor>, got {first.Color.GetType()}";
    if (first.Key is not ValueSlot<SmokeKey> key)
        return $"{mode}: IValueSlot<SmokeKey> expected ValueSlot<SmokeKey>, got {first.Key.GetType()}";
    if (first.OptionalKey is not ValueSlot<SmokeKey?> optionalKey)
        return $"{mode}: IValueSlot<SmokeKey?> expected ValueSlot<SmokeKey?>, got {first.OptionalKey.GetType()}";

    count.Set(42);
    if (count.Value != 42 || !count.Holds(42) || count.Holds(41))
        return $"{mode}: ValueSlot<int> expected to hold 42, got {count.Value}";

    if (limit.Value is not null || !limit.Holds(null))
        return $"{mode}: ValueSlot<int?> expected to start null, got {limit.Value}";
    limit.Set(7);
    if (limit.Value != 7 || !limit.Holds(7) || limit.Holds(null))
        return $"{mode}: ValueSlot<int?> expected to hold 7, got {limit.Value}";
    limit.Set(null);
    if (limit.Value is not null || !limit.Holds(null) || limit.Holds(7))
        return $"{mode}: ValueSlot<int?> expected to hold null again, got {limit.Value}";

    color.Set(SmokeColor.Green);
    if (color.Value != SmokeColor.Green || !color.Holds(SmokeColor.Green) || color.Holds(SmokeColor.Red))
        return $"{mode}: ValueSlot<SmokeColor> expected to hold Green, got {color.Value}";

    key.Set(new SmokeKey(1, 2));
    if (key.Value != new SmokeKey(1, 2) || !key.Holds(new SmokeKey(1, 2)) || key.Holds(new SmokeKey(2, 1)))
        return $"{mode}: ValueSlot<SmokeKey> expected to hold (1, 2), got {key.Value}";

    if (optionalKey.Value is not null || !optionalKey.Holds(null))
        return $"{mode}: ValueSlot<SmokeKey?> expected to start null, got {optionalKey.Value}";
    optionalKey.Set(new SmokeKey(3, 4));
    if (optionalKey.Value != new SmokeKey(3, 4) || !optionalKey.Holds(new SmokeKey(3, 4)) || optionalKey.Holds(null))
        return $"{mode}: ValueSlot<SmokeKey?> expected to hold (3, 4), got {optionalKey.Value}";

    return CheckValueTypeTransients(provider, first, second, mode)
        ?? CheckValueTypeSingletons(provider, first, second, mode);
}

// Transient: every resolution is a new instance with its own state.
static string? CheckValueTypeTransients(IServiceProvider provider, ValueTypeConsumer first, ValueTypeConsumer second, string mode)
{
    if (ReferenceEquals(first.Limit, second.Limit))
        return $"{mode}: transient IValueSlot<int?> resolved the same instance twice";
    if (second.Limit.Value is not null)
        return $"{mode}: a new IValueSlot<int?> expected to start null, got {second.Limit.Value}";
    var direct = provider.GetRequiredService<IValueSlot<SmokeKey?>>();
    if (direct is not ValueSlot<SmokeKey?> || ReferenceEquals(direct, first.OptionalKey) || direct.Value is not null)
        return $"{mode}: direct IValueSlot<SmokeKey?> expected a new ValueSlot<SmokeKey?>, got {direct.GetType()}";

    return null;
}

// Singleton: one instance per closed form, shared by every resolution.
static string? CheckValueTypeSingletons(IServiceProvider provider, ValueTypeConsumer first, ValueTypeConsumer second, string mode)
{
    if (first.Limits is not ValueRegistry<int?> limits)
        return $"{mode}: IValueRegistry<int?> expected ValueRegistry<int?>, got {first.Limits.GetType()}";
    if (first.Keys is not ValueRegistry<SmokeKey> keys)
        return $"{mode}: IValueRegistry<SmokeKey> expected ValueRegistry<SmokeKey>, got {first.Keys.GetType()}";
    if (!ReferenceEquals(limits, second.Limits) || !ReferenceEquals(limits, provider.GetRequiredService<IValueRegistry<int?>>()))
        return $"{mode}: singleton IValueRegistry<int?> resolved more than one instance";
    if (!ReferenceEquals(keys, second.Keys) || !ReferenceEquals(keys, provider.GetRequiredService<IValueRegistry<SmokeKey>>()))
        return $"{mode}: singleton IValueRegistry<SmokeKey> resolved more than one instance";

    limits.Add(null);
    second.Limits.Add(5);
    if (limits.Count != 2 || !limits.Contains(null) || !limits.Contains(5) || limits.Contains(6))
        return $"{mode}: ValueRegistry<int?> expected to hold null and 5, got {limits.Count} values";
    keys.Add(new SmokeKey(7, 8));
    if (keys.Count != 1 || !second.Keys.Contains(new SmokeKey(7, 8)) || keys.Contains(new SmokeKey(8, 7)))
        return $"{mode}: ValueRegistry<SmokeKey> expected to hold (7, 8), got {keys.Count} values";

    return null;
}

// IEnumerable<T> of a value-type closed form: one registration each, and the singleton is the
// instance every other resolution returns.
static string? CheckValueTypeEnumerables(IServiceProvider provider, string mode)
{
    var slots = provider.GetServices<IValueSlot<int>>().ToList();
    if (slots.Count != 1 || slots[0] is not ValueSlot<int>)
        return $"{mode}: IEnumerable<IValueSlot<int>> expected one ValueSlot<int>, got {slots.Count}";
    var registries = provider.GetServices<IValueRegistry<SmokeKey>>().ToList();
    if (registries.Count != 1 || !ReferenceEquals(registries[0], provider.GetRequiredService<IValueRegistry<SmokeKey>>()))
        return $"{mode}: IEnumerable<IValueRegistry<SmokeKey>> expected the singleton, got {registries.Count}";

    return null;
}

static string? CheckRootDisposal(Func<IServiceProvider> create, string mode)
{
    var provider = create();
    var probe = provider.GetRequiredService<IDisposalProbe>();
    var generic = provider.GetRequiredService<IGenericDisposalProbe<int>>();
    var decorated = provider.GetRequiredService<IDecoratedDisposalProbe<int>>();
    if (probe.IsDisposed || generic.IsDisposed || decorated.IsDisposed)
        return $"{mode}: a root transient was disposed before the provider";

    ((IDisposable)provider).Dispose();
    if (!probe.IsDisposed)
        return $"{mode}: the disposable transient DisposalProbe resolved from the root was not disposed with it";
    if (!generic.IsDisposed)
        return $"{mode}: the disposable transient GenericDisposalProbe<int> resolved from the root was not disposed with it";
    if (decorated is not AuditedDisposalProbe<int> audited || !audited.IsDisposed || !audited.Inner.IsDisposed)
        return $"{mode}: the decorated transient IDecoratedDisposalProbe<int> and its inner were not both disposed with the root";

    return null;
}

// [Decorator] and Key on open generics closed over value types, and a concrete closed type as a
// constructor parameter: each closed form resolves, with its lifetime, in every mode.
static string? CheckOpenGenericFeatures(IServiceProvider provider, string mode)
{
    using (var scope = provider.CreateScope())
    {
        var sp = scope.ServiceProvider;
        var consumer = sp.GetRequiredService<OpenGenericFeaturesConsumer>();
        if (consumer.Store is not AuditedValueStore<int> audited)
            return $"{mode}: IValueStore<int> expected AuditedValueStore<int>, got {consumer.Store.GetType()}";
        if (audited.Inner is not ValueStore<int> || !ReferenceEquals(audited.Inner, sp.GetRequiredService<ValueStore<int>>()))
            return $"{mode}: AuditedValueStore<int> expected to wrap the scoped ValueStore<int>, got {audited.Inner.GetType()}";
        if (!ReferenceEquals(audited, sp.GetRequiredService<IValueStore<int>>()))
            return $"{mode}: scoped IValueStore<int> resolved more than one instance in a scope";
        var stores = sp.GetServices<IValueStore<int>>().ToList();
        if (stores.Count != 1 || !ReferenceEquals(stores[0], audited))
            return $"{mode}: IEnumerable<IValueStore<int>> expected the one decorated instance, got {stores.Count}";
        audited.Set(9);
        if (audited.Value != 9 || !string.Equals(audited.Describe(), "audited(store)", StringComparison.Ordinal))
            return $"{mode}: AuditedValueStore<int> expected to hold 9 as 'audited(store)', got {audited.Value} as '{audited.Describe()}'";

        var registry = sp.GetRequiredService<ValueRegistry<long>>();
        if (!ReferenceEquals(registry, consumer.Registry))
            return $"{mode}: singleton ValueRegistry<long> resolved more than one instance";
        registry.Add(3L);
        if (!consumer.Registry.Contains(3L))
            return $"{mode}: ValueRegistry<long> expected to hold 3";
    }

    var counter = provider.GetRequiredKeyedService<IKeyedCounter<SmokeKey>>("primary");
    if (counter is not KeyedCounter<SmokeKey>)
        return $"{mode}: keyed IKeyedCounter<SmokeKey> expected KeyedCounter<SmokeKey>, got {counter.GetType()}";
    if (!ReferenceEquals(counter, provider.GetRequiredKeyedService<IKeyedCounter<SmokeKey>>("primary")))
        return $"{mode}: keyed singleton IKeyedCounter<SmokeKey> resolved more than one instance";
    if (counter.Next() != 1 || counter.Next() != 2)
        return $"{mode}: KeyedCounter<SmokeKey> expected to count 1, 2";
    if (provider.GetService<IKeyedCounter<SmokeKey>>() is not null)
        return $"{mode}: IKeyedCounter<SmokeKey> is keyed, but resolved without a key";

    return null;
}

static string? CheckNonGenericDecorators(Func<IServiceProvider> create, string mode)
{
    var provider = create();

    var log = provider.GetRequiredService<IAuditLog>();
    if (log is not TimestampedAuditLog timestamped)
        return $"{mode}: singleton IAuditLog expected TimestampedAuditLog, got {log.GetType().Name}";
    if (!ReferenceEquals(timestamped.Inner, provider.GetRequiredService<AuditLog>()))
        return $"{mode}: TimestampedAuditLog expected to wrap the singleton AuditLog";

    IUnitOfWork unit;
    using (var scope = provider.CreateScope())
    {
        var sp = scope.ServiceProvider;
        if (!ReferenceEquals(log, sp.GetRequiredService<IAuditLog>()))
            return $"{mode}: singleton IAuditLog resolved more than one instance";

        unit = sp.GetRequiredService<IUnitOfWork>();
        if (unit is not TracedUnitOfWork traced || !ReferenceEquals(traced.Inner, sp.GetRequiredService<UnitOfWork>()))
            return $"{mode}: scoped IUnitOfWork expected TracedUnitOfWork around the scoped UnitOfWork, got {unit.GetType().Name}";
        if (!ReferenceEquals(unit, sp.GetRequiredService<IUnitOfWork>()))
            return $"{mode}: scoped IUnitOfWork resolved more than one instance in a scope";

        var sink = sp.GetRequiredService<IReportSink>();
        if (sink is not BufferedReportSink || !string.Equals(sink.Name, "buffered(sink)", StringComparison.Ordinal))
            return $"{mode}: IReportSink registered with As expected 'buffered(sink)', got {sink.GetType().Name}";
        if (sp.GetService<ReportSink>() is not null || sp.GetService<IOtherSink>() is not null)
            return $"{mode}: ReportSink is registered only as IReportSink, but resolved as another type";
    }
    if (!unit.IsDisposed)
        return $"{mode}: the scoped UnitOfWork that TracedUnitOfWork wraps was not disposed with its scope";

    ((IDisposable)provider).Dispose();
    if (!timestamped.IsDisposed || !timestamped.Inner.IsDisposed)
        return $"{mode}: the singleton TimestampedAuditLog and its inner were not both disposed with the provider";

    return null;
}

// The hybrid container resolves a keyed service it does not know from its Microsoft DI fallback,
// in the root and in a scope, as it does an unkeyed one.
static string? CheckKeyedFallback(IServiceProvider hybrid)
{
    var manual = hybrid.GetRequiredKeyedService<IGreeter>("manual");
    if (manual is not ManualGreeter)
        return $"hybrid container: keyed IGreeter 'manual' expected ManualGreeter from the fallback, got {manual.GetType().Name}";
    using (var scope = hybrid.CreateScope())
    {
        if (!ReferenceEquals(manual, scope.ServiceProvider.GetRequiredKeyedService<IGreeter>("manual")))
            return "hybrid container: keyed singleton IGreeter 'manual' resolved another instance in a scope";
    }
    if (hybrid.GetKeyedService<IGreeter>("unknown") is not null)
        return "hybrid container: keyed IGreeter 'unknown' expected null";

    return null;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"AOT smoke: FAIL — {message}");
    return 1;
}
