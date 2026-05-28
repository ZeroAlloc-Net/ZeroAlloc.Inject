# ZeroAlloc.Inject — Backlog

Candidate enhancements identified during real-world usage. Each item is independent and can be implemented in any order. Order is rough priority, not commitment. Items graduate from this backlog when the friction or value is concrete enough to justify the work.

---

## B1 — Extend aot-smoke to cover Scoped lifetime, Decorators, and closed-generic factories

**What.** The existing `aot-smoke` project (`samples/ZeroAlloc.Inject.AotSmoke/`) exercises `Singleton` + `Transient` service resolution and basic constructor injection. It does NOT touch three other code paths the generator emits: `Scoped` lifetime services, the `Decorator` pattern (emitted but not asserted), and closed-generic factory dispatch. A regression in any of those three under Native AOT + the trimmer would surface in a downstream consumer rather than in CI.

**Why.** Surfaced 2026-05-27 during the org-wide AOT-smoke coverage survey done after [ZeroAlloc.Serialisation](https://github.com/ZeroAlloc-Net/ZeroAlloc.Serialisation) shipped 2.3.1 + 2.3.2 reactively. ZA.Serialisation's smoke covered only the V0 `[ZeroAllocSerializable]` path while V1 `[ValueObject]` paths were left un-validated — two patches shipped because of that gap. Same "smoke exists but partial" pattern applies here.

**Sketch.** Extend `samples/ZeroAlloc.Inject.AotSmoke/Program.cs` with three additional fixtures + assertions:

- A `Scoped` lifetime service resolved twice within a single scope (assert instance identity), again across scopes (assert different instances).
- A decorator chain (e.g. `LoggingDecorator<IFoo>` wrapping a concrete `Foo`) resolved and invoked end-to-end; assert the decorator wraps the base.
- A closed-generic factory (e.g. `IHandler<TRequest>` registered for a specific `TRequest`); assert the right closed-generic instance is resolved.

Each fixture rounds out to one assertion plus a non-zero exit on failure (matches the existing smoke shape).

**Tradeoff / risks.** Extends an existing CI job — no new infrastructure. Smoke binary stays small (~50 extra LOC). Risk of false positives if the trimmer changes; mitigated by the assertion-per-path structure (a single regression fails the matching invariant, not a fragile string match).

**Graduation signal.** First downstream consumer pattern that hits a Scoped/Decorator/closed-generic AOT bug. Or proactive: pair with the ZA.Inject work that next touches the generator.

---

## How items get added here

Open a PR adding a new section in this file. Use the same `What / Why / Sketch / Tradeoff / Graduation signal` structure. Items remain open until a follow-up PR strikes them through with a `✅ shipped X.Y.Z` marker and links the release.
