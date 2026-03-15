# ZInject

[![CI](https://github.com/MarcelRoozekrans/ZInject/actions/workflows/ci.yml/badge.svg)](https://github.com/MarcelRoozekrans/ZInject/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ZInject.svg)](https://www.nuget.org/packages/ZInject)

Compile-time DI registration for .NET. A Roslyn source generator that auto-discovers services via attributes and generates `IServiceCollection` extension methods and a **Native AOT-compatible** `IServiceProvider`. No reflection, no runtime scanning.

## Quick Start

Install the packages:

```
dotnet add package ZInject
dotnet add package ZInject.Generator
```

> **Tip:** If you plan to use the [generated container](#generated-container), install `ZInject.Container` instead — it bundles the generator and attributes in a single package.

Decorate your services:

```csharp
using ZInject;

[Transient]
public class OrderService : IOrderService { }

[Scoped]
public class UserRepository : IUserRepository { }

[Singleton]
public class CacheService : ICacheService { }
```

Register them in one call:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMyAppServices(); // Generated at compile time
```

That's it. The generator creates an extension method named after your assembly (e.g., `MyApp.Domain` becomes `AddMyAppDomainServices()`).

## Features

### Registration Behavior

By default, services are registered against **all implemented interfaces** plus their **concrete type**, using `TryAdd` semantics (prevents duplicates).

```csharp
// Registers as IOrderService + IDisposable is filtered out + concrete OrderService
[Transient]
public class OrderService : IOrderService, IDisposable { }
```

System interfaces like `IDisposable`, `IAsyncDisposable`, `IComparable<T>`, `IEquatable<T>`, `IFormattable`, `ICloneable`, and `IConvertible` are automatically excluded.

### Narrow Registration with `As`

```csharp
// Only registers as IReadRepository<T>, not IWriteRepository<T>
[Scoped(As = typeof(IReadRepository<>))]
public class Repository<T> : IReadRepository<T>, IWriteRepository<T> { }
```

### Keyed Services (.NET 8+)

```csharp
[Singleton(Key = "redis")]
public class RedisCache : ICache { }

[Singleton(Key = "memory")]
public class MemoryCache : ICache { }
```

### Allow Duplicate Registrations

Default is `TryAdd` (skip if already registered). Use `AllowMultiple` for scenarios like multiple `IHostedService` implementations:

```csharp
[Transient(AllowMultiple = true)]
public class EmailNotifier : INotifier { }

[Transient(AllowMultiple = true)]
public class SmsNotifier : INotifier { }
```

### Open Generics

```csharp
[Scoped]
public class Repository<T> : IRepository<T> { }

// Generates: services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(Repository<>)));
```

### Custom Method Name

Override the generated method name with an assembly-level attribute:

```csharp
[assembly: ZInject("AddDomainServices")]
```

### Decorator Ordering and Conditional Decorators

`[DecoratorOf]` is the explicit form of `[Decorator]` — it names the decorated interface, controls ordering, and supports conditional application.

```csharp
[DecoratorOf(typeof(IRetriever), Order = 1, WhenRegistered = typeof(SomeOptions))]
public class LoggingRetriever : IRetriever
{
    public LoggingRetriever(IRetriever inner, [OptionalDependency] ILogger? logger) { }
}

[DecoratorOf(typeof(IRetriever), Order = 2)]
public class TracingRetriever : IRetriever
{
    public TracingRetriever(IRetriever inner) { }
}
```

**`Order`** — ascending: `Order = 1` is innermost (closest to the real implementation). Higher numbers wrap further out.

**`WhenRegistered`** — the decorator is only wired up if the specified type is present in the `IServiceCollection` at the time `AddXxxServices()` is called. One O(n) scan at startup; no impact on resolution.

**`[OptionalDependency]`** — marks a constructor parameter as optional. The generator emits `GetService<T>()` (returns `null`) instead of `GetRequiredService<T>()` (throws). The parameter must be nullable (`ILogger?`).

## Diagnostics

ZInject reports issues at compile time:

| ID | Severity | Description |
|---|---|---|
| ZI001 | Error | Multiple lifetime attributes on same class |
| ZI002 | Error | Attribute on non-class type |
| ZI003 | Error | Attribute on abstract or static class |
| ZI004 | Error | `As` type not implemented by the class |
| ZI005 | Error | `Key` used below .NET 8 |
| ZI006 | Warning | No public constructor |
| ZI007 | Warning | No interfaces (concrete-only registration) |
| ZI008 | Warning | Missing `Microsoft.Extensions.DependencyInjection.Abstractions` |
| ZI009 | Error | Multiple public constructors without `[ActivatorUtilitiesConstructor]` |
| ZI010 | Error | Constructor parameter is a primitive/value type |
| ZI011 | Error | Decorator has no matching interface parameter |
| ZI012 | Error | Decorated interface not registered as a service |
| ZI013 | Warning | Decorator on abstract or static class |
| ZI014 | Error | Circular dependency detected (compile-time cycle detection) |
| ZI015 | Error | `[OptionalDependency]` on non-nullable parameter |
| ZI016 | Error | `[DecoratorOf]` interface not implemented by the class |
| ZI017 | Error | Two decorators for the same interface share the same `Order` |
| ZI018 | Warning | Open generic has no detected closed usages — won't resolve from standalone container |

## Generated Container

ZInject can replace the default MS DI container with a source-generated `IServiceProvider`. This eliminates reflection-based resolution at runtime. Two modes are available depending on whether you need MS DI integration.

### Installation

```
dotnet add package ZInject.Container
```

This single package includes everything: the source generator, the attributes (`ZInject`), and the container base classes. When the generator detects a reference to `ZInject.Container`, it automatically emits two generated provider classes per assembly.

### Hybrid Mode (MS DI integration)

Wraps the generated container around an MS DI fallback. Unknown service types (framework services, third-party) are resolved by the MS DI provider. Use this for ASP.NET Core or any host that requires `IServiceCollection` integration.

**Console App:**
```csharp
var services = new ServiceCollection();
services.AddMyAppServices(); // Generated registration method

IServiceProvider provider = services.BuildZInjectServiceProvider();
var myService = provider.GetRequiredService<IMyService>();
```

**ASP.NET Core:**
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMyAppServices();
builder.Host.UseServiceProviderFactory(new ZInjectServiceProviderFactory());

var app = builder.Build();
```

### Standalone Mode (no MS DI runtime)

A fully self-contained provider with no dependency on `Microsoft.Extensions.DependencyInjection` at runtime. Instantiated directly — no `ServiceCollection`, no `BuildServiceProvider`. Unknown service types return `null`.

```csharp
// Instantiate directly — no ServiceCollection needed
IServiceProvider provider = new MyAppStandaloneServiceProvider();
var myService = provider.GetRequiredService<IMyService>();
```

Use this for console tools, background workers, or any scenario where you own all the services and don't need framework integration.

### How It Works

The generated container uses a type-switch (`if`/`else if` chain on `typeof(T)`) to resolve services directly, bypassing dictionary lookups and reflection. Singletons are stored in fields and initialized via `Interlocked.CompareExchange`. Scoped services are tracked per-scope and disposed in reverse registration order.

### Current Limitations

- **Open generics** (e.g., `IRepository<>`) delegate to the fallback in hybrid mode. In standalone mode, closed types are enumerated at compile time via constructor parameter analysis — services that are never used as a constructor parameter in the assembly won't be resolvable (ZI018 warning).

## Benchmarks

All benchmarks on .NET 9.0, BenchmarkDotNet v0.15.8, Windows 11 (Intel Core i9-12900HK), X64 RyuJIT AVX2.

### Startup / Registration

| Method | Mean | Allocated |
|---|---:|---:|
| MS DI — `BuildServiceProvider()` | 139 ns | 528 B |
| ZInject Container — `BuildZInjectServiceProvider()` | 4,477 ns | 9,368 B |
| Standalone — `new MyAppStandaloneServiceProvider()` | **5 ns** | **32 B** |

The hybrid container has a one-time build cost (generating internal data structures). The standalone provider has virtually none.

### Resolution

| Scenario | MS DI | ZInject Container | Standalone |
|---|---:|---:|---:|
| Transient (no deps) | 23 ns | **13 ns** | 15 ns |
| Transient (1 dep) | 20 ns | 28 ns | **27 ns** |
| Transient (2 deps) | 44 ns | 56 ns | **55 ns** |
| Singleton | 16 ns | **11 ns** | 12 ns |
| Decorated transient | 41 ns / 48 B | **18 ns / 48 B** | 17 ns / 48 B |
| `IEnumerable<T>` (3 impls) | **63 ns** | 70 ns | 89 ns |
| Create scope | 71 ns / 128 B | 141 ns / 216 B | **56 ns / 88 B** |
| Resolve scoped (scope + resolve + dispose) | ~26,000 ns / 808 B | ~12,300 ns / 120 B | **~12,000 ns / 120 B** |
| Open generic (compile-time closed) | 21 ns / 24 B | *(delegates to MS DI)* | **19 ns / 24 B** |

The standalone provider's `CreateScope` is ~2.5× faster and uses ~60% less memory than the hybrid mode because it doesn't allocate a fallback scope wrapper. Decorated transients resolve **~2.3× faster** than MS DI across both generated modes. Scoped resolution (full lifecycle) uses only 120 B vs MS DI's 808 B — ~7× less memory. Open-generic resolution in standalone hits a direct `typeof(T)` branch — ~2× faster than MS DI with no resolution overhead beyond creating the object itself.

## Native AOT

Because ZInject generates all service instantiation as plain `new ClassName(...)` constructor calls at compile time, it is compatible with Native AOT publishing — no `Activator.CreateInstance`, no `Type.GetMethod`, no reflection in generated code.

| Mode | Native AOT |
|---|---|
| `AddXxxServices()` extension method | ✅ Generated registration code is AOT-safe. Runtime resolution uses your MS DI configuration. |
| Standalone container (closed generics) | ✅ Fully AOT-compatible. Direct `new` calls, `typeof(T)` type switches, `Interlocked.CompareExchange` for singletons — zero reflection. |
| Standalone container (open generics) | ✅ Compile-time enumerated. Closed types are discovered via constructor parameter analysis at build time. Fully AOT-safe. |
| Hybrid container (known services) | ✅ AOT-safe for services registered with ZInject. |
| Hybrid container (unknown services) | ⚠️ Falls back to MS DI, which uses reflection. |

**The standalone container is 100% AOT-compatible**, including open generics. Closed types are enumerated at compile time via constructor parameter analysis — zero reflection at runtime.

## How It Compares to Scrutor

| | ZInject | Scrutor |
|---|---|---|
| Discovery | Compile-time source gen | Runtime assembly scanning |
| Reflection | None | Yes |
| Native AOT | ✅ Standalone mode | ❌ |
| Startup cost | Zero | Scales with assembly size |
| IDE support | Compile errors + warnings | Runtime exceptions |
| Configuration | Attributes on classes | Fluent API in `Program.cs` |

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- C# 12+

## License

MIT
