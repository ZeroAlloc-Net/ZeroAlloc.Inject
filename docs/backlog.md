# ZeroAlloc.Inject — Backlog

Candidate enhancements identified during real-world usage. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

---

## ~~B1 — Extend aot-smoke to cover Scoped lifetime, Decorators, and closed-generic factories~~ — ✅ shipped 2026-05-28

**Shipped:** Three new fixtures in `samples/ZeroAlloc.Inject.AotSmoke/` (`HttpRequestContext.cs` for `[Scoped]`; `DecoratorFixture.cs` for `[Decorator]`; `InventoryFixture.cs` for open-generic `IInventory<T>` with a stub `InventoryConsumer` surfacing `IInventory<Product>`) plus matching assertion blocks in `Program.cs`. Asserts ReferenceEquals identity for Scoped (positive within scope + negative across scopes), type-check + behavioral check for Decorator, and behavioral check for the closed-generic resolution.

**Findings worth flagging** (durable record):

- **Open-generic closed-usage requires a stub consumer.** Per `docs/advanced.md:248-256`, an open-generic class registered via `[Singleton/Transient/Scoped]` only resolves closed forms that appear as ctor parameters somewhere in the assembly. The InventoryFixture stub `[Transient] internal sealed class InventoryConsumer(IInventory<Product> _)` is the canonical pattern.
- **`ZAI007` informational fires for the stub** ("Class implements no interfaces and will only be registered as its concrete type"). Expected and harmless — the stub's job is to be a syntactic touchpoint, not to be resolved.
- **`MA0048` enabled but NOT promoted to errors** in this project — fixture files can bundle interface + impl + decorator/consumer per file, matching the existing `Services.cs` convention.

**Design + plan:** [`docs/plans/2026-05-28-aot-smoke-inject-paths-design.md`](plans/2026-05-28-aot-smoke-inject-paths-design.md) + [`docs/plans/2026-05-28-aot-smoke-inject-paths.md`](plans/2026-05-28-aot-smoke-inject-paths.md).

---

## How items get added here

Open a PR adding a new section in this file. Use the same `What / Why / Sketch / Tradeoff / Graduation signal` structure. Items remain open until a follow-up PR strikes them through with a `✅ shipped X.Y.Z` marker and links the release.
