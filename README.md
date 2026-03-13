# ZInject

[![CI](https://github.com/MarcelRoozekrans/ZInject/actions/workflows/ci.yml/badge.svg)](https://github.com/MarcelRoozekrans/ZInject/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ZInject.svg)](https://www.nuget.org/packages/ZInject)

Compile-time DI registration for .NET. A Roslyn source generator that auto-discovers services via attributes and generates `IServiceCollection` extension methods. No reflection, no runtime scanning.

## Quick Start

Install both packages:

```
dotnet add package ZInject
dotnet add package ZInject.Generator
```

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

## Generated Container

ZInject can replace the default MS DI container with a source-generated `IServiceProvider`. This eliminates reflection-based resolution at runtime. Two modes are available depending on whether you need MS DI integration.

### Installation

Add the `ZInject.Container` package alongside the existing packages:

```
dotnet add package ZInject
dotnet add package ZInject.Generator
dotnet add package ZInject.Container
```

When the generator detects a reference to `ZInject.Container`, it automatically emits two generated provider classes per assembly.

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

- **Open generics** (e.g., `IRepository<>`) delegate to the fallback in hybrid mode; in standalone mode they are resolved via code-generated delegate factories with `MakeGenericType` at runtime (cached after the first call per closed type)

## Benchmarks

All benchmarks on .NET 9.0.14, BenchmarkDotNet v0.14.0, Windows 11, X64 RyuJIT AVX2.

### Startup / Registration

| Method | Mean | Allocated |
|---|---:|---:|
| MS DI — `BuildServiceProvider()` | 177 ns | 528 B |
| ZInject Container — `BuildZInjectServiceProvider()` | 9,584 ns | 9,296 B |
| Standalone — `new MyAppStandaloneServiceProvider()` | **9 ns** | **40 B** |

The hybrid container has a one-time build cost (generating internal data structures). The standalone provider has virtually none.

### Resolution

| Scenario | MS DI | ZInject Container | Standalone |
|---|---:|---:|---:|
| Transient (no deps) | 21 ns | **11 ns** | 13 ns |
| Transient (1 dep) | 44 ns | 34 ns | **34 ns** |
| Transient (2 deps) | 43 ns | 79 ns | 83 ns |
| Singleton | 13 ns | **8 ns** | 10 ns |
| Decorated transient | 48 ns / 48 B | 18 ns / 48 B | **11 ns / 48 B** |
| `IEnumerable<T>` (3 impls) | 52 ns | **51 ns** | 47 ns |
| Create scope | 80 ns / 128 B | 123 ns / 216 B | **59 ns / 88 B** |
| Resolve scoped (scope + resolve + dispose) | 9,855 ns / 992 B | 5,811 ns / 520 B | **6,475 ns / 808 B** |
| Open generic (closed at runtime) | 18 ns / 24 B | *(delegates to MS DI)* | **34 ns / 24 B** |

The standalone provider's `CreateScope` is ~2× faster and uses ~60% less memory than the hybrid mode because it doesn't allocate a fallback scope wrapper. Decorated transients resolve **~2× faster** than MS DI across all modes. Open-generic resolution in standalone uses code-generated delegate factories with `MakeGenericType` (~1.9× vs MS DI); the delegate is compiled and cached on the first call per closed type, so subsequent resolutions are near-zero overhead.

## How It Compares to Scrutor

| | ZInject | Scrutor |
|---|---|---|
| Discovery | Compile-time source gen | Runtime assembly scanning |
| Reflection | None | Yes |
| Startup cost | Zero | Scales with assembly size |
| IDE support | Compile errors + warnings | Runtime exceptions |
| Configuration | Attributes on classes | Fluent API in `Program.cs` |

## Requirements

- .NET 8.0, .NET 9.0, or .NET 10.0
- C# 12+

## License

MIT
