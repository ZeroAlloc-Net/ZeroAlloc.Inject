# aot-smoke Coverage Extension — Design

**Date:** 2026-05-28
**Scope:** ZeroAlloc.Inject aot-smoke project (`samples/ZeroAlloc.Inject.AotSmoke/`) — extend coverage from the existing `Singleton` + `Transient` baseline to also exercise three previously-uncovered generator-emission paths: `[Scoped]` lifetime services, the `[Decorator]` pattern, and open-generic services with compile-time-detected closed usages. Closes backlog item B1.

## Background

The existing aot-smoke project exercises two service paths: `Greeter` (`[Singleton]`) and `WelcomeService` (`[Transient]` with constructor-injected `IGreeter`). It asserts the composition works, Singleton identity, and Transient distinctness.

It does NOT touch three more-interesting paths the generator emits:

- **`[Scoped]` lifetime** — services that share an instance within a `IServiceScope` but get a new instance per scope. Resolved via `provider.CreateScope().ServiceProvider.GetRequiredService<T>()`.
- **`[Decorator]` pattern** — a class that wraps another service implementing the same interface. The generator infers the decorated interface from the user's class's interfaces matched against ctor params, then emits a registration chain (`base → decorator`) such that consumers resolving the interface receive the wrapped form.
- **Open generic with closed usages** — `[Singleton/Transient/Scoped]` on a generic class like `InMemoryInventory<T>`. Per the standalone container's design, the generator scans ctor parameters across the assembly at compile time to enumerate every closed form (e.g. `IInventory<Product>`) and emits a `typeof(IInventory<Product>)` branch in the type-switch resolver.

Surfaced 2026-05-27 during the org-wide aot-smoke coverage survey done after [ZeroAlloc.Serialisation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation) shipped 2.3.1 + 2.3.2 reactively. Same "smoke exists but partial" pattern applies here — the next downstream consumer to use any of these three Inject paths under Native AOT will discover the regression instead of CI doing so.

## Goal

A regression in the generator-emitted code for any of the three paths fails the aot-smoke job locally. The existing Singleton + Transient baseline stays green; the three new fixtures + assertions are strictly additive.

## Decisions

### D-1: three independent fixtures, one per uncovered path

`HttpRequestContext.cs` (Scoped), `DecoratorFixture.cs` (Decorator), `InventoryFixture.cs` (open-generic). Each in its own file (or bundled file per the existing `Services.cs` convention — multiple types per file is acceptable in this project). Smoke assertions are organised by fixture so a failure pinpoints which feature broke.

**Considered and rejected:**

- **Single combined fixture** with all three patterns intertwined. Smaller LOC but assertion failures become ambiguous.

### D-2: tight assertions per fixture

- **Scoped**: `ReferenceEquals` within scope (positive identity), `!ReferenceEquals` across scopes (negative identity). Two assertions, both load-bearing.
- **Decorator**: type check (`foo is LoggingFoo`) AND behavioral check (`DoStuff("x") == "[logged] base:x"`). The type check catches "decorator not registered"; the behavioral check catches "decorator registered but bypassed" (which would slip past the type check if the generator somehow registered both forms and resolution returned the wrong one).
- **Open-generic closed-usage**: behavioral check (`Describe() == "InMemoryInventory<Product>"`). The string includes the closed type-parameter name, so a regression to the open form or to a different closed form fails the assertion.

### D-3: simple `[Decorator]` only, defer `[DecoratorOf]` Order coverage

The simple `[Decorator]` form (generator infers the decorated interface from ctor parameters) covers the load-bearing path the backlog sketched. `[DecoratorOf(Order=N)]` ordering is a real-but-marginal stretch that the backlog didn't include — defer to a future B1.5 if a regression in decorator ordering ever surfaces.

### D-4: stub `InventoryConsumer` for the open-generic closed-usage

Per `docs/advanced.md:248-256` — the standalone container's compile-time closed-usage scan requires `IInventory<Product>` to appear as a constructor parameter somewhere in the assembly. Without the stub, `ZAI018` warning fires and the closed form isn't resolvable. The smoke project needs to be `[Transient] internal sealed class InventoryConsumer(IInventory<Product> _)` even though the consumer is never directly used — its only job is to surface the closed form to the generator's ctor scan.

### D-5: no library API changes, no NuGet release

CI hygiene only. No `src/` files touched. PR ships smoke project changes + a backlog strikethrough. release-please skips the release manifest.

### D-6: mirror the existing smoke's `Fail(message)` helper pattern

The current `Program.cs` defines a `static int Fail(string message)` at the bottom that writes to `Console.Error` and returns 1. New blocks use the same helper for consistency.

## Design

### Fixture 1 — `[Scoped]` lifetime

**File (NEW):** `samples/ZeroAlloc.Inject.AotSmoke/HttpRequestContext.cs`

```csharp
using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

public interface IHttpRequestContext
{
    int RequestId { get; }
}

[Scoped]
public sealed class HttpRequestContext : IHttpRequestContext
{
    private static int _next;
    public HttpRequestContext()
    {
        RequestId = System.Threading.Interlocked.Increment(ref _next);
    }
    public int RequestId { get; }
}
```

Constructor increments a static counter so each new instance has a distinct `RequestId`. The smoke uses ReferenceEquals (cheaper, more robust), but the counter is useful in the diagnostic output if an assertion fails.

### Fixture 2 — `[Decorator]` wrapping `[Transient]`

**File (NEW):** `samples/ZeroAlloc.Inject.AotSmoke/DecoratorFixture.cs`

```csharp
using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

public interface IFoo
{
    string DoStuff(string input);
}

[Transient]
public sealed class Foo : IFoo
{
    public string DoStuff(string input) => $"base:{input}";
}

[Decorator]
public sealed class LoggingFoo : IFoo
{
    private readonly IFoo _inner;
    public LoggingFoo(IFoo inner) => _inner = inner;
    public string DoStuff(string input) => $"[logged] {_inner.DoStuff(input)}";
}
```

The generator infers the decorated interface (`IFoo`) from `LoggingFoo`'s ctor parameter type and emits a chain `Foo → LoggingFoo`. Resolving `IFoo` returns a `LoggingFoo` whose `_inner` is the `Foo`.

### Fixture 3 — open generic with closed usage

**File (NEW):** `samples/ZeroAlloc.Inject.AotSmoke/InventoryFixture.cs`

```csharp
using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

public interface IInventory<T>
{
    string Describe();
}

[Transient]
public sealed class InMemoryInventory<T> : IInventory<T>
{
    public string Describe() => $"InMemoryInventory<{typeof(T).Name}>";
}

public sealed record Product(int Id, string Name);

// Stub: surfaces IInventory<Product> as a ctor parameter so the generator's
// compile-time closed-usage scan emits a typeof(IInventory<Product>) branch
// in the type-switch resolver. The class is internal and never resolved at
// runtime — its only purpose is to exist as a syntactic touchpoint for the
// generator. Per docs/advanced.md without this, ZAI018 fires and the closed
// form is not resolvable.
[Transient]
internal sealed class InventoryConsumer
{
    public InventoryConsumer(IInventory<Product> _) { }
}
```

### Program.cs assertions

Inserted BEFORE the existing `Console.WriteLine("AOT smoke: PASS");`, AFTER the existing Singleton + Transient assertion blocks:

```csharp
// Scoped lifetime — same instance within a scope, distinct across scopes.
using (var scope1 = provider.CreateScope())
{
    var c1a = scope1.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    var c1b = scope1.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    if (!ReferenceEquals(c1a, c1b))
        return Fail("IHttpRequestContext Scoped: two resolutions in same scope should share instance");

    using var scope2 = provider.CreateScope();
    var c2 = scope2.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    if (ReferenceEquals(c1a, c2))
        return Fail("IHttpRequestContext Scoped: resolutions in different scopes should be distinct");
}

// Decorator pattern — resolving IFoo returns LoggingFoo wrapping Foo.
var foo = provider.GetRequiredService<IFoo>();
if (foo is not LoggingFoo)
    return Fail($"IFoo Decorator: expected LoggingFoo, got {foo.GetType().Name}");
var fooResult = foo.DoStuff("x");
if (!string.Equals(fooResult, "[logged] base:x", StringComparison.Ordinal))
    return Fail($"IFoo Decorator: expected '[logged] base:x', got '{fooResult}'");

// Open-generic closed-usage — IInventory<Product> resolves to the closed form.
var inv = provider.GetRequiredService<IInventory<Product>>();
var invDesc = inv.Describe();
if (!string.Equals(invDesc, "InMemoryInventory<Product>", StringComparison.Ordinal))
    return Fail($"IInventory<Product> closed-generic: expected 'InMemoryInventory<Product>', got '{invDesc}'");
```

Total Program.cs growth: ~25 LOC.

### Backlog update

Replace the existing B1 entry in `docs/backlog.md` with the struck-through shipped marker:

```markdown
## ~~B1 — Extend aot-smoke to cover Scoped lifetime, Decorators, and closed-generic factories~~ — ✅ shipped 2026-05-28

**Shipped:** Three new fixtures in `samples/ZeroAlloc.Inject.AotSmoke/` (`HttpRequestContext.cs` for `[Scoped]`; `DecoratorFixture.cs` for `[Decorator]`; `InventoryFixture.cs` for open-generic `IInventory<T>` with a stub consumer surfacing `IInventory<Product>`) plus matching assertion blocks in `Program.cs`. Asserts ReferenceEquals identity for Scoped (positive within scope + negative across scopes), type-check + behavioral check for Decorator, and behavioral check for the closed-generic resolution.

**Design + plan:** [`docs/plans/2026-05-28-aot-smoke-inject-paths-design.md`](plans/2026-05-28-aot-smoke-inject-paths-design.md) + [`docs/plans/2026-05-28-aot-smoke-inject-paths.md`](plans/2026-05-28-aot-smoke-inject-paths.md).
```

### Files touched

- **NEW:** `samples/ZeroAlloc.Inject.AotSmoke/HttpRequestContext.cs`
- **NEW:** `samples/ZeroAlloc.Inject.AotSmoke/DecoratorFixture.cs`
- **NEW:** `samples/ZeroAlloc.Inject.AotSmoke/InventoryFixture.cs`
- **MOD:** `samples/ZeroAlloc.Inject.AotSmoke/Program.cs` — three additional assertion blocks
- **MOD:** `docs/backlog.md` — strike B1 shipped

Total commit footprint: ~80 LOC.

## Out of scope

- **`[DecoratorOf(Order=...)]` ordering coverage** — second decorator in a chain; defer to follow-up backlog if a regression ever surfaces
- **Captive dependency diagnostic** — compile-time generator warning, not a runtime smoke
- **Standalone mode variants** — existing smoke uses hybrid mode (default); standalone is a separate workstream
- **`WhenRegistered` conditional decorator** — `[DecoratorOf(WhenRegistered=...)]` parameter; defer
