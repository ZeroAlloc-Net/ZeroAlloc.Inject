# Phase 2: Factory Lambdas Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Evolve the source generator from type-based registrations to factory lambdas with direct `new()` calls, eliminating `Activator.CreateInstance` for all attributed services.

**Architecture:** Extend `ServiceRegistrationInfo` with constructor parameter metadata. Add constructor analysis to `GetServiceInfo`. Change emission methods to output `sp => new Foo(sp.GetRequiredService<IBar>())` instead of `TryAddTransient<IBar, Foo>()`. Open generics stay unchanged (ServiceDescriptor). Add ZI009/ZI010 diagnostics.

**Tech Stack:** Roslyn IIncrementalGenerator (netstandard2.0), xUnit tests, C# source generation

---

### Task 1: Add ConstructorParameterInfo data model

**Files:**
- Create: `src/ZeroInject.Generator/ConstructorParameterInfo.cs`
- Test: `tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs`

**Step 1: Write the failing test**

Create `tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs`:

```csharp
namespace ZeroInject.Tests.GeneratorTests;

public class FactoryRegistrationTests
{
    [Fact]
    public void ParameterlessConstructor_GeneratesFactoryLambda()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("sp => new global::TestApp.MyService()", output);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZeroInject.Tests --filter "FactoryRegistrationTests.ParameterlessConstructor_GeneratesFactoryLambda" --no-build`
Expected: FAIL — output still contains `TryAddTransient<...>` not factory lambda

**Step 3: Create ConstructorParameterInfo**

Create `src/ZeroInject.Generator/ConstructorParameterInfo.cs`:

```csharp
#nullable enable
using System;

namespace ZeroInject.Generator
{
    internal sealed class ConstructorParameterInfo : IEquatable<ConstructorParameterInfo>
    {
        public string FullyQualifiedTypeName { get; }
        public string ParameterName { get; }
        public bool IsOptional { get; }

        public ConstructorParameterInfo(
            string fullyQualifiedTypeName,
            string parameterName,
            bool isOptional)
        {
            FullyQualifiedTypeName = fullyQualifiedTypeName;
            ParameterName = parameterName;
            IsOptional = isOptional;
        }

        public bool Equals(ConstructorParameterInfo? other)
        {
            if (other is null) return false;
            return FullyQualifiedTypeName == other.FullyQualifiedTypeName
                && ParameterName == other.ParameterName
                && IsOptional == other.IsOptional;
        }

        public override bool Equals(object? obj) => Equals(obj as ConstructorParameterInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + FullyQualifiedTypeName.GetHashCode();
                hash = hash * 31 + ParameterName.GetHashCode();
                return hash;
            }
        }
    }
}
```

**Step 4: Extend ServiceRegistrationInfo**

Modify `src/ZeroInject.Generator/ServiceRegistrationInfo.cs`:

Add a `ConstructorParameters` property (`List<ConstructorParameterInfo>`), add it to the constructor, and include it in `Equals`/`GetHashCode`.

```csharp
// Add to properties:
public List<ConstructorParameterInfo> ConstructorParameters { get; }

// Add to constructor parameter list:
// List<ConstructorParameterInfo> constructorParameters
// Add assignment:
// ConstructorParameters = constructorParameters;

// Update Equals to include:
// && ConstructorParameters.Count == other.ConstructorParameters.Count
```

Full updated constructor signature:
```csharp
public ServiceRegistrationInfo(
    string ns,
    string typeName,
    string fullyQualifiedName,
    string lifetime,
    List<string> interfaces,
    string? asType,
    string? key,
    bool allowMultiple,
    bool isOpenGeneric,
    string? openGenericArity,
    bool hasPublicConstructor,
    List<ConstructorParameterInfo> constructorParameters)
```

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ConstructorParameterInfo.cs src/ZeroInject.Generator/ServiceRegistrationInfo.cs tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs
git commit -m "feat: add ConstructorParameterInfo data model and extend ServiceRegistrationInfo"
```

---

### Task 2: Add constructor analysis to GetServiceInfo

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs` (lines 163-293, `GetServiceInfo` method)

**Step 1: Update GetServiceInfo to analyze constructors**

In `GetServiceInfo`, after the existing `hasPublicConstructor` check (lines 271-279), add constructor parameter extraction:

```csharp
// After the hasPublicConstructor loop, add:
var constructorParameters = new List<ConstructorParameterInfo>();
IMethodSymbol? chosenCtor = null;

// Collect public constructors
var publicCtors = new List<IMethodSymbol>();
foreach (var ctor in typeSymbol.InstanceConstructors)
{
    if (ctor.DeclaredAccessibility == Accessibility.Public)
    {
        publicCtors.Add(ctor);
    }
}

if (publicCtors.Count == 1)
{
    chosenCtor = publicCtors[0];
}
else if (publicCtors.Count > 1)
{
    // Look for [ActivatorUtilitiesConstructor]
    foreach (var ctor in publicCtors)
    {
        foreach (var ctorAttr in ctor.GetAttributes())
        {
            if (ctorAttr.AttributeClass?.Name == "ActivatorUtilitiesConstructorAttribute"
                || ctorAttr.AttributeClass?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructorAttribute")
            {
                chosenCtor = ctor;
                break;
            }
        }
        if (chosenCtor != null) break;
    }
    // If no [ActivatorUtilitiesConstructor] found, chosenCtor stays null — ZI009 reported later
}

if (chosenCtor != null)
{
    foreach (var param in chosenCtor.Parameters)
    {
        var paramTypeFqn = param.Type.ToDisplayString(FullyQualifiedFormat);
        var isOptional = param.HasExplicitDefaultValue;
        constructorParameters.Add(new ConstructorParameterInfo(paramTypeFqn, param.Name, isOptional));
    }
}
```

Update the `return new ServiceRegistrationInfo(...)` call to pass `constructorParameters`.

**Step 2: Build to verify compilation**

Run: `dotnet build src/ZeroInject.Generator`
Expected: BUILD SUCCEEDED

**Step 3: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs
git commit -m "feat: add constructor analysis to GetServiceInfo"
```

---

### Task 3: Add ZI009 and ZI010 diagnostic descriptors

**Files:**
- Modify: `src/ZeroInject.Generator/DiagnosticDescriptors.cs`
- Modify: `src/ZeroInject.Generator/ServiceRegistrationInfo.cs` (add `HasMultipleConstructors` and `HasPrimitiveParameter` flags)

**Step 1: Write the failing tests**

Add to `tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs`:

```csharp
[Fact]
public void MultipleConstructors_WithoutAttribute_ProducesZI009()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService() { }
            public MyService(int x) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains(diagnostics, d => d.Id == "ZI009");
}

[Fact]
public void PrimitiveParameter_ProducesZI010()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService(string name) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains(diagnostics, d => d.Id == "ZI010");
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests --filter "FactoryRegistrationTests" --no-build`
Expected: FAIL — ZI009 and ZI010 not reported

**Step 3: Add diagnostic descriptors**

Add to `src/ZeroInject.Generator/DiagnosticDescriptors.cs`:

```csharp
public static readonly DiagnosticDescriptor MultipleConstructorsNoAttribute = new DiagnosticDescriptor(
    "ZI009",
    "Multiple public constructors without [ActivatorUtilitiesConstructor]",
    "Class '{0}' has multiple public constructors. Apply [ActivatorUtilitiesConstructor] to the preferred constructor.",
    "ZeroInject",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);

public static readonly DiagnosticDescriptor PrimitiveConstructorParameter = new DiagnosticDescriptor(
    "ZI010",
    "Constructor parameter is a primitive/value type",
    "Constructor parameter '{0}' of class '{1}' is a primitive/value type ({2}). Use IOptions<T> or a wrapper type instead.",
    "ZeroInject",
    DiagnosticSeverity.Error,
    isEnabledByDefault: true);
```

**Step 4: Add tracking flags to ServiceRegistrationInfo**

Add two new properties:
```csharp
public bool HasMultipleConstructors { get; }
public string? PrimitiveParameterName { get; }
public string? PrimitiveParameterType { get; }
```

Update the constructor to accept and assign these. Update `GetServiceInfo` to set them:
- `HasMultipleConstructors = publicCtors.Count > 1 && chosenCtor == null`
- Check each constructor parameter's type: if it's a value type (`param.Type.IsValueType`) or is `string`, `Uri`, `CancellationToken`, set `PrimitiveParameterName`/`PrimitiveParameterType`

**Step 5: Report diagnostics in RegisterSourceOutput**

In the `RegisterSourceOutput` lambda in `Initialize()`, after the existing ZI006/ZI007 checks, add:

```csharp
if (svc.HasMultipleConstructors)
{
    spc.ReportDiagnostic(Diagnostic.Create(
        DiagnosticDescriptors.MultipleConstructorsNoAttribute,
        Location.None,
        svc.TypeName));
}

if (svc.PrimitiveParameterName != null)
{
    spc.ReportDiagnostic(Diagnostic.Create(
        DiagnosticDescriptors.PrimitiveConstructorParameter,
        Location.None,
        svc.PrimitiveParameterName,
        svc.TypeName,
        svc.PrimitiveParameterType));
}
```

**Step 6: Run tests to verify they pass**

Run: `dotnet test tests/ZeroInject.Tests --filter "FactoryRegistrationTests"`
Expected: PASS

**Step 7: Commit**

```bash
git add src/ZeroInject.Generator/DiagnosticDescriptors.cs src/ZeroInject.Generator/ServiceRegistrationInfo.cs src/ZeroInject.Generator/ZeroInjectGenerator.cs tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs
git commit -m "feat: add ZI009 and ZI010 diagnostics for constructor validation"
```

---

### Task 4: Emit factory lambdas for non-keyed, non-open-generic registrations

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs` (EmitSingleRegistration, EmitConcreteRegistration)

This is the core change. All non-open-generic registrations switch from type-based to factory lambda.

**Step 1: Verify the parameterless constructor test still fails**

Run: `dotnet test tests/ZeroInject.Tests --filter "ParameterlessConstructor_GeneratesFactoryLambda"`
Expected: FAIL

**Step 2: Add helper method for factory lambda body**

Add a private static method to generate the factory lambda body:

```csharp
private static string BuildFactoryLambda(string implType, List<ConstructorParameterInfo> parameters)
{
    if (parameters.Count == 0)
    {
        return "sp => new " + implType + "()";
    }

    var sb = new StringBuilder();
    sb.Append("sp => new ");
    sb.Append(implType);
    sb.Append("(\n");

    for (int i = 0; i < parameters.Count; i++)
    {
        var param = parameters[i];
        var method = param.IsOptional ? "GetService" : "GetRequiredService";
        sb.Append("                sp.");
        sb.Append(method);
        sb.Append("<");
        sb.Append(param.FullyQualifiedTypeName);
        sb.Append(">()");
        if (i < parameters.Count - 1)
        {
            sb.Append(",");
        }
        sb.Append("\n");
    }

    sb.Append("            )");
    return sb.ToString();
}
```

**Step 3: Update EmitRegistration to pass constructor parameters**

Change `EmitRegistration` signature to pass `svc.ConstructorParameters` to `EmitSingleRegistration` and `EmitConcreteRegistration`.

**Step 4: Update EmitSingleRegistration**

For non-open-generic, non-keyed registrations, change from:
```csharp
sb.AppendLine(string.Format(
    "            services.{0}<{1}, {2}>();",
    method, serviceType, implType));
```
To:
```csharp
var factory = BuildFactoryLambda(implType, constructorParameters);
sb.AppendLine(string.Format(
    "            services.{0}<{1}>({2});",
    method, serviceType, factory));
```

**Step 5: Update EmitConcreteRegistration**

For non-open-generic, non-keyed registrations, change from:
```csharp
sb.AppendLine(string.Format(
    "            services.{0}<{1}>();",
    method, implType));
```
To:
```csharp
var factory = BuildFactoryLambda(implType, constructorParameters);
sb.AppendLine(string.Format(
    "            services.{0}({1});",
    method, factory));
```

**Step 6: Run the parameterless constructor test**

Run: `dotnet test tests/ZeroInject.Tests --filter "ParameterlessConstructor_GeneratesFactoryLambda"`
Expected: PASS

**Step 7: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs
git commit -m "feat: emit factory lambdas for non-keyed non-open-generic registrations"
```

---

### Task 5: Emit factory lambdas for keyed registrations

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs` (keyed branches in EmitSingleRegistration, EmitConcreteRegistration)
- Test: `tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs`

**Step 1: Write the failing test**

Add to `FactoryRegistrationTests.cs`:

```csharp
[Fact]
public void KeyedService_GeneratesFactoryLambda()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface ICache { }

        [Singleton(Key = "redis")]
        public class RedisCache : ICache { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("sp => new global::TestApp.RedisCache()", output);
    Assert.Contains("\"redis\"", output);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZeroInject.Tests --filter "FactoryRegistrationTests.KeyedService_GeneratesFactoryLambda"`
Expected: FAIL

**Step 3: Update keyed registration emission**

In `EmitSingleRegistration`, update the keyed branch to use factory lambdas:
```csharp
if (key != null)
{
    var method = useAdd ? "AddKeyed" + lifetime : "TryAddKeyed" + lifetime;
    var factory = BuildFactoryLambda(implType, constructorParameters);
    sb.AppendLine(string.Format(
        "            services.{0}<{1}>(\"{2}\", (sp, _) => ({3}).Invoke(sp));",
        method, serviceType, key, factory));
}
```

Note: Keyed service factories have signature `(IServiceProvider, object?)`. We wrap: `(sp, _) => new Foo(...)`.

Actually, for keyed registrations, the overload that takes a factory is:
`services.TryAddKeyedTransient<IService>(key, (sp, key) => new Impl(...))`

Update keyed emission for `EmitSingleRegistration`:
```csharp
var factory = BuildFactoryLambda(implType, constructorParameters);
// Replace "sp =>" with "(sp, _) =>" for keyed factory signature
var keyedFactory = factory.Replace("sp => ", "(sp, _) => ");
sb.AppendLine(string.Format(
    "            services.{0}<{1}>(\"{2}\", {3});",
    method, serviceType, key, keyedFactory));
```

Same for `EmitConcreteRegistration` keyed branch.

**Step 4: Run test to verify it passes**

Run: `dotnet test tests/ZeroInject.Tests --filter "FactoryRegistrationTests.KeyedService_GeneratesFactoryLambda"`
Expected: PASS

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs
git commit -m "feat: emit factory lambdas for keyed service registrations"
```

---

### Task 6: Add factory registration tests for constructor parameters

**Files:**
- Modify: `tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs`

**Step 1: Write tests for constructor injection patterns**

Add to `FactoryRegistrationTests.cs`:

```csharp
[Fact]
public void SingleParameter_GeneratesGetRequiredService()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }
        public interface ILogger { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService(ILogger logger) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("sp => new global::TestApp.MyService(", output);
    Assert.Contains("sp.GetRequiredService<global::TestApp.ILogger>()", output);
}

[Fact]
public void MultipleParameters_GeneratesAllGetRequiredService()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }
        public interface IRepo { }
        public interface ILogger { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService(IRepo repo, ILogger logger) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("sp.GetRequiredService<global::TestApp.IRepo>()", output);
    Assert.Contains("sp.GetRequiredService<global::TestApp.ILogger>()", output);
}

[Fact]
public void OptionalParameter_GeneratesGetService()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }
        public interface ILogger { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService(ILogger? logger = null) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("sp.GetService<global::TestApp.ILogger>()", output);
    Assert.DoesNotContain("GetRequiredService", output);
}

[Fact]
public void MixedRequiredAndOptionalParameters()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }
        public interface IRepo { }
        public interface ILogger { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService(IRepo repo, ILogger? logger = null) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("sp.GetRequiredService<global::TestApp.IRepo>()", output);
    Assert.Contains("sp.GetService<global::TestApp.ILogger>()", output);
}

[Fact]
public void MultipleConstructors_WithActivatorUtilitiesConstructor_UsesMarked()
{
    var source = """
        using ZeroInject;
        using Microsoft.Extensions.DependencyInjection;
        namespace TestApp;

        public interface IMyService { }
        public interface IRepo { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService() { }

            [ActivatorUtilitiesConstructor]
            public MyService(IRepo repo) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("sp.GetRequiredService<global::TestApp.IRepo>()", output);
    Assert.DoesNotContain(diagnostics, d => d.Id == "ZI009");
}

[Fact]
public void OpenGeneric_StillUsesServiceDescriptor()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IRepository<T> { }

        [Scoped]
        public class Repository<T> : IRepository<T> { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("ServiceDescriptor.Scoped(typeof(global::TestApp.IRepository<>), typeof(global::TestApp.Repository<>))", output);
    Assert.DoesNotContain("sp =>", output);
}

[Fact]
public void ConcreteOnly_GeneratesFactoryLambda()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        [Transient]
        public class PlainService { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("sp => new global::TestApp.PlainService()", output);
}

[Fact]
public void PrimitiveParameter_String_ProducesZI010()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService(string connectionString) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains(diagnostics, d => d.Id == "ZI010");
}

[Fact]
public void PrimitiveParameter_Int_ProducesZI010()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService(int count) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains(diagnostics, d => d.Id == "ZI010");
}

[Fact]
public void MultipleConstructors_WithoutAttribute_NoFactory()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IMyService { }

        [Transient]
        public class MyService : IMyService
        {
            public MyService() { }
            public MyService(int x) { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains(diagnostics, d => d.Id == "ZI009");
}
```

**Step 2: Run all factory tests**

Run: `dotnet test tests/ZeroInject.Tests --filter "FactoryRegistrationTests"`
Expected: All PASS

**Step 3: Commit**

```bash
git add tests/ZeroInject.Tests/GeneratorTests/FactoryRegistrationTests.cs
git commit -m "test: add comprehensive factory registration tests for constructor patterns"
```

---

### Task 7: Update existing BasicRegistrationTests assertions

**Files:**
- Modify: `tests/ZeroInject.Tests/GeneratorTests/BasicRegistrationTests.cs`

Since factory lambdas change the output format, existing assertions need updating. The output changes from:
- `TryAddTransient<IFoo, Foo>()` → `TryAddTransient<IFoo>(sp => new Foo())`
- `TryAddTransient<Foo>()` → `TryAddTransient(sp => new Foo())`

**Step 1: Update each test's assertions**

Replace the assertions in each test. Key pattern changes:

| Old assertion | New assertion |
|---|---|
| `TryAddTransient<IFoo, Foo>` | `TryAddTransient<global::TestApp.IFoo>(sp => new global::TestApp.Foo()` |
| `TryAddTransient<Foo>` | `TryAddTransient(sp => new global::TestApp.Foo()` |
| `AddTransient<IFoo, Foo>()` | `AddTransient<global::TestApp.IFoo>(sp => new global::TestApp.Foo()` |
| `TryAddKeyedSingleton<IFoo, Foo>("k")` | `TryAddKeyedSingleton<global::TestApp.IFoo>("k", (sp, _) => new global::TestApp.Foo()` |

Here are the specific test updates needed:

**TransientAttribute_GeneratesTryAddTransient:**
```csharp
Assert.Contains("TryAddTransient<global::TestApp.IMyService>(sp => new global::TestApp.MyService())", output);
Assert.Contains("TryAddTransient(sp => new global::TestApp.MyService())", output);
```

**ScopedAttribute_GeneratesTryAddScoped:**
```csharp
Assert.Contains("TryAddScoped<global::TestApp.IRepo>(sp => new global::TestApp.Repo())", output);
Assert.Contains("TryAddScoped(sp => new global::TestApp.Repo())", output);
```

**SingletonAttribute_GeneratesTryAddSingleton:**
```csharp
Assert.Contains("TryAddSingleton<global::TestApp.ICache>(sp => new global::TestApp.Cache())", output);
Assert.Contains("TryAddSingleton(sp => new global::TestApp.Cache())", output);
```

**ConcreteOnly_RegistersConcreteType:**
```csharp
Assert.Contains("TryAddTransient(sp => new global::TestApp.PlainService())", output);
```

**FilteredInterfaces_AreExcluded:**
```csharp
Assert.Contains("TryAddTransient<global::TestApp.IMyService>(sp => new global::TestApp.MyService())", output);
Assert.DoesNotContain("IDisposable", output);
```

**KeyedService_GeneratesKeyedRegistration:**
```csharp
Assert.Contains("TryAddKeyedSingleton<global::TestApp.ICache>(\"redis\", (sp, _) => new global::TestApp.RedisCache())", output);
Assert.Contains("TryAddKeyedSingleton(\"redis\", (sp, _) => new global::TestApp.RedisCache())", output);
```

**AllowMultiple_GeneratesAddInsteadOfTryAdd:**
```csharp
Assert.Contains("AddTransient<global::TestApp.IJob>(sp => new global::TestApp.MyJob())", output);
Assert.DoesNotContain("TryAdd", output);
```

**AsProperty_NarrowsRegistration:**
```csharp
Assert.Contains("TryAddTransient<global::TestApp.IFoo>(sp => new global::TestApp.MyService())", output);
Assert.DoesNotContain("IBar", output);
Assert.DoesNotContain("TryAddTransient(sp => new global::TestApp.MyService())", output);
```

**MultipleInterfaces_RegistersAll:**
```csharp
Assert.Contains("TryAddTransient<global::TestApp.IFoo>(sp => new global::TestApp.MultiService())", output);
Assert.Contains("TryAddTransient<global::TestApp.IBar>(sp => new global::TestApp.MultiService())", output);
Assert.Contains("TryAddTransient<global::TestApp.IBaz>(sp => new global::TestApp.MultiService())", output);
Assert.Contains("TryAddTransient(sp => new global::TestApp.MultiService())", output);
```

**MixedLifetimes_AllRegistered:**
```csharp
Assert.Contains("TryAddTransient<global::TestApp.IA>(sp => new global::TestApp.SvcA())", output);
Assert.Contains("TryAddScoped<global::TestApp.IB>(sp => new global::TestApp.SvcB())", output);
Assert.Contains("TryAddSingleton<global::TestApp.IC>(sp => new global::TestApp.SvcC())", output);
```

**OnlyFilteredInterfaces_RegistersConcreteOnly:**
```csharp
Assert.Contains("TryAddTransient(sp => new global::TestApp.MyService())", output);
Assert.DoesNotContain("IDisposable", output);
```

**IEquatable_IsFiltered:**
```csharp
Assert.Contains("TryAddTransient<global::TestApp.IMyService>(sp => new global::TestApp.MyService())", output);
Assert.DoesNotContain("IEquatable", output);
```

**KeyedPlusAllowMultiple_GeneratesAddKeyed:**
```csharp
Assert.Contains("AddKeyedSingleton<global::TestApp.ICache>(\"redis\", (sp, _) => new global::TestApp.RedisCache())", output);
Assert.DoesNotContain("TryAdd", output);
```

**KeyedPlusAs_NarrowsKeyedRegistration:**
```csharp
Assert.Contains("TryAddKeyedScoped<global::TestApp.IRead>(\"main\", (sp, _) => new global::TestApp.Store())", output);
Assert.DoesNotContain("IWrite", output);
```

**AsProperty_StillRegistersConcreteType:**
```csharp
Assert.Contains("TryAddTransient<global::TestApp.IFoo>(sp => new global::TestApp.Foo())", output);
Assert.DoesNotContain("IBar", output);
```

**Step 2: Run all BasicRegistrationTests**

Run: `dotnet test tests/ZeroInject.Tests --filter "BasicRegistrationTests"`
Expected: All 17 tests PASS

**Step 3: Commit**

```bash
git add tests/ZeroInject.Tests/GeneratorTests/BasicRegistrationTests.cs
git commit -m "test: update BasicRegistrationTests assertions for factory lambda output"
```

---

### Task 8: Run full test suite and fix any remaining failures

**Files:**
- Possibly modify: any test or generator file with issues

**Step 1: Run the full test suite**

Run: `dotnet test tests/ZeroInject.Tests`
Expected: All tests PASS (BasicRegistrationTests, OpenGenericTests, MethodNamingTests, DiagnosticTests, FactoryRegistrationTests, AttributeTests)

**Step 2: Fix any failures**

If any tests fail, analyze the output and fix. Common issues:
- OpenGenericTests should be unchanged (they assert `ServiceDescriptor` output)
- MethodNamingTests should be unchanged (they assert method/class names)
- DiagnosticTests may need minor assertion updates if factory lambda output affects them

**Step 3: Commit any fixes**

```bash
git add -A
git commit -m "fix: resolve remaining test failures after factory lambda migration"
```

---

### Task 9: Update README diagnostics table

**Files:**
- Modify: `README.md` (diagnostics table at line 99-113)

**Step 1: Add ZI009 and ZI010 to the diagnostics table**

Add after ZI008:
```markdown
| ZI009 | Error | Multiple public constructors without `[ActivatorUtilitiesConstructor]` |
| ZI010 | Error | Constructor parameter is a primitive/value type |
```

**Step 2: Commit**

```bash
git add README.md
git commit -m "docs: add ZI009 and ZI010 to diagnostics table"
```

---

### Task 10: Update sample application

**Files:**
- Modify: `samples/ZeroInject.Sample/Services.cs` (optional — add a service with constructor injection to showcase factory lambdas)

**Step 1: Read current sample**

Check `samples/ZeroInject.Sample/Services.cs` for current state.

**Step 2: Add a service with constructor dependencies**

Add an example service that demonstrates constructor injection:
```csharp
public interface IOrderService { }

[Transient]
public class OrderService : IOrderService
{
    public OrderService(IGreetingService greetingService) { }
}
```

**Step 3: Build the sample to verify generated code**

Run: `dotnet build samples/ZeroInject.Sample`
Expected: BUILD SUCCEEDED

**Step 4: Run the sample**

Run: `dotnet run --project samples/ZeroInject.Sample`
Expected: Runs without error

**Step 5: Commit**

```bash
git add samples/ZeroInject.Sample/Services.cs
git commit -m "feat: add constructor injection example to sample app"
```

---

### Task 11: Final verification

**Step 1: Run full test suite**

Run: `dotnet test tests/ZeroInject.Tests`
Expected: All tests PASS

**Step 2: Build entire solution**

Run: `dotnet build ZeroInject.slnx`
Expected: BUILD SUCCEEDED with no warnings

**Step 3: Verify generated output looks correct**

Run a quick manual check by examining the generated output from one of the tests to confirm factory lambda format.
