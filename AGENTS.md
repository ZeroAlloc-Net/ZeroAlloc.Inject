# AGENTS.md: ZeroAlloc.Inject

Compile-time dependency injection for .NET. A Roslyn incremental source generator reads `[Transient]`,
`[Scoped]` and `[Singleton]` on classes and writes the registrations, plus an optional hybrid and a
standalone container, with no reflection. Three NuGet packages ship: `ZeroAlloc.Inject` (attributes),
`ZeroAlloc.Inject.Generator` (the analyzer) and `ZeroAlloc.Inject.Container` (bundles both and adds the
container base classes).

## Layout

- `src/ZeroAlloc.Inject.Generator` targets `netstandard2.0` with `EnforceExtendedAnalyzerRules`, so it
  runs inside the compiler host. Keep it free of reflection, and keep cached incremental models value-equal
  (`IncrementalityTests` fails when a step stops caching).
- `ZeroAlloc.Inject.Container` references the generator without its output and copies the built generator
  DLL into `analyzers/dotnet/cs` through the `IncludeGeneratorInPackage` target, so a package check must
  build first.
- `src/ZeroAlloc.Inject` and `src/ZeroAlloc.Inject.Container` target `net10.0;net11.0`.
- `samples/ZeroAlloc.Inject.AotSmoke` is a NativeAOT publish fixture, not a demo. It gets its own CI job.
  `samples/Directory.Build.props` silences `MA0048`, `ZAI007` and `ZAI018` there on purpose.
- `docs/` is user documentation. `docs/plans/` holds design notes.

## Build and test

The SDK is pinned by `global.json` to 11.0.100 (`rollForward: latestMinor`). The test project targets
`net10.0` and `net11.0`, so running it needs the .NET 10 and .NET 11 runtimes, the same two frameworks
the libraries ship.

CI job `build` in `.github/workflows/ci.yml`:

```bash
dotnet tool restore
SEMVER=$(dotnet dotnet-gitversion /output json /showvariable SemVer)
dotnet restore ZeroAlloc.Inject.slnx
dotnet build ZeroAlloc.Inject.slnx --configuration Release --no-restore -p:Version=$SEMVER
dotnet test ZeroAlloc.Inject.slnx --configuration Release --no-build --verbosity normal --logger "trx;LogFilePrefix=test-results" --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
dotnet pack ZeroAlloc.Inject.slnx --configuration Release --no-build --output ./artifacts -p:Version=$SEMVER
```

`$SEMVER` is set by the third command of the block. Pack fails with
`ZAVERSION001` or `ZAVERSION002` unless the same `-p:Version` was passed to the build that produced the
assemblies. The test step also feeds the coverage gate, which fails below 80 percent line coverage.

CI job `aot-smoke` (needs clang and zlib1g-dev; the .NET 11 SDK from `global.json`):

```bash
dotnet restore samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj
dotnet publish samples/ZeroAlloc.Inject.AotSmoke/ZeroAlloc.Inject.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out
timeout --kill-after=10s 120s ./aot-out/ZeroAlloc.Inject.AotSmoke
```

The binary prints `AOT smoke: PASS`.

CI job `sdk10-consumer` (needs the .NET 10.0.1xx SDK; the smoke folder's own `global.json` pins
10.0.100 with `rollForward: latestPatch`, so a newer band cannot stand in for it). It builds two
consumers of the packed packages with Roslyn 5.0: `Generator` references `ZeroAlloc.Inject` and
`ZeroAlloc.Inject.Generator`, `Container` references `ZeroAlloc.Inject.Container` alone and so loads
the generator copy that package bundles:

```bash
dotnet pack ZeroAlloc.Inject.slnx -c Release -o artifacts -p:Version=9.9.9-local.sha$(git rev-parse --short HEAD)
cd tests/ZeroAlloc.Inject.Sdk10Smoke
dotnet run --project Generator -c Release -p:ZeroAllocInjectVersion=9.9.9-local.sha$(git rev-parse --short HEAD)
dotnet run --project Container -c Release -p:ZeroAllocInjectVersion=9.9.9-local.sha$(git rev-parse --short HEAD)
```

CI job `roslyn-floor` calls the shared `roslyn-floor.yml` from ZeroAlloc-Net/.github: it packs the
solution and fails when a packed DLL references Roslyn above 5.0.0.

The `Benchmarks` workflow smoke-runs
`benchmarks/ZeroAlloc.Inject.Benchmarks` on `net10.0` through a shared workflow.

Fast inner loop, verified locally and run by no CI job:

```bash
dotnet test tests/ZeroAlloc.Inject.Tests -c Release -f net10.0 --filter "FullyQualifiedName~ResolutionAllocationTests"
```

## Conventions and gotchas

- `TreatWarningsAsErrors` is on and `EnforceCodeStyleInBuild` too, with Meziantou, Roslynator and
  ZeroAlloc analyzers on every non-`netstandard2.0` project. A new warning breaks the build.
- Public API is locked by `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt` in `ZeroAlloc.Inject` and
  `ZeroAlloc.Inject.Container`; `RS0016` and `RS0017` are errors. New public members go in
  `Unshipped.txt`. Do not edit `Shipped.txt` by hand: the release workflow moves entries, and the
  `release-tracking` job fails a release PR that does not.
- Diagnostics `ZAI001` to `ZAI021` are declared in `DiagnosticDescriptors.cs`, tracked in
  `AnalyzerReleases.Unshipped.md` and documented in `docs/diagnostics.md`. Change all three together.
  `ZAI002` and `ZAI005` are retired and their IDs are never reused.
- Behaviour must match in both container modes (hybrid and standalone). `DecoratorModeParityTests` and
  the `AllocationGate` assertions in `ResolutionAllocationTests` cover this; a resolve path change needs
  a case in each mode.
- Generator and container tests compile source strings through `GeneratorTestHelper` and `GeneratedApp`
  and load the result into a separate context. Add a test source there, not a new sample project.
- The generator references `Microsoft.CodeAnalysis.CSharp` 5.0.0, the compiler of SDK band 10.0.1xx,
  the oldest supported SDK. Raise it only together with the documented minimum SDK: a compiler older
  than the Roslyn the generator references skips it with `CS9057`, and nothing is generated.
  `renovate.json` holds it below 5.1 for that reason; the test projects may use a newer Roslyn.
- `tests/ZeroAlloc.Inject.Sdk10Smoke/global.json` stays on 10.0.100 with `rollForward: latestPatch`.
  Moving it to a newer patch makes the smoke build with a newer compiler and check nothing; the shared
  Renovate preset ignores that file.
- `Version` in `Directory.Build.props` is a `1.0.0` fallback and is never a release version.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
