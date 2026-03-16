# Documentation Design — ZeroAlloc.Inject

**Date:** 2026-03-16
**Status:** Approved

## Goal

Write extensive, real-world-grounded documentation for the ZeroAlloc.Inject library. Docs go in `docs/`, split into narrative docs and a `reference/` subfolder. All architecture diagrams use Mermaid.

## Chosen Approach

**Approach C — Mixed: narrative + reference**

Narrative docs for learning, a dedicated `reference/` subfolder for lookup tables. Scales well as the project grows.

## File Structure

```
docs/
├── getting-started.md
├── service-registration.md
├── decorators.md
├── container-modes.md
├── advanced.md
├── native-aot.md
└── reference/
    ├── diagnostics.md
    └── benchmarks.md
```

## Per-file Content

### `getting-started.md`
- Package installation (three variants)
- Annotate a service, call the generated extension method — minimal working example
- Mermaid diagram: attribute → generator → generated code → DI container
- Forward links to container modes

### `service-registration.md`
- Lifetime attributes: `[Transient]`, `[Scoped]`, `[Singleton]`
- Default interface discovery + system interface exclusion list
- `As` narrowing, `Key` (keyed services .NET 8+), `AllowMultiple`
- Open generics
- Custom method name via `[assembly: ZeroAllocInject("...")]`
- Real-world examples: repository pattern, background workers, multiple `IHostedService`
- Mermaid diagram: service class → registered interface mapping

### `decorators.md`
- `[Decorator]` — simple implicit form
- `[DecoratorOf]` — explicit, with `Order`, `WhenRegistered`, `[OptionalDependency]`
- Mermaid layering diagram: caller → outer decorator → inner decorator → real implementation
- Real-world example: logging + tracing + caching stack
- Conditional decorator pattern with feature flags

### `container-modes.md`
- Mermaid decision flowchart: choosing between MS DI extension, Hybrid, Standalone
- Hybrid mode: ASP.NET Core and Console App setup
- Standalone mode: direct instantiation, scope lifecycle
- Mermaid scope lifecycle diagram
- Trade-off table: startup cost, AOT support, open generics, framework services

### `advanced.md`
- Multi-assembly setups: chaining `AddXxxServices()` calls
- `[ActivatorUtilitiesConstructor]` for multiple constructors
- `IEnumerable<T>` resolution with `AllowMultiple`
- Scoped-inside-singleton pitfall

### `native-aot.md`
- AOT compatibility matrix per mode
- Step-by-step: publishing a standalone app with Native AOT
- What to avoid (hybrid + unknown services)

### `reference/diagnostics.md`
- Full table: ZAI001–ZAI018 with ID, severity, description, cause, fix example

### `reference/benchmarks.md`
- Registration benchmark table
- Resolution benchmark table
- Methodology (BenchmarkDotNet, hardware spec)
- Interpretation guide
