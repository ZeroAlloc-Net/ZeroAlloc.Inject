# IEnumerable<T> Support Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Generate `IEnumerable<T>` resolution in the generated container so that resolving a collection of services returns instances from the same fields/logic as single-service resolution, eliminating the singleton identity split with the fallback provider.

**Architecture:** At generator time, group all non-keyed services by service type (interface). For each service type, emit an `if (serviceType == typeof(IEnumerable<T>))` check in both `ResolveKnown` (root) and `ResolveScopedKnown` (scope) that returns a typed array. Each array element uses the same resolution pattern as single-service resolution (cached fields for singletons/scoped, fresh instances for transients). Also fix last-wins behavior for `GetService<T>()` with multiple registrations.

**Tech Stack:** C# source generator (Roslyn `IIncrementalGenerator`), `netstandard2.0` for generator, `net8.0`/`net10.0` for runtime/tests, xUnit.

---

### Task 1: Build the service-type grouping data structure

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:600-617`

**Context:** The `GenerateServiceProviderClass` method currently separates services into `transients`, `singletons`, `scopeds` lists. We need an additional data structure that groups services by their *service type* (interface) for `IEnumerable<T>` emission.

**Step 1: Add grouping logic after the existing lifetime separation**

After line 617 (after the `foreach` loop that populates `transients`, `singletons`, `scopeds`), add:

```csharp
// Group non-keyed services by service type for IEnumerable<T> support.
// Each entry maps a service type to the list of (service, lifetime, fieldIndex).
// fieldIndex is the index in the corresponding lifetime list (for singleton/scoped field references).
var serviceTypeGroups = new Dictionary<string, List<(ServiceRegistrationInfo svc, string lifetime, int fieldIndex)>>();

for (int i = 0; i < transients.Count; i++)
{
    var svc = transients[i];
    foreach (var st in GetServiceTypes(svc))
    {
        if (!serviceTypeGroups.ContainsKey(st))
            serviceTypeGroups[st] = new List<(ServiceRegistrationInfo, string, int)>();
        serviceTypeGroups[st].Add((svc, "Transient", i));
    }
}
for (int i = 0; i < singletons.Count; i++)
{
    var svc = singletons[i];
    foreach (var st in GetServiceTypes(svc))
    {
        if (!serviceTypeGroups.ContainsKey(st))
            serviceTypeGroups[st] = new List<(ServiceRegistrationInfo, string, int)>();
        serviceTypeGroups[st].Add((svc, "Singleton", i));
    }
}
for (int i = 0; i < scopeds.Count; i++)
{
    var svc = scopeds[i];
    foreach (var st in GetServiceTypes(svc))
    {
        if (!serviceTypeGroups.ContainsKey(st))
            serviceTypeGroups[st] = new List<(ServiceRegistrationInfo, string, int)>();
        serviceTypeGroups[st].Add((svc, "Scoped", i));
    }
}
```

**Step 2: Build and run tests to verify no regressions**

Run: `dotnet test --nologo --verbosity quiet`
Expected: All 137 tests pass (no behavior change yet).

**Step 3: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs
git commit -m "feat: add service-type grouping for IEnumerable<T> support"
```

---

### Task 2: Emit IEnumerable<T> checks in ResolveKnown (root)

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs` — inside `GenerateServiceProviderClass`, before the `return null;` line at the end of `ResolveKnown`

**Context:** The `ResolveKnown` method currently handles transients and singletons individually. We need to add `IEnumerable<T>` checks before the `return null;`. The `using System.Collections.Generic;` import is needed in the generated code. Root excludes scoped services.

**Step 1: Write the failing generator test**

Add to `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`:

```csharp
[Fact]
public void IEnumerable_SingleTransient_GeneratesArrayInResolveKnown()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IFoo { }

        [Transient]
        public class Foo : IFoo { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("IEnumerable<global::TestApp.IFoo>", output);
    Assert.Contains("new global::TestApp.IFoo[]", output);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --verbosity quiet --filter IEnumerable_SingleTransient_GeneratesArrayInResolveKnown`
Expected: FAIL — `IEnumerable` not found in output.

**Step 3: Implement IEnumerable<T> emission in ResolveKnown**

In `GenerateServiceProviderClass`, find the line that emits `return null;` for `ResolveKnown` (after singletons). Before that line, add:

```csharp
// IEnumerable<T> resolution
foreach (var kvp in serviceTypeGroups)
{
    var serviceType = kvp.Key;
    var entries = kvp.Value;

    // Root excludes scoped services
    var rootEntries = new List<(ServiceRegistrationInfo svc, string lifetime, int fieldIndex)>();
    foreach (var entry in entries)
    {
        if (entry.lifetime != "Scoped")
            rootEntries.Add(entry);
    }
    if (rootEntries.Count == 0) continue;

    sb.AppendLine("            if (serviceType == typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">))");
    sb.AppendLine("            {");
    sb.Append("                return new " + serviceType + "[] { ");

    for (int j = 0; j < rootEntries.Count; j++)
    {
        if (j > 0) sb.Append(", ");
        var entry = rootEntries[j];

        if (entry.lifetime == "Transient")
        {
            sb.Append(BuildNewExpression(entry.svc));
        }
        else if (entry.lifetime == "Singleton")
        {
            sb.Append("(" + serviceType + ")GetService(typeof(" + serviceType + "))!");
        }
    }

    sb.AppendLine(" };");
    sb.AppendLine("            }");
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --nologo --verbosity quiet --filter IEnumerable_SingleTransient_GeneratesArrayInResolveKnown`
Expected: PASS

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs
git commit -m "feat: emit IEnumerable<T> checks in root ResolveKnown"
```

---

### Task 3: Emit IEnumerable<T> checks in ResolveScopedKnown (scope)

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs` — inside `ResolveScopedKnown`, before the `return null;` line

**Context:** The scope version includes all lifetimes (transient + singleton + scoped). Transients use `TrackDisposable` when `ImplementsDisposable`. Singletons delegate to `Root.GetService`. Scoped services use the inline lazy-init pattern with `_scoped_N` fields.

**Step 1: Write the failing generator test**

Add to `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`:

```csharp
[Fact]
public void IEnumerable_InScope_IncludesAllLifetimes()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IHandler { }

        [Transient(AllowMultiple = true)]
        public class HandlerA : IHandler { }

        [Singleton(AllowMultiple = true)]
        public class HandlerB : IHandler { }

        [Scoped(AllowMultiple = true)]
        public class HandlerC : IHandler { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    // Find the scope's ResolveScopedKnown section
    var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
    Assert.Contains("IEnumerable<global::TestApp.IHandler>", scopeSection);
    // All three should be in the array
    Assert.Contains("HandlerA", scopeSection);
    Assert.Contains("HandlerB", scopeSection);
    Assert.Contains("HandlerC", scopeSection);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --verbosity quiet --filter IEnumerable_InScope_IncludesAllLifetimes`
Expected: FAIL — scope section doesn't contain `IEnumerable`.

**Step 3: Implement IEnumerable<T> emission in ResolveScopedKnown**

In `GenerateServiceProviderClass`, find the line that emits `return null;` for `ResolveScopedKnown` (inside the Scope class). Before that line, add:

```csharp
// IEnumerable<T> resolution in scope (all lifetimes)
foreach (var kvp in serviceTypeGroups)
{
    var serviceType = kvp.Key;
    var entries = kvp.Value;
    if (entries.Count == 0) continue;

    sb.AppendLine("                if (serviceType == typeof(System.Collections.Generic.IEnumerable<" + serviceType + ">))");
    sb.AppendLine("                {");
    sb.Append("                    return new " + serviceType + "[] { ");

    for (int j = 0; j < entries.Count; j++)
    {
        if (j > 0) sb.Append(", ");
        var entry = entries[j];

        if (entry.lifetime == "Transient")
        {
            var newExpr = BuildNewExpressionForScope(entry.svc);
            if (entry.svc.ImplementsDisposable)
            {
                sb.Append("TrackDisposable(" + newExpr + ")");
            }
            else
            {
                sb.Append(newExpr);
            }
        }
        else if (entry.lifetime == "Singleton")
        {
            sb.Append("(" + serviceType + ")Root.GetService(typeof(" + serviceType + "))!");
        }
        else if (entry.lifetime == "Scoped")
        {
            var fieldName = "_scoped_" + entry.fieldIndex;
            var newExpr = BuildNewExpressionForScope(entry.svc);
            if (entry.svc.ImplementsDisposable)
            {
                // Inline: field ?? (field = TrackDisposable(new T()))
                // Use a helper expression to keep the array init readable
                sb.Append(fieldName + " ?? (" + fieldName + " = TrackDisposable(" + newExpr + "))");
            }
            else
            {
                sb.Append(fieldName + " ?? (" + fieldName + " = " + newExpr + ")");
            }
        }
    }

    sb.AppendLine(" };");
    sb.AppendLine("                }");
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test --nologo --verbosity quiet --filter IEnumerable_InScope_IncludesAllLifetimes`
Expected: PASS

**Step 5: Run all tests**

Run: `dotnet test --nologo --verbosity quiet`
Expected: All tests pass.

**Step 6: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs
git commit -m "feat: emit IEnumerable<T> checks in scope ResolveScopedKnown"
```

---

### Task 4: Fix last-wins behavior for GetService with multiple registrations

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs` — change how `EmitTypeChecks` works for transients with multiple registrations for same interface

**Context:** Currently, the generator emits `if (serviceType == typeof(IFoo)) return new Foo1();` for each transient. When two transients implement `IFoo` (via `AllowMultiple`), the first one wins. MS DI returns the last registration. For singletons/scoped, the same issue exists but is less common. The fix: when emitting single-service checks in `ResolveKnown`/`ResolveScopedKnown`, only the **last** registration for a given service type should produce a match for `GetService`.

**Step 1: Write the failing test**

Add to `tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs`:

```csharp
[Fact]
public void GetService_WithMultipleRegistrations_ReturnsLastRegistered()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IHandler { }
        [Transient(AllowMultiple = true)]
        public class HandlerA : IHandler { }
        [Transient(AllowMultiple = true)]
        public class HandlerB : IHandler { }
        """;

    var (assembly, provider) = BuildAndCreateProvider(source);
    var handlerType = assembly.GetType("TestApp.IHandler")!;

    var instance = provider.GetService(handlerType);
    Assert.NotNull(instance);
    // Last registered (HandlerB) should win
    Assert.Equal("HandlerB", instance!.GetType().Name);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --verbosity quiet --filter GetService_WithMultipleRegistrations_ReturnsLastRegistered`
Expected: FAIL — returns HandlerA (first wins).

**Step 3: Implement last-wins**

The approach: before emitting single-service checks for transients and singletons, build a set of service types that have already been emitted by a later registration. Only emit the `if` check for the **last** registration for each service type.

In `GenerateServiceProviderClass`, replace the transient and singleton emission blocks in `ResolveKnown`. Build a `HashSet<string>` of service types already claimed by a later entry:

```csharp
// Build last-wins lookup: for each service type, which is the last registration?
var lastWinsRoot = new HashSet<string>();
// Walk backwards through singletons, then transients (singletons come after transients in the output)
for (int i = singletons.Count - 1; i >= 0; i--)
{
    foreach (var st in GetServiceTypes(singletons[i]))
        lastWinsRoot.Add(st);
}
for (int i = transients.Count - 1; i >= 0; i--)
{
    foreach (var st in GetServiceTypes(transients[i]))
        lastWinsRoot.Add(st);
}
```

Then in the transient/singleton emit loops, track which service types have already been emitted. Skip emission if the service type was already emitted by a later registration:

```csharp
var emittedRootTypes = new HashSet<string>();

// Emit transients — iterate in reverse, only emit first occurrence (= last registered)
// Actually, simpler: iterate forward, but only emit if this is the LAST registration for this type.
// We need to know which index is the last for each type.
```

**Alternative simpler approach:** Build a dictionary mapping each service type to its last registration index+list. Then emit checks only for those last entries. For `ResolveKnown`, iterate all transients and singletons, but skip emitting the `if (serviceType == typeof(...))` check if this registration is NOT the last one for that service type in the combined list.

Build this data structure using the existing `serviceTypeGroups`:

```csharp
// Determine which (svc, lifetime, fieldIndex) is the last registration per service type
// (for single-resolution last-wins behavior)
var lastRegistrationPerType = new Dictionary<string, (ServiceRegistrationInfo svc, string lifetime, int fieldIndex)>();
foreach (var kvp in serviceTypeGroups)
{
    // Last entry in the list is the last registered
    lastRegistrationPerType[kvp.Key] = kvp.Value[kvp.Value.Count - 1];
}
```

Then modify the transient emission loop:

```csharp
// Transients
foreach (var svc in transients)
{
    var newExpr = BuildNewExpression(svc);
    var serviceTypes = GetServiceTypes(svc);
    foreach (var serviceType in serviceTypes)
    {
        // Only emit if this is the last registration for this service type
        if (lastRegistrationPerType.TryGetValue(serviceType, out var last)
            && last.svc == svc && last.lifetime == "Transient")
        {
            sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
            sb.AppendLine("                return " + newExpr + ";");
        }
    }
}
```

Same pattern for singletons — only emit the singleton block if it's the last registration.

Apply the same logic in `ResolveScopedKnown` for scope (using all lifetimes including scoped).

**Step 4: Run test to verify it passes**

Run: `dotnet test --nologo --verbosity quiet --filter GetService_WithMultipleRegistrations_ReturnsLastRegistered`
Expected: PASS

**Step 5: Run all tests**

Run: `dotnet test --nologo --verbosity quiet`
Expected: All tests pass.

**Step 6: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs
git commit -m "fix: GetService returns last registration (last-wins, matching MS DI)"
```

---

### Task 5: Integration tests for IEnumerable<T> runtime behavior

**Files:**
- Modify: `tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs`

**Step 1: Write integration tests**

Add the following tests:

```csharp
// 17. IEnumerable<T> returns all registered implementations
[Fact]
public void IEnumerable_ReturnsAllRegistrations()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IHandler { }
        [Transient(AllowMultiple = true)]
        public class HandlerA : IHandler { }
        [Transient(AllowMultiple = true)]
        public class HandlerB : IHandler { }
        """;

    var (assembly, provider) = BuildAndCreateProvider(source);
    var enumerableType = typeof(IEnumerable<>).MakeGenericType(assembly.GetType("TestApp.IHandler")!);

    var result = provider.GetService(enumerableType);
    Assert.NotNull(result);

    var array = ((System.Collections.IEnumerable)result!).Cast<object>().ToArray();
    Assert.Equal(2, array.Length);
}

// 18. Singleton identity consistent between GetService and IEnumerable
[Fact]
public void IEnumerable_SingletonIdentity_ConsistentWithGetService()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface ICache { }
        [Singleton]
        public class Cache : ICache { }
        """;

    var (assembly, provider) = BuildAndCreateProvider(source);
    var cacheType = assembly.GetType("TestApp.ICache")!;
    var enumerableType = typeof(IEnumerable<>).MakeGenericType(cacheType);

    var single = provider.GetService(cacheType);
    var enumerable = provider.GetService(enumerableType);
    var array = ((System.Collections.IEnumerable)enumerable!).Cast<object>().ToArray();

    Assert.Single(array);
    Assert.Same(single, array[0]);
}

// 19. Scoped identity consistent between GetService and IEnumerable in scope
[Fact]
public void IEnumerable_ScopedIdentity_ConsistentWithGetService()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IRepo { }
        [Scoped]
        public class Repo : IRepo { }
        """;

    var (assembly, provider) = BuildAndCreateProvider(source);
    var repoType = assembly.GetType("TestApp.IRepo")!;
    var enumerableType = typeof(IEnumerable<>).MakeGenericType(repoType);
    var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;

    using var scope = scopeFactory.CreateScope();
    var single = scope.ServiceProvider.GetService(repoType);
    var enumerable = scope.ServiceProvider.GetService(enumerableType);
    var array = ((System.Collections.IEnumerable)enumerable!).Cast<object>().ToArray();

    Assert.Single(array);
    Assert.Same(single, array[0]);
}

// 20. IEnumerable at root excludes scoped services
[Fact]
public void IEnumerable_AtRoot_ExcludesScopedServices()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IService { }
        [Scoped]
        public class ScopedOnly : IService { }
        """;

    var (assembly, provider) = BuildAndCreateProvider(source);
    var enumerableType = typeof(IEnumerable<>).MakeGenericType(assembly.GetType("TestApp.IService")!);

    // At root, scoped-only IEnumerable should fall through to fallback (empty)
    var result = provider.GetService(enumerableType);
    // Fallback MS DI returns empty enumerable for unregistered IEnumerable<T>
    // or null if it falls through — either is acceptable
    if (result != null)
    {
        var array = ((System.Collections.IEnumerable)result).Cast<object>().ToArray();
        Assert.Empty(array);
    }
}

// 21. IEnumerable in scope includes all lifetimes
[Fact]
public void IEnumerable_InScope_IncludesAllLifetimes()
{
    const string source = """
        using ZeroInject;
        namespace TestApp;
        public interface IHandler { }
        [Transient(AllowMultiple = true)]
        public class TransientHandler : IHandler { }
        [Singleton(AllowMultiple = true)]
        public class SingletonHandler : IHandler { }
        [Scoped(AllowMultiple = true)]
        public class ScopedHandler : IHandler { }
        """;

    var (assembly, provider) = BuildAndCreateProvider(source);
    var handlerType = assembly.GetType("TestApp.IHandler")!;
    var enumerableType = typeof(IEnumerable<>).MakeGenericType(handlerType);
    var scopeFactory = (IServiceScopeFactory)provider.GetService(typeof(IServiceScopeFactory))!;

    using var scope = scopeFactory.CreateScope();
    var result = scope.ServiceProvider.GetService(enumerableType);
    Assert.NotNull(result);

    var array = ((System.Collections.IEnumerable)result!).Cast<object>().ToArray();
    Assert.Equal(3, array.Length);
}
```

**Step 2: Run all integration tests**

Run: `dotnet test --nologo --verbosity quiet --filter "FullyQualifiedName~IntegrationTests"`
Expected: All pass.

**Step 3: Run full test suite**

Run: `dotnet test --nologo --verbosity quiet`
Expected: All tests pass.

**Step 4: Commit**

```bash
git add tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs
git commit -m "test: add integration tests for IEnumerable<T> support"
```

---

### Task 6: Additional generator tests and edge cases

**Files:**
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Add generator-level tests for edge cases**

```csharp
[Fact]
public void IEnumerable_Singleton_DelegatesToGetService()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface ICache { }

        [Singleton]
        public class Cache : ICache { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("IEnumerable<global::TestApp.ICache>", output);
    Assert.Contains("GetService(typeof(global::TestApp.ICache))", output);
}

[Fact]
public void IEnumerable_ScopedOnly_ExcludedFromRootResolveKnown()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IRepo { }

        [Scoped]
        public class Repo : IRepo { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    // Root ResolveKnown should NOT have IEnumerable<IRepo> (scoped excluded)
    var resolveKnownStart = output.IndexOf("protected override object? ResolveKnown");
    var createScopeStart = output.IndexOf("protected override global::ZeroInject.Container.ZeroInjectScope CreateScopeCore");
    var resolveKnown = output.Substring(resolveKnownStart, createScopeStart - resolveKnownStart);
    Assert.DoesNotContain("IEnumerable<global::TestApp.IRepo>", resolveKnown);

    // Scope ResolveScopedKnown SHOULD have it
    var scopeSection = output.Substring(output.IndexOf("ResolveScopedKnown"));
    Assert.Contains("IEnumerable<global::TestApp.IRepo>", scopeSection);
}

[Fact]
public void IEnumerable_MultipleTransients_GeneratesArrayWithAll()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IHandler { }

        [Transient(AllowMultiple = true)]
        public class HandlerA : IHandler { }

        [Transient(AllowMultiple = true)]
        public class HandlerB : IHandler { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("IEnumerable<global::TestApp.IHandler>", output);
    Assert.Contains("new global::TestApp.HandlerA()", output);
    Assert.Contains("new global::TestApp.HandlerB()", output);
}
```

**Step 2: Run tests**

Run: `dotnet test --nologo --verbosity quiet`
Expected: All tests pass.

**Step 3: Commit**

```bash
git add tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs
git commit -m "test: add generator tests for IEnumerable<T> edge cases"
```

---

### Task 7: Final verification and cleanup

**Files:**
- Review: all modified files

**Step 1: Run full test suite**

Run: `dotnet test --nologo --verbosity quiet`
Expected: All tests pass, 0 warnings from library/test projects.

**Step 2: Build to check for warnings**

Run: `dotnet build --nologo --verbosity quiet 2>&1 | grep -i warning`
Expected: No new warnings (only the existing ZI007 from sample project).

**Step 3: Commit any cleanup**

If any cleanup was needed, commit it.
