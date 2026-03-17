# ZeroAlloc.Inject

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Inject.svg)](https://www.nuget.org/packages/ZeroAlloc.Inject)
[![CI](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Compile-time DI registration for .NET. A Roslyn source generator that auto-discovers services via attributes and generates `IServiceCollection` extension methods and a **Native AOT-compatible** `IServiceProvider`. No reflection, no runtime scanning — misconfigured dependencies surface as build errors before the application ever starts.

## Install

For ASP.NET Core and Generic Host projects (MS DI runtime):

```bash
dotnet add package ZeroAlloc.Inject
dotnet add package ZeroAlloc.Inject.Generator
```

For standalone or hybrid container mode (no separate attribute package needed):

```bash
dotnet add package ZeroAlloc.Inject.Container
```

`ZeroAlloc.Inject.Container` bundles the generator, attributes, and the container base classes in a single package.

## 30-Second Example

```csharp
// 1. Annotate your service with a lifetime attribute
using ZeroAlloc.Inject;

[Transient]                                    // registered as IOrderService + OrderService
public class OrderService : IOrderService
{
    private readonly IProductRepository _repo;

    public OrderService(IProductRepository repo) => _repo = repo;

    public Order PlaceOrder(int productId) => /* ... */
}

// 2. Register all attributed services in one generated call
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMyAppServices();           // generated at compile time from assembly name

// 3. Resolve the service — or let the framework inject it for you
var app = builder.Build();
var service = app.Services.GetRequiredService<IOrderService>();
```

The generator derives the method name from the assembly name: `MyApp` → `AddMyAppServices()`, `MyApp.Domain` → `AddMyAppDomainServices()`. Override with `[assembly: ZeroAllocInject("AddDomainServices")]`.

## Performance

.NET 9.0, BenchmarkDotNet v0.15.8, Windows 11 (Intel Core i9-12900HK), X64 RyuJIT AVX2.

| Scenario | MS DI | Hybrid Container | Standalone |
|---|---:|---:|---:|
| Startup / registration | 139 ns / 528 B | 4,477 ns / 9,368 B | **5 ns / 32 B** |
| Transient (no deps) | 23 ns | **13 ns** | 15 ns |
| Singleton | 16 ns | **11 ns** | 12 ns |
| Decorated transient | 41 ns / 48 B | **18 ns / 48 B** | 17 ns / 48 B |
| Resolve scoped (full lifecycle) | ~26,000 ns / 808 B | ~12,300 ns / 120 B | **~12,000 ns / 120 B** |

Full methodology, all scenarios, and analysis: [docs/performance.md](docs/performance.md).

## Features

- **Compile-time registration** — `[Transient]`, `[Scoped]`, `[Singleton]` attributes; the generator emits all `IServiceCollection.Add*` calls
- **Zero reflection** — generated code uses direct `new ClassName(...)` constructor calls and `typeof(T)` type switches
- **Native AOT compatible** — standalone container is 100% AOT-safe including open generics
- **Three container modes** — MS DI extension method, Hybrid (wraps MS DI), and Standalone (no runtime MS DI dependency)
- **Decorators** — `[Decorator]` / `[DecoratorOf]` with ordering, conditional application (`WhenRegistered`), and optional dependencies
- **Keyed services** — `Key = "redis"` on any lifetime attribute (.NET 8+)
- **Open generics** — single attribute covers all closed forms; standalone mode enumerates closed usages at compile time
- **Compile-time diagnostics** — ZAI001–ZAI018 reported as build errors/warnings, including circular dependency detection
- **`TryAdd` by default** — prevents duplicate registrations; opt in to `AllowMultiple = true` for `IHostedService` scenarios
- **Multi-assembly** — each assembly generates its own extension method; call them in sequence at the composition root

## Documentation

| Page | Description |
|---|---|
| [Getting Started](docs/getting-started.md) | Install, annotate services, call the generated method |
| [Service Registration](docs/service-registration.md) | Lifetime attributes, `As`, keyed services, open generics |
| [Container Modes](docs/container-modes.md) | MS DI Extension, Hybrid, and Standalone — trade-offs and setup |
| [Decorators](docs/decorators.md) | `[Decorator]`, `[DecoratorOf]`, ordering, conditional decorators |
| [Native AOT](docs/native-aot.md) | Trimmer safety, publishing, ASP.NET Core AOT setup |
| [Advanced Patterns](docs/advanced.md) | Multi-assembly, constructor disambiguation, collection injection |
| [Compiler Diagnostics](docs/diagnostics.md) | ZAI001–ZAI018 reference with triggers and fixes |
| [Performance](docs/performance.md) | Full benchmark tables and analysis |
| [Testing](docs/testing.md) | Unit testing without the container, integration test setup per mode |

## How It Compares to Scrutor

| | ZeroAlloc.Inject | Scrutor |
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
