# Open Generics in Standalone + Decorator Support Design

**Date:** 2026-03-11

---

## Goal

Two features in one branch:

1. **Open generics in standalone mode** — `IRepository<T>` registered via `[Scoped]` on `Repository<T>` is currently unresolvable in the standalone container (returns null). Add a runtime open-generic resolver that handles this without MS DI.

2. **Decorator support** — a new `[Decorator]` attribute that lets a class wrap another service. The generator wires the decoration chain statically in all three outputs (registration extension, hybrid container, standalone container).

---

## Approach

**Approach A — full static generation for decorators, runtime fallback for open generics.**

- Open generics: standalone provider gets a static map of open-generic service → impl + lifetime. On cache miss in `ResolveKnown`, a helper method checks the map, constructs the closed concrete type via bounded reflection, and caches by lifetime.
- Decorators: generator emits static wrapping code in the registration extension (non-generic only) and in both containers (all cases). Zero runtime reflection for known decorator chains.

---

## Feature 1: Open Generics in Standalone Mode

### Hybrid mode

No changes required. Unknown types fall through to the MS DI fallback which already handles open generics natively.

### Standalone mode — new runtime open-generic resolver

**New type in `ZeroInject.Container`:**

```csharp
public readonly struct OpenGenericEntry
{
    public Type ImplType { get; }
    public ServiceLifetime Lifetime { get; }
    public Type? DecoratorImplType { get; } // null if no decorator

    public OpenGenericEntry(Type implType, ServiceLifetime lifetime, Type? decoratorImplType = null)
    {
        ImplType = implType;
        Lifetime = lifetime;
        DecoratorImplType = decoratorImplType;
    }
}
```

**Changes to `ZeroInjectStandaloneProvider` base class:**

```csharp
protected virtual IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => null;

private ConcurrentDictionary<Type, object>? _openGenericSingletons;

protected object? ResolveOpenGenericRoot(Type serviceType)
{
    if (OpenGenericMap == null || !serviceType.IsGenericType) return null;
    var openDef = serviceType.GetGenericTypeDefinition();
    if (!OpenGenericMap.TryGetValue(openDef, out var entry)) return null;

    switch (entry.Lifetime)
    {
        case ServiceLifetime.Singleton:
            _openGenericSingletons ??= new ConcurrentDictionary<Type, object>();
            return _openGenericSingletons.GetOrAdd(serviceType, _ => ConstructOpenGeneric(serviceType, entry));
        default: // Transient
            return ConstructOpenGeneric(serviceType, entry);
    }
    // Scoped open generics: resolved from scope, not root
}

private object ConstructOpenGeneric(Type serviceType, OpenGenericEntry entry)
{
    var typeArgs = serviceType.GenericTypeArguments;
    var closedImpl = entry.ImplType.MakeGenericType(typeArgs);
    var inner = CreateInstance(closedImpl);
    if (entry.DecoratorImplType != null)
    {
        var closedDecorator = entry.DecoratorImplType.MakeGenericType(typeArgs);
        return CreateInstance(closedDecorator, inner);
    }
    return inner;
}

protected object CreateInstance(Type type, object? innerArg = null)
{
    var ctor = type.GetConstructors()[0];
    var parameters = ctor.GetParameters();
    var args = new object?[parameters.Length];
    for (int i = 0; i < parameters.Length; i++)
    {
        if (innerArg != null && parameters[i].ParameterType.IsAssignableFrom(innerArg.GetType()))
            args[i] = innerArg;
        else
            args[i] = GetService(parameters[i].ParameterType);
    }
    return ctor.Invoke(args);
}
```

**Changes to `ZeroInjectStandaloneScope` base class:** similar pattern, adds:
- `protected virtual IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => null`
- `private Dictionary<Type, object>? _openGenericScoped`
- `ResolveOpenGenericScoped(Type)` — dispatches by lifetime: singleton → delegates to `_root`, scoped → cache in `_openGenericScoped` with `_trackLock`, transient → `_root.CreateInstance(...)`

**Generated standalone provider — `OpenGenericMap` override:**

```csharp
protected override IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => _s_openGenericMap;

private static readonly Dictionary<Type, OpenGenericEntry> _s_openGenericMap = new()
{
    { typeof(global::IRepository<>), new(typeof(global::Repository<>), ServiceLifetime.Scoped) },
};
```

**`ResolveKnown` (root provider) — appends open-generic fallback:**

```csharp
protected override object? ResolveKnown(Type serviceType)
{
    if (serviceType == typeof(global::ISimpleService)) return new global::SimpleService();
    // ... all non-generic cases ...
    return ResolveOpenGenericRoot(serviceType);
}
```

**`ResolveScopedKnown` (scope) — same pattern:**

```csharp
protected override object? ResolveScopedKnown(Type serviceType)
{
    // ... static cases ...
    return ResolveOpenGenericScoped(serviceType);
}
```

---

## Feature 2: Decorator Support

### New attribute

Added to `ZeroInject` package:

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class DecoratorAttribute : Attribute { }
```

### Generator detection

For each class with `[Decorator]`:

1. Find the *decorated interface*: the interface that the class both implements and takes as a constructor parameter type.
2. Find the *inner registration*: the service registered with `[Transient]`/`[Scoped]`/`[Singleton]` that implements the same interface.
3. The decorator inherits the inner service's lifetime.
4. The decorator may have additional constructor parameters beyond the inner service — these are injected normally.

### Registration extension (MS DI path) — non-generic only

```csharp
// Inner service registered as concrete only
services.TryAddScoped<global::OrderService>();

// Interface registered via factory that wraps the inner
services.AddScoped<global::IOrderService>(
    sp => new global::LoggingOrderService(sp.GetRequiredService<global::OrderService>()));
```

**Open-generic decorated services are NOT supported in the registration extension** — MS DI does not support open-generic factory registrations. These are handled in the generated containers only.

### Hybrid container

The `ResolveKnown` type-switch emits the decoration chain statically:

```csharp
if (serviceType == typeof(global::IOrderService))
    return new global::LoggingOrderService(new global::OrderService());
```

For scoped decorator of scoped inner — both inner and outer are tracked for disposal.

### Standalone container

Same static code as hybrid. For open-generic decorated services, the `OpenGenericEntry.DecoratorImplType` is set and `ConstructOpenGeneric` wraps the inner in the decorator.

### Chaining multiple decorators

If both `LoggingOrderService` and `MetricsOrderService` decorate `IOrderService`, the generator chains them in declaration order (alphabetical by class name, or by source order):

```csharp
new MetricsOrderService(new LoggingOrderService(new OrderService()))
```

### New diagnostics

| ID | Severity | Description |
|---|---|---|
| ZI011 | Error | `[Decorator]` class has no constructor parameter matching any interface it implements |
| ZI012 | Error | `[Decorator]` interface has no registered implementation in this assembly |
| ZI013 | Warning | `[Decorator]` on abstract or static class |

---

## Data Flow Summary

```
[Scoped] Repository<T>  →  generator collects as OpenGenericEntry(IRepository<>, Repository<>, Scoped)
                           ↓
                    Registration extension: services.TryAdd(typeof(IRepository<>), typeof(Repository<>))
                    Standalone provider:    _s_openGenericMap[typeof(IRepository<>)] = entry
                    Standalone scope:       ResolveOpenGenericScoped → cached per-scope

[Decorator] LoggingOrderService  →  generator finds it decorates IOrderService via ctor param
                                    ↓
                         Registration extension: TryAddScoped<OrderService>() + AddScoped<IOrderService>(factory)
                         Hybrid container:       if (IOrderService) → new Logging(new Order())
                         Standalone container:   if (IOrderService) → new Logging(new Order())

[Decorator] LoggingRepository<T>  →  OpenGenericEntry(IRepository<>, Repository<>, Scoped, LoggingRepository<>)
                                      ↓
                         Standalone open-generic path: ConstructOpenGeneric wraps inner in decorator
```

---

## Testing Strategy

- **Unit tests** for `ZeroInjectStandaloneProvider` and `ZeroInjectStandaloneScope` base class open-generic methods
- **Generator tests** for:
  - `[Decorator]` class emits correct wrapping code in registration extension
  - `[Decorator]` class emits correct type-switch case in hybrid and standalone containers
  - Open-generic provider overrides `OpenGenericMap`
  - New diagnostics ZI011, ZI012, ZI013
- **Integration tests** for:
  - Open-generic transient/singleton/scoped resolution in standalone
  - Non-generic decorator resolved from registration extension (MS DI path)
  - Non-generic decorator resolved from hybrid and standalone containers
  - Open-generic decorated service resolved from standalone via runtime path
  - Decorator disposal (outer and inner both disposed in correct order)
  - Multiple decorators chained correctly
