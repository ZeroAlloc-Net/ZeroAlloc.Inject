# ZeroInject Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a source-generated DI registration library that auto-discovers services via `[Transient]`, `[Scoped]`, `[Singleton]` attributes and generates `IServiceCollection.AddXxxServices()` extension methods at compile time.

**Architecture:** Single `IIncrementalGenerator` finds attributed classes, extracts registration metadata, and emits a `ServiceCollectionExtensions.g.cs` file with `TryAdd`/`Add` calls. Core library is attributes-only with zero dependencies. Follows the same patterns as ZMediator (at `c:/Projects/Prive/ZMediator`).

**Tech Stack:** C#, Roslyn `IIncrementalGenerator`, `Microsoft.CodeAnalysis.CSharp` 4.12.0, xUnit, `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`, `net8.0`+`net10.0` (core), `netstandard2.0` (generator).

**Reference:** ZMediator project at `c:/Projects/Prive/ZMediator` — follow the same solution structure, `.csproj` patterns, test helper approach, and `Directory.Build.props` conventions.

---

### Task 1: Solution Scaffolding

**Files:**
- Create: `ZeroInject.slnx`
- Create: `Directory.Build.props`
- Create: `src/ZeroInject/ZeroInject.csproj`
- Create: `src/ZeroInject.Generator/ZeroInject.Generator.csproj`
- Create: `tests/ZeroInject.Tests/ZeroInject.Tests.csproj`
- Create: `samples/ZeroInject.Sample/ZeroInject.Sample.csproj`
- Create: `.gitignore`

**Step 1: Create `.gitignore`**

Standard .NET gitignore (bin, obj, .vs, *.user, etc.).

**Step 2: Create `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>

    <!-- NuGet package metadata -->
    <Authors>Marcel Roozekrans</Authors>
    <Company>Marcel Roozekrans</Company>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/MarcelRoozekrans/ZeroInject</PackageProjectUrl>
    <RepositoryUrl>https://github.com/MarcelRoozekrans/ZeroInject</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <Description>A compile-time DI registration library for .NET. Roslyn source generator wires all service registrations at compile time — no reflection, no scanning.</Description>
    <PackageTags>dependency-injection;di;source-generator;roslyn;ioc</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageIcon>icon.png</PackageIcon>
    <Copyright>Copyright (c) Marcel Roozekrans</Copyright>
  </PropertyGroup>
  <ItemGroup Condition="'$(IsPackable)' != 'false'">
    <None Include="$(MSBuildThisFileDirectory)README.md" Pack="true" PackagePath="\" />
    <None Include="$(MSBuildThisFileDirectory)assets\icon.png" Pack="true" PackagePath="\" />
  </ItemGroup>
  <ItemGroup Condition="'$(TargetFramework)' != 'netstandard2.0'">
    <PackageReference Include="Meziantou.Analyzer" Version="3.0.19">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

**Step 3: Create `src/ZeroInject/ZeroInject.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <RootNamespace>ZeroInject</RootNamespace>
    <PackageId>ZeroInject</PackageId>
  </PropertyGroup>
</Project>
```

**Step 4: Create `src/ZeroInject.Generator/ZeroInject.Generator.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <RootNamespace>ZeroInject.Generator</RootNamespace>
    <PackageId>ZeroInject.Generator</PackageId>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsRoslynComponent>true</IsRoslynComponent>
    <NoWarn>$(NoWarn);RS2008</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.Analyzers" Version="3.11.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**Step 5: Create `tests/ZeroInject.Tests/ZeroInject.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>ZeroInject.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.12.0" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Analyzer.Testing" Version="1.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.Testing.Verifiers.XUnit" Version="1.*" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroInject\ZeroInject.csproj" />
    <ProjectReference Include="..\..\src\ZeroInject.Generator\ZeroInject.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="true" />
  </ItemGroup>
</Project>
```

**Step 6: Create `samples/ZeroInject.Sample/ZeroInject.Sample.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>ZeroInject.Sample</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\ZeroInject\ZeroInject.csproj" />
    <ProjectReference Include="..\..\src\ZeroInject.Generator\ZeroInject.Generator.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

**Step 7: Create `ZeroInject.slnx`**

```xml
<Solution>
  <Folder Name="/samples/">
    <Project Path="samples/ZeroInject.Sample/ZeroInject.Sample.csproj" />
  </Folder>
  <Folder Name="/src/">
    <Project Path="src/ZeroInject.Generator/ZeroInject.Generator.csproj" />
    <Project Path="src/ZeroInject/ZeroInject.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ZeroInject.Tests/ZeroInject.Tests.csproj" />
  </Folder>
</Solution>
```

**Step 8: Build solution to verify scaffolding**

Run: `dotnet build ZeroInject.slnx`
Expected: Build succeeded with 0 errors.

**Step 9: Commit**

```bash
git add .gitignore Directory.Build.props ZeroInject.slnx src/ tests/ samples/
git commit -m "feat: scaffold ZeroInject solution structure"
```

---

### Task 2: Core Attributes

**Files:**
- Create: `src/ZeroInject/ServiceAttribute.cs`
- Create: `src/ZeroInject/TransientAttribute.cs`
- Create: `src/ZeroInject/ScopedAttribute.cs`
- Create: `src/ZeroInject/SingletonAttribute.cs`
- Create: `src/ZeroInject/ZeroInjectAttribute.cs`

**Step 1: Write the failing test**

Create: `tests/ZeroInject.Tests/AttributeTests.cs`

```csharp
namespace ZeroInject.Tests;

public class AttributeTests
{
    [Fact]
    public void TransientAttribute_IsServiceAttribute()
    {
        var attr = new TransientAttribute();
        Assert.IsAssignableFrom<ServiceAttribute>(attr);
    }

    [Fact]
    public void ScopedAttribute_IsServiceAttribute()
    {
        var attr = new ScopedAttribute();
        Assert.IsAssignableFrom<ServiceAttribute>(attr);
    }

    [Fact]
    public void SingletonAttribute_IsServiceAttribute()
    {
        var attr = new SingletonAttribute();
        Assert.IsAssignableFrom<ServiceAttribute>(attr);
    }

    [Fact]
    public void ServiceAttribute_DefaultValues()
    {
        var attr = new TransientAttribute();
        Assert.Null(attr.As);
        Assert.Null(attr.Key);
        Assert.False(attr.AllowMultiple);
    }

    [Fact]
    public void ServiceAttribute_SetProperties()
    {
        var attr = new TransientAttribute
        {
            As = typeof(IDisposable),
            Key = "mykey",
            AllowMultiple = true
        };
        Assert.Equal(typeof(IDisposable), attr.As);
        Assert.Equal("mykey", attr.Key);
        Assert.True(attr.AllowMultiple);
    }

    [Fact]
    public void ZeroInjectAttribute_StoresMethodName()
    {
        var attr = new ZeroInjectAttribute("AddMyServices");
        Assert.Equal("AddMyServices", attr.MethodName);
    }

    [Fact]
    public void TransientAttribute_TargetsClassOnly()
    {
        var usage = typeof(TransientAttribute)
            .GetCustomAttributes(typeof(AttributeUsageAttribute), false)
            .Cast<AttributeUsageAttribute>()
            .Single();

        Assert.Equal(AttributeTargets.Class, usage.ValidOn);
        Assert.False(usage.AllowMultiple);
        Assert.False(usage.Inherited);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test tests/ZeroInject.Tests/ --no-build`
Expected: FAIL — types don't exist yet.

**Step 3: Write `ServiceAttribute.cs`**

```csharp
namespace ZeroInject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public abstract class ServiceAttribute : Attribute
{
    public Type? As { get; set; }
    public string? Key { get; set; }
    public bool AllowMultiple { get; set; }
}
```

**Step 4: Write `TransientAttribute.cs`**

```csharp
namespace ZeroInject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TransientAttribute : ServiceAttribute;
```

**Step 5: Write `ScopedAttribute.cs`**

```csharp
namespace ZeroInject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ScopedAttribute : ServiceAttribute;
```

**Step 6: Write `SingletonAttribute.cs`**

```csharp
namespace ZeroInject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SingletonAttribute : ServiceAttribute;
```

**Step 7: Write `ZeroInjectAttribute.cs`**

```csharp
namespace ZeroInject;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class ZeroInjectAttribute : Attribute
{
    public string MethodName { get; }

    public ZeroInjectAttribute(string methodName)
    {
        MethodName = methodName;
    }
}
```

**Step 8: Run tests**

Run: `dotnet test tests/ZeroInject.Tests/ -v minimal`
Expected: All 7 tests pass.

**Step 9: Commit**

```bash
git add src/ZeroInject/ tests/ZeroInject.Tests/AttributeTests.cs
git commit -m "feat: add core service lifetime attributes and ZeroInject assembly attribute"
```

---

### Task 3: Generator Test Infrastructure + Minimal Generator Skeleton

**Files:**
- Create: `tests/ZeroInject.Tests/GeneratorTests/GeneratorTestHelper.cs`
- Create: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Create: `src/ZeroInject.Generator/ServiceRegistrationInfo.cs`
- Create: `src/ZeroInject.Generator/DiagnosticDescriptors.cs`

**Step 1: Write the test helper**

Create: `tests/ZeroInject.Tests/GeneratorTests/GeneratorTestHelper.cs`

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace ZeroInject.Tests.GeneratorTests;

internal static class GeneratorTestHelper
{
    public static (string output, ImmutableArray<Diagnostic> diagnostics) RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .ToList();

        references.Add(MetadataReference.CreateFromFile(typeof(TransientAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new Generator.ZeroInjectGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedTrees = outputCompilation.SyntaxTrees
            .Where(t => t.FilePath.Contains("ZeroInject"))
            .ToList();

        var output = string.Join("\n", generatedTrees.Select(t => t.GetText().ToString()));
        return (output, diagnostics);
    }
}
```

**Step 2: Write the first failing generator test**

Create: `tests/ZeroInject.Tests/GeneratorTests/BasicRegistrationTests.cs`

```csharp
namespace ZeroInject.Tests.GeneratorTests;

public class BasicRegistrationTests
{
    [Fact]
    public void NoAttributedClasses_GeneratesEmptyMethod()
    {
        var source = """
            namespace TestApp;
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("Error", diagnostics.Select(d => d.Severity.ToString()));
    }

    [Fact]
    public void TransientAttribute_GeneratesTryAddTransient()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IMyService { }

            [Transient]
            public class MyService : IMyService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.DoesNotContain("Error", diagnostics.Select(d => d.Severity.ToString()));
        Assert.Contains("TryAddTransient<global::TestApp.IMyService, global::TestApp.MyService>", output);
        Assert.Contains("TryAddTransient<global::TestApp.MyService>", output);
    }
}
```

**Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests/ --filter "FullyQualifiedName~BasicRegistrationTests" -v minimal`
Expected: FAIL — generator doesn't exist yet.

**Step 4: Create `ServiceRegistrationInfo.cs`**

Data model for passing info from syntax analysis to code generation.

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace ZeroInject.Generator;

internal sealed class ServiceRegistrationInfo : IEquatable<ServiceRegistrationInfo>
{
    public string Namespace { get; }
    public string TypeName { get; }
    public string FullyQualifiedName { get; }
    public string Lifetime { get; }  // "Transient", "Scoped", "Singleton"
    public List<string> Interfaces { get; }
    public string? AsType { get; }
    public string? Key { get; }
    public bool AllowMultiple { get; }
    public bool IsOpenGeneric { get; }
    public string? OpenGenericArity { get; }

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
        string? openGenericArity)
    {
        Namespace = ns;
        TypeName = typeName;
        FullyQualifiedName = fullyQualifiedName;
        Lifetime = lifetime;
        Interfaces = interfaces;
        AsType = asType;
        Key = key;
        AllowMultiple = allowMultiple;
        IsOpenGeneric = isOpenGeneric;
        OpenGenericArity = openGenericArity;
    }

    public bool Equals(ServiceRegistrationInfo? other)
    {
        if (other is null) return false;
        return FullyQualifiedName == other.FullyQualifiedName
            && Lifetime == other.Lifetime
            && AsType == other.AsType
            && Key == other.Key
            && AllowMultiple == other.AllowMultiple;
    }

    public override bool Equals(object? obj) => Equals(obj as ServiceRegistrationInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + FullyQualifiedName.GetHashCode();
            hash = hash * 31 + Lifetime.GetHashCode();
            return hash;
        }
    }
}
```

**Step 5: Create `DiagnosticDescriptors.cs`**

```csharp
using Microsoft.CodeAnalysis;

namespace ZeroInject.Generator;

internal static class DiagnosticDescriptors
{
    public static readonly DiagnosticDescriptor MultipleLifetimeAttributes = new(
        "ZI001",
        "Multiple lifetime attributes",
        "Class '{0}' has multiple lifetime attributes. Only one of [Transient], [Scoped], or [Singleton] is allowed.",
        "ZeroInject",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AttributeOnNonClass = new(
        "ZI002",
        "Attribute on non-class type",
        "'{0}' is not a class. Service attributes can only be applied to classes.",
        "ZeroInject",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AttributeOnAbstractOrStatic = new(
        "ZI003",
        "Attribute on abstract or static class",
        "Class '{0}' is abstract or static and cannot be registered as a service.",
        "ZeroInject",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AsTypeNotImplemented = new(
        "ZI004",
        "As type not implemented",
        "Class '{0}' does not implement '{1}' specified in the As property.",
        "ZeroInject",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor KeyedServiceNotSupported = new(
        "ZI005",
        "Keyed services require .NET 8+",
        "Class '{0}' uses Key property but the target framework does not support keyed services (requires .NET 8+).",
        "ZeroInject",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoPublicConstructor = new(
        "ZI006",
        "No public constructor",
        "Class '{0}' has no public constructor. The DI container requires a public constructor to resolve this service.",
        "ZeroInject",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoInterfaces = new(
        "ZI007",
        "No interfaces implemented",
        "Class '{0}' implements no interfaces and will only be registered as its concrete type.",
        "ZeroInject",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingDIAbstractions = new(
        "ZI008",
        "Missing DI abstractions",
        "Microsoft.Extensions.DependencyInjection.Abstractions is not referenced. Generated code will not compile.",
        "ZeroInject",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
```

**Step 6: Create minimal `ZeroInjectGenerator.cs`**

A skeleton that compiles but generates nothing yet — just enough to make the "no attributed classes" test pass.

```csharp
using Microsoft.CodeAnalysis;

namespace ZeroInject.Generator;

[Generator]
public sealed class ZeroInjectGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Will be implemented in subsequent tasks
    }
}
```

**Step 7: Build and run tests**

Run: `dotnet test tests/ZeroInject.Tests/ -v minimal`
Expected: `NoAttributedClasses_GeneratesEmptyMethod` passes. `TransientAttribute_GeneratesTryAddTransient` fails (generator doesn't emit anything yet). This is expected — next task implements it.

**Step 8: Commit**

```bash
git add src/ZeroInject.Generator/ tests/ZeroInject.Tests/GeneratorTests/
git commit -m "feat: add generator skeleton, test infrastructure, data model, and diagnostic descriptors"
```

---

### Task 4: Generator — Syntax Provider and Service Discovery

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`

This task implements the syntax provider that finds all classes decorated with `[Transient]`, `[Scoped]`, or `[Singleton]` and extracts `ServiceRegistrationInfo`.

**Step 1: Write additional failing tests**

Add to `tests/ZeroInject.Tests/GeneratorTests/BasicRegistrationTests.cs`:

```csharp
[Fact]
public void ScopedAttribute_GeneratesTryAddScoped()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IRepo { }

        [Scoped]
        public class Repo : IRepo { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("TryAddScoped<global::TestApp.IRepo, global::TestApp.Repo>", output);
    Assert.Contains("TryAddScoped<global::TestApp.Repo>", output);
}

[Fact]
public void SingletonAttribute_GeneratesTryAddSingleton()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface ICache { }

        [Singleton]
        public class Cache : ICache { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("TryAddSingleton<global::TestApp.ICache, global::TestApp.Cache>", output);
    Assert.Contains("TryAddSingleton<global::TestApp.Cache>", output);
}

[Fact]
public void ConcreteOnly_RegistersConcreteType()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        [Transient]
        public class PlainService { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("TryAddTransient<global::TestApp.PlainService>", output);
}

[Fact]
public void FilteredInterfaces_AreExcluded()
{
    var source = """
        using ZeroInject;
        using System;
        namespace TestApp;

        public interface IMyService { }

        [Transient]
        public class MyService : IMyService, IDisposable
        {
            public void Dispose() { }
        }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("TryAddTransient<global::TestApp.IMyService, global::TestApp.MyService>", output);
    Assert.DoesNotContain("IDisposable", output);
}

[Fact]
public void GeneratedMethod_ReturnsIServiceCollection()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        [Transient]
        public class Svc { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("public static IServiceCollection", output);
    Assert.Contains("return services;", output);
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests/ --filter "FullyQualifiedName~BasicRegistrationTests" -v minimal`
Expected: All new tests FAIL.

**Step 3: Implement the full generator**

Replace `src/ZeroInject.Generator/ZeroInjectGenerator.cs` with the full implementation:

```csharp
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroInject.Generator;

[Generator]
public sealed class ZeroInjectGenerator : IIncrementalGenerator
{
    private static readonly HashSet<string> FilteredInterfaces = new()
    {
        "System.IDisposable",
        "System.IAsyncDisposable",
        "System.IComparable",
        "System.ICloneable",
        "System.IConvertible",
        "System.IFormattable"
    };

    // Prefixes for generic filtered interfaces (IComparable<T>, IEquatable<T>)
    private static readonly string[] FilteredGenericPrefixes = new[]
    {
        "System.IComparable<",
        "System.IEquatable<"
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var serviceProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZeroInject.TransientAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Transient", ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        var scopedProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZeroInject.ScopedAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Scoped", ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        var singletonProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZeroInject.SingletonAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, ct) => GetServiceInfo(ctx, "Singleton", ct))
            .Where(static info => info is not null)
            .Select(static (info, _) => info!);

        // Collect the assembly-level ZeroInject attribute for method name override
        var assemblyAttributeProvider = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "ZeroInject.ZeroInjectAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetMethodNameOverride(ctx))
            .Where(static name => name is not null)
            .Collect();

        var allServices = serviceProvider
            .Collect()
            .Combine(scopedProvider.Collect())
            .Combine(singletonProvider.Collect())
            .Combine(assemblyAttributeProvider)
            .Combine(context.CompilationProvider);

        context.RegisterSourceOutput(allServices, static (spc, source) =>
        {
            var ((((transients, scopeds), singletons), methodNames), compilation) = source;

            var all = transients.AddRange(scopeds).AddRange(singletons);

            if (all.IsEmpty)
                return;

            var methodName = methodNames.IsEmpty
                ? GetDefaultMethodName(compilation.AssemblyName ?? "App")
                : methodNames[0]!;

            var className = methodName.Replace("Add", "") + "ServiceCollectionExtensions";

            var code = GenerateExtensionClass(className, methodName, all);
            spc.AddSource("ServiceCollectionExtensions.g.cs", code);
        });
    }

    private static string? GetMethodNameOverride(GeneratorAttributeSyntaxContext ctx)
    {
        var attr = ctx.Attributes.FirstOrDefault();
        if (attr is null) return null;

        var args = attr.ConstructorArguments;
        if (args.Length > 0 && args[0].Value is string name)
            return name;

        return null;
    }

    private static string GetDefaultMethodName(string assemblyName)
    {
        // "MyApp.Domain" -> "AddMyAppDomainServices"
        var cleaned = assemblyName.Replace(".", "").Replace("-", "").Replace("_", "");
        return "Add" + cleaned + "Services";
    }

    private static ServiceRegistrationInfo? GetServiceInfo(
        GeneratorAttributeSyntaxContext ctx,
        string lifetime,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        if (typeSymbol.IsAbstract || typeSymbol.IsStatic)
            return null;

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        var fullyQualifiedName = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var isOpenGeneric = typeSymbol.IsGenericType;
        string? openGenericArity = isOpenGeneric ? typeSymbol.TypeParameters.Length.ToString() : null;

        // Extract attribute properties
        string? asType = null;
        string? key = null;
        var allowMultiple = false;

        var attr = ctx.Attributes.FirstOrDefault();
        if (attr is not null)
        {
            foreach (var named in attr.NamedArguments)
            {
                switch (named.Key)
                {
                    case "As" when named.Value.Value is INamedTypeSymbol asSymbol:
                        asType = asSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        break;
                    case "Key" when named.Value.Value is string k:
                        key = k;
                        break;
                    case "AllowMultiple" when named.Value.Value is bool am:
                        allowMultiple = am;
                        break;
                }
            }
        }

        // Collect implemented interfaces (excluding filtered ones)
        var interfaces = new List<string>();
        foreach (var iface in typeSymbol.AllInterfaces)
        {
            var fullName = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var metadataName = iface.OriginalDefinition.ToDisplayString();

            if (IsFilteredInterface(metadataName))
                continue;

            interfaces.Add(fullName);
        }

        return new ServiceRegistrationInfo(
            ns,
            typeSymbol.Name,
            fullyQualifiedName,
            lifetime,
            interfaces,
            asType,
            key,
            allowMultiple,
            isOpenGeneric,
            openGenericArity);
    }

    private static bool IsFilteredInterface(string fullyQualifiedName)
    {
        if (FilteredInterfaces.Contains(fullyQualifiedName))
            return true;

        foreach (var prefix in FilteredGenericPrefixes)
        {
            if (fullyQualifiedName.StartsWith(prefix))
                return true;
        }

        return false;
    }

    private static string GenerateExtensionClass(
        string className,
        string methodName,
        ImmutableArray<ServiceRegistrationInfo> services)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection.Extensions;");
        sb.AppendLine();
        sb.AppendLine("namespace Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");
        sb.AppendLine($"    public static IServiceCollection {methodName}(this IServiceCollection services)");
        sb.AppendLine("    {");

        foreach (var svc in services)
        {
            sb.AppendLine();
            sb.AppendLine($"        // {svc.TypeName}");
            GenerateRegistration(sb, svc);
        }

        sb.AppendLine();
        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static void GenerateRegistration(StringBuilder sb, ServiceRegistrationInfo svc)
    {
        var method = svc.AllowMultiple ? "Add" : "TryAdd";
        var lifetime = svc.Lifetime;
        var hasKey = svc.Key is not null;

        if (svc.AsType is not null)
        {
            // Narrowed registration via As property
            if (svc.IsOpenGeneric)
            {
                GenerateOpenGenericRegistration(sb, svc, method, lifetime, svc.AsType);
            }
            else if (hasKey)
            {
                if (svc.AllowMultiple)
                    sb.AppendLine($"        services.AddKeyed{lifetime}<{svc.AsType}, {svc.FullyQualifiedName}>(\"{svc.Key}\");");
                else
                    sb.AppendLine($"        services.TryAddKeyed{lifetime}<{svc.AsType}, {svc.FullyQualifiedName}>(\"{svc.Key}\");");
            }
            else
            {
                sb.AppendLine($"        services.{method}{lifetime}<{svc.AsType}, {svc.FullyQualifiedName}>();");
            }
            return;
        }

        // Register against all interfaces
        foreach (var iface in svc.Interfaces)
        {
            if (svc.IsOpenGeneric)
            {
                GenerateOpenGenericRegistration(sb, svc, method, lifetime, iface);
            }
            else if (hasKey)
            {
                if (svc.AllowMultiple)
                    sb.AppendLine($"        services.AddKeyed{lifetime}<{iface}, {svc.FullyQualifiedName}>(\"{svc.Key}\");");
                else
                    sb.AppendLine($"        services.TryAddKeyed{lifetime}<{iface}, {svc.FullyQualifiedName}>(\"{svc.Key}\");");
            }
            else
            {
                sb.AppendLine($"        services.{method}{lifetime}<{iface}, {svc.FullyQualifiedName}>();");
            }
        }

        // Register concrete type
        if (svc.IsOpenGeneric)
        {
            var openType = GetOpenGenericType(svc.FullyQualifiedName);
            if (svc.AllowMultiple)
                sb.AppendLine($"        services.Add(ServiceDescriptor.{lifetime}(typeof({openType}), typeof({openType})));");
            else
                sb.AppendLine($"        services.TryAdd(ServiceDescriptor.{lifetime}(typeof({openType}), typeof({openType})));");
        }
        else if (hasKey)
        {
            if (svc.AllowMultiple)
                sb.AppendLine($"        services.AddKeyed{lifetime}<{svc.FullyQualifiedName}>(\"{svc.Key}\");");
            else
                sb.AppendLine($"        services.TryAddKeyed{lifetime}<{svc.FullyQualifiedName}>(\"{svc.Key}\");");
        }
        else
        {
            sb.AppendLine($"        services.{method}{lifetime}<{svc.FullyQualifiedName}>();");
        }
    }

    private static void GenerateOpenGenericRegistration(
        StringBuilder sb,
        ServiceRegistrationInfo svc,
        string method,
        string lifetime,
        string interfaceType)
    {
        var openImpl = GetOpenGenericType(svc.FullyQualifiedName);
        var openIface = GetOpenGenericType(interfaceType);

        if (svc.AllowMultiple)
            sb.AppendLine($"        services.Add(ServiceDescriptor.{lifetime}(typeof({openIface}), typeof({openImpl})));");
        else
            sb.AppendLine($"        services.TryAdd(ServiceDescriptor.{lifetime}(typeof({openIface}), typeof({openImpl})));");
    }

    private static string GetOpenGenericType(string fullyQualifiedGenericType)
    {
        // "global::TestApp.Repository<T>" -> "global::TestApp.Repository<>"
        var idx = fullyQualifiedGenericType.IndexOf('<');
        if (idx < 0) return fullyQualifiedGenericType;
        return fullyQualifiedGenericType.Substring(0, idx) + "<>";
    }
}
```

**Step 4: Run tests**

Run: `dotnet test tests/ZeroInject.Tests/ --filter "FullyQualifiedName~BasicRegistrationTests" -v minimal`
Expected: All tests pass.

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ tests/ZeroInject.Tests/
git commit -m "feat: implement core source generator with basic service discovery and registration"
```

---

### Task 5: Keyed Services and AllowMultiple

**Files:**
- Modify: `tests/ZeroInject.Tests/GeneratorTests/BasicRegistrationTests.cs` (add tests)
- Generator already handles this — just need test coverage.

**Step 1: Write failing tests**

Add to `tests/ZeroInject.Tests/GeneratorTests/BasicRegistrationTests.cs`:

```csharp
[Fact]
public void KeyedService_GeneratesKeyedRegistration()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface ICache { }

        [Singleton(Key = "redis")]
        public class RedisCache : ICache { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("TryAddKeyedSingleton<global::TestApp.ICache, global::TestApp.RedisCache>(\"redis\")", output);
    Assert.Contains("TryAddKeyedSingleton<global::TestApp.RedisCache>(\"redis\")", output);
}

[Fact]
public void AllowMultiple_GeneratesAddInsteadOfTryAdd()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IJob { }

        [Transient(AllowMultiple = true)]
        public class MyJob : IJob { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("services.AddTransient<global::TestApp.IJob, global::TestApp.MyJob>()", output);
    Assert.DoesNotContain("TryAdd", output);
}

[Fact]
public void AsProperty_NarrowsRegistration()
{
    var source = """
        using ZeroInject;
        namespace TestApp;

        public interface IFoo { }
        public interface IBar { }

        [Transient(As = typeof(IFoo))]
        public class MyService : IFoo, IBar { }
        """;

    var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

    Assert.Contains("TryAddTransient<global::TestApp.IFoo, global::TestApp.MyService>", output);
    Assert.DoesNotContain("IBar", output);
    Assert.DoesNotContain("TryAddTransient<global::TestApp.MyService>", output);
}
```

**Step 2: Run tests**

Run: `dotnet test tests/ZeroInject.Tests/ --filter "FullyQualifiedName~BasicRegistrationTests" -v minimal`
Expected: All pass (generator already handles these). If any fail, fix the generator.

**Step 3: Commit**

```bash
git add tests/ZeroInject.Tests/
git commit -m "test: add coverage for keyed services, AllowMultiple, and As property"
```

---

### Task 6: Open Generic Support

**Files:**
- Add tests in `tests/ZeroInject.Tests/GeneratorTests/OpenGenericTests.cs`
- May need generator fixes.

**Step 1: Write failing tests**

Create: `tests/ZeroInject.Tests/GeneratorTests/OpenGenericTests.cs`

```csharp
namespace ZeroInject.Tests.GeneratorTests;

public class OpenGenericTests
{
    [Fact]
    public void OpenGeneric_GeneratesServiceDescriptor()
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
    }

    [Fact]
    public void OpenGeneric_WithAs_NarrowsRegistration()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            public interface IReadRepo<T> { }
            public interface IWriteRepo<T> { }

            [Scoped(As = typeof(IReadRepo<>))]
            public class Repo<T> : IReadRepo<T>, IWriteRepo<T> { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("typeof(global::TestApp.IReadRepo<>), typeof(global::TestApp.Repo<>)", output);
        Assert.DoesNotContain("IWriteRepo", output);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test tests/ZeroInject.Tests/ --filter "FullyQualifiedName~OpenGenericTests" -v minimal`
Expected: Pass (or fix generator if needed).

**Step 3: Commit**

```bash
git add tests/ZeroInject.Tests/GeneratorTests/OpenGenericTests.cs
git commit -m "test: add open generic registration tests"
```

---

### Task 7: Assembly-Level Method Name Override

**Files:**
- Add tests in `tests/ZeroInject.Tests/GeneratorTests/MethodNamingTests.cs`

**Step 1: Write tests**

Create: `tests/ZeroInject.Tests/GeneratorTests/MethodNamingTests.cs`

```csharp
namespace ZeroInject.Tests.GeneratorTests;

public class MethodNamingTests
{
    [Fact]
    public void DefaultMethodName_DerivedFromAssemblyName()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Assembly name in test helper is "TestAssembly"
        Assert.Contains("AddTestAssemblyServices", output);
    }

    [Fact]
    public void ZeroInjectAttribute_OverridesMethodName()
    {
        var source = """
            using ZeroInject;

            [assembly: ZeroInject("AddDomainServices")]

            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("AddDomainServices", output);
        Assert.DoesNotContain("AddTestAssemblyServices", output);
    }

    [Fact]
    public void ClassName_DerivedFromMethodName()
    {
        var source = """
            using ZeroInject;

            [assembly: ZeroInject("AddDomainServices")]

            namespace TestApp;

            [Transient]
            public class Svc { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains("public static class DomainServicesServiceCollectionExtensions", output);
    }
}
```

**Step 2: Run tests**

Run: `dotnet test tests/ZeroInject.Tests/ --filter "FullyQualifiedName~MethodNamingTests" -v minimal`
Expected: Pass.

**Step 3: Commit**

```bash
git add tests/ZeroInject.Tests/GeneratorTests/MethodNamingTests.cs
git commit -m "test: add method naming and assembly attribute tests"
```

---

### Task 8: Diagnostics

**Files:**
- Create: `tests/ZeroInject.Tests/GeneratorTests/DiagnosticTests.cs`
- May need to add diagnostic reporting to `ZeroInjectGenerator.cs`.

**Step 1: Write failing tests**

Create: `tests/ZeroInject.Tests/GeneratorTests/DiagnosticTests.cs`

```csharp
using Microsoft.CodeAnalysis;

namespace ZeroInject.Tests.GeneratorTests;

public class DiagnosticTests
{
    [Fact]
    public void AbstractClass_ProducesZI003()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public abstract class AbstractService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        // Abstract classes are silently skipped in the predicate, so no registration.
        // However, we want a diagnostic. This may require updating the generator.
        Assert.DoesNotContain("AbstractService", output);
    }

    [Fact]
    public void NoPublicConstructor_ProducesZI006()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class NoCtorService
            {
                private NoCtorService() { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "ZI006");
    }

    [Fact]
    public void NoInterfaces_ProducesZI007()
    {
        var source = """
            using ZeroInject;
            namespace TestApp;

            [Transient]
            public class PlainService { }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

        Assert.Contains(diagnostics, d => d.Id == "ZI007");
    }
}
```

**Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ZeroInject.Tests/ --filter "FullyQualifiedName~DiagnosticTests" -v minimal`
Expected: FAIL — diagnostics not emitted yet.

**Step 3: Add diagnostic reporting to the generator**

Modify `ZeroInjectGenerator.cs` to add a separate diagnostic pipeline. Register a second source output that receives the collected services and reports diagnostics. Key changes:

- In `GetServiceInfo`, also collect whether the class has a public constructor and whether it has interfaces.
- Add a `bool HasPublicConstructor` and `int InterfaceCount` to `ServiceRegistrationInfo`.
- In the `RegisterSourceOutput`, report ZI006 if no public constructor, ZI007 if no interfaces and no As type.
- For ZI003 (abstract/static), change the predicate to accept all `ClassDeclarationSyntax` and report the diagnostic inside `GetServiceInfo` instead of silently skipping.

Note: This requires extending `ServiceRegistrationInfo` with extra fields and adding `context.ReportDiagnostic(...)` in the source output callback. The exact implementation may vary based on what compiles — adjust as needed.

**Step 4: Run tests**

Run: `dotnet test tests/ZeroInject.Tests/ --filter "FullyQualifiedName~DiagnosticTests" -v minimal`
Expected: All pass.

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ tests/ZeroInject.Tests/GeneratorTests/DiagnosticTests.cs
git commit -m "feat: add compile-time diagnostics ZI003, ZI006, ZI007"
```

---

### Task 9: Sample Application

**Files:**
- Create: `samples/ZeroInject.Sample/Program.cs`
- Create: `samples/ZeroInject.Sample/Services.cs`

**Step 1: Write `Services.cs`**

```csharp
using ZeroInject;

namespace ZeroInject.Sample;

public interface IGreetingService
{
    string Greet(string name);
}

public interface ICache
{
    string Get(string key);
}

[Transient]
public class GreetingService : IGreetingService
{
    public string Greet(string name) => $"Hello, {name}!";
}

[Singleton(Key = "memory")]
public class MemoryCache : ICache
{
    public string Get(string key) => $"cached:{key}";
}

[Scoped]
public class ScopedWorker
{
    public string DoWork() => "Working...";
}
```

**Step 2: Write `Program.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using ZeroInject.Sample;

var services = new ServiceCollection();
services.AddZeroInjectSampleServices();

var provider = services.BuildServiceProvider();

var greeting = provider.GetRequiredService<IGreetingService>();
Console.WriteLine(greeting.Greet("World"));

var worker = provider.GetRequiredService<ScopedWorker>();
Console.WriteLine(worker.DoWork());
```

**Step 3: Build and run**

Run: `dotnet run --project samples/ZeroInject.Sample/`
Expected output:
```
Hello, World!
Working...
```

**Step 4: Commit**

```bash
git add samples/
git commit -m "feat: add ZeroInject sample application"
```

---

### Task 10: Build Verification and Cleanup

**Step 1: Run full build**

Run: `dotnet build ZeroInject.slnx`
Expected: 0 errors, 0 warnings (or only expected analyzer warnings).

**Step 2: Run all tests**

Run: `dotnet test ZeroInject.slnx -v minimal`
Expected: All tests pass.

**Step 3: Run sample**

Run: `dotnet run --project samples/ZeroInject.Sample/`
Expected: Outputs greeting and work messages.

**Step 4: Final commit if any cleanup needed**

```bash
git add -A
git commit -m "chore: final cleanup and build verification"
```
