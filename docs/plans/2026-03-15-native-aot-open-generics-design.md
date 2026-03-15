# Design: Native AOT — Compile-Time Closed Generic Enumeration

**Date:** 2026-03-15
**Status:** Approved

## Summary

Replace the standalone container's runtime open generic resolution (which uses `MakeGenericType` + `Delegate.CreateDelegate`) with compile-time enumerated closed generic factories. After this change, the standalone container contains zero reflection and is fully Native AOT compatible for all services that don't use open generics with unknown closed types.

The `AddXxxServices()` extension method path is unchanged — MS DI handles open generics via its own mechanism.

## Problem

The standalone container currently emits, for each open generic registration:

```csharp
// Runtime reflection — not AOT safe:
private static readonly MethodInfo _og_mi_0 =
    typeof(MyAppStandaloneServiceProvider).GetMethod("OG_Factory_0", ...);
private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, object>> _og_dc_0 = new();

// In ResolveKnown:
if (serviceType.IsGenericType)
{
    var _genDef = serviceType.GetGenericTypeDefinition();
    if (_genDef == typeof(IRepository<>))
    {
        var _d_0 = _og_dc_0.GetOrAdd(serviceType, OgDelegateCreator, _og_mi_0);
        return _d_0(this);
    }
}
```

## Solution: Compile-Time Closed-Type Enumeration

The generator runs a fixed-point analysis after collecting all services, discovering every closed generic type used as a constructor parameter (transitively). It then generates explicit factory entries:

```csharp
// Zero reflection — fully AOT safe:
if (serviceType == typeof(global::IRepository<global::Order>))
    return new global::Repository<global::Order>(
        sp.GetRequiredService<global::IOrderContext>());
if (serviceType == typeof(global::IRepository<global::User>))
    return new global::Repository<global::User>(
        sp.GetRequiredService<global::IUserContext>());
```

## Data Model Additions

### `ConstructorParameterInfo` — two new fields

```csharp
public string? UnboundGenericInterfaceFqn { get; }  // e.g. "global::IRepository<>" — null if not a closed generic
public ImmutableArray<string> TypeArgumentMetadataNames { get; } // e.g. ["MyApp.Order"] for GetTypeByMetadataName
```

Populated in `GetServiceInfo` when `param.Type` is a closed generic (`IsGenericType && !IsUnboundGenericType`):
- `UnboundGenericInterfaceFqn` = `param.Type.ConstructedFrom.ToDisplayString(FullyQualifiedFormat)`
- `TypeArgumentMetadataNames` = each `typeArg` → `typeArg.ContainingNamespace.ToDisplayString() + "." + typeArg.MetadataName` (strip leading `.` when namespace is empty)

### `ServiceRegistrationInfo` — one new field (open generics only)

```csharp
public string? ImplementationMetadataName { get; } // e.g. "MyApp.Repository`1" — null for closed services
```

Populated in `GetServiceInfo` when `IsOpenGeneric = true`:
- `typeSymbol.ContainingNamespace.ToDisplayString() + "." + typeSymbol.MetadataName`

### New: `ClosedGenericFactoryInfo`

```csharp
internal sealed class ClosedGenericFactoryInfo : IEquatable<ClosedGenericFactoryInfo>
{
    public string InterfaceFqn { get; }           // "global::IRepository<global::Order>"
    public string ImplementationFqn { get; }      // "global::Repository<global::Order>"
    public string Lifetime { get; }               // "Transient" | "Scoped" | "Singleton"
    public ImmutableArray<ConstructorParameterInfo> Parameters { get; } // substituted params
}
```

## Analysis Pass: `FindClosedGenericUsages`

A new `IncrementalValueProvider` step added to `Initialize`:

```csharp
var closedGenericUsages = transients
    .Combine(scopeds)
    .Combine(singletons)
    .Combine(context.CompilationProvider)
    .Select(static (data, ct) => FindClosedGenericUsages(data, ct));
```

`FindClosedGenericUsages` algorithm:

1. Build lookup: `unbound FQN → ServiceRegistrationInfo` for all open generic registrations
2. Seed a work queue with all closed generic constructor parameters found across all registered services
3. **Fixed-point loop**: for each item in the work queue:
   a. If already processed, skip
   b. Match `UnboundGenericInterfaceFqn` against the open generic lookup
   c. If matched: use `compilation.GetTypeByMetadataName(implementationMetadataName)` to get the impl symbol, `typeArgSymbols` from `compilation.GetTypeByMetadataName(metadataName)` per type arg, then `implSymbol.Construct(typeArgSymbols)` to close it
   d. Get the closed implementation's constructor parameters (fully substituted by Roslyn)
   e. Emit a `ClosedGenericFactoryInfo` record
   f. Add any new closed generic params of the closed implementation to the work queue
4. Continue until work queue is empty

## Code Generation Changes

### Removed from standalone container output:
- `private static readonly MethodInfo _og_mi_N` fields
- `private static readonly ConcurrentDictionary<...> _og_dc_N` fields
- `private static readonly ConcurrentDictionary<...> _og_sc_N` singleton cache fields (for singleton open generics)
- `private static object OG_Factory_N<T>(IServiceProvider sp)` methods
- The `if (serviceType.IsGenericType) { var _genDef = ... }` block in `ResolveKnown`

### Added to standalone container output:
- Explicit `if (serviceType == typeof(global::IFoo<global::Bar>)) return ...` entries in `ResolveKnown` for each `ClosedGenericFactoryInfo`
- Singleton caching via `Interlocked.CompareExchange` on a per-closed-type field, identical to closed service singletons

### `AddXxxServices()` — unchanged:
`services.TryAdd(ServiceDescriptor.Transient(typeof(IRepository<>), typeof(Repository<>)))` stays as-is.

## New Diagnostic: ZI018

```csharp
public static readonly DiagnosticDescriptor NoDetectedClosedUsages = new DiagnosticDescriptor(
    "ZI018",
    "No closed usages detected for open generic",
    "Open generic '{0}' is registered but no closed usages were detected in this assembly. " +
    "It will not be resolvable from the standalone or hybrid container.",
    "ZInject",
    DiagnosticSeverity.Warning,
    isEnabledByDefault: true);
```

Emitted when an open generic service produces zero `ClosedGenericFactoryInfo` records after the fixed-point analysis.

## What Does NOT Change

- `[Decorator]`, `[DecoratorOf]`, `[OptionalDependency]` — no changes
- `AddXxxServices()` extension method — no changes
- Closed service registration and resolution — no changes
- Existing diagnostics ZI001–ZI017 — no changes
- All 259 existing tests continue to pass (some output assertion strings will change)
