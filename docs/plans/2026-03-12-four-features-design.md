# Design: IServiceProviderIsService, Multi-Decorator Stacking, Circular Dependency Detection

Date: 2026-03-12

## Scope

Four features to bring ZeroInject to production readiness:

1. **`IServiceProviderIsService`** — needed by ASP.NET MVC model binding
2. **Thread safety of scoped resolution** — documentation only (matches MS DI contract)
3. **Multi-decorator stacking** — chain multiple decorators per interface
4. **Compile-time circular dependency detection** — ZI014 diagnostic

## Feature 1: `IServiceProviderIsService`

### Problem

Libraries like ASP.NET MVC check `IServiceProviderIsService` to decide whether to resolve a parameter from DI or bind it from the HTTP request. Without it, middleware silently misbehaves.

### Design

**Hybrid mode** — `ZeroInjectServiceProviderBase` implements `IServiceProviderIsService` and delegates to the fallback provider:

```csharp
public abstract class ZeroInjectServiceProviderBase : IServiceProvider, IServiceScopeFactory,
    IServiceProviderIsService, IDisposable, IAsyncDisposable
{
    public bool IsService(Type serviceType) =>
        ResolveKnown(serviceType) != null
        || (_fallback as IServiceProviderIsService)?.IsService(serviceType) == true;
}
```

Wait — `ResolveKnown` creates instances. We need a check-only path. Two options:

**Option A: Generated `IsKnownService(Type)` abstract method.** The generator emits a method that returns `true` for all registered service types without creating instances. This is clean but adds another abstract method to override.

**Option B: Attempt-and-discard via `ResolveKnown`.** Wasteful — creates and discards transient instances.

**Chosen: Option A.** The generator already has the full list of service types. Emitting a simple type-check method is trivial.

**Hybrid mode:**
```csharp
// Base class
public bool IsService(Type serviceType) =>
    IsKnownService(serviceType)
    || (_fallback as IServiceProviderIsService)?.IsService(serviceType) == true;

protected abstract bool IsKnownService(Type serviceType);
```

**Standalone mode:**
```csharp
// Base class implements IServiceProviderIsService
public bool IsService(Type serviceType) => IsKnownService(serviceType);
protected abstract bool IsKnownService(Type serviceType);
```

**Generated `IsKnownService` override** (same for both modes):
```csharp
protected override bool IsKnownService(Type serviceType)
{
    if (serviceType == typeof(IFoo)) return true;
    if (serviceType == typeof(IBar)) return true;
    // ... all registered service types
    // Open generics:
    if (serviceType.IsGenericType)
    {
        var genDef = serviceType.GetGenericTypeDefinition();
        if (genDef == typeof(IRepo<>)) return true;
    }
    return false;
}
```

**Scope classes** also implement `IServiceProviderIsService` — delegate to root's `IsKnownService`.

### Files changed

- `ZeroInjectServiceProviderBase.cs` — add `IServiceProviderIsService`, `IsKnownService` abstract
- `ZeroInjectStandaloneProvider.cs` — add `IServiceProviderIsService`, `IsKnownService` abstract
- `ZeroInjectScope.cs` — implement `IServiceProviderIsService`, delegate to root
- `ZeroInjectStandaloneScope.cs` — implement `IServiceProviderIsService`, delegate to root
- `ZeroInjectGenerator.cs` — emit `IsKnownService` override in both hybrid and standalone providers + scopes

## Feature 2: Thread Safety — Documentation Only

### Decision

MS DI does **not** lock scoped resolution. Scopes are per-request/per-thread by design. ZeroInject matches this contract. No code changes needed.

### Action

- Update `docs/features.md`: move from "Vital" to "Implemented" with note "Matches MS DI contract: scopes are not thread-safe"
- Optionally add XML doc comment to `ZeroInjectStandaloneScope` noting the contract

## Feature 3: Multi-Decorator Stacking

### Problem

`decoratorsByInterface` is `Dictionary<string, DecoratorRegistrationInfo>` — second decorator silently overwrites first (line 207 of generator). Real apps need chains like `IRepo → CachingRepo → LoggingRepo → RetryRepo`.

### Design

**Data structure change:**
```csharp
// Before:
Dictionary<string, DecoratorRegistrationInfo>
// After:
Dictionary<string, List<DecoratorRegistrationInfo>>
```

**Registration order = wrapping order.** First decorator registered wraps the concrete, last decorator is outermost. This matches MS DI convention.

Given decorators `[Decorator] CachingRepo`, `[Decorator] LoggingRepo`, `[Decorator] RetryRepo` all decorating `IRepo`:

```csharp
// Generated factory (MS DI extension):
services.TryAddTransient<IRepo>(sp =>
    new RetryRepo(new LoggingRepo(new CachingRepo(new ConcreteRepo(
        sp.GetRequiredService<IDb>())))));

// Standalone ResolveKnown:
if (serviceType == typeof(IRepo))
    return new RetryRepo(new LoggingRepo(new CachingRepo(new ConcreteRepo(
        (IDb)GetService(typeof(IDb))!))));
```

Each decorator's constructor receives the previous layer as its decorated interface parameter; other parameters are resolved from the container.

**Open generic decorators** follow the same pattern — the factory method chains them:
```csharp
private static object OG_Factory_0<T>(IServiceProvider sp) =>
    new LoggingRepo<T>(new CachingRepo<T>(new Repo<T>()));
```

### Generator changes

1. Change `decoratorsByInterface` to `Dictionary<string, List<DecoratorRegistrationInfo>>`
2. Change population loop (line 206-207) to add to list
3. Update `BuildDecoratedNewExpression` to accept `List<DecoratorRegistrationInfo>` and chain them
4. Update `EmitOpenGenericFactoryMethod` to chain multiple decorators
5. Update ZI012 validation to check per-decorator that inner service exists

### Validation

- Each decorator in the chain must implement the same interface
- Each decorator must have a constructor parameter matching the decorated interface (already validated by ZI011)
- The first decorator wraps the concrete; subsequent decorators wrap the previous decorator

## Feature 4: Compile-Time Circular Dependency Detection (ZI014)

### Problem

Circular dependencies cause stack overflows at runtime in all DI containers. ZeroInject has full visibility of the dependency graph at source-generation time and can catch these at compile time — a unique differentiator.

### Design

**Graph construction:** After collecting all `ServiceRegistrationInfo`, build a directed graph:
- Node = service type FQN (each interface or concrete type)
- Edge = constructor parameter type → service that provides it

**Cycle detection:** Standard DFS with coloring (white/gray/black). When a gray node is revisited, we have a cycle.

**Diagnostic:**
```csharp
public static readonly DiagnosticDescriptor CircularDependency = new DiagnosticDescriptor(
    "ZI014",
    "Circular dependency detected",
    "Circular dependency detected: {0}",  // e.g. "A → B → C → A"
    "ZeroInject",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Edge cases:**
- **Optional dependencies** (`IsOptional = true`): Skip these edges — they use `GetService` (nullable) and won't stack-overflow
- **Open generics**: Include in the graph using the unbound type (`IRepo<>` → `Repo<>`)
- **Decorators**: Include decorator edges (decorator depends on its inner service)
- **Self-loops via decorator**: A decorator for `IFoo` depends on `IFoo` — this is intentional and should NOT be flagged. The decorator receives the concrete, not itself. Skip edges where a decorator's interface parameter matches its own decorated interface.

### Algorithm placement

Run after all services and decorators are collected (after line 207), before code generation begins. Report ZI014 for each cycle found.

### Implementation

New static method in `ZeroInjectGenerator.cs`:
```csharp
private static void DetectCircularDependencies(
    SourceProductionContext spc,
    ImmutableArray<ServiceRegistrationInfo> services,
    Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface)
```

Build adjacency list mapping each service type to its constructor parameter types (excluding optional params and decorator self-references). Run DFS, report each cycle as ZI014.

### Files changed

- `DiagnosticDescriptors.cs` — add ZI014
- `ZeroInjectGenerator.cs` — add `DetectCircularDependencies`, call it before generation
- `docs/features.md` — update feature table

## Testing Strategy

Each feature gets:
1. **Generator tests** — verify emitted source code contains expected patterns
2. **Integration tests** — verify runtime behavior with compiled generated code
3. **Diagnostic tests** — verify correct diagnostics emitted for edge cases

Specific test cases:
- `IServiceProviderIsService`: returns true for registered types, false for unknown, true for open generic closed forms
- Multi-decorator: 2-decorator chain, 3-decorator chain, decorator + open generic, ordering is correct
- Circular dependency: A→B→A detected, A→B→C→A detected, optional dependency breaks cycle, decorator self-ref not flagged
