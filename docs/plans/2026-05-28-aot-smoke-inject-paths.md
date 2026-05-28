# aot-smoke Inject Paths Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Extend `samples/ZeroAlloc.Inject.AotSmoke/` to cover three previously-uncovered generator-emission paths under `PublishAot=true`: `[Scoped]` lifetime, `[Decorator]` wrapping, and open-generic `[Transient]` services with compile-time-detected closed usages. Closes backlog item B1.

**Architecture:** Three new fixture files (`HttpRequestContext.cs`, `DecoratorFixture.cs`, `InventoryFixture.cs`), each focused on one path. Three new assertion blocks appended to `Program.cs` — Scoped uses `ReferenceEquals` for within/across-scope identity, Decorator uses type-check + behavioral check, open-generic uses behavioral check. No library changes; smoke + backlog strikethrough only.

**Tech Stack:** .NET 10, `PublishAot=true`, MS.Extensions.DependencyInjection, ZeroAlloc.Inject generator-emitted `AddZeroAllocInjectAotSmokeServices()` extension method.

**Design doc:** `docs/plans/2026-05-28-aot-smoke-inject-paths-design.md` (committed at `47ed3a8`).

**Working branch:** `chore/aot-smoke-cover-inject-paths` (off `main` at `721f900` — the 1.7.1 release; design committed).

---

## Phase 0 — Orient (5 min)

### Task 0.1: Read the existing smoke + relevant attribute surfaces

**Files (read-only):**

- `samples/ZeroAlloc.Inject.AotSmoke/Program.cs` — current Singleton + Transient assertion shape; new blocks insert BEFORE the `Console.WriteLine("AOT smoke: PASS");` and reuse the existing `static int Fail(string message)` helper.
- `samples/ZeroAlloc.Inject.AotSmoke/Services.cs` — current fixture file (4 types in one file — confirms MA0048 is NOT enforced; bundling types per fixture file is acceptable).
- `src/ZeroAlloc.Inject/ScopedAttribute.cs` — `[Scoped]` is just `public sealed class ScopedAttribute : ServiceAttribute`. Use on a class like `[Singleton]`/`[Transient]`.
- `src/ZeroAlloc.Inject/DecoratorAttribute.cs` — `[Decorator]` zero-config form (generator infers decorated interface from ctor params).
- `docs/decorators.md` — example shape: decorated class implements same interface as the service it wraps, accepts the inner via a ctor parameter of that interface type.
- `docs/advanced.md:230-270` — open-generic standalone-mode behavior: the generator scans the assembly for closed usages (ctor parameters mentioning `IInventory<Product>`) and emits a typeof-switch branch for each. A stub `[Transient] InventoryConsumer(IInventory<Product> _)` is required to surface the closed form.

---

## Phase 1 — Fixture 1: `[Scoped]` lifetime (25 min, 4 tasks)

### Task 1.1: Write `HttpRequestContext.cs`

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

The static counter ensures each new instance has a distinct `RequestId` — useful for diagnostic output if `ReferenceEquals` assertions fail and you need to inspect what was returned.

### Task 1.2: Append the Scoped assertion block to Program.cs

**File (MOD):** `samples/ZeroAlloc.Inject.AotSmoke/Program.cs`

Read the file first. The structure ends with the Transient distinctness assertion, then `Console.WriteLine("AOT smoke: PASS");`, then `return 0;`, then the `static int Fail(...)` helper.

Insert the Scoped block AFTER the Transient block and BEFORE the `Console.WriteLine("AOT smoke: PASS");` line:

```csharp
// Scoped lifetime — same instance within a scope, distinct across scopes.
using (var scope1 = provider.CreateScope())
{
    var c1a = scope1.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    var c1b = scope1.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    if (!ReferenceEquals(c1a, c1b))
        return Fail($"IHttpRequestContext Scoped: two resolutions in same scope should share instance (got RequestId {c1a.RequestId} vs {c1b.RequestId})");

    using var scope2 = provider.CreateScope();
    var c2 = scope2.ServiceProvider.GetRequiredService<IHttpRequestContext>();
    if (ReferenceEquals(c1a, c2))
        return Fail($"IHttpRequestContext Scoped: resolutions in different scopes should be distinct (both got RequestId {c1a.RequestId})");
}
```

The outer `using (var scope1 = provider.CreateScope())` block contains the inner `scope2` so both scopes exist concurrently. Required for the cross-scope assertion to be meaningful.

### Task 1.3: Build the smoke project

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Inject
dotnet build samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj -c Release 2>&1 | tail -10
```

Expected: build succeeds, no warnings.

If `provider.CreateScope()` isn't found, the smoke project may be missing `using Microsoft.Extensions.DependencyInjection;` (existing in Program.cs already — verify).

If `ZAI018` warning surfaces for `IHttpRequestContext`, the generator's scan didn't detect a closed usage. This shouldn't fire for `[Scoped]` on a non-generic service. If it does, surface to user — that's a generator concern.

### Task 1.4: Run + commit

```bash
dotnet run -c Release --project samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj 2>&1 | tail -5
```

Expected: `AOT smoke: PASS`.

```bash
git add samples/ZeroAlloc.Inject.AotSmoke/HttpRequestContext.cs \
        samples/ZeroAlloc.Inject.AotSmoke/Program.cs
git commit -m "chore(aot-smoke): cover [Scoped] lifetime via HttpRequestContext fixture

IHttpRequestContext with [Scoped] HttpRequestContext exercises the
generator's scoped-lifetime emission. Asserts ReferenceEquals within a
scope (same instance) and ReferenceEquals=false across scopes (distinct
instances). Static-counter ctor gives diagnostic visibility on assertion
failure."
```

---

## Phase 2 — Fixture 2: `[Decorator]` (25 min, 4 tasks)

### Task 2.1: Write `DecoratorFixture.cs`

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

The generator infers `IFoo` as the decorated interface because:
1. `LoggingFoo` implements `IFoo`
2. `LoggingFoo`'s only ctor parameter is `IFoo`
3. `IFoo` is already registered (via `[Transient] Foo`)

Per `docs/decorators.md:75-81`, these are exactly the requirements for the simple `[Decorator]` form.

### Task 2.2: Append the Decorator assertion block to Program.cs

**File (MOD):** `samples/ZeroAlloc.Inject.AotSmoke/Program.cs`

Insert AFTER the Scoped block, BEFORE `Console.WriteLine("AOT smoke: PASS");`:

```csharp
// Decorator pattern — resolving IFoo returns LoggingFoo wrapping Foo.
var foo = provider.GetRequiredService<IFoo>();
if (foo is not LoggingFoo)
    return Fail($"IFoo Decorator: expected LoggingFoo, got {foo.GetType().Name}");
var fooResult = foo.DoStuff("x");
if (!string.Equals(fooResult, "[logged] base:x", StringComparison.Ordinal))
    return Fail($"IFoo Decorator: expected '[logged] base:x', got '{fooResult}'");
```

The type-check catches "decorator wasn't registered" (would return Foo directly). The behavioral check catches "decorator registered but didn't invoke inner" (would return `[logged] ` without the `base:x` suffix) or "decorator did wrong thing" (any output ≠ exact string).

### Task 2.3: Build

```bash
dotnet build samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj -c Release 2>&1 | tail -10
```

Expected: build succeeds.

If `ZAI012` warning fires ("inner interface not registered"), check that `[Transient] Foo : IFoo` is present in the same assembly. Should be — same file.

### Task 2.4: Run + commit

```bash
dotnet run -c Release --project samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj 2>&1 | tail -5
```

Expected: `AOT smoke: PASS`.

```bash
git add samples/ZeroAlloc.Inject.AotSmoke/DecoratorFixture.cs \
        samples/ZeroAlloc.Inject.AotSmoke/Program.cs
git commit -m "chore(aot-smoke): cover [Decorator] pattern via LoggingFoo wrapping Foo

[Decorator] LoggingFoo wraps [Transient] Foo via a single ctor parameter
of type IFoo. The generator infers the decorated interface from the ctor
parameter and emits the chain. Asserts the resolved instance is
LoggingFoo (type check) and that DoStuff('x') returns '[logged] base:x'
(behavioral check — confirms both decorator and inner are invoked)."
```

---

## Phase 3 — Fixture 3: Open generic with closed usage (30 min, 5 tasks)

### Task 3.1: Write `InventoryFixture.cs`

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
// in the type-switch resolver. Internal because it's never resolved at
// runtime — its only purpose is to exist as a syntactic touchpoint for the
// generator. Per docs/advanced.md:248-256, without this ZAI018 fires and
// the closed form is not resolvable from the standalone/hybrid container.
[Transient]
internal sealed class InventoryConsumer
{
    public InventoryConsumer(IInventory<Product> _) { }
}
```

The `Describe()` implementation returns a string containing the closed type-parameter name (`InMemoryInventory<Product>`). The smoke's assertion matches this exact string, so a regression to the wrong closed form or to the open form would fail.

### Task 3.2: Append the Inventory assertion block to Program.cs

**File (MOD):** `samples/ZeroAlloc.Inject.AotSmoke/Program.cs`

Insert AFTER the Decorator block, BEFORE `Console.WriteLine("AOT smoke: PASS");`:

```csharp
// Open-generic closed-usage — IInventory<Product> resolves to the closed form.
var inv = provider.GetRequiredService<IInventory<Product>>();
var invDesc = inv.Describe();
if (!string.Equals(invDesc, "InMemoryInventory<Product>", StringComparison.Ordinal))
    return Fail($"IInventory<Product> closed-generic: expected 'InMemoryInventory<Product>', got '{invDesc}'");
```

### Task 3.3: Build

```bash
dotnet build samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj -c Release 2>&1 | tail -15
```

Expected: build succeeds, no `ZAI018` warning (the `InventoryConsumer` stub ensures the closed form is detected).

If `ZAI018` DOES fire for `IInventory<Product>`, verify:
- `InventoryConsumer` exists in the same assembly
- The ctor parameter type is exactly `IInventory<Product>` (not `IInventory<object>` or some other form)
- The class is decorated with `[Transient]` so the generator visits it

If the smoke csproj is in standalone or hybrid mode, ZAI018 is fatal for resolvability. The existing smoke uses the default mode (whatever it is — verify via `samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj` for container-mode hints).

### Task 3.4: Run + commit

```bash
dotnet run -c Release --project samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj 2>&1 | tail -5
```

Expected: `AOT smoke: PASS`.

If the run fails with `InvalidOperationException: No service for type 'IInventory<Product>' has been registered`, the closed-usage scan didn't pick up the stub. Likely cause: the stub's ctor parameter type doesn't match what the generator scans for. Inspect the generated source at `obj/Release/net10.0/generated/ZeroAlloc.Inject.Generator/.../*.g.cs` to see what the type-switch contains.

### Task 3.5: Commit Phase 3

```bash
git add samples/ZeroAlloc.Inject.AotSmoke/InventoryFixture.cs \
        samples/ZeroAlloc.Inject.AotSmoke/Program.cs
git commit -m "chore(aot-smoke): cover open-generic with closed-usage detection

IInventory<T> with [Transient] InMemoryInventory<T> plus a stub
[Transient] InventoryConsumer(IInventory<Product> _) to surface the
closed form to the generator's compile-time scan. Per docs/advanced.md
this is the canonical pattern in standalone/hybrid mode where open
generics require enumerated closed branches. Asserts Describe() returns
'InMemoryInventory<Product>' — catches a regression to the wrong closed
form or to the open form."
```

---

## Phase 4 — Strike B1 + push + PR (15 min, 3 tasks)

### Task 4.1: Update `docs/backlog.md`

Read the file to find the existing B1 entry. Replace the ENTIRE B1 block (the open `## B1 — Extend aot-smoke ...` with What / Why / Sketch / Tradeoff / Graduation signal subsections) with:

```markdown
## ~~B1 — Extend aot-smoke to cover Scoped lifetime, Decorators, and closed-generic factories~~ — ✅ shipped 2026-05-28

**Shipped:** Three new fixtures in `samples/ZeroAlloc.Inject.AotSmoke/` (`HttpRequestContext.cs` for `[Scoped]`; `DecoratorFixture.cs` for `[Decorator]`; `InventoryFixture.cs` for open-generic `IInventory<T>` with a stub `InventoryConsumer` surfacing `IInventory<Product>`) plus matching assertion blocks in `Program.cs`. Asserts ReferenceEquals identity for Scoped (positive within scope + negative across scopes), type-check + behavioral check for Decorator, and behavioral check for the closed-generic resolution.

**Design + plan:** [`docs/plans/2026-05-28-aot-smoke-inject-paths-design.md`](plans/2026-05-28-aot-smoke-inject-paths-design.md) + [`docs/plans/2026-05-28-aot-smoke-inject-paths.md`](plans/2026-05-28-aot-smoke-inject-paths.md).
```

Don't add a new entry — strikethrough in place.

### Task 4.2: Build + commit docs

```bash
cd c:/Projects/Prive/ZeroAlloc/ZeroAlloc.Inject
dotnet build -c Release 2>&1 | tail -5
```

Expected: full solution builds clean.

```bash
git add docs/backlog.md
git commit -m "docs(backlog): strike B1 shipped (aot-smoke coverage extension)"
```

### Task 4.3: Push + open PR + STOP

```bash
git push -u origin chore/aot-smoke-cover-inject-paths

gh pr create \
  --title "chore(aot-smoke): cover Scoped lifetime, Decorator, open-generic closed-usages" \
  --body "$(cat <<'EOF'
## Summary

Closes backlog item B1. The existing aot-smoke project covered only the Singleton + Transient baseline; this PR adds three independent fixtures + assertion blocks exercising the three previously-uncovered generator-emission paths under `PublishAot=true`:

- `[Scoped]` lifetime via `HttpRequestContext` — `ReferenceEquals` identity within a scope, distinctness across scopes
- `[Decorator]` pattern via `LoggingFoo` wrapping `[Transient] Foo` — type check + behavioral check on the wrapped result
- Open-generic `[Transient] InMemoryInventory<T>` with a stub `InventoryConsumer(IInventory<Product>)` to surface the closed form — behavioral check on `Describe()` matching `InMemoryInventory<Product>`

## Why now

Surfaced 2026-05-27 during the org-wide aot-smoke coverage survey done after [ZeroAlloc.Serialisation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation) shipped 2.3.1 + 2.3.2 reactively. Same "smoke exists but partial" pattern applied to ZA.Inject. This PR closes it. Already-shipped sibling: ZeroAlloc.Validation [PR #51](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/pull/51) for B4.

## What changed

- 3 new fixture files (`HttpRequestContext.cs`, `DecoratorFixture.cs`, `InventoryFixture.cs`)
- 3 new assertion blocks in `Program.cs` (~25 LOC)
- `docs/backlog.md` — B1 entry struck shipped

## Decisions ([design doc](docs/plans/2026-05-28-aot-smoke-inject-paths-design.md))

- **Three independent fixtures, not one combined** — failure messages pinpoint which feature regressed
- **Simple `[Decorator]` only** — backlog sketched "wrapping a concrete Foo" (singular); `[DecoratorOf(Order=N)]` ordering coverage deferred
- **Stub `InventoryConsumer` for the closed-generic** — per `docs/advanced.md:248-256` this is the canonical way to surface `IInventory<Product>` to the generator's compile-time scan

## SemVer

No package version bump — CI-only changes. release-please will treat as `chore:` and skip the release manifest.

## Test plan

- [x] Local build clean (`dotnet build -c Release`, 0 warnings 0 errors)
- [x] Local JIT run prints `AOT smoke: PASS`
- [ ] CI build clean
- [ ] CI aot-smoke job passes (the real AOT publish on ubuntu-latest)

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

**STOP** after `gh pr create` succeeds. Do NOT admin-merge.

## Constraints

- Docs-only commits beyond the smoke project additions. No `src/` changes.
- PR body via single-quoted heredoc to avoid shell quoting fragility.
- PR title contains no `[ValueObject]`-style brackets (release-please safe).
- PowerShell on Windows; use `Bash` for the heredoc commit + PR body.

## Final report after Phase 4

After `gh pr create`:
- PR URL
- Commit hash for the docs commit
- Final build status (clean?)
- Confirmation: NOT admin-merged

---

## Verification checklist

- [ ] **Phase 1:** HttpRequestContext fixture compiles + Scoped assertion fires correctly (same instance within scope, distinct across)
- [ ] **Phase 2:** DecoratorFixture compiles + `foo is LoggingFoo` + behavioral string match both pass
- [ ] **Phase 3:** InventoryFixture compiles WITHOUT ZAI018 + `IInventory<Product>` resolves to `InMemoryInventory<Product>`
- [ ] **Phase 4:** B1 backlog struck through, PR opens with all three fixtures + all assertion blocks visible in the diff

## Out of scope (deferred to backlog or future PRs)

- **`[DecoratorOf(Order=...)]` ordering** — single-decorator covers load-bearing emission
- **Captive dependency diagnostic** — compile-time generator warning, not a runtime smoke
- **Standalone mode variants** — existing smoke uses default (hybrid?) mode; standalone is a separate workstream
- **`[DecoratorOf(WhenRegistered=...)]` conditional decorator** — defer
- **ZA.AsyncEvents B1 (#75 backlog)** — separate workstream
