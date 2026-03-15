# ZeroAlloc.Inject Rebrand Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Rename everything from `ZInject` to `ZeroAlloc.Inject` — packages, namespaces, classes, diagnostic codes, directories, solution file, CI workflows, and docs — with no backward compatibility.

**Architecture:** Git-mv the directories, rename project/solution files, then do a global find-replace across all `.cs`, `.csproj`, `.slnx`, `.yml`, `.md` files. Class names with `ZInject` prefix get renamed to `ZeroAllocInject`. Diagnostic prefix changes from `ZI` to `ZAI`.

**Tech Stack:** .NET 8/9/10, C# source generators (Roslyn), MSBuild, GitHub Actions, NuGet.

**Future note:** Repo may eventually move to the `ZeroAlloc-Net` GitHub org (`https://github.com/ZeroAlloc-Net`). Not in scope here.

---

### Task 1: Rename directories and project files

**Files:**
- Rename: `src/ZInject/` → `src/ZeroAlloc.Inject/`
- Rename: `src/ZInject.Container/` → `src/ZeroAlloc.Inject.Container/`
- Rename: `src/ZInject.Generator/` → `src/ZeroAlloc.Inject.Generator/`
- Rename: `tests/ZInject.Tests/` → `tests/ZeroAlloc.Inject.Tests/`
- Rename: `benchmarks/ZInject.Benchmarks/` → `benchmarks/ZeroAlloc.Inject.Benchmarks/`
- Rename: `samples/ZInject.Sample/` → `samples/ZeroAlloc.Inject.Sample/`
- Rename `.csproj` files inside each new directory to match

**Step 1: Rename directories with git mv**

```bash
cd c:/Projects/Prive/DI
git mv src/ZInject src/ZeroAlloc.Inject
git mv src/ZInject.Container src/ZeroAlloc.Inject.Container
git mv src/ZInject.Generator src/ZeroAlloc.Inject.Generator
git mv tests/ZInject.Tests tests/ZeroAlloc.Inject.Tests
git mv benchmarks/ZInject.Benchmarks benchmarks/ZeroAlloc.Inject.Benchmarks
git mv samples/ZInject.Sample samples/ZeroAlloc.Inject.Sample
```

**Step 2: Rename .csproj files inside each directory**

```bash
git mv src/ZeroAlloc.Inject/ZInject.csproj src/ZeroAlloc.Inject/ZeroAlloc.Inject.csproj
git mv src/ZeroAlloc.Inject.Container/ZInject.Container.csproj src/ZeroAlloc.Inject.Container/ZeroAlloc.Inject.Container.csproj
git mv src/ZeroAlloc.Inject.Generator/ZInject.Generator.csproj src/ZeroAlloc.Inject.Generator/ZeroAlloc.Inject.Generator.csproj
git mv tests/ZeroAlloc.Inject.Tests/ZInject.Tests.csproj tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj
git mv benchmarks/ZeroAlloc.Inject.Benchmarks/ZInject.Benchmarks.csproj benchmarks/ZeroAlloc.Inject.Benchmarks/ZeroAlloc.Inject.Benchmarks.csproj
git mv samples/ZeroAlloc.Inject.Sample/ZInject.Sample.csproj samples/ZeroAlloc.Inject.Sample/ZeroAlloc.Inject.Sample.csproj
```

**Step 3: Rename the solution file**

```bash
git mv ZInject.slnx ZeroAlloc.Inject.slnx
```

**Step 4: Verify rename is tracked by git**

```bash
git status
```
Expected: all renames shown as `renamed:`.

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename directories and project files to ZeroAlloc.Inject"
```

---

### Task 2: Update solution file project references

**Files:**
- Modify: `ZeroAlloc.Inject.slnx`

**Step 1: Open and update ZeroAlloc.Inject.slnx**

Replace all old paths with new paths. The file should look like:

```xml
<Solution>
  <Folder Name="/samples/">
    <Project Path="samples/ZeroAlloc.Inject.Sample/ZeroAlloc.Inject.Sample.csproj" />
  </Folder>
  <Folder Name="/src/">
    <Project Path="src/ZeroAlloc.Inject.Generator/ZeroAlloc.Inject.Generator.csproj" />
    <Project Path="src/ZeroAlloc.Inject/ZeroAlloc.Inject.csproj" />
    <Project Path="src/ZeroAlloc.Inject.Container/ZeroAlloc.Inject.Container.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj" />
  </Folder>
  <Folder Name="/benchmarks/">
    <Project Path="benchmarks/ZeroAlloc.Inject.Benchmarks/ZeroAlloc.Inject.Benchmarks.csproj" />
  </Folder>
</Solution>
```

**Step 2: Verify solution loads**

```bash
dotnet sln ZeroAlloc.Inject.slnx list
```
Expected: lists all 5 projects with new paths.

**Step 3: Commit**

```bash
git add ZeroAlloc.Inject.slnx
git commit -m "refactor: update solution file project references"
```

---

### Task 3: Update .csproj files — package IDs, assembly names, project references

**Files:**
- Modify: `src/ZeroAlloc.Inject/ZeroAlloc.Inject.csproj`
- Modify: `src/ZeroAlloc.Inject.Container/ZeroAlloc.Inject.Container.csproj`
- Modify: `src/ZeroAlloc.Inject.Generator/ZeroAlloc.Inject.Generator.csproj`
- Modify: `tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj`
- Modify: `benchmarks/ZeroAlloc.Inject.Benchmarks/ZeroAlloc.Inject.Benchmarks.csproj`
- Modify: `samples/ZeroAlloc.Inject.Sample/ZeroAlloc.Inject.Sample.csproj`

**Step 1: In each .csproj, replace all occurrences of:**
- `<PackageId>ZInject</PackageId>` → `<PackageId>ZeroAlloc.Inject</PackageId>`
- `<PackageId>ZInject.Container</PackageId>` → `<PackageId>ZeroAlloc.Inject.Container</PackageId>`
- `<PackageId>ZInject.Generator</PackageId>` → `<PackageId>ZeroAlloc.Inject.Generator</PackageId>`
- `<AssemblyName>ZInject</AssemblyName>` → `<AssemblyName>ZeroAlloc.Inject</AssemblyName>` (if present)
- `<RootNamespace>ZInject</RootNamespace>` → `<RootNamespace>ZeroAlloc.Inject</RootNamespace>` (if present)
- Any `<ProjectReference Include="...ZInject...` paths → updated paths

**Step 2: Read each csproj to find exact current content before editing** (use Read tool, not grep).

**Step 3: Verify the solution still restores**

```bash
dotnet restore ZeroAlloc.Inject.slnx
```
Expected: restore succeeds (0 errors).

**Step 4: Commit**

```bash
git add src/ tests/ benchmarks/ samples/
git commit -m "refactor: update csproj package IDs and project references"
```

---

### Task 4: Replace C# namespaces and using directives

This is the bulk of the rename. We do a global find-replace on all non-generated `.cs` files (exclude `obj/` directories).

**Step 1: Replace namespace declarations**

In all `.cs` files under `src/`, `tests/`, `benchmarks/`, `samples/` (excluding `obj/`):

| Old | New |
|---|---|
| `namespace ZInject;` | `namespace ZeroAlloc.Inject;` |
| `namespace ZInject.Container;` | `namespace ZeroAlloc.Inject.Container;` |
| `namespace ZInject.Generator` | `namespace ZeroAlloc.Inject.Generator` |
| `namespace ZInject.Tests` | `namespace ZeroAlloc.Inject.Tests` |
| `namespace ZInject.Tests.GeneratorTests` | `namespace ZeroAlloc.Inject.Tests.GeneratorTests` |
| `namespace ZInject.Tests.ContainerTests` | `namespace ZeroAlloc.Inject.Tests.ContainerTests` |

**Step 2: Replace using directives**

| Old | New |
|---|---|
| `using ZInject;` | `using ZeroAlloc.Inject;` |
| `using ZInject.Container;` | `using ZeroAlloc.Inject.Container;` |
| `using ZInject.Generator;` | `using ZeroAlloc.Inject.Generator;` |

**Step 3: Replace fully-qualified type references in generator string literals**

In `src/ZeroAlloc.Inject.Generator/ZeroAllocInjectGenerator.cs`:

| Old string | New string |
|---|---|
| `"ZInject.TransientAttribute"` | `"ZeroAlloc.Inject.TransientAttribute"` |
| `"ZInject.ScopedAttribute"` | `"ZeroAlloc.Inject.ScopedAttribute"` |
| `"ZInject.SingletonAttribute"` | `"ZeroAlloc.Inject.SingletonAttribute"` |
| `"ZInject.ZInjectAttribute"` | `"ZeroAlloc.Inject.ZeroAllocInjectAttribute"` |
| `"ZInject.DecoratorAttribute"` | `"ZeroAlloc.Inject.DecoratorAttribute"` |
| `"ZInject.DecoratorOfAttribute"` | `"ZeroAlloc.Inject.DecoratorOfAttribute"` |
| `"ZInject.OptionalDependencyAttribute"` | `"ZeroAlloc.Inject.OptionalDependencyAttribute"` |
| `if (asm.Name == "ZInject.Container")` | `if (asm.Name == "ZeroAlloc.Inject.Container")` |

**Step 4: Replace generated namespace string literals in the generator**

| Old | New |
|---|---|
| `"namespace ZInject.Generated"` | `"namespace ZeroAlloc.Inject.Generated"` |
| `"global::ZInject.Container.ZInjectServiceProviderBase"` | `"global::ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase"` |
| `"global::ZInject.Container.ZInjectScope"` | `"global::ZeroAlloc.Inject.Container.ZeroAllocInjectScope"` |
| `"global::ZInject.Container.ZInjectStandaloneProvider"` | `"global::ZeroAlloc.Inject.Container.ZeroAllocInjectStandaloneProvider"` |
| `"global::ZInject.Container.ZInjectStandaloneScope"` | `"global::ZeroAlloc.Inject.Container.ZeroAllocInjectStandaloneScope"` |
| `"global::ZInject.Generated."` | `"global::ZeroAlloc.Inject.Generated."` |

**Step 5: Replace source file hint names in the generator**

| Old | New |
|---|---|
| `"ZInject.ServiceCollectionExtensions.g.cs"` | `"ZeroAlloc.Inject.ServiceCollectionExtensions.g.cs"` |
| `"ZInject.ServiceProvider.g.cs"` | `"ZeroAlloc.Inject.ServiceProvider.g.cs"` |

**Step 6: Build to check for compile errors**

```bash
dotnet build ZeroAlloc.Inject.slnx --configuration Release
```
Expected: 0 errors.

**Step 7: Commit**

```bash
git add src/ tests/ benchmarks/ samples/
git commit -m "refactor: update all C# namespaces and using directives to ZeroAlloc.Inject"
```

---

### Task 5: Rename C# classes with ZInject prefix

**Files:**
- Modify: `src/ZeroAlloc.Inject/ZInjectAttribute.cs` → rename file too
- Modify: `src/ZeroAlloc.Inject.Container/ZInjectScope.cs` → rename file
- Modify: `src/ZeroAlloc.Inject.Container/ZInjectServiceProviderBase.cs` → rename file
- Modify: `src/ZeroAlloc.Inject.Container/ZInjectStandaloneProvider.cs` → rename file
- Modify: `src/ZeroAlloc.Inject.Container/ZInjectStandaloneScope.cs` → rename file
- Modify: `src/ZeroAlloc.Inject.Generator/ZInjectGenerator.cs` → rename file
- Modify: `tests/ZeroAlloc.Inject.Tests/GeneratorTests/GeneratorTestHelper.cs`
- Modify: `samples/ZeroAlloc.Inject.Sample/Program.cs`

**Step 1: Rename source files**

```bash
git mv src/ZeroAlloc.Inject/ZInjectAttribute.cs src/ZeroAlloc.Inject/ZeroAllocInjectAttribute.cs
git mv src/ZeroAlloc.Inject.Container/ZInjectScope.cs src/ZeroAlloc.Inject.Container/ZeroAllocInjectScope.cs
git mv src/ZeroAlloc.Inject.Container/ZInjectServiceProviderBase.cs src/ZeroAlloc.Inject.Container/ZeroAllocInjectServiceProviderBase.cs
git mv src/ZeroAlloc.Inject.Container/ZInjectStandaloneProvider.cs src/ZeroAlloc.Inject.Container/ZeroAllocInjectStandaloneProvider.cs
git mv src/ZeroAlloc.Inject.Container/ZInjectStandaloneScope.cs src/ZeroAlloc.Inject.Container/ZeroAllocInjectStandaloneScope.cs
git mv src/ZeroAlloc.Inject.Generator/ZInjectGenerator.cs src/ZeroAlloc.Inject.Generator/ZeroAllocInjectGenerator.cs
```

**Step 2: In each renamed file, rename the class**

| Old class name | New class name |
|---|---|
| `ZInjectAttribute` | `ZeroAllocInjectAttribute` |
| `ZInjectScope` | `ZeroAllocInjectScope` |
| `ZInjectServiceProviderBase` | `ZeroAllocInjectServiceProviderBase` |
| `ZInjectStandaloneProvider` | `ZeroAllocInjectStandaloneProvider` |
| `ZInjectStandaloneScope` | `ZeroAllocInjectStandaloneScope` |
| `ZInjectGenerator` | `ZeroAllocInjectGenerator` |

**Step 3: Update all references to these class names across the codebase**

In `GeneratorTestHelper.cs`: `typeof(ZeroAlloc.Inject.Container.ZeroAllocInjectServiceProviderBase)`
In `ZeroAllocInjectGenerator.cs`: the `[Generator]` attribute stays, class name changes.
In `Program.cs` (sample): `BuildZeroAllocInjectServiceProvider()` (covered in Task 6).

**Step 4: Update the generated extension method and factory class names in the generator**

In `ZeroAllocInjectGenerator.cs`, replace the string literals for generated class names:

| Old | New |
|---|---|
| `"public static class ZInjectServiceCollectionExtensions"` | `"public static class ZeroAllocInjectServiceCollectionExtensions"` |
| `"public static IServiceProvider BuildZInjectServiceProvider(this IServiceCollection services)"` | `"public static IServiceProvider BuildZeroAllocInjectServiceProvider(this IServiceCollection services)"` |
| `"public sealed class ZInjectServiceProviderFactory : IServiceProviderFactory<IServiceCollection>"` | `"public sealed class ZeroAllocInjectServiceProviderFactory : IServiceProviderFactory<IServiceCollection>"` |

**Step 5: Build**

```bash
dotnet build ZeroAlloc.Inject.slnx --configuration Release
```
Expected: 0 errors.

**Step 6: Commit**

```bash
git add -A
git commit -m "refactor: rename ZInject-prefixed classes to ZeroAllocInject"
```

---

### Task 6: Rename diagnostic codes ZI→ZAI and update category string

**Files:**
- Modify: `src/ZeroAlloc.Inject.Generator/DiagnosticDescriptors.cs`
- Modify: `tests/ZeroAlloc.Inject.Tests/GeneratorTests/DiagnosticTests.cs`

**Step 1: In DiagnosticDescriptors.cs**

Replace every occurrence of:
- `"ZI001"` → `"ZAI001"`, `"ZI002"` → `"ZAI002"`, ..., `"ZI018"` → `"ZAI018"`
- `"ZInject"` (category string) → `"ZeroAlloc.Inject"` (appears 18 times, once per descriptor)

**Step 2: In DiagnosticTests.cs, update every diagnostic ID string**

Replace:
- `"ZI006"` → `"ZAI006"` (all occurrences — test method names and Assert calls)
- `"ZI007"` → `"ZAI007"`
- `"ZI011"` → `"ZAI011"`
- `"ZI012"` → `"ZAI012"`
- `"ZI013"` → `"ZAI013"`
- `"ZI014"` → `"ZAI014"`
- `"ZI015"` → `"ZAI015"`
- `"ZI016"` → `"ZAI016"`
- `"ZI017"` → `"ZAI017"`
- `"ZI018"` → `"ZAI018"`

Also rename test method names from e.g. `NoPublicConstructor_ProducesZI006` → `NoPublicConstructor_ProducesZAI006`.

**Step 3: Search for any other ZI0xx references in tests or docs**

```bash
grep -rn "ZI0[0-9][0-9]" . --include="*.cs" --include="*.md" --exclude-dir=obj
```
Expected: 0 matches.

**Step 4: Build and run tests**

```bash
dotnet build ZeroAlloc.Inject.slnx --configuration Release
dotnet test tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj --configuration Release --no-build --verbosity normal
```
Expected: all tests pass.

**Step 5: Commit**

```bash
git add src/ tests/
git commit -m "refactor: rename diagnostic codes ZI→ZAI and update category to ZeroAlloc.Inject"
```

---

### Task 7: Update CI workflows

**Files:**
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/publish.yml`
- Modify: `.github/workflows/release-please.yml` (if it references ZInject)

**Step 1: Read each workflow file first** (use Read tool).

**Step 2: In ci.yml, replace:**
- `ZInject.slnx` → `ZeroAlloc.Inject.slnx`
- `tests/ZInject.Tests/ZInject.Tests.csproj` → `tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj`

**Step 3: In publish.yml, replace:**
- `ZInject.slnx` → `ZeroAlloc.Inject.slnx`
- `tests/ZInject.Tests/ZInject.Tests.csproj` → `tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj`

**Step 4: Check release-please.yml for any ZInject references and update.**

**Step 5: Commit**

```bash
git add .github/
git commit -m "refactor: update CI workflows for ZeroAlloc.Inject directory/solution rename"
```

---

### Task 8: Update README.md and docs

**Files:**
- Modify: `README.md`
- Modify: `docs/features.md` (if it exists)
- Modify: any other `.md` files

**Step 1: Search for all ZInject references in markdown**

```bash
grep -rn "ZInject\|ZI0[0-9][0-9]" . --include="*.md" --exclude-dir=obj
```

**Step 2: Update README.md**

Key replacements:
- Package name: `ZInject` → `ZeroAlloc.Inject`
- `using ZInject;` → `using ZeroAlloc.Inject;`
- `services.BuildZInjectServiceProvider()` → `services.BuildZeroAllocInjectServiceProvider()`
- `[assembly: ZInject("...")]` → `[assembly: ZeroAllocInject("...")]`
- Diagnostic codes: `ZI0xx` → `ZAI0xx`
- NuGet install commands: `dotnet add package ZInject` → `dotnet add package ZeroAlloc.Inject`

**Step 3: Update docs/features.md with same replacements.**

**Step 4: Commit**

```bash
git add README.md docs/
git commit -m "docs: update README and docs for ZeroAlloc.Inject rebrand"
```

---

### Task 9: Update samples

**Files:**
- Modify: `samples/ZeroAlloc.Inject.Sample/Program.cs`
- Modify: `samples/ZeroAlloc.Inject.Sample/UseCases/*.cs`

**Step 1: In all sample files, replace:**
- `using ZInject;` → `using ZeroAlloc.Inject;`
- `BuildZInjectServiceProvider()` → `BuildZeroAllocInjectServiceProvider()`
- `[assembly: ZInject("...")]` → `[assembly: ZeroAllocInject("...")]`

**Step 2: Build sample to verify it compiles**

```bash
dotnet build samples/ZeroAlloc.Inject.Sample/ZeroAlloc.Inject.Sample.csproj --configuration Release
```
Expected: 0 errors.

**Step 3: Commit**

```bash
git add samples/
git commit -m "refactor: update samples for ZeroAlloc.Inject rebrand"
```

---

### Task 10: Final build and full test run

**Step 1: Clean and full build**

```bash
dotnet build ZeroAlloc.Inject.slnx --configuration Release
```
Expected: 0 errors, 0 warnings (outside of known intentional ones).

**Step 2: Run all tests**

```bash
dotnet test tests/ZeroAlloc.Inject.Tests/ZeroAlloc.Inject.Tests.csproj --configuration Release --no-build --verbosity normal
```
Expected: all tests pass, 0 failures.

**Step 3: Verify no remaining ZInject references in source (excluding obj/)**

```bash
grep -rn "ZInject" . --include="*.cs" --include="*.csproj" --include="*.slnx" --include="*.yml" --include="*.md" --exclude-dir=obj --exclude-dir=bin 2>/dev/null | grep -v "ZeroAlloc.Inject" | grep -v "^Binary"
```
Expected: 0 lines (all remaining hits should contain `ZeroAlloc.Inject`).

**Step 4: Pack to verify package IDs**

```bash
dotnet pack ZeroAlloc.Inject.slnx --configuration Release --no-build --output ./artifacts
ls ./artifacts/
```
Expected: `ZeroAlloc.Inject.*.nupkg`, `ZeroAlloc.Inject.Container.*.nupkg`, `ZeroAlloc.Inject.Generator.*.nupkg`.

**Step 5: Commit**

```bash
git add -A
git commit -m "refactor: final verification — ZeroAlloc.Inject rebrand complete"
```

---

### Task 11: Rename GitHub repo

This step is manual (cannot be done via CLI without GitHub CLI):

```bash
gh repo rename ZeroAlloc.Inject --repo MarcelRoozekrans/ZInject
```

GitHub will auto-redirect old URLs (`MarcelRoozekrans/ZInject`) to the new name.

Also update any hardcoded repo URL in README badges, if present.

**Step 1: Check README for hardcoded repo URLs**

```bash
grep -n "MarcelRoozekrans/ZInject" README.md
```

**Step 2: Update any badge URLs or links.**

**Step 3: Commit and push**

```bash
git add README.md
git commit -m "docs: update repo URLs to ZeroAlloc.Inject"
git push
```

**Step 4: Rename the repo**

```bash
gh repo rename ZeroAlloc.Inject --repo MarcelRoozekrans/ZInject
```

**Step 5: Update the remote URL locally**

```bash
git remote set-url origin https://github.com/MarcelRoozekrans/ZeroAlloc.Inject.git
```
