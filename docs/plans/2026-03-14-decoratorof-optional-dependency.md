# DecoratorOf and OptionalDependency Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add `[DecoratorOf(typeof(IFoo), Order = N, WhenRegistered = typeof(T))]` and `[OptionalDependency]` attributes to ZInject, with full generator support across all three outputs (ServiceCollectionExtensions, hybrid container, standalone container).

**Architecture:** Two new attributes in `src/ZInject`. `[OptionalDependency]` extends existing `IsOptional` detection in `ConstructorParameterInfo` (already wired to `GetService<T>()` everywhere). `[DecoratorOf]` adds a second generator pipeline merged with the existing `[Decorator]` pipeline before the big `Combine` chain; `DecoratorRegistrationInfo` gains `Order` and `WhenRegisteredFqn` fields; the `WhenRegistered` check is emitted as a runtime `if (services.Any(...))` guard in `AddXxxServices()`.

**Tech Stack:** C# 12, Roslyn incremental generators (`IIncrementalGenerator`), xUnit, `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`

---

### Task 1: Add `OptionalDependencyAttribute`

**Files:**
- Create: `src/ZInject/OptionalDependencyAttribute.cs`
- Modify: `src/ZInject.Generator/DiagnosticDescriptors.cs`
- Modify: `src/ZInject.Generator/ZInjectGenerator.cs` (two methods: `GetServiceInfo`, `GetDecoratorInfo`)
- Test: `tests/ZInject.Tests/GeneratorTests/DiagnosticTests.cs`

**Step 1: Write failing test for [OptionalDependency] generating GetService**

Open `tests/ZInject.Tests/GeneratorTests/DiagnosticTests.cs` and add:

```csharp
[Fact]
public void OptionalDependency_GeneratesGetService_InsteadOfGetRequiredService()
{
    var source = """
        using ZInject;
        public interface IFoo { }
        public interface ILogger { }
        [Transient]
        public class FooImpl : IFoo
        {
            public FooImpl([OptionalDependency] ILogger? logger) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    Assert.Contains("GetService<global::ILogger>", output);
    Assert.DoesNotContain("GetRequiredService<global::ILogger>", output);
}
```

**Step 2: Run test — expect failure**

```
dotnet test tests/ZInject.Tests --filter "OptionalDependency_GeneratesGetService_InsteadOfGetRequiredService" -v n
```

Expected: FAIL — `OptionalDependencyAttribute` does not exist yet.

**Step 3: Create the attribute**

Create `src/ZInject/OptionalDependencyAttribute.cs`:

```csharp
namespace ZInject;

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class OptionalDependencyAttribute : Attribute { }
```

**Step 4: Wire [OptionalDependency] detection in GetServiceInfo**

In `src/ZInject.Generator/ZInjectGenerator.cs`, find `GetServiceInfo` (~line 439). The constructor parameter loop currently reads:

```csharp
bool isOptional = param.HasExplicitDefaultValue;
```

Change to also check for the attribute:

```csharp
bool isOptional = param.HasExplicitDefaultValue
    || param.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "ZInject.OptionalDependencyAttribute");
```

**Step 5: Wire [OptionalDependency] detection in GetDecoratorInfo**

In `GetDecoratorInfo` (~line 2632), find:

```csharp
ctorParams.Add(new ConstructorParameterInfo(matchFqn, param.Name, param.HasExplicitDefaultValue));
```

Change to:

```csharp
bool isOptional = param.HasExplicitDefaultValue
    || param.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "ZInject.OptionalDependencyAttribute");
ctorParams.Add(new ConstructorParameterInfo(matchFqn, param.Name, isOptional));
```

**Step 6: Run test — expect pass**

```
dotnet test tests/ZInject.Tests --filter "OptionalDependency_GeneratesGetService_InsteadOfGetRequiredService" -v n
```

Expected: PASS

**Step 7: Write failing test for ZI015 (non-nullable optional)**

Add to `DiagnosticTests.cs`:

```csharp
[Fact]
public void OptionalDependency_OnNonNullableParameter_ReportsZI015()
{
    var source = """
        using ZInject;
        public interface ILogger { }
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo
        {
            public FooImpl([OptionalDependency] ILogger logger) { }
        }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, d => d.Id == "ZI015");
}
```

**Step 8: Run test — expect failure**

```
dotnet test tests/ZInject.Tests --filter "OptionalDependency_OnNonNullableParameter_ReportsZI015" -v n
```

Expected: FAIL — ZI015 not emitted yet.

**Step 9: Add ZI015 to DiagnosticDescriptors**

Add to `src/ZInject.Generator/DiagnosticDescriptors.cs`:

```csharp
public static readonly DiagnosticDescriptor OptionalDependencyOnNonNullable = new DiagnosticDescriptor(
    "ZI015",
    "[OptionalDependency] on non-nullable parameter",
    "Parameter '{0}' of class '{1}' is marked [OptionalDependency] but its type '{2}' is not nullable; change the parameter type to '{2}?'",
    "ZInject",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Step 10: Report ZI015 in GetServiceInfo**

In `GetServiceInfo`, inside the constructor parameter loop, after checking `[OptionalDependency]`, capture non-nullable violations. Add a new list to `ServiceRegistrationInfo` or capture as a tuple. The simplest approach: add a `string? OptionalNonNullableParamName` and `string? OptionalNonNullableParamType` to `ServiceRegistrationInfo` (same pattern as `PrimitiveParameterName`).

In `ServiceRegistrationInfo.cs`, add:

```csharp
public string? OptionalNonNullableParamName { get; }
public string? OptionalNonNullableParamType { get; }
```

Update the constructor accordingly.

In `GetServiceInfo`, inside the ctor parameter loop, add:

```csharp
bool hasOptionalAttr = param.GetAttributes()
    .Any(a => a.AttributeClass?.ToDisplayString() == "ZInject.OptionalDependencyAttribute");
bool isNullable = param.Type.NullableAnnotation == NullableAnnotation.Annotated
    || param.Type.IsReferenceType == false; // value types can't be [OptionalDependency] but let error from other check

if (hasOptionalAttr && !param.Type.NullableAnnotation.HasFlag(NullableAnnotation.Annotated)
    && primitiveParameterName == null && optionalNonNullableParamName == null)
{
    optionalNonNullableParamName = param.Name;
    optionalNonNullableParamType = paramTypeFqn;
}
```

In `RegisterSourceOutput`, after the `PrimitiveParameterName` diagnostic check, add:

```csharp
if (svc.OptionalNonNullableParamName != null)
{
    spc.ReportDiagnostic(Diagnostic.Create(
        DiagnosticDescriptors.OptionalDependencyOnNonNullable,
        Location.None,
        svc.OptionalNonNullableParamName,
        svc.TypeName,
        svc.OptionalNonNullableParamType));
}
```

**Step 11: Run ZI015 test — expect pass**

```
dotnet test tests/ZInject.Tests --filter "OptionalDependency_OnNonNullableParameter_ReportsZI015" -v n
```

Expected: PASS

**Step 12: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All pass.

**Step 13: Commit**

```bash
git add src/ZInject/OptionalDependencyAttribute.cs \
        src/ZInject.Generator/DiagnosticDescriptors.cs \
        src/ZInject.Generator/ZInjectGenerator.cs \
        src/ZInject.Generator/ServiceRegistrationInfo.cs \
        tests/ZInject.Tests/GeneratorTests/DiagnosticTests.cs
git commit -m "feat: add [OptionalDependency] attribute with ZI015 diagnostic"
```

---

### Task 2: Add `DecoratorOfAttribute` and extend `DecoratorRegistrationInfo`

**Files:**
- Create: `src/ZInject/DecoratorOfAttribute.cs`
- Modify: `src/ZInject.Generator/DecoratorRegistrationInfo.cs`
- Modify: `src/ZInject.Generator/DiagnosticDescriptors.cs`

**Step 1: Create the attribute**

Create `src/ZInject/DecoratorOfAttribute.cs`:

```csharp
namespace ZInject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DecoratorOfAttribute : Attribute
{
    public DecoratorOfAttribute(Type decoratedInterface)
    {
        DecoratedInterface = decoratedInterface;
    }

    public Type DecoratedInterface { get; }
    public int Order { get; set; }
    public Type? WhenRegistered { get; set; }
}
```

**Step 2: Extend DecoratorRegistrationInfo**

In `src/ZInject.Generator/DecoratorRegistrationInfo.cs`, add two new fields:

```csharp
public int Order { get; }
public string? WhenRegisteredFqn { get; }  // null = unconditional
```

Update the constructor to accept and assign them. Update `Equals` and `GetHashCode` to include `Order` and `WhenRegisteredFqn`.

**Step 3: Add ZI016 and ZI017 to DiagnosticDescriptors**

```csharp
public static readonly DiagnosticDescriptor DecoratorOfInterfaceNotImplemented = new DiagnosticDescriptor(
    "ZI016",
    "[DecoratorOf] interface not implemented",
    "Class '{0}' is marked [DecoratorOf({1})] but does not implement that interface",
    "ZInject",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor DecoratorOfDuplicateOrder = new DiagnosticDescriptor(
    "ZI017",
    "Duplicate decorator Order",
    "Interface '{0}' has two decorators with the same Order={1}: '{2}' and '{3}'. Orders must be unique per interface.",
    "ZInject",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Step 4: Run all tests — expect pass (no changes to generator yet)**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All pass.

**Step 5: Commit**

```bash
git add src/ZInject/DecoratorOfAttribute.cs \
        src/ZInject.Generator/DecoratorRegistrationInfo.cs \
        src/ZInject.Generator/DiagnosticDescriptors.cs
git commit -m "feat: add DecoratorOfAttribute and extend DecoratorRegistrationInfo with Order/WhenRegisteredFqn"
```

---

### Task 3: Wire `[DecoratorOf]` pipeline in the generator

**Files:**
- Modify: `src/ZInject.Generator/ZInjectGenerator.cs`
- Test: `tests/ZInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs`

**Step 1: Write failing test for basic [DecoratorOf] wrapping**

Add to `DecoratorGeneratorTests.cs`:

```csharp
[Fact]
public void DecoratorOf_BasicWrapping_GeneratesCorrectly()
{
    var source = """
        using ZInject;
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo { }
        [DecoratorOf(typeof(IFoo))]
        public class LoggingFoo : IFoo
        {
            public LoggingFoo(IFoo inner) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    Assert.Contains("new global::LoggingFoo", output);
    Assert.Contains("GetRequiredService<global::FooImpl>", output);
}
```

**Step 2: Run test — expect failure**

```
dotnet test tests/ZInject.Tests --filter "DecoratorOf_BasicWrapping_GeneratesCorrectly" -v n
```

Expected: FAIL — `[DecoratorOf]` is not processed by the generator yet.

**Step 3: Add GetDecoratorOfInfo method**

In `ZInjectGenerator.cs`, add a new private static method after `GetDecoratorInfo`:

```csharp
private static DecoratorRegistrationInfo? GetDecoratorOfInfo(
    GeneratorAttributeSyntaxContext ctx,
    CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol) return null;

    var typeName = typeSymbol.Name;
    var fqn = typeSymbol.ToDisplayString(FullyQualifiedFormat);
    bool isAbstractOrStatic = typeSymbol.IsAbstract || typeSymbol.IsStatic;
    bool isOpenGeneric = typeSymbol.IsGenericType;
    int arity = typeSymbol.TypeParameters.Length;

    if (isOpenGeneric)
        fqn = ToUnboundGenericString(fqn, arity);

    // Read attribute: constructor arg = decoratedInterface, named args = Order, WhenRegistered
    var attr = ctx.Attributes.FirstOrDefault();
    if (attr == null) return null;

    string? decoratedInterfaceFqn = null;
    if (attr.ConstructorArguments.Length > 0
        && attr.ConstructorArguments[0].Value is INamedTypeSymbol decoratedSymbol)
    {
        decoratedInterfaceFqn = decoratedSymbol.ToDisplayString(FullyQualifiedFormat);
        if (isOpenGeneric && decoratedSymbol.IsGenericType)
            decoratedInterfaceFqn = ToUnboundGenericString(decoratedInterfaceFqn, arity);
    }

    int order = 0;
    string? whenRegisteredFqn = null;

    foreach (var named in attr.NamedArguments)
    {
        if (named.Key == "Order" && named.Value.Value is int orderVal)
            order = orderVal;
        else if (named.Key == "WhenRegistered" && named.Value.Value is INamedTypeSymbol whenSymbol)
            whenRegisteredFqn = whenSymbol.ToDisplayString(FullyQualifiedFormat);
    }

    // Collect all interfaces to validate ZI016
    var interfaces = new System.Collections.Generic.HashSet<string>();
    foreach (var iface in typeSymbol.AllInterfaces)
    {
        var ifaceFqn = iface.ToDisplayString(FullyQualifiedFormat);
        if (isOpenGeneric && iface.IsGenericType)
            ifaceFqn = ToUnboundGenericString(ifaceFqn, arity);
        interfaces.Add(ifaceFqn);
    }

    // Validate that the class implements the declared interface (ZI016 reported in RegisterSourceOutput)
    bool implementsDeclaredInterface = decoratedInterfaceFqn != null && interfaces.Contains(decoratedInterfaceFqn);
    if (!implementsDeclaredInterface)
        decoratedInterfaceFqn = null; // signal ZI016

    // Build constructor params
    IMethodSymbol? ctor = null;
    foreach (var c in typeSymbol.InstanceConstructors)
    {
        if (c.DeclaredAccessibility == Accessibility.Public) { ctor = c; break; }
    }

    var ctorParams = new List<ConstructorParameterInfo>();
    if (ctor != null && !isAbstractOrStatic)
    {
        foreach (var param in ctor.Parameters)
        {
            var paramTypeFqn = param.Type.ToDisplayString(FullyQualifiedFormat);
            var matchFqn = (isOpenGeneric && param.Type is INamedTypeSymbol pt && pt.IsGenericType)
                ? ToUnboundGenericString(paramTypeFqn, arity)
                : paramTypeFqn;
            bool isOptional = param.HasExplicitDefaultValue
                || param.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "ZInject.OptionalDependencyAttribute");
            ctorParams.Add(new ConstructorParameterInfo(matchFqn, param.Name, isOptional));
        }
    }

    bool implementsDisposable = false;
    foreach (var iface in typeSymbol.AllInterfaces)
    {
        var name = iface.ToDisplayString();
        if (name == "System.IDisposable" || name == "System.IAsyncDisposable")
        { implementsDisposable = true; break; }
    }

    return new DecoratorRegistrationInfo(
        typeName, fqn, decoratedInterfaceFqn, isOpenGeneric, ctorParams,
        implementsDisposable, isAbstractOrStatic, order, whenRegisteredFqn);
}
```

**Step 4: Add the [DecoratorOf] pipeline in Initialize**

In `Initialize`, after the existing `decorators` pipeline declaration (~line 93), add:

```csharp
var decoratorOfs = context.SyntaxProvider.ForAttributeWithMetadataName(
    "ZInject.DecoratorOfAttribute",
    predicate: static (node, _) => true,
    transform: static (ctx, ct) => GetDecoratorOfInfo(ctx, ct))
    .Where(static x => x != null)
    .Collect();
```

Then merge `decorators` and `decoratorOfs` into one collection before the big `Combine`:

```csharp
var allDecorators = decorators.Combine(decoratorOfs)
    .Select(static (pair, _) =>
    {
        var builder = ImmutableArray.CreateBuilder<DecoratorRegistrationInfo?>();
        builder.AddRange(pair.Left);
        builder.AddRange(pair.Right);
        return builder.ToImmutable();
    });
```

Replace `decorators` with `allDecorators` in the `Combine` chain:

```csharp
var combined = transients
    .Combine(scopeds)
    .Combine(singletons)
    .Combine(assemblyAttr)
    .Combine(assemblyName)
    .Combine(hasContainer)
    .Combine(allDecorators);
```

**Step 5: Run test — expect pass**

```
dotnet test tests/ZInject.Tests --filter "DecoratorOf_BasicWrapping_GeneratesCorrectly" -v n
```

Expected: PASS

**Step 6: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All pass.

**Step 7: Commit**

```bash
git add src/ZInject.Generator/ZInjectGenerator.cs
git commit -m "feat: add [DecoratorOf] pipeline to generator"
```

---

### Task 4: Implement `Order` sorting and ZI016/ZI017 diagnostics

**Files:**
- Modify: `src/ZInject.Generator/ZInjectGenerator.cs` (RegisterSourceOutput validation section)
- Test: `tests/ZInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs`
- Test: `tests/ZInject.Tests/GeneratorTests/DiagnosticTests.cs`

**Step 1: Write failing test for Order sorting**

Add to `DecoratorGeneratorTests.cs`:

```csharp
[Fact]
public void DecoratorOf_Order_InnerMostFirst()
{
    var source = """
        using ZInject;
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo { }
        [DecoratorOf(typeof(IFoo), Order = 2)]
        public class OuterFoo : IFoo
        {
            public OuterFoo(IFoo inner) { }
        }
        [DecoratorOf(typeof(IFoo), Order = 1)]
        public class InnerFoo : IFoo
        {
            public InnerFoo(IFoo inner) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    // InnerFoo (Order=1) should appear before OuterFoo (Order=2) in the chain
    var innerIdx = output.IndexOf("new global::InnerFoo");
    var outerIdx = output.IndexOf("new global::OuterFoo");
    Assert.True(innerIdx < outerIdx, "InnerFoo (Order=1) must be emitted before OuterFoo (Order=2)");
}
```

**Step 2: Run test — may pass or fail depending on collection order**

```
dotnet test tests/ZInject.Tests --filter "DecoratorOf_Order_InnerMostFirst" -v n
```

**Step 3: Write failing test for ZI016**

Add to `DiagnosticTests.cs`:

```csharp
[Fact]
public void DecoratorOf_InterfaceNotImplemented_ReportsZI016()
{
    var source = """
        using ZInject;
        public interface IFoo { }
        public interface IBar { }
        [Transient]
        public class FooImpl : IFoo { }
        [DecoratorOf(typeof(IBar))]
        public class BadDecorator : IFoo
        {
            public BadDecorator(IFoo inner) { }
        }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, d => d.Id == "ZI016");
}
```

**Step 4: Write failing test for ZI017**

```csharp
[Fact]
public void DecoratorOf_DuplicateOrder_ReportsZI017()
{
    var source = """
        using ZInject;
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo { }
        [DecoratorOf(typeof(IFoo), Order = 1)]
        public class DecoratorA : IFoo
        {
            public DecoratorA(IFoo inner) { }
        }
        [DecoratorOf(typeof(IFoo), Order = 1)]
        public class DecoratorB : IFoo
        {
            public DecoratorB(IFoo inner) { }
        }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, d => d.Id == "ZI017");
}
```

**Step 5: Run these three tests — expect failures**

```
dotnet test tests/ZInject.Tests --filter "DecoratorOf_Order_InnerMostFirst|ZI016|ZI017" -v n
```

**Step 6: Add Order sorting and ZI016/ZI017 in RegisterSourceOutput**

In the `validDecorators` loop (around line 176 in `RegisterSourceOutput`), ZI016 is already handled via `dec.DecoratedInterfaceFqn == null` (set to null by `GetDecoratorOfInfo` when interface not implemented). The existing ZI011 check covers this for `[Decorator]`, but for `[DecoratorOf]` we need ZI016. Add a flag to `DecoratorRegistrationInfo` indicating which attribute was used, OR distinguish by checking `dec.WhenRegisteredFqn` being a sentinel value.

Simpler: add a `bool IsDecoratorOf` field to `DecoratorRegistrationInfo`. Use it to choose ZI011 vs ZI016:

In `DecoratorRegistrationInfo`, add:
```csharp
public bool IsDecoratorOf { get; }
```

In `GetDecoratorOfInfo`, pass `isDecoratorOf: true`. In `GetDecoratorInfo`, pass `isDecoratorOf: false`.

In `RegisterSourceOutput`, change the `DecoratedInterfaceFqn == null` check:

```csharp
if (dec.DecoratedInterfaceFqn == null)
{
    var descriptor = dec.IsDecoratorOf
        ? DiagnosticDescriptors.DecoratorOfInterfaceNotImplemented
        : DiagnosticDescriptors.DecoratorNoMatchingInterface;
    spc.ReportDiagnostic(Diagnostic.Create(descriptor, Location.None, dec.TypeName, ""));
    continue;
}
```

After building `decoratorsByInterface`, sort each list and check for duplicates:

```csharp
foreach (var kvp in decoratorsByInterface)
{
    var list = kvp.Value;
    // Sort by Order ascending
    list.Sort((a, b) => a.Order.CompareTo(b.Order));

    // Check for duplicate Order values (ZI017)
    for (int i = 0; i < list.Count - 1; i++)
    {
        if (list[i].Order == list[i + 1].Order)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.DecoratorOfDuplicateOrder,
                Location.None,
                kvp.Key,
                list[i].Order.ToString(),
                list[i].TypeName,
                list[i + 1].TypeName));
        }
    }
}
```

**Step 7: Run the three tests — expect pass**

```
dotnet test tests/ZInject.Tests --filter "DecoratorOf_Order_InnerMostFirst|ZI016|ZI017" -v n
```

Expected: PASS

**Step 8: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All pass.

**Step 9: Commit**

```bash
git add src/ZInject.Generator/ZInjectGenerator.cs \
        src/ZInject.Generator/DecoratorRegistrationInfo.cs \
        tests/ZInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs \
        tests/ZInject.Tests/GeneratorTests/DiagnosticTests.cs
git commit -m "feat: add Order sorting and ZI016/ZI017 diagnostics for [DecoratorOf]"
```

---

### Task 5: Implement `WhenRegistered` conditional code generation

**Files:**
- Modify: `src/ZInject.Generator/ZInjectGenerator.cs` (GenerateExtensionClass, BuildDecoratorChain, standalone/hybrid container generators)
- Test: `tests/ZInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs`

**Step 1: Write failing test for WhenRegistered**

Add to `DecoratorGeneratorTests.cs`:

```csharp
[Fact]
public void DecoratorOf_WhenRegistered_EmitsConditionalCheck()
{
    var source = """
        using ZInject;
        public interface IFoo { }
        public class SomeOptions { }
        [Transient]
        public class FooImpl : IFoo { }
        [DecoratorOf(typeof(IFoo), WhenRegistered = typeof(SomeOptions))]
        public class ConditionalFoo : IFoo
        {
            public ConditionalFoo(IFoo inner) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    Assert.Contains("services.Any(d => d.ServiceType == typeof(global::SomeOptions))", output);
    Assert.Contains("new global::ConditionalFoo", output);
}

[Fact]
public void DecoratorOf_NoWhenRegistered_DoesNotEmitConditionalCheck()
{
    var source = """
        using ZInject;
        public interface IFoo { }
        [Transient]
        public class FooImpl : IFoo { }
        [DecoratorOf(typeof(IFoo))]
        public class UnconditionalFoo : IFoo
        {
            public UnconditionalFoo(IFoo inner) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    Assert.DoesNotContain("services.Any", output);
    Assert.Contains("new global::UnconditionalFoo", output);
}
```

**Step 2: Run tests — expect failure**

```
dotnet test tests/ZInject.Tests --filter "WhenRegistered" -v n
```

**Step 3: Update BuildDecoratorChain to accept WhenRegisteredFqn**

In `ZInjectGenerator.cs`, find the `BuildDecoratorChain` method (~line 632). It currently takes a list of `DecoratorRegistrationInfo`. The chain is emitted per-decorator in a loop.

Update the loop in `GenerateExtensionClass` that calls the decorator wrapping. Find where decorators are emitted for `AddXxxServices()` (search for the section that iterates `decoratorsByInterface` and emits `AddTransient<IFoo>(...)`).

For each decorator in the sorted list, wrap the emitted code in a conditional when `WhenRegisteredFqn != null`:

```csharp
// Before emitting a decorator registration:
bool isConditional = dec.WhenRegisteredFqn != null;
if (isConditional)
{
    sb.AppendLine($"            if (services.Any(d => d.ServiceType == typeof({dec.WhenRegisteredFqn})))");
    sb.AppendLine("            {");
}

// ... existing decorator registration code, indented with extra 4 spaces if isConditional ...

if (isConditional)
{
    sb.AppendLine("            }");
}
```

Apply the same pattern to the hybrid container generator and standalone container generator (search for the sections that handle `decoratorsByInterface` in `GenerateServiceProviderClass` and `GenerateStandaloneServiceProviderClass`). For containers, the guard emits:

```csharp
// In container resolve methods — check is done once at build/registration time not at resolve time,
// so WhenRegistered applies only to AddXxxServices(). Container providers skip the WhenRegistered guard
// (the decorator is already baked in at compile time). If WhenRegistered is needed for containers,
// it must be applied consistently. For now: containers also emit the conditional using a bool field
// set at construction time.
```

> **Note:** Container generation is more complex for `WhenRegistered` since resolution happens in a type-switch, not in `AddXxxServices()`. For the initial implementation, apply `WhenRegistered` only in `AddXxxServices()` (the ServiceCollectionExtensions output). The container outputs include all decorators unconditionally — add a TODO comment to address in a follow-up.

**Step 4: Run tests — expect pass**

```
dotnet test tests/ZInject.Tests --filter "WhenRegistered" -v n
```

Expected: PASS

**Step 5: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All pass.

**Step 6: Commit**

```bash
git add src/ZInject.Generator/ZInjectGenerator.cs \
        tests/ZInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs
git commit -m "feat: emit WhenRegistered conditional guard in AddXxxServices() for [DecoratorOf]"
```

---

### Task 6: Final integration test and README update

**Files:**
- Test: `tests/ZInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs`
- Modify: `README.md`

**Step 1: Write integration test combining Order + WhenRegistered + OptionalDependency**

```csharp
[Fact]
public void DecoratorOf_FullChain_OrderAndWhenRegisteredAndOptional()
{
    var source = """
        using ZInject;
        public interface IRetriever { }
        public class SomeOptions { }
        public interface ILogger { }
        [Transient]
        public class RealRetriever : IRetriever { }
        [DecoratorOf(typeof(IRetriever), Order = 1, WhenRegistered = typeof(SomeOptions))]
        public class LoggingRetriever : IRetriever
        {
            public LoggingRetriever(IRetriever inner, [OptionalDependency] ILogger? logger) { }
        }
        [DecoratorOf(typeof(IRetriever), Order = 2)]
        public class TracingRetriever : IRetriever
        {
            public TracingRetriever(IRetriever inner) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    // Order=1 LoggingRetriever wraps RealRetriever, conditional on SomeOptions
    Assert.Contains("services.Any(d => d.ServiceType == typeof(global::SomeOptions))", output);
    Assert.Contains("new global::LoggingRetriever", output);
    Assert.Contains("GetService<global::ILogger>", output);         // optional dep
    // Order=2 TracingRetriever wraps unconditionally
    Assert.DoesNotContain("SomeOptions", output.Substring(output.IndexOf("TracingRetriever")));
    Assert.Contains("new global::TracingRetriever", output);
    // Order: LoggingRetriever emitted before TracingRetriever
    Assert.True(output.IndexOf("LoggingRetriever") < output.IndexOf("TracingRetriever"));
}
```

**Step 2: Run integration test**

```
dotnet test tests/ZInject.Tests --filter "DecoratorOf_FullChain" -v n
```

Expected: PASS

**Step 3: Update README**

In `README.md`, add a new subsection under `## Features` (before `## Generated Container`):

```markdown
### Decorator Ordering and Conditional Decorators

`[DecoratorOf]` is the explicit form of `[Decorator]` — it names the decorated interface, controls ordering, and supports conditional application.

```csharp
[DecoratorOf(typeof(IRetriever), Order = 1, WhenRegistered = typeof(SomeOptions))]
public class LoggingRetriever : IRetriever
{
    public LoggingRetriever(IRetriever inner, [OptionalDependency] ILogger? logger) { }
}

[DecoratorOf(typeof(IRetriever), Order = 2)]
public class TracingRetriever : IRetriever
{
    public TracingRetriever(IRetriever inner) { }
}
```

**`Order`** — ascending: `Order = 1` is innermost (closest to the real implementation). Higher numbers wrap further out.

**`WhenRegistered`** — the decorator is only wired up if the specified type is present in the `IServiceCollection` at the time `AddXxxServices()` is called. One O(n) scan at startup; no impact on resolution.

**`[OptionalDependency]`** — marks a constructor parameter as optional. The generator emits `GetService<T>()` (returns `null`) instead of `GetRequiredService<T>()` (throws). The parameter must be nullable (`ILogger?`).

Also added diagnostics:

| ID | Severity | Description |
|---|---|---|
| ZI015 | Error | `[OptionalDependency]` on non-nullable parameter |
| ZI016 | Error | `[DecoratorOf]` interface not implemented by the class |
| ZI017 | Error | Two decorators for the same interface share the same `Order` |
```

**Step 4: Run all tests**

```
dotnet test tests/ZInject.Tests -v n
```

Expected: All 245+ pass.

**Step 5: Commit**

```bash
git add tests/ZInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs README.md
git commit -m "feat: integration test and README for DecoratorOf/OptionalDependency"
```

---

### Task 7: Create PR

```bash
git push -u origin feat/decorator-of-optional-dependency
gh pr create \
  --title "feat: add [DecoratorOf] and [OptionalDependency] attributes" \
  --body "Adds [DecoratorOf(typeof(IFoo), Order=N, WhenRegistered=typeof(T))] and [OptionalDependency] to ZInject..."
```
