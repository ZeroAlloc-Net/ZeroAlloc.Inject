# ZInject — Feature Status

Last updated: 2026-03-12

## Implemented

| Feature | Hybrid | Standalone | Notes |
|---------|:------:|:----------:|-------|
| `[Transient]` lifetime | ✅ | ✅ | New instance per resolution |
| `[Scoped]` lifetime | ✅ | ✅ | One instance per scope |
| `[Singleton]` lifetime | ✅ | ✅ | Thread-safe lazy init via `Interlocked.CompareExchange` |
| `[Decorator]` (single + stacked) | ✅ | ✅ | Supports chaining: `Retry → Logging → Caching → Concrete` |
| `[As(typeof(...))]` explicit binding | ✅ | ✅ | Narrows registration to specific interface(s) |
| Keyed services (`Key = "..."`) | ✅ | ✅ | `IKeyedServiceProvider`; requires .NET 8+ (ZI005) |
| `AllowMultiple` (multi-registration) | ✅ | ✅ | Switches from `TryAdd*` to `Add*` |
| `IEnumerable<T>` resolution | ✅ | ✅ | Array of all implementations per service type |
| Open generics (`IRepo<T>` → `Repo<T>`) | ✅¹ | ✅ | Standalone uses code-gen delegate factories |
| Open generic + decorator | ✅¹ | ✅ | Decorator wraps inner via `MakeGenericType` |
| Optional dependencies | ✅ | ✅ | `GetService` (nullable) instead of `GetRequiredService` |
| Concrete-only registration | ✅ | ✅ | No interface required; ZI007 warning emitted |
| Multiple interfaces per class | ✅ | ✅ | Each interface + concrete type registered |
| `IServiceScopeFactory` | ✅ | ✅ | `CreateScope()` on all provider types |
| `IDisposable` / `IAsyncDisposable` | ✅ | ✅ | Singletons + scoped services disposed in reverse order |
| Constructor parameter resolution | ✅ | ✅ | Auto-resolved via `GetService` / `GetRequiredService` |
| `[ActivatorUtilitiesConstructor]` | ✅ | ✅ | Disambiguates multiple public constructors (ZI009) |
| Assembly-level method name override | ✅ | ✅ | `[assembly: ZInject("AddCustomName")]` |
| Filtered system interfaces | ✅ | ✅ | `IDisposable`, `IEquatable<T>`, etc. excluded |
| `IServiceProviderIsService` | ✅ | ✅ | Generated `IsKnownService` type-check; hybrid delegates to fallback |
| `IServiceProviderIsKeyedService` | ✅ | ✅ | Generated `IsKnownKeyedService` key+type check; hybrid delegates to fallback |
| Scoped thread safety | ✅ | ✅ | Matches MS DI contract — scopes are per-request, not thread-safe |
| Circular dependency detection | ✅ | ✅ | Compile-time ZI014 error — unique among .NET DI containers |

¹ Hybrid mode delegates open generics to the MS DI fallback.

## Compile-Time Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| ZI001 | Error | Multiple lifetime attributes on same class |
| ZI002 | Error | Attribute on non-class type |
| ZI003 | Error | Attribute on abstract or static class |
| ZI004 | Error | `As` type not implemented by class |
| ZI005 | Error | `Key` used below .NET 8 |
| ZI006 | Warning | No public constructor |
| ZI007 | Warning | No interfaces (concrete-only registration) |
| ZI008 | Warning | Missing `Microsoft.Extensions.DependencyInjection.Abstractions` |
| ZI009 | Error | Multiple public constructors without `[ActivatorUtilitiesConstructor]` |
| ZI010 | Error | Constructor parameter is primitive/value type |
| ZI011 | Error | Decorator has no matching interface parameter |
| ZI012 | Error | Decorated interface not registered as a service |
| ZI013 | Warning | Decorator on abstract/static class |
| ZI014 | Error | Circular dependency detected (compile-time cycle detection) |
