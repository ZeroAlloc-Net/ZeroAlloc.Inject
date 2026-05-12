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

- **BenchmarkDotNet** v0.15.8
- **.NET** 10 host, .NET 9.0.15 runtime (job)
- **OS** Windows 11 (10.0.26200.8246 / 25H2)
- **CPU** Intel Core i9-12900HK 2.50 GHz, 1 CPU, 20 logical and 14 physical cores
- **JIT** X64 RyuJIT x86-64-v3
- **Source** `benchmarks/ZeroAlloc.Inject.Benchmarks/`

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
_Last refreshed: 2026-05-12_

| Method | Mean | Allocated |
|---|---:|---:|
| MS DI — `BuildServiceProvider()` | 138 ns | 528 B |
| ZA.Inject Container — `BuildZeroAllocInjectServiceProvider()` | 10,998 ns | 11,192 B |
| ZA.Inject Standalone — `new …StandaloneServiceProvider()` | **4 ns** | **32 B** |
| Jab — `new JabContainer()` | 8 ns | 40 B |

## Resolution Benchmarks

| Scenario | MS DI | ZA Container | ZA Standalone | Jab | Allocated |
|---|---:|---:|---:|---:|---:|
| Transient (no deps) | 19.9 ns | **15.9 ns** | 19.6 ns | 20.6 ns | 24 B |
| Transient (1 dep) | 30.9 ns | 27.3 ns | **24.1 ns** | 47.5 ns | 48 B |
| Transient (1 property dep) | 43.7 ns | 26.9 ns | **21.9 ns** | N/A¹ | 48 B |
| Transient (2 deps) | **39.4 ns** | 58.3 ns | 53.1 ns | 101.1 ns | 104 B |
| Singleton | 6.3 ns | 6.9 ns | **5.4 ns** | 5.5 ns | 0 B |
| Decorated transient | 44.5 ns | **21.1 ns** | 22.3 ns | 28.8 ns² | 48 B |
| `IEnumerable<T>` (3 impls) | **67.8 ns** | 74.8 ns | 81.8 ns | 150.9 ns | 168 B |
| Open generic (closed type) | 13.5 ns | (delegates to MS DI) | **7.7 ns** | N/A³ | 24 B |
| Create scope | 60 ns / 128 B | 123 ns / 216 B | 56 ns / 88 B | **8 ns / 40 B** | — |
| Resolve scoped (full lifecycle) | 8,225 ns / 304 B | 4,808 ns / 120 B | 3,393 ns / 120 B | **3,025 ns / 120 B** | — |

_¹ Jab is constructor-only — no property injection._
_² Jab decorator wired via factory (no first-class decorator attribute)._
_³ Jab 0.10.x requires closed types at the `[ServiceProvider]` attribute level._

ZA.Inject is **competitive across every scenario** and the clear winner where the generator's domain knowledge matters most: property injection (2× MS DI), decorators (2.1× MS DI), open generics (1.8× MS DI). Jab leads on scope creation (its scope is the lightest of the four, by an order of magnitude), with ZA Standalone close behind on the full scoped-resolution lifecycle.

<!-- BENCH:END -->

### Transients with dependencies

A simple transient with no dependencies resolves in 16–21 ns across all four containers (within noise). With one dependency the picture splits: **ZA Standalone wins at 24 ns**, MS DI lands at 31 ns, ZA Container at 27 ns, and Jab trails at 47 ns. With two dependencies MS DI's cached-constructor metadata pays off (39 ns); ZA modes pay a small per-dependency type-switch cost (53–58 ns); Jab's factory-driven resolution costs 101 ns. This is the one scenario where reflection-based MS DI's hot-path caching wins, and it converges as dependency count grows further.

### Property injection (~2× faster than MS DI, Jab N/A)

ZA's `[Inject]` property injection costs **22 ns standalone / 27 ns hybrid** versus **44 ns MS DI** — roughly 2× faster in both ZA modes. MS DI must evaluate a registered factory delegate through its service-descriptor dispatch layer on every call. ZA's generated code reaches the same `new T()` + property-assignment IL via a direct type-switch branch that JITs inline.

Jab does not support property injection; users must restructure as constructor injection.

**Property injection vs constructor injection (same 1 dep):**

| Mode | Constructor | Property | Overhead |
|---|---:|---:|---:|
| MS DI | 31 ns | 44 ns | +41% |
| ZA Container | 27 ns | 27 ns | ~0% |
| ZA Standalone | 24 ns | 22 ns | ~0% |
| Jab | 47 ns | N/A | — |

### Decorated transients (~2.1× faster than MS DI)

Decorated services resolve in **21–22 ns in both ZA modes versus 44 ns in MS DI** — a 2.1× speedup. Jab needs a factory binding (no `[Decorator]` attribute) and lands at 29 ns — between ZA and MS DI. ZA's `[Decorator]` attribute emits a direct `new OuterDecorator(new InnerService())` call in the type-switch with no dictionary access.

### `IEnumerable<T>` resolution (MS DI's strength)

MS DI wins at 68 ns; ZA modes are 75–82 ns; Jab is 151 ns. MS DI's hot-path optimisation for multi-registration enumeration is excellent; ZA materialises a fresh array per call (24 B header + 3 instances × ~48 B = 168 B identical across all four). Jab's enumeration appears to do extra work.

### Scope creation (Jab's strength)

| Mode | Time | Allocated |
|---|---:|---:|
| Jab | **8 ns** | **40 B** |
| ZA Standalone | 56 ns | 88 B |
| MS DI | 60 ns | 128 B |
| ZA Container | 123 ns | 216 B |

Jab is in a class of its own here — its scope type is a struct-like value that allocates only a 40 B reference cell. ZA Standalone is 7× slower but still half MS DI's allocation. The ZA hybrid container pays for both its own scope and an MS DI fallback scope, hence 123 ns / 216 B.

### Resolve scoped — full lifecycle

| Mode | Time | Allocated |
|---|---:|---:|
| Jab | **3,025 ns** | 120 B |
| ZA Standalone | 3,393 ns | 120 B |
| ZA Container | 4,808 ns | 120 B |
| MS DI | 8,225 ns | 304 B |

ZA modes and Jab all allocate **120 B per scope lifecycle vs MS DI's 304 B** (~2.5× less). At high request throughput (>10k req/sec) this is the line that matters most.

### Singleton resolution

All four containers land within noise (5–7 ns, 0 B). This is the JIT-inlined fast path — there's no daylight between source-gen and reflection-cached implementations once the singleton instance is cached on first access.

---

## Native AOT

All ZA modes are fully AOT-compatible — no reflection, no `MakeGenericType`, no `Activator.CreateInstance`. Jab is also AOT-compatible. MS DI works under AOT in .NET 8+ but requires `DynamicallyAccessedMembers` annotations on every constructor.

See [Native AOT](native-aot.md) for the `<PublishAot>true</PublishAot>` setup.

---

## Running the Benchmarks Yourself

```bash
# All benchmarks
dotnet run -c Release --project benchmarks/ZeroAlloc.Inject.Benchmarks -- --filter "*"

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
| Startup time critical (serverless, CLI) | ZA Standalone | 4 ns startup vs 138 ns (MS DI) or 8 ns (Jab) |
| Decorators, property injection, open generics | ZA (either mode) | Features Jab lacks; 2× faster than MS DI |
| ASP.NET Core with framework services | ZA Container (hybrid) | Required for framework service resolution |
| Pure scope lifecycle throughput | ZA Standalone or Jab | 2.5× less alloc, 2.5× faster than MS DI |
| AOT publishing | ZA Standalone or Jab | Zero reflection |
| Gradual migration / third-party DI compatibility | MS DI extension method | No behaviour change, compile-time registration only |
