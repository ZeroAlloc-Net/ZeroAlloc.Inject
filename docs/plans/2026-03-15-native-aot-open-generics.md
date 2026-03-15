# Native AOT: Compile-Time Closed Generic Enumeration Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace the standalone container's runtime `MakeGenericType`/`Delegate.CreateDelegate` open generic resolution with compile-time enumerated closed generic factories, making the standalone container fully Native AOT compatible.

**Architecture:** A new incremental pipeline step (`FindClosedGenericUsages`) uses the `CompilationProvider` plus all collected service registrations to run a fixed-point analysis. It discovers every closed generic type used transitively across constructor parameters, produces `ClosedGenericFactoryInfo` records, and passes them to code generation. The code generator replaces all `MethodInfo`/`ConcurrentDictionary`/`MakeGenericMethod` output with explicit `typeof(T) == ...` entries.

**Tech Stack:** C# 12, Roslyn incremental generators (`IIncrementalGenerator`, `CompilationProvider`, `INamedTypeSymbol.Construct()`), xUnit, `Microsoft.CodeAnalysis.CSharp`

**Design doc:** `docs/plans/2026-03-15-native-aot-open-generics-design.md`

---

### Task 1: Extend data models with open generic metadata

**Files:**
- Modify: `src/ZInject.Generator/ConstructorParameterInfo.cs`
- Modify: `src/ZInject.Generator/ServiceRegistrationInfo.cs`
- Create: `src/ZInject.Generator/ClosedGenericFactoryInfo.cs`

No test changes yet — this is pure data model infrastructure.

**Step 1: Add two fields to `ConstructorParameterInfo`**

The existing class is in `src/ZInject.Generator/ConstructorParameterInfo.cs`. Add:

```csharp
public string? UnboundGenericInterfaceFqn { get; }
public ImmutableArray<string> TypeArgumentMetadataNames { get; }
```

Update the constructor to accept them (add after `isOptional`):
```csharp
public ConstructorParameterInfo(
    string fullyQualifiedTypeName,
    string parameterName,
    bool isOptional,
    string? unboundGenericInterfaceFqn = null,
    ImmutableArray<string> typeArgumentMetadataNames = default)
{
    FullyQualifiedTypeName = fullyQualifiedTypeName;
    ParameterName = parameterName;
    IsOptional = isOptional;
    UnboundGenericInterfaceFqn = unboundGenericInterfaceFqn;
    TypeArgumentMetadataNames = typeArgumentMetadataNames.IsDefault
        ? ImmutableArray<string>.Empty
        : typeArgumentMetadataNames;
}
```

Update `Equals` to include both new fields:
```csharp
&& UnboundGenericInterfaceFqn == other.UnboundGenericInterfaceFqn
&& TypeArgumentMetadataNames.SequenceEqual(other.TypeArgumentMetadataNames)
```

Update `GetHashCode` to include `UnboundGenericInterfaceFqn`:
```csharp
hash = hash * 31 + (UnboundGenericInterfaceFqn?.GetHashCode() ?? 0);
```

**Step 2: Add `ImplementationMetadataName` to `ServiceRegistrationInfo`**

Add:
```csharp
public string? ImplementationMetadataName { get; }
```

Update the constructor (add as the last parameter with default `null`):
```csharp
public ServiceRegistrationInfo(
    // ... all existing params ...,
    string? implementationMetadataName = null)
{
    // ... existing assignments ...
    ImplementationMetadataName = implementationMetadataName;
}
```

Update `Equals` to include it:
```csharp
|| ImplementationMetadataName != other.ImplementationMetadataName
```

**Step 3: Populate new fields in `ZInjectGenerator.GetServiceInfo`**

In `ZInjectGenerator.cs`, find the constructor parameter processing loop (around line 497). After the existing line:
```csharp
var paramTypeFqn = param.Type.ToDisplayString(FullyQualifiedFormat);
```

Add closed generic detection:
```csharp
string? unboundFqn = null;
ImmutableArray<string> typeArgMetadataNames = ImmutableArray<string>.Empty;
if (param.Type is INamedTypeSymbol namedParam
    && namedParam.IsGenericType
    && !namedParam.IsUnboundGenericType)
{
    unboundFqn = namedParam.ConstructedFrom.ToDisplayString(FullyQualifiedFormat);
    var builder = ImmutableArray.CreateBuilder<string>(namedParam.TypeArguments.Length);
    foreach (var typeArg in namedParam.TypeArguments)
    {
        var ns = typeArg.ContainingNamespace?.IsGlobalNamespace == false
            ? typeArg.ContainingNamespace.ToDisplayString()
            : null;
        builder.Add(ns != null
            ? ns + "." + typeArg.MetadataName
            : typeArg.MetadataName);
    }
    typeArgMetadataNames = builder.ToImmutable();
}
```

Update the `new ConstructorParameterInfo(...)` call to pass the new fields:
```csharp
constructorParameters.Add(new ConstructorParameterInfo(
    paramTypeFqn,
    param.Name,
    isOptional,
    unboundFqn,
    typeArgMetadataNames));
```

For `ImplementationMetadataName`, find where `ServiceRegistrationInfo` is constructed in `GetServiceInfo` (near the end of the method, around line 550). When `isOpenGeneric` is true, compute:
```csharp
string? implementationMetadataName = null;
if (isOpenGeneric)
{
    var ns = typeSymbol.ContainingNamespace?.IsGlobalNamespace == false
        ? typeSymbol.ContainingNamespace.ToDisplayString()
        : null;
    implementationMetadataName = ns != null
        ? ns + "." + typeSymbol.MetadataName
        : typeSymbol.MetadataName;
}
```

Pass it to the `ServiceRegistrationInfo` constructor.

**Step 4: Create `ClosedGenericFactoryInfo.cs`**

```csharp
using System.Collections.Immutable;

namespace ZInject.Generator;

internal sealed class ClosedGenericFactoryInfo : IEquatable<ClosedGenericFactoryInfo>
{
    public string InterfaceFqn { get; }
    public string ImplementationFqn { get; }
    public string Lifetime { get; }
    public ImmutableArray<ConstructorParameterInfo> Parameters { get; }

    public ClosedGenericFactoryInfo(
        string interfaceFqn,
        string implementationFqn,
        string lifetime,
        ImmutableArray<ConstructorParameterInfo> parameters)
    {
        InterfaceFqn = interfaceFqn;
        ImplementationFqn = implementationFqn;
        Lifetime = lifetime;
        Parameters = parameters;
    }

    public bool Equals(ClosedGenericFactoryInfo? other)
    {
        if (other is null) return false;
        return string.Equals(InterfaceFqn, other.InterfaceFqn, StringComparison.Ordinal)
            && string.Equals(ImplementationFqn, other.ImplementationFqn, StringComparison.Ordinal)
            && string.Equals(Lifetime, other.Lifetime, StringComparison.Ordinal)
            && Parameters.SequenceEqual(other.Parameters);
    }

    public override bool Equals(object? obj) => Equals(obj as ClosedGenericFactoryInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + InterfaceFqn.GetHashCode();
            hash = hash * 31 + ImplementationFqn.GetHashCode();
            hash = hash * 31 + Lifetime.GetHashCode();
            return hash;
        }
    }
}
```

**Step 5: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All 259 pass (no behaviour changed yet).

**Step 6: Commit**

```bash
git add src/ZInject.Generator/ConstructorParameterInfo.cs \
        src/ZInject.Generator/ServiceRegistrationInfo.cs \
        src/ZInject.Generator/ClosedGenericFactoryInfo.cs \
        src/ZInject.Generator/ZInjectGenerator.cs
git commit -m "feat: extend data models with open generic metadata for AOT analysis"
```

---

### Task 2: `FindClosedGenericUsages` — single-layer analysis

**Files:**
- Modify: `src/ZInject.Generator/ZInjectGenerator.cs`
- Test: `tests/ZInject.Tests/GeneratorTests/OpenGenericTests.cs`

**Step 1: Write a failing test asserting closed generic entries appear in standalone output**

Add to `tests/ZInject.Tests/GeneratorTests/OpenGenericTests.cs`:

```csharp
[Fact]
public void OpenGeneric_StandaloneContainer_EmitsExplicitClosedTypeEntry()
{
    var source = """
        using ZInject;
        namespace TestApp;
        public interface IRepository<T> { }
        public class OrderContext { }
        [Transient]
        public class Repository<T> : IRepository<T>
        {
            public Repository(OrderContext ctx) { }
        }
        [Transient]
        public class OrderService
        {
            public OrderService(IRepository<OrderContext> repo) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    // Explicit closed type entry — no MakeGenericType
    Assert.Contains("typeof(global::TestApp.IRepository<global::TestApp.OrderContext>)", output);
    Assert.DoesNotContain("MakeGenericType", output);
    Assert.DoesNotContain("GetMethod", output);
}
```

**Step 2: Run test — expect failure**

```
dotnet test tests/ZInject.Tests --filter "OpenGeneric_StandaloneContainer_EmitsExplicitClosedTypeEntry" -v n
```

Expected: FAIL — output still uses `MakeGenericType`.

**Step 3: Add `FindClosedGenericUsages` method to `ZInjectGenerator.cs`**

Add a new private static method:

```csharp
private static ImmutableArray<ClosedGenericFactoryInfo> FindClosedGenericUsages(
    (((ImmutableArray<ServiceRegistrationInfo?> transients,
       ImmutableArray<ServiceRegistrationInfo?> scopeds),
      ImmutableArray<ServiceRegistrationInfo?> singletons),
     Compilation compilation) data,
    CancellationToken ct)
{
    var transients   = data.Item1.Item1.Item1;
    var scopeds      = data.Item1.Item1.Item2;
    var singletons   = data.Item1.Item2;
    var compilation  = data.Item2;

    // Build lookup: unbound interface FQN → open generic ServiceRegistrationInfo
    var openGenericMap = new Dictionary<string, ServiceRegistrationInfo>(StringComparer.Ordinal);
    foreach (var svc in transients.Concat(scopeds).Concat(singletons))
    {
        if (svc == null || !svc.IsOpenGeneric || svc.ImplementationMetadataName == null) continue;
        var ifaces = svc.AsType != null ? new List<string> { svc.AsType } : svc.Interfaces;
        foreach (var iface in ifaces)
            openGenericMap.TryAdd(iface, svc);
    }

    if (openGenericMap.Count == 0) return ImmutableArray<ClosedGenericFactoryInfo>.Empty;

    // Collect all constructor parameters from all services as starting work items
    var workQueue = new Queue<ConstructorParameterInfo>();
    var processed = new HashSet<string>(StringComparer.Ordinal); // keyed by InterfaceFqn
    var results = new List<ClosedGenericFactoryInfo>();

    foreach (var svc in transients.Concat(scopeds).Concat(singletons))
    {
        if (svc == null) continue;
        foreach (var param in svc.ConstructorParameters)
            if (param.UnboundGenericInterfaceFqn != null)
                workQueue.Enqueue(param);
    }

    while (workQueue.Count > 0)
    {
        ct.ThrowIfCancellationRequested();
        var param = workQueue.Dequeue();
        var closedFqn = param.FullyQualifiedTypeName;

        if (!processed.Add(closedFqn)) continue;
        if (param.UnboundGenericInterfaceFqn == null) continue;
        if (!openGenericMap.TryGetValue(param.UnboundGenericInterfaceFqn, out var og)) continue;

        // Resolve impl symbol and close it
        var implSymbol = compilation.GetTypeByMetadataName(og.ImplementationMetadataName!);
        if (implSymbol == null) continue;

        var typeArgSymbols = new ITypeSymbol[param.TypeArgumentMetadataNames.Length];
        bool allResolved = true;
        for (int i = 0; i < param.TypeArgumentMetadataNames.Length; i++)
        {
            var sym = compilation.GetTypeByMetadataName(param.TypeArgumentMetadataNames[i]);
            if (sym == null) { allResolved = false; break; }
            typeArgSymbols[i] = sym;
        }
        if (!allResolved) continue;

        var closedImpl = implSymbol.Construct(typeArgSymbols);
        var closedImplFqn = closedImpl.ToDisplayString(FullyQualifiedFormat);

        // Build constructor parameters for the closed impl
        var ctor = closedImpl.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(static c => c.Parameters.Length)
            .FirstOrDefault();
        if (ctor == null) continue;

        var ctorParams = ImmutableArray.CreateBuilder<ConstructorParameterInfo>(ctor.Parameters.Length);
        foreach (var ctorParam in ctor.Parameters)
        {
            var ctorParamFqn = ctorParam.Type.ToDisplayString(FullyQualifiedFormat);
            string? unboundFqn = null;
            ImmutableArray<string> typeArgMeta = ImmutableArray<string>.Empty;
            if (ctorParam.Type is INamedTypeSymbol namedCtorParam
                && namedCtorParam.IsGenericType && !namedCtorParam.IsUnboundGenericType)
            {
                unboundFqn = namedCtorParam.ConstructedFrom.ToDisplayString(FullyQualifiedFormat);
                var metaBuilder = ImmutableArray.CreateBuilder<string>(namedCtorParam.TypeArguments.Length);
                foreach (var ta in namedCtorParam.TypeArguments)
                {
                    var ns = ta.ContainingNamespace?.IsGlobalNamespace == false
                        ? ta.ContainingNamespace.ToDisplayString() : null;
                    metaBuilder.Add(ns != null ? ns + "." + ta.MetadataName : ta.MetadataName);
                }
                typeArgMeta = metaBuilder.ToImmutable();
                // Add to work queue for fixed-point
                workQueue.Enqueue(new ConstructorParameterInfo(ctorParamFqn, ctorParam.Name, false, unboundFqn, typeArgMeta));
            }
            ctorParams.Add(new ConstructorParameterInfo(ctorParamFqn, ctorParam.Name, false, unboundFqn, typeArgMeta));
        }

        results.Add(new ClosedGenericFactoryInfo(
            closedFqn,
            closedImplFqn,
            og.Lifetime,
            ctorParams.ToImmutable()));
    }

    return results.ToImmutableArray();
}
```

**Step 4: Wire the new pipeline into `Initialize`**

In `ZInjectGenerator.Initialize`, add after the `singletons` pipeline (before the `assemblyAttr` pipeline):

```csharp
var closedGenericUsages = transients
    .Combine(scopeds)
    .Combine(singletons)
    .Combine(context.CompilationProvider)
    .Select(static (data, ct) => FindClosedGenericUsages(data, ct));
```

Then add `closedGenericUsages` to the `combined` chain:

```csharp
var combined = transients
    .Combine(scopeds)
    .Combine(singletons)
    .Combine(assemblyAttr)
    .Combine(assemblyName)
    .Combine(hasContainer)
    .Combine(allDecorators)
    .Combine(closedGenericUsages);   // NEW
```

Update `RegisterSourceOutput` to unpack the new data:
```csharp
var closedGenericFactories = data.Right;  // ImmutableArray<ClosedGenericFactoryInfo>
// existing unpacking stays: data.Left.Left... etc (just add .Left to existing chain)
```

**Step 5: Run the failing test — expect pass**

```
dotnet test tests/ZInject.Tests --filter "OpenGeneric_StandaloneContainer_EmitsExplicitClosedTypeEntry" -v n
```

Expected: PASS (once code gen is updated in Task 3).

> **Note:** The test will still fail here because we haven't updated code gen yet. Mark this as expected — Task 3 wires up code gen.

**Step 6: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All 259 pass (no code gen changed yet, new pipeline data is collected but unused).

**Step 7: Commit**

```bash
git add src/ZInject.Generator/ZInjectGenerator.cs
git commit -m "feat: add FindClosedGenericUsages analysis pass for AOT open generic discovery"
```

---

### Task 3: Emit explicit closed type entries in code generation

**Files:**
- Modify: `src/ZInject.Generator/ZInjectGenerator.cs`
- Test: `tests/ZInject.Tests/GeneratorTests/OpenGenericTests.cs`

**Step 1: Run the Task 2 test — confirm it still fails**

```
dotnet test tests/ZInject.Tests --filter "OpenGeneric_StandaloneContainer_EmitsExplicitClosedTypeEntry" -v n
```

Expected: FAIL.

**Step 2: Update `GenerateStandaloneServiceProviderClass` to use `closedGenericFactories`**

Find the open generic code generation section (around line 1650):
```csharp
if (openGenerics.Count > 0)
{
    // ... emits _og_mi_N, _og_dc_N, OG_Factory_N ...
}
```

Replace it entirely with code that emits explicit entries from `closedGenericFactories`. The method signature needs a new parameter:

```csharp
private static string GenerateStandaloneServiceProviderClass(
    // existing params ...
    ImmutableArray<ClosedGenericFactoryInfo> closedGenericFactories)  // NEW
```

In `ResolveKnown`, where `EmitOpenGenericRootResolve` is called, instead emit:

```csharp
foreach (var cgf in closedGenericFactories)
{
    if (string.Equals(cgf.Lifetime, "Scoped", StringComparison.Ordinal))
        continue; // scoped not resolved from root

    sb.AppendLine("            if (serviceType == typeof(" + cgf.InterfaceFqn + "))");
    sb.AppendLine("            {");

    if (string.Equals(cgf.Lifetime, "Singleton", StringComparison.Ordinal))
    {
        // Singleton field: private TYPE? _s_closed_N; (unique per entry)
        var fieldName = "_s_cg_" + sanitizedIndex;
        sb.AppendLine("                return global::System.Threading.Interlocked.CompareExchange(");
        sb.AppendLine("                    ref " + fieldName + ", ");
        sb.AppendLine("                    " + fieldName + " ?? " + BuildNewExpr(cgf) + ", null)");
        sb.AppendLine("                    ?? " + fieldName + ";");
    }
    else // Transient
    {
        sb.AppendLine("                return " + BuildNewExpr(cgf) + ";");
    }

    sb.AppendLine("            }");
}
```

Where `BuildNewExpr(cgf)` generates:
```csharp
private static string BuildNewExpr(ClosedGenericFactoryInfo cgf)
{
    var sb = new StringBuilder();
    sb.Append("new ").Append(cgf.ImplementationFqn).Append("(");
    for (int i = 0; i < cgf.Parameters.Length; i++)
    {
        if (i > 0) sb.Append(", ");
        var p = cgf.Parameters[i];
        if (p.IsOptional)
            sb.Append("sp.GetService<").Append(p.FullyQualifiedTypeName).Append(">()");
        else
            sb.Append("sp.GetRequiredService<").Append(p.FullyQualifiedTypeName).Append(">()");
    }
    sb.Append(")");
    return sb.ToString();
}
```

Add singleton fields at the class level for each singleton closed generic (same as existing closed service singleton fields).

Also add singleton support per scope for scoped closed generics in the `ResolveScoped` method.

**Step 3: Run the Task 2 test — expect pass**

```
dotnet test tests/ZInject.Tests --filter "OpenGeneric_StandaloneContainer_EmitsExplicitClosedTypeEntry" -v n
```

Expected: PASS.

**Step 4: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All 259 pass (open generic integration tests still pass because they test runtime resolution behaviour, not output shape).

**Step 5: Commit**

```bash
git add src/ZInject.Generator/ZInjectGenerator.cs
git commit -m "feat: emit explicit closed generic entries in standalone container — replace MakeGenericType"
```

---

### Task 4: Remove old open generic machinery

**Files:**
- Modify: `src/ZInject.Generator/ZInjectGenerator.cs`
- Test: `tests/ZInject.Tests/GeneratorTests/OpenGenericTests.cs`

**Step 1: Write a test asserting no reflection in standalone output**

Add to `tests/ZInject.Tests/GeneratorTests/OpenGenericTests.cs`:

```csharp
[Fact]
public void OpenGeneric_StandaloneContainer_ContainsNoReflection()
{
    var source = """
        using ZInject;
        namespace TestApp;
        public interface IRepository<T> { }
        public class Order { }
        [Transient]
        public class Repository<T> : IRepository<T> { }
        [Transient]
        public class OrderService
        {
            public OrderService(IRepository<Order> repo) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    Assert.DoesNotContain("MakeGenericType", output);
    Assert.DoesNotContain("MakeGenericMethod", output);
    Assert.DoesNotContain("GetMethod(", output);
    Assert.DoesNotContain("Delegate.CreateDelegate", output);
    Assert.DoesNotContain("ConcurrentDictionary", output);
    Assert.DoesNotContain("GetGenericTypeDefinition", output);
}
```

**Step 2: Run test — expect failure**

```
dotnet test tests/ZInject.Tests --filter "OpenGeneric_StandaloneContainer_ContainsNoReflection" -v n
```

Expected: FAIL — old machinery still in output.

**Step 3: Remove the old open generic field/method emission from `GenerateStandaloneServiceProviderClass`**

Remove the block (around line 1650):
```csharp
if (openGenerics.Count > 0)
{
    sb.AppendLine("        // Open generic factory infra ...");
    // ... _og_mi_N, _og_dc_N, OG_Factory_N emission ...
}
```

Remove the call to `EmitOpenGenericRootResolve` (now replaced by the explicit entries from Task 3).

Remove the private methods: `EmitOpenGenericRootResolve`, `EmitOpenGenericFactoryMethod`, `BuildOpenGenericNewExpr`, `BuildOpenGenericTypeParams` — confirm they are no longer called anywhere before deleting.

Remove the `OgDelegateCreator` constant.

Remove the `ToUnboundGenericString` helper if it's only used by the removed methods — search for other callers first.

**Step 4: Run the no-reflection test — expect pass**

```
dotnet test tests/ZInject.Tests --filter "OpenGeneric_StandaloneContainer_ContainsNoReflection" -v n
```

Expected: PASS.

**Step 5: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Some existing open generic tests may fail because they assert on the old output shape (e.g., `Assert.Contains("GetGenericTypeDefinition")`). Fix those assertions to match the new explicit-entry output. Do NOT change behaviour — only fix string assertions to reflect the new generated code shape.

**Step 6: Commit**

```bash
git add src/ZInject.Generator/ZInjectGenerator.cs \
        tests/ZInject.Tests/GeneratorTests/OpenGenericTests.cs
git commit -m "feat: remove MakeGenericType open generic machinery — standalone container is now reflection-free"
```

---

### Task 5: Chained open generic test (fixed-point verification)

**Files:**
- Test: `tests/ZInject.Tests/GeneratorTests/OpenGenericTests.cs`
- Test: `tests/ZInject.Tests/ContainerTests/IntegrationTests.cs`

**Step 1: Write a chained open generic generator test**

```csharp
[Fact]
public void OpenGeneric_ChainedDependency_BothClosedTypesEmitted()
{
    var source = """
        using ZInject;
        namespace TestApp;
        public interface IRepository<T> { }
        public interface IContext<T> { }
        public class Order { }
        [Transient]
        public class Repository<T> : IRepository<T>
        {
            public Repository(IContext<T> ctx) { }
        }
        [Transient]
        public class Context<T> : IContext<T> { }
        [Transient]
        public class OrderService
        {
            public OrderService(IRepository<Order> repo) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    // Both closed types discovered transitively
    Assert.Contains("typeof(global::TestApp.IRepository<global::TestApp.Order>)", output);
    Assert.Contains("typeof(global::TestApp.IContext<global::TestApp.Order>)", output);
    Assert.DoesNotContain("MakeGenericType", output);
}
```

**Step 2: Run test — expect pass (fixed-point is already in Task 2's implementation)**

```
dotnet test tests/ZInject.Tests --filter "OpenGeneric_ChainedDependency_BothClosedTypesEmitted" -v n
```

Expected: PASS — the work queue in `FindClosedGenericUsages` already handles this.

If it fails, the fixed-point logic has a bug — debug `FindClosedGenericUsages` to ensure `Repository<Order>`'s constructor params (`IContext<Order>`) are enqueued and processed.

**Step 3: Write a chained integration test**

Add to `tests/ZInject.Tests/ContainerTests/IntegrationTests.cs`:

```csharp
[Fact]
public void OpenGeneric_ChainedDependency_Standalone_ResolvesCorrectly()
{
    const string source = """
        using ZInject;
        namespace TestApp;
        public interface IRepository<T> { string Name { get; } }
        public interface IContext<T> { string Tag { get; } }
        [Transient]
        public class Repository<T> : IRepository<T>
        {
            private readonly IContext<T> _ctx;
            public Repository(IContext<T> ctx) { _ctx = ctx; }
            public string Name => "repo:" + _ctx.Tag;
        }
        [Transient]
        public class Context<T> : IContext<T>
        {
            public string Tag => typeof(T).Name;
        }
        [Transient]
        public class OrderService
        {
            public IRepository<Order> Repo { get; }
            public OrderService(IRepository<Order> repo) { Repo = repo; }
        }
        public class Order { }
        """;

    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var svcType = assembly.GetType("TestApp.OrderService")!;
    var svc = provider.GetService(svcType)!;
    var repoProp = svcType.GetProperty("Repo")!;
    var repo = repoProp.GetValue(svc)!;
    var nameProp = repo.GetType().GetProperty("Name")!;
    Assert.Equal("repo:Order", (string)nameProp.GetValue(repo)!);
}
```

**Step 4: Run integration test**

```
dotnet test tests/ZInject.Tests --filter "OpenGeneric_ChainedDependency_Standalone" -v n
```

Expected: PASS.

**Step 5: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All pass.

**Step 6: Commit**

```bash
git add tests/ZInject.Tests/GeneratorTests/OpenGenericTests.cs \
        tests/ZInject.Tests/ContainerTests/IntegrationTests.cs
git commit -m "test: add chained open generic tests verifying fixed-point AOT discovery"
```

---

### Task 6: ZI018 — warn when open generic has no detected usages

**Files:**
- Modify: `src/ZInject.Generator/DiagnosticDescriptors.cs`
- Modify: `src/ZInject.Generator/ZInjectGenerator.cs`
- Test: `tests/ZInject.Tests/GeneratorTests/DiagnosticTests.cs`

**Step 1: Write a failing ZI018 test**

Add to `tests/ZInject.Tests/GeneratorTests/DiagnosticTests.cs`:

```csharp
[Fact]
public void ZI018_OpenGenericNoDetectedUsages_ReportsWarning()
{
    var source = """
        using ZInject;
        public interface IRepository<T> { }
        [Transient]
        public class Repository<T> : IRepository<T> { }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, static d => d.Id == "ZI018");
    Assert.All(diagnostics.Where(static d => d.Id == "ZI018"),
        static d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
}
```

**Step 2: Run test — expect failure**

```
dotnet test tests/ZInject.Tests --filter "ZI018" -v n
```

Expected: FAIL — ZI018 not yet implemented.

**Step 3: Add ZI018 to `DiagnosticDescriptors.cs`**

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

**Step 4: Emit ZI018 in `RegisterSourceOutput`**

After unpacking `closedGenericFactories`, check each open generic:

```csharp
var closedFqnSet = new HashSet<string>(
    closedGenericFactories.Select(static cgf => cgf.InterfaceFqn),
    StringComparer.Ordinal);

foreach (var svc in openGenerics)
{
    var ifaces = svc.AsType != null ? new List<string> { svc.AsType } : svc.Interfaces;
    bool anyUsage = ifaces.Any(iface => closedFqnSet.Any(fqn => fqn.StartsWith(
        iface.TrimEnd('>', ' ').Split('<')[0], StringComparison.Ordinal)));
    if (!anyUsage)
    {
        spc.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.NoDetectedClosedUsages,
            Location.None,
            svc.TypeName));
    }
}
```

> **Implementation note:** The "any usage" check should match the open generic registration's interface against the discovered closed types. The simplest reliable approach: check if any `ClosedGenericFactoryInfo.InterfaceFqn` has the same unbound form as the open generic's registered interface. You can use `openGenericMap` built during `FindClosedGenericUsages`, or rebuild it here. Adjust the check as needed after reading the actual FQN formats.

**Step 5: Run ZI018 test — expect pass**

```
dotnet test tests/ZInject.Tests --filter "ZI018" -v n
```

Expected: PASS.

**Step 6: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All pass.

**Step 7: Commit**

```bash
git add src/ZInject.Generator/DiagnosticDescriptors.cs \
        src/ZInject.Generator/ZInjectGenerator.cs \
        tests/ZInject.Tests/GeneratorTests/DiagnosticTests.cs
git commit -m "feat: add ZI018 warning when open generic has no detected closed usages"
```

---

### Task 7: Update docs and README

**Files:**
- Modify: `docs/features.md`
- Modify: `README.md`

**Step 1: Update `docs/features.md`**

Change the open generic rows from `✅¹` to `✅` and remove the footnote about hybrid mode delegating to MS DI fallback:

```markdown
| Open generics (`IRepo<T>` → `Repo<T>`) | ✅ | ✅ | Standalone enumerates closed types at compile time |
| Open generic + decorator | ✅ | ✅ | Decorator wraps closed instances directly |
```

Remove or update the `¹` footnote line.

Add ZI018 to the diagnostics table:
```markdown
| ZI018 | Warning | No closed usages of open generic detected — won't resolve from standalone/hybrid container |
```

**Step 2: Update `README.md` Native AOT table**

Change the open generic row:
```markdown
| Standalone container (open generics) | ✅ Compile-time enumerated. Closed types discovered via constructor parameter analysis. Fully AOT-safe. |
```

Update the intro tagline if needed to remove the parenthetical caveat about open generics.

**Step 3: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All pass.

**Step 4: Commit**

```bash
git add docs/features.md README.md
git commit -m "docs: update Native AOT section and features.md — open generics now fully AOT-safe"
```

---

### Task 8: Create PR

```bash
git push -u origin feat/native-aot-open-generics
gh pr create \
  --base main \
  --title "feat: Native AOT — compile-time closed generic enumeration" \
  --body "Replaces runtime MakeGenericType/Delegate.CreateDelegate in the standalone container with compile-time enumerated closed generic factories. The standalone container is now fully reflection-free and Native AOT compatible..."
```
