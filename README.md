# ZeroAlloc.Inject

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Inject.svg)](https://www.nuget.org/packages/ZeroAlloc.Inject)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Inject/graph/badge.svg)](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Inject)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Compile-time dependency injection for .NET: put `[Transient]`, `[Scoped]` or `[Singleton]` on a class
and the generator writes the registrations, with no reflection, no assembly scanning and NativeAOT-ready.

## Why ZeroAlloc.Inject

- **Starts almost instantly.** Building the standalone provider takes about 1% of the time and 112 B,
  against 4,384 B for Microsoft DI's `BuildServiceProvider()`.
- **About 2× faster than Microsoft DI where the generator knows your graph.** Decorated services and
  property injection resolve in 0.59× and 0.45× of its time. Singleton and cached scoped resolves
  allocate 0 B, asserted by `AllocationGate` tests in both container modes.
- **Works under NativeAOT and trimming.** Every PR publishes and runs an AOT sample with trim warnings
  as errors.
- **Compile-time diagnostics instead of runtime surprises.** An ambiguous constructor, a decorator over
  an unregistered interface or a circular dependency is a build error, from `ZAI001` to `ZAI021`.

## Install

```bash
dotnet add package ZeroAlloc.Inject.Container
```

The container package bundles the attributes, the generator and both generated containers. ASP.NET Core
apps that keep Microsoft DI can reference `ZeroAlloc.Inject` and `ZeroAlloc.Inject.Generator` instead.

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject;

var services = new ServiceCollection();
services.AddMyAppServices();                 // generated from the assembly name, MyApp
using var provider = services.BuildServiceProvider();

provider.GetRequiredService<IGreeter>().Greet("Ada");

public interface IClock { DateTime Now { get; } }
public interface IGreeter { void Greet(string name); }

[Singleton]
public sealed class SystemClock : IClock { public DateTime Now => DateTime.Now; }

[Transient]
public sealed class Greeter(IClock clock) : IGreeter
{
    public void Greet(string name) => Console.WriteLine($"Hello {name}, it is {clock.Now:t}.");
}
```

For a container with no Microsoft DI at runtime, replace the first three lines with
`using var provider = new ZeroAlloc.Inject.Generated.MyAppStandaloneServiceProvider();`. Either way,
resolution is a generated type switch over direct `new` calls.

## What you get

- **Lifetime attributes** that register a class under its interfaces, with `As` to pick the service type.
- **Three container modes:** generated `IServiceCollection` extensions for Microsoft DI, a hybrid
  container that falls back to Microsoft DI for framework services, and a standalone container.
  See [container modes](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/docs/container-modes.md).
- **Decorators** with ordering, conditional application and optional dependencies.
  See [decorators](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/docs/decorators.md).
- **Keyed services**, **open generics** and **property injection**.
- **Safe defaults:** `TryAdd` semantics, with `AllowMultiple = true` for `IHostedService` and other
  multi-registrations.
- **Multi-assembly** composition: each assembly generates its own `Add…Services()` method.

## Performance

BenchmarkDotNet, .NET 10, same run. Time is the ratio against Microsoft DI; allocations are exact.

| Scenario | Microsoft DI | ZA Container | ZA Standalone | Jab |
|---|---|---|---|---|
| Build the provider | 1.00× / 4,384 B | 0.67× / 2,720 B | 0.01× / 112 B | <0.01× / 40 B |
| Decorated transient | 1.00× / 48 B | 0.59× / 48 B | 0.56× / 48 B | 0.26× / 48 B |
| First scoped resolve in a new scope | 1.00× / 304 B | 0.66× / 120 B | 0.54× / 120 B | 0.54× / 120 B |

On .NET 10 Jab is the fastest in every scenario it supports; it has no property injection, no decorator attribute and no open generics. Microsoft DI is ahead of ZA with two constructor dependencies and for open generics. Every
scenario, with the full numbers: [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/docs/performance.md).

## Learn more

- Documentation: [inject.zeroalloc.net](https://inject.zeroalloc.net), including
  [getting started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/docs/getting-started.md), [Native AOT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/docs/native-aot.md),
  [diagnostics](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/docs/diagnostics.md) and [testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/docs/testing.md).
- Related packages: [ZeroAlloc.Mediator](https://github.com/ZeroAlloc-Net/ZeroAlloc.Mediator),
  [ZeroAlloc.Collections](https://github.com/ZeroAlloc-Net/ZeroAlloc.Collections) and
  [ZeroAlloc.Templates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Templates).
- Contributing: issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/issues).
- [CHANGELOG](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/CHANGELOG.md).

## License

MIT. See [LICENSE](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/blob/main/LICENSE).
