# ZeroInject

Compile-time DI registration for .NET. A Roslyn source generator that auto-discovers services via attributes and generates `IServiceCollection` extension methods. No reflection, no runtime scanning.

## Quick Start

Install both packages:

```
dotnet add package ZeroInject
dotnet add package ZeroInject.Generator
```

Decorate your services:

```csharp
using ZeroInject;

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
[assembly: ZeroInject("AddDomainServices")]
```

## Diagnostics

ZeroInject reports issues at compile time:

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

## Generated Container (Phase 3)

ZeroInject can replace the default MS DI container entirely with a source-generated `IServiceProvider`. This eliminates reflection-based resolution at runtime for near-zero overhead.

### Installation

Add the `ZeroInject.Container` package alongside the existing packages:

```
dotnet add package ZeroInject
dotnet add package ZeroInject.Generator
dotnet add package ZeroInject.Container
```

When the generator detects a reference to `ZeroInject.Container`, it automatically emits a generated service provider class and a `BuildZeroInjectServiceProvider` extension method.

### Console App Usage

```csharp
var services = new ServiceCollection();
services.AddMyAppServices(); // Generated registration method

IServiceProvider provider = services.BuildZeroInjectServiceProvider();
var myService = provider.GetRequiredService<IMyService>();
```

### ASP.NET Core Usage

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMyAppServices();
builder.Host.UseServiceProviderFactory(new ZeroInjectServiceProviderFactory());

var app = builder.Build();
```

### How It Works

The generated container uses a type-switch (`if`/`else if` chain on `typeof(T)`) to resolve services directly, avoiding dictionary lookups and reflection. For any service type the container does not know about (e.g., framework services), it falls back to the standard MS DI provider.

### Current Limitations (v1)

- **`IEnumerable<T>` resolution** delegates to the fallback MS DI provider
- **Open generics** (e.g., `IRepository<>`) delegate to the fallback MS DI provider
- **`IServiceProviderIsService`** delegates to the fallback MS DI provider

## How It Compares to Scrutor

| | ZeroInject | Scrutor |
|---|---|---|
| Discovery | Compile-time source gen | Runtime assembly scanning |
| Reflection | None | Yes |
| Startup cost | Zero | Scales with assembly size |
| IDE support | Compile errors + warnings | Runtime exceptions |
| Configuration | Attributes on classes | Fluent API in `Program.cs` |

## Requirements

- .NET 8.0 or .NET 10.0
- C# 12+

## License

MIT
