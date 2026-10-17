---
id: performance
title: Performance
slug: /docs/performance
description: Startup time and resolution benchmarks for ZeroAlloc.Inject vs MS DI and Jab.
sidebar_position: 8
---

# Performance

## Methodology

All benchmarks were collected with the following configuration:

- **BenchmarkDotNet** v0.15.8, measured 2026-10-17 on an otherwise idle machine
- **.NET** 10.0.13, SDK 10.0.402
- **OS** Windows 11 (10.0.26300.9711)
- **CPU** Intel Core i9-12900HK 2.50 GHz, 1 CPU, 20 logical and 14 physical cores
- **JIT** X64 RyuJIT x86-64-v3
- **Source** `benchmarks/ZeroAlloc.Inject.Benchmarks/`

> Allocations are exact. Speed is shown as same-run ratios; absolute timings are not published.

Every benchmark ran out of process in three launches, pinned to two performance cores
(`--affinity 20`). Each table gives each container's mean time relative to MS DI in the same
scenario and the same run, with the lowest and highest ratio of the three launches in brackets.
A ratio below 1 is faster than MS DI.

Four containers participate in every scenario where applicable:

- **MS DI** (`Microsoft.Extensions.DependencyInjection`) — reflection-based runtime registration, the .NET default
- **ZA.Inject Container** — hybrid mode, ZA-generated provider wrapping `IServiceCollection` for framework-service fallback
- **ZA.Inject Standalone** — pure generated provider, no `IServiceCollection`, smallest startup footprint
- **Jab** — the other source-gen DI library; included as the direct rival for the "compile-time DI" positioning

**Apples-to-apples**: every container resolves through the same `IServiceProvider.GetService(...)` extension method path (MS DI's `GetRequiredService<T>()`) — Jab's typed accessor `GetService<T>()` resolves via the same surface. The benchmark Services.cs file defines exactly one service graph; each container registers the same set.

**Where competitors are absent** is documented in the table: Jab is constructor-only (no property injection) and does not support open-generic registration in 0.10.x, so those rows are MS DI + ZA only.

**What each benchmark measures:**

- _Registration benchmarks_ measure the one-time cost of producing a ready-to-use provider — `BuildServiceProvider()`, `BuildZeroAllocInjectServiceProvider()`, `new MyAppStandaloneServiceProvider()`, or `new JabContainer()`.
- _Resolution benchmarks_ measure the steady-state per-call cost of resolving a single service from an already-built provider.

---

## Registration / Startup Benchmarks

<!-- BENCH:START -->
_Last refreshed: 2026-10-17_

| Method | Ratio | Allocated |
|---|---:|---:|
| MS DI — `BuildServiceProvider()` | 1.00 | 4,384 B |
| ZA.Inject Container — `BuildZeroAllocInjectServiceProvider()` | **0.67** (0.63–0.74) | **2,720 B** |
| ZA.Inject Standalone — `new …StandaloneServiceProvider()` | **0.01** (0.01–0.01) | 112 B |
| Jab — `new JabContainer()` | <0.01 | **40 B** |

`BuildZeroAllocInjectServiceProvider()` snapshots the `IServiceCollection` and **lazy-builds the MS DI fallback `IServiceProvider` on first need** via `Interlocked.CompareExchange`. Applications whose registrations are fully ZA-owned (the common case for `[Singleton]`/`[Transient]`/`[Scoped]`-attributed services) never pay the `BuildServiceProvider()` cost at all. The standalone provider and Jab are plain constructor calls, two orders of magnitude cheaper still.

## Resolution Benchmarks

| Scenario | MS DI | ZA Container | ZA Standalone | Jab | Allocated |
|---|---:|---:|---:|---:|---:|
| Transient (no deps) | 1.00 | 0.62 (0.54–0.68) | 0.58 (0.51–0.69) | **0.30** (0.25–0.36) | 24 B |
| Transient (1 dep) | 1.00 | 0.98 (0.87–1.11) | 0.92 (0.83–1.02) | **0.54** (0.52–0.55) | 48 B |
| Transient (1 property dep) | 1.00 | **0.45** (0.43–0.47) | **0.45** (0.44–0.46) | N/A¹ | 48 B |
| Transient (2 deps) | 1.00 | 1.09 (1.03–1.13) | 1.14 (1.07–1.21) | **0.62** (0.57–0.67) | 104 B |
| Singleton | 1.00 | 0.85 (0.74–1.03) | 0.83 (0.71–1.10) | **0.19** (0.15–0.21) | 0 B |
| Decorated transient | 1.00 | 0.59 (0.57–0.62) | 0.56 (0.51–0.60) | **0.26**² (0.23–0.28) | 48 B |
| `IEnumerable<T>` (3 impls) | 1.00 | 0.96 (0.88–1.01) | 0.90 (0.75–0.99) | **0.62** (0.50–0.70) | 168 B |
| Open generic (closed type) | **1.00** | 1.12 (1.07–1.21) | 1.08 (1.07–1.09) | N/A³ | 24 B |
| Create scope | 1.00 / 128 B | 1.09 (1.05–1.13) / 96 B | 0.85 (0.84–0.86) / 88 B | **0.16** (0.16–0.17) / **40 B** | — |
| Resolve scoped, first in a new scope⁴ | 1.00 / 304 B | 0.66 (0.59–0.78) / 120 B | **0.54** (0.49–0.63) / 120 B | **0.54** (0.52–0.57) / 120 B | — |
| Resolve scoped, cached⁵ | 1.00 | 0.23 (0.22–0.24) | 0.22 (0.21–0.23) | **0.04** (0.03–0.04) | 0 B |

_¹ Jab is constructor-only — no property injection._
_² Jab decorator wired via factory (no first-class decorator attribute)._
_³ Jab 0.10.x requires closed types at the `[ServiceProvider]` attribute level._
_⁴ One resolve per fresh scope: BenchmarkDotNet runs one invocation per iteration, so this is the first, creating resolve, and the scope itself is created outside the measurement. The 120 B is the benchmark's `ScopedService`, a 24 B object plus its 96 B Guid string; ZA and Jab add nothing to it, MS DI adds 184 B._
_⁵ A service already resolved in a scope created in `[GlobalSetup]`._

On .NET 10, **Jab is the fastest container in every scenario it supports**, and the lightest on scope creation. ZA.Inject's clear wins over MS DI are where the generator's domain knowledge matters: property injection (2.2× faster), decorators (1.7×), cached scoped resolves (4.3×) and the first resolve in a new scope (1.5–1.9×), all of which also allocate no more than MS DI. With two dependencies, for open generics and, in hybrid mode, for scope creation, ZA is up to 14% slower than MS DI.

The **ZA.Inject Container** creates the MS DI fallback scope only on first fallback resolution, via `Interlocked.CompareExchange`. For applications whose registrations are fully ZA-owned (the common case), the fallback scope is never created at all, and creating a scope does not build the MS DI fallback provider either: it is built only if something asks for a service no registration provides. **`IEnumerable<T>` resolutions are also cached** when every entry is `Singleton`: `EnumerableCacheTests` checks that the same array comes back, and `ResolutionAllocationTests` asserts 0 B for repeat resolutions in both modes; the benchmark row above shows the all-`Transient` registration path, which remains genuinely allocation-bound — the cache fires for the singleton-multi-impl scenario.

<!-- BENCH:END -->

### Transients with dependencies

Without dependencies both ZA modes resolve a transient in about 0.6× MS DI's time, and Jab in 0.3×. With one dependency the ZA modes and MS DI land within the launch spread of each other, and with two dependencies MS DI's cached constructor metadata puts it 9–14% ahead of ZA, which pays a small per-dependency type-switch cost. Jab stays ahead of all three at 0.54–0.62×.

### Property injection (~2× faster than MS DI, Jab N/A)

ZA's `[Inject]` property injection takes **0.45× MS DI's time in both modes**, a 2.2× speedup. MS DI must evaluate a registered factory delegate through its service-descriptor dispatch layer on every call. ZA's generated code reaches the same `new T()` + property-assignment IL via a direct type-switch branch that JITs inline.

Jab does not support property injection; users must restructure as constructor injection.

**Property injection vs constructor injection (same 1 dep),** each as a ratio of MS DI's constructor-injection time in the same run:

| Mode | Constructor | Property |
|---|---:|---:|
| MS DI | 1.00 | 2.18 |
| ZA Container | 0.98 | 0.98 |
| ZA Standalone | 0.92 | 0.98 |
| Jab | 0.54 | N/A |

MS DI's property injection costs about twice its constructor injection; in both ZA modes the two cost the same.

### Decorated transients (~1.7× faster than MS DI)

Decorated services resolve in **0.56–0.59× MS DI's time in both ZA modes**, a 1.7× speedup. ZA's `[Decorator]` attribute emits a direct `new OuterDecorator(new InnerService())` call in the type-switch with no dictionary access. Jab needs a factory binding (no `[Decorator]` attribute) and is faster still, at 0.26×.

### `IEnumerable<T>` resolution

The ZA modes resolve close to MS DI (0.90–0.96×), and Jab in 0.62×. Every container materialises a fresh array per call, so all four allocate the same 168 B (24 B header + 3 instances × ~48 B).

### Scope creation (Jab's strength)

| Mode | Ratio | Allocated |
|---|---:|---:|
| Jab | **0.16** (0.16–0.17) | **40 B** |
| ZA Standalone | 0.85 (0.84–0.86) | 88 B |
| MS DI | 1.00 | 128 B |
| ZA Container | 1.09 (1.05–1.13) | 96 B |

Jab is in a class of its own here, 5–7× faster than the ZA modes. Both ZA modes allocate less than MS DI. The ZA hybrid container creates the MS DI fallback scope only when a resolve needs it, so its scope allocates nearly as little as the standalone one, though it costs slightly more time than MS DI's.

### Resolve scoped — first resolve in a new scope

| Mode | Ratio | Allocated |
|---|---:|---:|
| ZA Standalone | **0.54** (0.49–0.63) | 120 B |
| Jab | **0.54** (0.52–0.57) | 120 B |
| ZA Container | 0.66 (0.59–0.78) | 120 B |
| MS DI | 1.00 | 304 B |

The 120 B is not container overhead: it is the benchmark's `ScopedService`, a 24 B object plus its 96 B Guid string, which every container has to create on the first resolve in a scope. ZA modes and Jab add **nothing** to it; MS DI adds 184 B, for 304 B in all. Creating the scope is measured separately under scope creation above.

### Resolve scoped — cached

Once a scope has resolved a service, every later resolve in that scope returns the cached instance. All four containers do that at **0 B**; ZA.Inject asserts it in `ResolutionAllocationTests`, for both the hybrid and the standalone container, alongside steady-state singleton resolution.

### Singleton resolution

All four containers allocate 0 B. The ZA modes take 0.83–0.85× MS DI's time, within the launch spread of it, and Jab takes 0.19×.

---

## Native AOT

All ZA modes are fully AOT-compatible — no reflection, no `MakeGenericType`, no `Activator.CreateInstance`. Jab is also AOT-compatible. MS DI works under AOT in .NET 8+ but requires `DynamicallyAccessedMembers` annotations on every constructor.

See [Native AOT](native-aot.md) for the `<PublishAot>true</PublishAot>` setup.

---

## Running the Benchmarks Yourself

```bash
# All benchmarks, as measured above
dotnet run -c Release --project benchmarks/ZeroAlloc.Inject.Benchmarks -f net10.0 -- --filter "*" --launchCount 3

# Only resolution
dotnet run -c Release --project benchmarks/ZeroAlloc.Inject.Benchmarks -- --filter "*Resolution*"

# Only registration
dotnet run -c Release --project benchmarks/ZeroAlloc.Inject.Benchmarks -- --filter "*Registration*"
```

> Release configuration is required. Debug builds disable inlining and inflate all numbers.

---

## When to Use Which Mode

| Scenario | Recommended | Reason |
|---|---|---|
| Startup time critical (serverless, CLI) | ZA Standalone or Jab | Building the provider takes about 1% of MS DI `BuildServiceProvider()` |
| Decorators, property injection, open generics | ZA (either mode) | Features Jab lacks; decorators and property injection 1.7–2.2× faster than MS DI |
| ASP.NET Core with framework services | ZA Container (hybrid) | Required for framework service resolution |
| Pure scope lifecycle throughput | Jab or ZA Standalone | Jab creates scopes ~6× faster than MS DI; ZA Standalone and Jab tie on the first scoped resolve, ~1.9× faster than MS DI |
| AOT publishing | ZA Standalone or Jab | Zero reflection |
| Gradual migration / third-party DI compatibility | MS DI extension method | No behaviour change, compile-time registration only |
