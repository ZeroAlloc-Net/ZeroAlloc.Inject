# Four Features Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Bring ZeroInject to production readiness by implementing `IServiceProviderIsService`, multi-decorator stacking, compile-time circular dependency detection, and documenting thread-safety contract.

**Architecture:** Each feature touches `DiagnosticDescriptors.cs` (new ZI014), the four base classes in `ZeroInject.Container`, and the code generator in `ZeroInjectGenerator.cs`. Features are independent — each task produces a passing build and test suite.

**Tech Stack:** C# 12, Roslyn `IIncrementalGenerator`, xUnit, `GeneratorTestHelper.RunGenerator()` / `RunGeneratorWithContainer()` for generator tests.

---

### Task 1: `IServiceProviderIsService` — Base Classes

**Files:**
- Modify: `src/ZeroInject.Container/ZeroInjectServiceProviderBase.cs:5` (class declaration)
- Modify: `src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs:5` (class declaration)
- Modify: `src/ZeroInject.Container/ZeroInjectScope.cs:5` (class declaration)
- Modify: `src/ZeroInject.Container/ZeroInjectStandaloneScope.cs:5` (class declaration)
- Test: `tests/ZeroInject.Tests/ContainerTests/ServiceProviderBaseTests.cs`
- Test: `tests/ZeroInject.Tests/ContainerTests/StandaloneProviderBaseTests.cs`

**Step 1: Write failing tests for hybrid provider**

Add to `ServiceProviderBaseTests.cs`:

```csharp
[Fact]
public void GetService_IServiceProviderIsService_ReturnsSelf()
{
    var fallback = new ServiceCollection().BuildServiceProvider();
    var provider = new TestProvider(fallback);
    var result = provider.GetService(typeof(IServiceProviderIsService));
    Assert.NotNull(result);
    Assert.Same(provider, result);
}
```

Update the existing `TestProvider` class (it already extends `ZeroInjectServiceProviderBase`) — add `IsKnownService` override:

```csharp
protected override bool IsKnownService(Type serviceType) => false;
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests --filter "ServiceProviderBaseTests" --no-build`
Expected: Build failure — `IsKnownService` does not exist yet.

**Step 3: Implement hybrid base class changes**

In `src/ZeroInject.Container/ZeroInjectServiceProviderBase.cs`:

1. Add `using Microsoft.Extensions.DependencyInjection;` (already present) — add interface:
```csharp
public abstract class ZeroInjectServiceProviderBase : IServiceProvider, IServiceScopeFactory,
    IServiceProviderIsService, IDisposable, IAsyncDisposable
```

2. Add `IsService` method and abstract `IsKnownService`:
```csharp
public bool IsService(Type serviceType)
{
    if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScopeFactory)
        || serviceType == typeof(IServiceProviderIsService))
        return true;
    return IsKnownService(serviceType)
        || (_fallback as IServiceProviderIsService)?.IsService(serviceType) == true;
}

protected abstract bool IsKnownService(Type serviceType);
```

3. Add to `GetService` — before the fallback line:
```csharp
if (serviceType == typeof(IServiceProviderIsService))
{
    return this;
}
```

**Step 4: Implement hybrid scope changes**

In `src/ZeroInject.Container/ZeroInjectScope.cs`:

1. Add interface:
```csharp
public abstract class ZeroInjectScope : IServiceScope, IServiceProvider,
    IServiceProviderIsService, IDisposable, IAsyncDisposable
```

2. Add `IsService` delegation:
```csharp
public bool IsService(Type serviceType) => ((IServiceProviderIsService)_root).IsService(serviceType);
```

3. Add to `GetService` — after `IServiceScopeFactory` check:
```csharp
if (serviceType == typeof(IServiceProviderIsService))
{
    return _root;
}
```

**Step 5: Implement standalone provider changes**

In `src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs`:

1. Add interface:
```csharp
public abstract class ZeroInjectStandaloneProvider : IServiceProvider, IServiceScopeFactory,
    IServiceProviderIsService, IDisposable, IAsyncDisposable
```

2. Add `IsService` + abstract:
```csharp
public bool IsService(Type serviceType)
{
    if (serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScopeFactory)
        || serviceType == typeof(IServiceProviderIsService))
        return true;
    return IsKnownService(serviceType);
}

protected abstract bool IsKnownService(Type serviceType);
```

3. Add to `GetService`:
```csharp
if (serviceType == typeof(IServiceProviderIsService))
{
    return this;
}
```

**Step 6: Implement standalone scope changes**

In `src/ZeroInject.Container/ZeroInjectStandaloneScope.cs`:

1. Add interface:
```csharp
public abstract class ZeroInjectStandaloneScope : IServiceScope, IServiceProvider,
    IServiceProviderIsService, IDisposable, IAsyncDisposable
```

2. Add `IsService` delegation:
```csharp
public bool IsService(Type serviceType) => ((IServiceProviderIsService)_root).IsService(serviceType);
```

3. Add to `GetService` — after `IServiceScopeFactory` check:
```csharp
if (serviceType == typeof(IServiceProviderIsService))
{
    return _root;
}
```

**Step 7: Fix test classes — add `IsKnownService` overrides**

All test classes extending these base classes need the new abstract override. Search for all test provider/scope subclasses and add:

```csharp
protected override bool IsKnownService(Type serviceType) => false;
```

**Step 8: Run tests to verify they pass**

Run: `dotnet test tests/ZeroInject.Tests --no-build`
Expected: All existing tests pass + new test passes.

**Step 9: Write failing tests for standalone provider**

Add to `StandaloneProviderBaseTests.cs`:

```csharp
[Fact]
public void GetService_IServiceProviderIsService_ReturnsSelf()
{
    var provider = new TestStandaloneProvider();
    var result = provider.GetService(typeof(IServiceProviderIsService));
    Assert.NotNull(result);
    Assert.Same(provider, result);
}
```

Update `TestStandaloneProvider` with `IsKnownService` override returning `false`.

**Step 10: Run tests**

Run: `dotnet test tests/ZeroInject.Tests --no-build`
Expected: PASS

**Step 11: Commit**

```bash
git add src/ZeroInject.Container/ tests/ZeroInject.Tests/ContainerTests/
git commit -m "feat: implement IServiceProviderIsService on all base classes"
```

---

### Task 2: `IServiceProviderIsService` — Generator Emission

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:729` (`GenerateServiceProviderClass`) and `:1323` (`GenerateStandaloneServiceProviderClass`)
- Test: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write failing generator tests**

Add to `ContainerGeneratorTests.cs`:

```csharp
[Fact]
public void Hybrid_IsKnownService_EmitsTypeChecks()
{
    var source = """
        using ZeroInject;
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("override bool IsKnownService(Type serviceType)", output);
    Assert.Contains("typeof(global::IFoo)", output);
    Assert.Contains("typeof(global::FooImpl)", output);
}

[Fact]
public void Standalone_IsKnownService_EmitsTypeChecks()
{
    var source = """
        using ZeroInject;
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    // Standalone provider should also have IsKnownService
    Assert.Contains("override bool IsKnownService(Type serviceType)", output);
}

[Fact]
public void IsKnownService_OpenGeneric_EmitsGenericTypeDefinitionCheck()
{
    var source = """
        using ZeroInject;
        public interface IRepo<T> { }
        [Transient]
        public class Repo<T> : IRepo<T> { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("IsKnownService", output);
    Assert.Contains("serviceType.IsGenericType", output);
    Assert.Contains("typeof(global::IRepo<>)", output);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests --filter "IsKnownService" --no-build`
Expected: FAIL — no `IsKnownService` emitted.

**Step 3: Implement generator emission**

Add a new helper method `EmitIsKnownService` in `ZeroInjectGenerator.cs` (before `EmitConcreteRegistration` at line ~2278):

```csharp
private static void EmitIsKnownService(
    StringBuilder sb,
    Dictionary<string, List<ServiceTypeGroupEntry>> serviceTypeGroups,
    List<ServiceRegistrationInfo> openGenerics,
    bool hasKeyedServices)
{
    sb.AppendLine("        protected override bool IsKnownService(global::System.Type serviceType)");
    sb.AppendLine("        {");

    if (hasKeyedServices)
    {
        sb.AppendLine("            if (serviceType == typeof(global::Microsoft.Extensions.DependencyInjection.IKeyedServiceProvider)) return true;");
    }

    // Closed types
    foreach (var kvp in serviceTypeGroups)
    {
        sb.AppendLine("            if (serviceType == typeof(" + kvp.Key + ")) return true;");
    }

    // Open generics
    if (openGenerics.Count > 0)
    {
        sb.AppendLine("            if (serviceType.IsGenericType)");
        sb.AppendLine("            {");
        sb.AppendLine("                var _genDef = serviceType.GetGenericTypeDefinition();");
        foreach (var svc in openGenerics)
        {
            foreach (var st in GetServiceTypes(svc))
            {
                sb.AppendLine("                if (_genDef == typeof(" + st + ")) return true;");
            }
        }
        sb.AppendLine("            }");
    }

    sb.AppendLine("            return false;");
    sb.AppendLine("        }");
}
```

Call `EmitIsKnownService` in both:
- `GenerateServiceProviderClass` — in the root provider class body (after `ResolveKnown`)
- `GenerateStandaloneServiceProviderClass` — in both the root provider and scope class bodies

For the scope: emit a simple delegation:
```csharp
sb.AppendLine("        protected override bool IsKnownService(global::System.Type serviceType) => Root.IsKnownService(serviceType);");
```

Wait — `IsKnownService` on the scope base is not abstract (it's on the provider base, and the scope delegates via `IsService`). Actually, looking back at Task 1: the scope classes do NOT have an abstract `IsKnownService` — they delegate `IsService` to root. So the generator only needs to emit `IsKnownService` in the root provider classes (hybrid + standalone). The scope classes inherit delegation from the base.

**Step 4: Run tests**

Run: `dotnet test tests/ZeroInject.Tests --no-build`
Expected: PASS

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ tests/ZeroInject.Tests/GeneratorTests/
git commit -m "feat: emit IsKnownService override from generator"
```

---

### Task 3: Thread Safety — Documentation Only

**Files:**
- Modify: `docs/features.md:55` (move from Vital to Implemented)

**Step 1: Update features.md**

Move "Thread safety of scoped service resolution" from the "Vital" section to the "Implemented" table with the note: "Matches MS DI contract — scopes are per-request, not thread-safe".

Remove the "Vital" subsection header if `IServiceProviderIsService` has also been moved.

**Step 2: Commit**

```bash
git add docs/features.md
git commit -m "docs: document scoped thread-safety matches MS DI contract"
```

---

### Task 4: Multi-Decorator Stacking — Data Structure

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:205-207` (dictionary type)
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:689-701` (`BuildDecoratedNewExpression`)
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:729` (`GenerateServiceProviderClass` signature)
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:1323` (`GenerateStandaloneServiceProviderClass` signature)
- Modify: All callers of `decoratorsByInterface`

**Step 1: Write failing test for multi-decorator**

Add to `tests/ZeroInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs`:

```csharp
[Fact]
public void MultiDecorator_TwoDecorators_ChainsInRegistrationOrder()
{
    var source = """
        using ZeroInject;
        public interface IRepo { }
        [Transient]
        public class ConcreteRepo : IRepo { }
        [Decorator]
        public class CachingRepo : IRepo
        {
            public CachingRepo(IRepo inner) { }
        }
        [Decorator]
        public class LoggingRepo : IRepo
        {
            public LoggingRepo(IRepo inner) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    // Outermost is LoggingRepo (last registered), wrapping CachingRepo, wrapping ConcreteRepo
    Assert.Contains("new global::LoggingRepo(", output);
    Assert.Contains("new global::CachingRepo(", output);
    Assert.Contains("new global::ConcreteRepo()", output);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZeroInject.Tests --filter "MultiDecorator" --no-build`
Expected: FAIL — only one decorator emitted (last one wins).

**Step 3: Change dictionary type**

In `ZeroInjectGenerator.cs`, change all occurrences of `Dictionary<string, DecoratorRegistrationInfo>` to `Dictionary<string, List<DecoratorRegistrationInfo>>`.

Line 205-207 becomes:
```csharp
var decoratorsByInterface = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<DecoratorRegistrationInfo>>();
foreach (var dec in validDecorators)
{
    if (!decoratorsByInterface.TryGetValue(dec.DecoratedInterfaceFqn!, out var list))
    {
        list = new System.Collections.Generic.List<DecoratorRegistrationInfo>();
        decoratorsByInterface[dec.DecoratedInterfaceFqn!] = list;
    }
    list.Add(dec);
}
```

Update all method signatures that accept `Dictionary<string, DecoratorRegistrationInfo>`:
- `GenerateExtensionClass` (line ~220)
- `GenerateServiceProviderClass` (line 729)
- `GenerateStandaloneServiceProviderClass` (line 1323)
- `BuildDecoratedNewExpression` (line 689)
- `EmitOpenGenericFactoryMethod` (line 2095)

**Step 4: Update `BuildDecoratedNewExpression` to chain decorators**

Replace `BuildDecoratedNewExpression` (lines 689-701):

```csharp
private static string BuildDecoratedNewExpression(
    ServiceRegistrationInfo svc,
    string serviceTypeFqn,
    Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface,
    bool forScope)
{
    var baseExpr = forScope ? BuildNewExpressionForScope(svc) : BuildNewExpression(svc);
    if (!decoratorsByInterface.TryGetValue(serviceTypeFqn, out var decorators))
        return baseExpr;

    // Chain decorators: first wraps concrete, each subsequent wraps previous
    var currentExpr = baseExpr;
    var innerConcreteFqn = svc.FullyQualifiedName;
    foreach (var decorator in decorators)
    {
        currentExpr = BuildNewExpressionWithDecorator(
            decorator, innerConcreteFqn, currentExpr, decorator.DecoratedInterfaceFqn!);
    }
    return currentExpr;
}
```

**Step 5: Update `EmitOpenGenericFactoryMethod` for decorator list**

Change parameter from `DecoratorRegistrationInfo? decorator` to `List<DecoratorRegistrationInfo>? decorators` (line 2095). Update the body to chain all decorators:

```csharp
private static void EmitOpenGenericFactoryMethod(
    StringBuilder sb,
    string methodName,
    string typeParams,
    ServiceRegistrationInfo svc,
    List<DecoratorRegistrationInfo>? decorators,
    int arity)
{
    var closedImpl = CloseGenericFqn(svc.FullyQualifiedName, arity);
    var innerExpr = BuildOpenGenericNewExpr(closedImpl, svc.ConstructorParameters, null, null, arity);

    if (decorators == null || decorators.Count == 0)
    {
        sb.AppendLine("        private static object " + methodName + typeParams + "(global::System.IServiceProvider sp)");
        sb.AppendLine("            => " + innerExpr + ";");
        return;
    }

    // Chain decorators using local variables
    sb.AppendLine("        private static object " + methodName + typeParams + "(global::System.IServiceProvider sp)");
    sb.AppendLine("        {");
    sb.AppendLine("            var _layer0 = " + innerExpr + ";");

    for (int i = 0; i < decorators.Count; i++)
    {
        var dec = decorators[i];
        var closedDecorator = CloseGenericFqn(dec.DecoratorFqn, arity);
        var decoratedIface = dec.DecoratedInterfaceFqn;
        var prevVar = "_layer" + i;
        var nextVar = "_layer" + (i + 1);
        var decoratorExpr = BuildOpenGenericNewExpr(closedDecorator, dec.ConstructorParameters, decoratedIface, prevVar, arity);

        if (i < decorators.Count - 1)
            sb.AppendLine("            var " + nextVar + " = " + decoratorExpr + ";");
        else
            sb.AppendLine("            return " + decoratorExpr + ";");
    }

    sb.AppendLine("        }");
}
```

Update the call site (line ~1484) to pass the list instead of single decorator:

```csharp
List<DecoratorRegistrationInfo>? decoratorList = null;
// look up decorator list for this open generic's service type
foreach (var st in GetServiceTypes(svc))
{
    if (decoratorsByInterface.TryGetValue(st, out var list))
    {
        decoratorList = list;
        break;
    }
}
EmitOpenGenericFactoryMethod(sb, "OG_Factory_" + ogIdx, typeParams, svc, decoratorList, arity);
```

**Step 6: Update MS DI extension registration for multi-decorator**

The MS DI extension class emits `AddScoped<IFoo>(sp => new Decorator(sp.GetRequiredService<Concrete>()))`. For multi-decorator this becomes a chained expression — `BuildDecoratedNewExpression` already handles this since we updated it in Step 4. Verify the extension class generation calls `BuildDecoratedNewExpression` and doesn't have its own single-decorator logic.

**Step 7: Run tests**

Run: `dotnet test tests/ZeroInject.Tests --no-build`
Expected: PASS — all existing decorator tests still pass + new multi-decorator test passes.

**Step 8: Write additional multi-decorator tests**

Add to `DecoratorGeneratorTests.cs`:

```csharp
[Fact]
public void MultiDecorator_ThreeDecorators_ChainsAll()
{
    var source = """
        using ZeroInject;
        public interface IRepo { }
        [Transient]
        public class ConcreteRepo : IRepo { }
        [Decorator]
        public class CachingRepo : IRepo { public CachingRepo(IRepo inner) { } }
        [Decorator]
        public class LoggingRepo : IRepo { public LoggingRepo(IRepo inner) { } }
        [Decorator]
        public class RetryRepo : IRepo { public RetryRepo(IRepo inner) { } }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    Assert.Contains("new global::RetryRepo(", output);
    Assert.Contains("new global::LoggingRepo(", output);
    Assert.Contains("new global::CachingRepo(", output);
}

[Fact]
public void MultiDecorator_WithExtraDeps_InjectsAllDeps()
{
    var source = """
        using ZeroInject;
        public interface IRepo { }
        public interface ILogger { }
        public interface ICache { }
        [Transient] public class RepoImpl : IRepo { }
        [Singleton] public class Logger : ILogger { }
        [Singleton] public class Cache : ICache { }
        [Decorator]
        public class CachingRepo : IRepo { public CachingRepo(IRepo inner, ICache cache) { } }
        [Decorator]
        public class LoggingRepo : IRepo { public LoggingRepo(IRepo inner, ILogger logger) { } }
        """;

    var (output, _) = GeneratorTestHelper.RunGenerator(source);
    // Both decorators resolve their extra deps
    Assert.Contains("GetRequiredService<global::ICache>", output);
    Assert.Contains("GetRequiredService<global::ILogger>", output);
}
```

**Step 9: Run tests**

Run: `dotnet test tests/ZeroInject.Tests --no-build`
Expected: PASS

**Step 10: Commit**

```bash
git add src/ZeroInject.Generator/ tests/ZeroInject.Tests/GeneratorTests/
git commit -m "feat: support multi-decorator stacking with chained wrapping"
```

---

### Task 5: Multi-Decorator — Standalone Container Tests

**Files:**
- Test: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write standalone multi-decorator generator test**

```csharp
[Fact]
public void Standalone_MultiDecorator_EmitsChainingInResolveKnown()
{
    var source = """
        using ZeroInject;
        public interface IRepo { }
        [Transient]
        public class ConcreteRepo : IRepo { }
        [Decorator]
        public class CachingRepo : IRepo { public CachingRepo(IRepo inner) { } }
        [Decorator]
        public class LoggingRepo : IRepo { public LoggingRepo(IRepo inner) { } }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    // Standalone provider chains decorators in ResolveKnown
    Assert.Contains("new global::LoggingRepo(", output);
    Assert.Contains("new global::CachingRepo(", output);
    Assert.Contains("new global::ConcreteRepo()", output);
}
```

**Step 2: Run tests**

Run: `dotnet test tests/ZeroInject.Tests --filter "Standalone_MultiDecorator" --no-build`
Expected: PASS (already works from Task 4 changes).

**Step 3: Commit**

```bash
git add tests/ZeroInject.Tests/GeneratorTests/
git commit -m "test: add standalone multi-decorator generator tests"
```

---

### Task 6: Circular Dependency Detection — Diagnostic Descriptor

**Files:**
- Modify: `src/ZeroInject.Generator/DiagnosticDescriptors.cs:109` (add ZI014)

**Step 1: Write failing diagnostic test**

Add to `tests/ZeroInject.Tests/GeneratorTests/DiagnosticTests.cs`:

```csharp
[Fact]
public void ZI014_CircularDependency_AB_ReportsError()
{
    var source = """
        using ZeroInject;
        public interface IA { }
        public interface IB { }
        [Transient]
        public class A : IA { public A(IB b) { } }
        [Transient]
        public class B : IB { public B(IA a) { } }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
}

[Fact]
public void ZI014_NoCycle_NoDiagnostic()
{
    var source = """
        using ZeroInject;
        public interface IA { }
        public interface IB { }
        [Transient]
        public class A : IA { public A(IB b) { } }
        [Transient]
        public class B : IB { }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZeroInject.Tests --filter "ZI014" --no-build`
Expected: FAIL — ZI014 doesn't exist yet.

**Step 3: Add ZI014 descriptor**

In `DiagnosticDescriptors.cs`, after the ZI013 entry (line 109), add:

```csharp
public static readonly DiagnosticDescriptor CircularDependency = new DiagnosticDescriptor(
    "ZI014",
    "Circular dependency detected",
    "Circular dependency detected: {0}",
    "ZeroInject",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Step 4: Commit**

```bash
git add src/ZeroInject.Generator/DiagnosticDescriptors.cs tests/ZeroInject.Tests/GeneratorTests/DiagnosticTests.cs
git commit -m "feat: add ZI014 circular dependency diagnostic descriptor"
```

---

### Task 7: Circular Dependency Detection — Algorithm

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs:208` (call site, after decorator dict built)

**Step 1: Implement `DetectCircularDependencies`**

Add this method to `ZeroInjectGenerator.cs` (near the other diagnostic-related code, before `GenerateExtensionClass`):

```csharp
private static void DetectCircularDependencies(
    SourceProductionContext spc,
    List<ServiceRegistrationInfo> allServices,
    Dictionary<string, List<DecoratorRegistrationInfo>> decoratorsByInterface)
{
    // Build service type → ServiceRegistrationInfo lookup
    // A service provides: its interfaces + concrete type (via GetServiceTypes)
    var serviceByType = new Dictionary<string, ServiceRegistrationInfo>();
    foreach (var svc in allServices)
    {
        foreach (var st in GetServiceTypes(svc))
        {
            serviceByType[st] = svc; // last-wins (same as resolution)
        }
    }

    // Build adjacency list: service FQN → list of dependency FQNs
    var adjacency = new Dictionary<string, List<string>>();
    foreach (var svc in allServices)
    {
        var deps = new List<string>();
        foreach (var param in svc.ConstructorParameters)
        {
            if (param.IsOptional) continue; // optional deps use GetService, won't stack-overflow
            if (serviceByType.ContainsKey(param.FullyQualifiedTypeName))
            {
                deps.Add(param.FullyQualifiedTypeName);
            }
        }
        // Register under each service type this class provides
        foreach (var st in GetServiceTypes(svc))
        {
            adjacency[st] = deps;
        }
    }

    // Add decorator edges: decorator depends on its non-self ctor params
    foreach (var kvp in decoratorsByInterface)
    {
        var interfaceFqn = kvp.Key;
        foreach (var dec in kvp.Value)
        {
            var deps = new List<string>();
            foreach (var param in dec.ConstructorParameters)
            {
                if (param.IsOptional) continue;
                // Skip self-reference (decorator's own interface param — this is intentional)
                if (param.FullyQualifiedTypeName == dec.DecoratedInterfaceFqn) continue;
                if (serviceByType.ContainsKey(param.FullyQualifiedTypeName))
                {
                    deps.Add(param.FullyQualifiedTypeName);
                }
            }
            // Decorator's deps are additional edges from the interface node
            if (adjacency.TryGetValue(interfaceFqn, out var existing))
            {
                existing.AddRange(deps);
            }
        }
    }

    // DFS cycle detection with coloring
    // 0 = white (unvisited), 1 = gray (in progress), 2 = black (done)
    var color = new Dictionary<string, int>();
    var parent = new Dictionary<string, string?>();
    foreach (var key in adjacency.Keys)
    {
        color[key] = 0;
        parent[key] = null;
    }

    var reportedCycles = new HashSet<string>(); // avoid duplicate reports

    foreach (var node in adjacency.Keys)
    {
        if (color.TryGetValue(node, out var c) && c == 0)
        {
            DfsCycleDetect(node, adjacency, color, parent, spc, reportedCycles);
        }
    }
}

private static void DfsCycleDetect(
    string node,
    Dictionary<string, List<string>> adjacency,
    Dictionary<string, int> color,
    Dictionary<string, string?> parent,
    SourceProductionContext spc,
    HashSet<string> reportedCycles)
{
    color[node] = 1; // gray

    if (adjacency.TryGetValue(node, out var deps))
    {
        foreach (var dep in deps)
        {
            if (!color.ContainsKey(dep))
            {
                color[dep] = 0; // ensure entry exists
            }

            if (color[dep] == 0)
            {
                parent[dep] = node;
                DfsCycleDetect(dep, adjacency, color, parent, spc, reportedCycles);
            }
            else if (color[dep] == 1)
            {
                // Cycle found — reconstruct path
                var cycle = new List<string> { dep };
                var current = node;
                while (current != null && current != dep)
                {
                    cycle.Add(current);
                    parent.TryGetValue(current, out current);
                }
                cycle.Add(dep);
                cycle.Reverse();
                var cyclePath = string.Join(" \u2192 ", cycle);

                if (reportedCycles.Add(cyclePath))
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        DiagnosticDescriptors.CircularDependency,
                        Location.None,
                        cyclePath));
                }
            }
        }
    }

    color[node] = 2; // black
}
```

**Step 2: Call `DetectCircularDependencies` from `RegisterSourceOutput`**

In the `RegisterSourceOutput` lambda, after line 207 (after `decoratorsByInterface` is built) and before line 209 (`if (allServices.Count == 0)`), add:

```csharp
DetectCircularDependencies(spc, allServices, decoratorsByInterface);
```

**Step 3: Run tests**

Run: `dotnet test tests/ZeroInject.Tests --no-build`
Expected: PASS — both ZI014 tests pass.

**Step 4: Write additional cycle detection tests**

Add to `DiagnosticTests.cs`:

```csharp
[Fact]
public void ZI014_ThreeNodeCycle_ReportsError()
{
    var source = """
        using ZeroInject;
        public interface IA { }
        public interface IB { }
        public interface IC { }
        [Transient] public class A : IA { public A(IB b) { } }
        [Transient] public class B : IB { public B(IC c) { } }
        [Transient] public class C : IC { public C(IA a) { } }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
}

[Fact]
public void ZI014_OptionalDependencyBreaksCycle_NoDiagnostic()
{
    var source = """
        using ZeroInject;
        public interface IA { }
        public interface IB { }
        [Transient]
        public class A : IA { public A(IB b = null) { } }
        [Transient]
        public class B : IB { public B(IA a) { } }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
}

[Fact]
public void ZI014_DecoratorSelfReference_NotFlagged()
{
    var source = """
        using ZeroInject;
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo { }
        [Decorator]
        public class LoggingFoo : IFoo
        {
            public LoggingFoo(IFoo inner) { }
        }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, static d => string.Equals(d.Id, "ZI014", StringComparison.Ordinal));
}
```

**Step 5: Run all tests**

Run: `dotnet test tests/ZeroInject.Tests --no-build`
Expected: PASS

**Step 6: Commit**

```bash
git add src/ZeroInject.Generator/ tests/ZeroInject.Tests/GeneratorTests/
git commit -m "feat: compile-time circular dependency detection (ZI014)"
```

---

### Task 8: Update Documentation and Features

**Files:**
- Modify: `docs/features.md`

**Step 1: Update features.md**

Move implemented features to the "Implemented" table:
- `IServiceProviderIsService` — add row with both Hybrid ✅ and Standalone ✅
- Multi-decorator stacking — add row
- Circular dependency detection (ZI014) — add row
- Thread safety — remove from Vital, note in table

Add ZI014 to the diagnostics table.

Update the "Not Yet Implemented" section to remove completed items.

**Step 2: Commit**

```bash
git add docs/features.md
git commit -m "docs: update features.md with newly implemented features"
```
