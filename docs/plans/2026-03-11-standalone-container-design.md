# Standalone Container Mode Design

## Problem

The generated container always requires a MS DI fallback provider. For console apps and scenarios where all services are ZeroInject-attributed, this adds unnecessary overhead:
- Scope creation is 1.6x slower (143.9 ns vs MS DI's 87.7 ns) due to fallback scope creation
- Extra 88 bytes per scope for fallback scope reference
- Container build requires `ServiceCollection` + `BuildServiceProvider` (8,013 ns)

## Approach

Add a standalone mode with separate base classes. No fallback provider, no `ServiceCollection`, direct instantiation via parameterless constructor. The generator emits both hybrid and standalone provider classes — user picks which to instantiate.

## Design

### Two Modes

- **Hybrid mode** (existing): `GeneratedProvider : ZeroInjectServiceProviderBase` — takes fallback `IServiceProvider`, delegates unknowns to it
- **Standalone mode** (new): `GeneratedStandaloneProvider : ZeroInjectStandaloneProvider` — no fallback, unknown types return null

### Separate Base Classes

Standalone gets its own base classes in `ZeroInject.Container`:

**`ZeroInjectStandaloneProvider`** — implements `IServiceProvider`, `IServiceScopeFactory`, `IDisposable`, `IAsyncDisposable`:
- No `_fallback` field
- `GetService`: returns `ResolveKnown(type)` or null (no fallback delegation)
- `CreateScope`: creates standalone scope directly (no fallback scope factory lookup)
- Disposal: disposes singleton instances only

**`ZeroInjectStandaloneScope`** — implements `IServiceScope`, `IServiceProvider`, `IDisposable`, `IAsyncDisposable`:
- No `_fallbackScope` field
- Same `_trackLock` + `_disposables` + `_disposed` pattern as `ZeroInjectScope`
- `GetService`: returns `ResolveScopedKnown(type)` or null
- Same `TrackDisposable` thread-safe pattern

### Why Separate Base Classes (Not Null Checks)

- No null checks on hot path — standalone `GetService` is a direct return
- No dead fields — scope doesn't carry unused `_fallbackScope` reference
- Clean single-responsibility separation
- The `ResolveKnown` / `ResolveScopedKnown` generated code is identical either way

### Generator Changes

The generator emits **two provider classes** when `ZeroInject.Container` is referenced:

```csharp
// Hybrid (existing)
sealed class MyAppServiceProvider : ZeroInjectServiceProviderBase { ... }

// Standalone (new)
sealed class MyAppStandaloneServiceProvider : ZeroInjectStandaloneProvider { ... }
```

Both share identical `ResolveKnown` and `ResolveScopedKnown` method bodies. The only differences:
- Base class name
- Constructor (parameterless vs fallback parameter)
- Scope nested class base (`ZeroInjectStandaloneScope` vs `ZeroInjectScope`)
- `CreateScopeCore` signature (no `IServiceScope` parameter)

### Usage

```csharp
// Standalone — zero MS DI overhead
var provider = new MyAppStandaloneServiceProvider();
var svc = provider.GetRequiredService<IMyService>();

// Hybrid — with MS DI fallback (existing, unchanged)
var services = new ServiceCollection();
services.AddMyAppServices();
var provider = services.BuildZeroInjectServiceProvider();
```

### Unknown Type Behavior

In standalone mode, `GetService` for unregistered types returns null — standard `IServiceProvider` contract. `GetRequiredService` throws as usual.

## Testing

### Generator tests
- Standalone provider class emitted alongside hybrid
- Inherits from `ZeroInjectStandaloneProvider`
- Standalone scope inherits from `ZeroInjectStandaloneScope`
- `ResolveKnown` body identical between hybrid and standalone
- Parameterless constructor generated

### Integration tests
- Resolve transient, singleton, scoped in standalone mode
- `IEnumerable<T>` resolution works
- Unknown type returns null
- `GetRequiredService` for unknown type throws
- Scope creation and disposal tracks disposables
- Singleton identity consistent across root and scope
- `IServiceProvider` and `IServiceScopeFactory` self-resolution

### Benchmarks
- Add standalone as 4th provider column in existing benchmarks
- Key metrics: scope creation, singleton resolution, transient resolution
