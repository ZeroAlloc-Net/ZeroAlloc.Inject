# IEnumerable<T> Support for Generated Container

## Problem

The generated container intercepts `GetService(typeof(IFoo))` in `ResolveKnown`, but `GetService(typeof(IEnumerable<IFoo>))` falls through to the MS DI fallback. This creates a singleton identity split: the fallback creates its own instances, so `GetService<ICache>()` and `IEnumerable<ICache>.First()` return different objects.

## Approach

Generate `IEnumerable<T>` checks in `ResolveKnown` (root) and `ResolveScopedKnown` (scope) at compile time. For each service type (interface) with registrations, emit a type check that returns a typed array.

## Design

### Grouping

At generator time, group all non-keyed, non-open-generic services by their service type (interface). Each interface maps to one or more `(ServiceRegistrationInfo, lifetime, fieldIndex)` entries. This map drives array construction for `IEnumerable<T>`.

### Root (`ResolveKnown`)

For each service type with registrations:

```csharp
if (serviceType == typeof(System.Collections.Generic.IEnumerable<IFoo>))
    return new IFoo[] { element0, element1, ... };
```

Element resolution per lifetime:
- **Transient**: `new Foo(...)` — fresh instance each call
- **Singleton**: `(IFoo)GetService(typeof(IFoo))!` — delegates to existing cached field + CompareExchange logic
- **Scoped**: excluded from root (not resolvable at root level)

### Scope (`ResolveScopedKnown`)

Same pattern but includes all lifetimes:
- **Transient**: `new Foo(...)` with `TrackDisposable` if `ImplementsDisposable`
- **Singleton**: `(IFoo)Root.GetService(typeof(IFoo))!` — delegates to root
- **Scoped**: inline lazy-init pattern reusing `_scoped_N` field, with `TrackDisposable` if disposable

### Single resolution: last-wins

When multiple services are registered for the same interface (via `AllowMultiple`), `GetService<IFoo>()` returns the **last** registration (matching MS DI). The generator emits `if` checks so that only the last-registered service's check appears for single resolution. `IEnumerable<T>` returns all in registration order.

### Edge cases

- **Keyed services**: excluded from `IEnumerable<T>` (MS DI resolves keyed services separately)
- **Unknown types**: `IEnumerable<T>` for unregistered interfaces falls through to fallback
- **`IEnumerable<T>` is always transient**: each call creates a new array, items inside respect their lifetimes

## Testing

### Generator tests
- Single transient emits array creation
- Singleton delegates to `GetService`
- `AllowMultiple` with multiple implementations emits multi-element array
- Scoped excluded from root, included in scope
- Last-wins for single `GetService` with multiple registrations

### Integration tests
- `IEnumerable<IFoo>` returns all implementations
- Singleton identity: `GetService<ICache>()` same as `IEnumerable<ICache>` element
- Scoped identity: same instance in single and enumerable resolution
- `IEnumerable<T>` in scope includes all lifetimes
- `IEnumerable<T>` at root excludes scoped
- Unknown `IEnumerable<T>` falls through to fallback
