---
id: performance
title: Performance
slug: /docs/performance
description: Startup time and resolution benchmarks for MS DI Extension, Hybrid, and Standalone modes.
sidebar_position: 8
---

# Performance

## Methodology

All benchmarks were collected with the following configuration:

- **BenchmarkDotNet** v0.15.8
- **.NET** 9.0.14
- **OS** Windows 11 (10.0.26200.7840 / 25H2)
- **CPU** Intel Core i9-12900HK 2.50 GHz, 1 CPU, 20 logical and 14 physical cores
- **JIT** X64 RyuJIT AVX2

**What each benchmark measures:**

- _Registration benchmarks_ measure the one-time cost of calling the setup method that produces a ready-to-use `IServiceProvider` — `BuildServiceProvider()`, `BuildZeroAllocInjectServiceProvider()`, or `new MyAppStandaloneServiceProvider()`.
- _Resolution benchmarks_ measure the cost of resolving a single service (or performing a single scoped lifecycle) from an already-built provider. Each scenario is run in a tight loop so the numbers represent the steady-state per-call cost.

---

## Registration / Startup Benchmarks

| Method | Mean | Allocated |
|---|---:|---:|
| MS DI — `BuildServiceProvider()` | 139 ns | 528 B |
| ZeroAlloc.Inject Container — `BuildZeroAllocInjectServiceProvider()` | 4,477 ns | 9,368 B |
| Standalone — `new MyAppStandaloneServiceProvider()` | 5 ns | 32 B |

The standalone provider wins by a very wide margin because there is genuinely nothing to build: the generated constructor stores a handful of field references and returns. The hybrid container pays a one-time cost to wrap the generated provider around the underlying MS DI `ServiceProvider` and allocate the internal data structures that support the fallback path — roughly 32× more time and 18× more memory than plain MS DI. If your application starts infrequently (a long-running web server, for example) the difference between 139 ns and 4,477 ns is imperceptible; if you run a CLI tool or a serverless function that cold-starts on every invocation, the 5 ns standalone instantiation is the clear choice.

---

## Resolution Benchmarks

| Scenario | MS DI | ZeroAlloc.Inject Container | Standalone |
|---|---:|---:|---:|
| Transient (no deps) | 23 ns | 13 ns | 15 ns |
| Transient (1 dep) | 20 ns | 28 ns | 27 ns |
| Transient (2 deps) | 44 ns | 56 ns | 55 ns |
| Singleton | 16 ns | 11 ns | 12 ns |
| Decorated transient | 41 ns / 48 B | 18 ns / 48 B | 17 ns / 48 B |
| `IEnumerable<T>` (3 impls) | 63 ns | 70 ns | 89 ns |
| Create scope | 71 ns / 128 B | 141 ns / 216 B | 56 ns / 88 B |
| Resolve scoped (scope + resolve + dispose) | ~26,000 ns / 808 B | ~12,300 ns / 120 B | ~12,000 ns / 120 B |
| Open generic (compile-time closed) | 21 ns / 24 B | (delegates to MS DI) | 19 ns / 24 B |

### Transients with dependencies

A simple transient with no dependencies resolves ~44 % faster in the generated containers than in MS DI (13–15 ns vs. 23 ns). As soon as dependencies are involved the picture reverses slightly: one dependency costs 28 ns in the hybrid container versus 20 ns in MS DI. The overhead comes from the generated `if`/`else if` type-switch that must be evaluated for each dependency the constructor needs. MS DI caches `ConstructorInfo` and parameter metadata after the first call, so its marginal cost per dependency is very low. The generated container makes a direct constructor call — no reflection — but the type-switch traversal adds a small, linear overhead per dependency. For services with two or more dependencies the numbers converge (55–56 ns vs. 44 ns) and will likely become indistinguishable at three or more dependencies as the allocation and construction cost dominates.

### Decorated transients (~2.3x faster)

Decorated services resolve in 17–18 ns across both generated modes versus 41 ns for MS DI — roughly 2.3× faster. MS DI resolves decorators by performing multiple dictionary lookups (one for the outer decorator, one for the inner service), allocating intermediate `ServiceDescriptor` state, and going through the reflection-based activation path. The generated container emits a direct `new OuterDecorator(new InnerService())` call sequence with a single type-switch entry — the entire chain is inlined into a handful of IL instructions with no dictionary access.

### Scoped resolution uses ~7x less memory (120 B vs. 808 B)

The full scoped lifecycle — `CreateScope()`, `GetRequiredService<T>()`, `DisposeAsync()` — allocates only 120 B in both generated modes versus 808 B in MS DI. MS DI's `ServiceScope` carries a `ServiceProvider` snapshot, a `ConcurrentDictionary` of realized services, and housekeeping lists for disposables. The generated scope tracks only what the generator knows is required: a fixed array of scoped service slots and a disposal list sized exactly to the number of registered scoped services. There is no dictionary, no lock, and no overhead for service types that do not exist in the assembly. At high request throughput this translates directly into reduced GC pressure.

### Standalone `CreateScope` is ~2.5x faster than hybrid

Creating a scope costs 56 ns / 88 B in standalone mode versus 141 ns / 216 B in hybrid mode. The hybrid container must allocate a wrapper object that holds both the generated scope and a corresponding MS DI `IServiceScope` for the fallback path — even when the fallback is never used during that scope's lifetime. The standalone container allocates only the generated scope object itself, with no wrapper and no fallback reference.

---

## Running the Benchmarks Yourself

```bash
# Run all benchmarks
dotnet run -c Release --project benchmarks/ZeroAlloc.Inject.Benchmarks

# Run only resolution benchmarks
dotnet run -c Release --project benchmarks/ZeroAlloc.Inject.Benchmarks -- --filter "*Resolution*"

# Run only registration benchmarks
dotnet run -c Release --project benchmarks/ZeroAlloc.Inject.Benchmarks -- --filter "*Registration*"
```

> BenchmarkDotNet requires Release configuration for accurate results. Debug builds disable inlining and optimizations, which inflates all numbers and makes comparisons meaningless.

---

## When to Use Which Mode

| Scenario | Recommended Mode | Reason |
|---|---|---|
| Startup time is critical (serverless, CLI tools) | Standalone | ~5 ns startup vs. 4,477 ns (hybrid) or 139 ns (MS DI) |
| Decorated services, heavy singleton use | Any generated mode | ~2× faster than MS DI |
| High-throughput scoped services (web APIs) | Standalone or Hybrid | ~7× less memory per scoped lifecycle |
| ASP.NET Core with framework services | Hybrid | Required for framework service resolution |
| AOT publishing | Standalone | Zero reflection |
| Gradual migration / third-party lib compatibility | MS DI extension method | No behavior change, compile-time registration only |
