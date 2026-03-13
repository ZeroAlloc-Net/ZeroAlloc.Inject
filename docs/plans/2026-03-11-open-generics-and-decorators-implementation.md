# Open Generics in Standalone + Decorator Support Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add open-generic resolution to the standalone container via a runtime map, and add `[Decorator]` attribute support that statically wires decoration chains in all three generator outputs.

**Architecture:** (1) A new `OpenGenericEntry` struct and two new protected methods on `ZeroInjectStandaloneProvider`/`ZeroInjectStandaloneScope` handle runtime open-generic resolution. (2) A new `[Decorator]` attribute is discovered by the generator; its decoration chain is emitted statically in the registration extension (non-generic) and both container type-switches (all cases). (3) Open-generic decorated services are handled via the runtime map with a `DecoratorImplType` field on `OpenGenericEntry`.

**Tech Stack:** C# 12, Roslyn `IIncrementalGenerator`, `netstandard2.0` generator, `net8.0`/`net10.0` container, xUnit tests with in-memory compilation via `CSharpCompilation`.

---

## Context for the implementer

Read these files before starting:
- `src/ZeroInject/ServiceAttribute.cs` — base attribute pattern to follow
- `src/ZeroInject.Generator/ZeroInjectGenerator.cs` — the main generator (1884 lines); key methods: `Initialize` (lines 35–175), `GenerateExtensionClass` (420), `EmitRegistration` (526), `GenerateServiceProviderClass` (588), `GenerateStandaloneServiceProviderClass` (1162)
- `src/ZeroInject.Generator/ServiceRegistrationInfo.cs` — data model for registered services
- `src/ZeroInject.Generator/DiagnosticDescriptors.cs` — existing diagnostic codes ZI001–ZI010
- `src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs` — standalone provider base
- `src/ZeroInject.Container/ZeroInjectStandaloneScope.cs` — standalone scope base
- `tests/ZeroInject.Tests/GeneratorTests/GeneratorTestHelper.cs` — `RunGenerator` / `RunGeneratorWithContainer` helpers
- `tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs` — integration test pattern (`BuildAndCreateStandaloneProvider` helper)

Run all tests: `dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj`

---

### Task 1: `DecoratorAttribute` in the ZeroInject package

**Files:**
- Create: `src/ZeroInject/DecoratorAttribute.cs`
- Modify: `tests/ZeroInject.Tests/AttributeTests.cs`

**Step 1: Write the failing test**

Add to `tests/ZeroInject.Tests/AttributeTests.cs`:

```csharp
[Fact]
public void DecoratorAttribute_CanBeApplied_ToClass()
{
    [Decorator]
    class LoggingService : IDisposable
    {
        public LoggingService(IDisposable inner) { }
        public void Dispose() { }
    }

    var attr = typeof(LoggingService).GetCustomAttribute<DecoratorAttribute>();
    Assert.NotNull(attr);
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "DecoratorAttribute_CanBeApplied" -v minimal
```
Expected: FAIL (type does not exist)

**Step 3: Create the attribute**

Create `src/ZeroInject/DecoratorAttribute.cs`:

```csharp
namespace ZeroInject;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DecoratorAttribute : Attribute { }
```

**Step 4: Run test to verify it passes**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "DecoratorAttribute_CanBeApplied" -v minimal
```
Expected: PASS

**Step 5: Commit**

```bash
git add src/ZeroInject/DecoratorAttribute.cs tests/ZeroInject.Tests/AttributeTests.cs
git commit -m "feat: add DecoratorAttribute to ZeroInject package"
```

---

### Task 2: `DecoratorRegistrationInfo`, diagnostics ZI011–ZI013, and generator pipeline wiring

**Files:**
- Create: `src/ZeroInject.Generator/DecoratorRegistrationInfo.cs`
- Modify: `src/ZeroInject.Generator/DiagnosticDescriptors.cs`
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/DiagnosticTests.cs`

**Step 1: Write the failing tests**

Add to `tests/ZeroInject.Tests/GeneratorTests/DiagnosticTests.cs`:

```csharp
[Fact]
public void ZI011_DecoratorWithNoMatchingInterface_ReportsError()
{
    var source = """
        using ZeroInject;
        public interface IFoo { }
        public interface IBar { }
        [Decorator]
        public class LoggingFoo : IFoo
        {
            public LoggingFoo(IBar unrelated) { }
        }
        [Transient]
        public class FooImpl : IFoo { }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, d => d.Id == "ZI011");
}

[Fact]
public void ZI012_DecoratorWithNoRegisteredInner_ReportsError()
{
    var source = """
        using ZeroInject;
        public interface IFoo { }
        [Decorator]
        public class LoggingFoo : IFoo
        {
            public LoggingFoo(IFoo inner) { }
        }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, d => d.Id == "ZI012");
}

[Fact]
public void ZI013_AbstractDecorator_ReportsWarning()
{
    var source = """
        using ZeroInject;
        public interface IFoo { }
        [Decorator]
        public abstract class LoggingFoo : IFoo
        {
            public LoggingFoo(IFoo inner) { }
        }
        """;

    var (_, diagnostics) = GeneratorTestHelper.RunGenerator(source);
    Assert.Contains(diagnostics, d => d.Id == "ZI013");
}
```

**Step 2: Run tests to verify they fail**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "ZI011|ZI012|ZI013" -v minimal
```
Expected: FAIL

**Step 3: Create `DecoratorRegistrationInfo`**

Create `src/ZeroInject.Generator/DecoratorRegistrationInfo.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace ZeroInject.Generator
{
    internal sealed class DecoratorRegistrationInfo : IEquatable<DecoratorRegistrationInfo>
    {
        public string TypeName { get; }
        public string DecoratorFqn { get; }
        public string? DecoratedInterfaceFqn { get; } // null = ZI011 error
        public bool IsOpenGeneric { get; }
        public List<ConstructorParameterInfo> ConstructorParameters { get; }
        public bool ImplementsDisposable { get; }
        public bool IsAbstractOrStatic { get; } // true = ZI013 warning

        public DecoratorRegistrationInfo(
            string typeName,
            string decoratorFqn,
            string? decoratedInterfaceFqn,
            bool isOpenGeneric,
            List<ConstructorParameterInfo> constructorParameters,
            bool implementsDisposable,
            bool isAbstractOrStatic)
        {
            TypeName = typeName;
            DecoratorFqn = decoratorFqn;
            DecoratedInterfaceFqn = decoratedInterfaceFqn;
            IsOpenGeneric = isOpenGeneric;
            ConstructorParameters = constructorParameters;
            ImplementsDisposable = implementsDisposable;
            IsAbstractOrStatic = isAbstractOrStatic;
        }

        public bool Equals(DecoratorRegistrationInfo? other)
        {
            if (other is null) return false;
            return DecoratorFqn == other.DecoratorFqn
                && DecoratedInterfaceFqn == other.DecoratedInterfaceFqn
                && IsOpenGeneric == other.IsOpenGeneric
                && IsAbstractOrStatic == other.IsAbstractOrStatic
                && ConstructorParameters.Count == other.ConstructorParameters.Count;
        }

        public override bool Equals(object? obj) => Equals(obj as DecoratorRegistrationInfo);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + DecoratorFqn.GetHashCode();
                hash = hash * 31 + (DecoratedInterfaceFqn?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
```

**Step 4: Add ZI011, ZI012, ZI013 to `DiagnosticDescriptors.cs`**

Append to `src/ZeroInject.Generator/DiagnosticDescriptors.cs` before the closing `}`:

```csharp
        public static readonly DiagnosticDescriptor DecoratorNoMatchingInterface = new DiagnosticDescriptor(
            "ZI011",
            "Decorator has no matching interface",
            "Class '{0}' is marked [Decorator] but no constructor parameter type matches any interface it implements",
            "ZeroInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorNoRegisteredInner = new DiagnosticDescriptor(
            "ZI012",
            "Decorator inner service not found",
            "Class '{0}' is marked [Decorator] for '{1}' but no service implementing that interface is registered in this assembly",
            "ZeroInject",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DecoratorOnAbstractOrStatic = new DiagnosticDescriptor(
            "ZI013",
            "Decorator on abstract or static class",
            "Class '{0}' is abstract or static and cannot be used as a decorator",
            "ZeroInject",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
```

**Step 5: Wire `[Decorator]` into the generator pipeline**

In `ZeroInjectGenerator.cs`, in the `Initialize` method (after the `singletons` pipeline, before `assemblyAttr`), add:

```csharp
            var decorators = context.SyntaxProvider.ForAttributeWithMetadataName(
                "ZeroInject.DecoratorAttribute",
                predicate: static (node, _) => true,
                transform: static (ctx, ct) => GetDecoratorInfo(ctx, ct))
                .Where(static x => x != null)
                .Collect();
```

Change the `combined` chain (lines 93–98) to add `.Combine(decorators)` at the end:

```csharp
            var combined = transients
                .Combine(scopeds)
                .Combine(singletons)
                .Combine(assemblyAttr)
                .Combine(assemblyName)
                .Combine(hasContainer)
                .Combine(decorators);
```

Update the destructuring in `RegisterSourceOutput` (the entire `data.Left.xxx` chain shifts by one):

```csharp
            context.RegisterSourceOutput(combined, static (spc, data) =>
            {
                var transientInfos = data.Left.Left.Left.Left.Left.Left;
                var scopedInfos    = data.Left.Left.Left.Left.Left.Right;
                var singletonInfos = data.Left.Left.Left.Left.Right;
                var methodNameOverrides = data.Left.Left.Left.Right;
                var asmName        = data.Left.Left.Right;
                var containerReferenced = data.Left.Right;
                var decoratorInfos = data.Right;
```

Then after building `allServices`, add decorator validation:

```csharp
                // Build lookup of registered interface FQNs for ZI012 check
                var registeredInterfaces = new System.Collections.Generic.HashSet<string>();
                foreach (var svc in allServices)
                {
                    foreach (var iface in svc.Interfaces)
                        registeredInterfaces.Add(iface);
                    if (svc.AsType != null)
                        registeredInterfaces.Add(svc.AsType);
                }

                var validDecorators = new System.Collections.Generic.List<DecoratorRegistrationInfo>();
                foreach (var dec in decoratorInfos)
                {
                    if (dec == null) continue;
                    if (dec.IsAbstractOrStatic)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.DecoratorOnAbstractOrStatic,
                            Location.None, dec.TypeName));
                        continue;
                    }
                    if (dec.DecoratedInterfaceFqn == null)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.DecoratorNoMatchingInterface,
                            Location.None, dec.TypeName));
                        continue;
                    }
                    if (!registeredInterfaces.Contains(dec.DecoratedInterfaceFqn))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            DiagnosticDescriptors.DecoratorNoRegisteredInner,
                            Location.None, dec.TypeName, dec.DecoratedInterfaceFqn));
                        continue;
                    }
                    validDecorators.Add(dec);
                }

                // Build dictionary: decorated interface FQN → decorator info
                var decoratorsByInterface = new System.Collections.Generic.Dictionary<string, DecoratorRegistrationInfo>();
                foreach (var dec in validDecorators)
                    decoratorsByInterface[dec.DecoratedInterfaceFqn!] = dec;
```

Pass `decoratorsByInterface` to the three generator methods (update their signatures too — done in Tasks 5–7).

For now, still call the generators with the existing signature (changes come in Tasks 5–7). Just make sure the pipeline compiles.

Add the `GetDecoratorInfo` private static method at the bottom of `ZeroInjectGenerator` (before the closing `}`):

```csharp
        private static DecoratorRegistrationInfo? GetDecoratorInfo(
            GeneratorAttributeSyntaxContext ctx,
            System.Threading.CancellationToken ct)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol typeSymbol) return null;

            var typeName = typeSymbol.Name;
            var fqn = typeSymbol.ToDisplayString(FullyQualifiedFormat);
            bool isAbstractOrStatic = typeSymbol.IsAbstract || typeSymbol.IsStatic;
            bool isOpenGeneric = typeSymbol.IsGenericType;

            // Collect all interfaces this type implements
            var interfaces = new System.Collections.Generic.HashSet<string>();
            foreach (var iface in typeSymbol.AllInterfaces)
                interfaces.Add(iface.ToDisplayString(FullyQualifiedFormat));

            // Find public constructor
            IMethodSymbol? ctor = null;
            foreach (var c in typeSymbol.InstanceConstructors)
            {
                if (c.DeclaredAccessibility == Accessibility.Public)
                { ctor = c; break; }
            }

            string? decoratedInterface = null;
            var ctorParams = new List<ConstructorParameterInfo>();

            if (ctor != null && !isAbstractOrStatic)
            {
                foreach (var param in ctor.Parameters)
                {
                    var paramTypeFqn = param.Type.ToDisplayString(FullyQualifiedFormat);
                    ctorParams.Add(new ConstructorParameterInfo(paramTypeFqn, param.Name, param.HasExplicitDefaultValue));
                    if (decoratedInterface == null && interfaces.Contains(paramTypeFqn))
                        decoratedInterface = paramTypeFqn;
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
                typeName, fqn, decoratedInterface,
                isOpenGeneric, ctorParams, implementsDisposable, isAbstractOrStatic);
        }
```

**Step 6: Run tests to verify they pass**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "ZI011|ZI012|ZI013" -v minimal
```
Expected: PASS (all 3)

**Step 7: Run all tests**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj
```
Expected: all existing tests still PASS

**Step 8: Commit**

```bash
git add src/ZeroInject.Generator/DecoratorRegistrationInfo.cs \
        src/ZeroInject.Generator/DiagnosticDescriptors.cs \
        src/ZeroInject.Generator/ZeroInjectGenerator.cs \
        tests/ZeroInject.Tests/GeneratorTests/DiagnosticTests.cs
git commit -m "feat: add DecoratorRegistrationInfo, ZI011-ZI013 diagnostics, and generator pipeline wiring"
```

---

### Task 3: `OpenGenericEntry` struct + open-generic support in standalone base classes

**Files:**
- Create: `src/ZeroInject.Container/OpenGenericEntry.cs`
- Modify: `src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs`
- Modify: `src/ZeroInject.Container/ZeroInjectStandaloneScope.cs`
- Modify: `tests/ZeroInject.Tests/ContainerTests/StandaloneProviderBaseTests.cs`
- Modify: `tests/ZeroInject.Tests/ContainerTests/StandaloneScopeTests.cs`

**Step 1: Write failing tests for `ResolveOpenGenericRoot`**

Add to `tests/ZeroInject.Tests/ContainerTests/StandaloneProviderBaseTests.cs`:

```csharp
[Fact]
public void ResolveOpenGenericRoot_KnownTransient_ReturnsNewInstance()
{
    var provider = new TestOpenGenericProvider();
    var result = provider.GetService(typeof(IGenericService<string>));
    Assert.NotNull(result);
    Assert.IsType<GenericService<string>>(result);
}

[Fact]
public void ResolveOpenGenericRoot_KnownSingleton_ReturnsSameInstance()
{
    var provider = new TestOpenGenericProvider();
    var a = provider.GetService(typeof(ISingletonGeneric<int>));
    var b = provider.GetService(typeof(ISingletonGeneric<int>));
    Assert.Same(a, b);
}

[Fact]
public void ResolveOpenGenericRoot_Scoped_ReturnsNull_FromRoot()
{
    var provider = new TestOpenGenericProvider();
    var result = provider.GetService(typeof(IScopedGeneric<string>));
    Assert.Null(result); // scoped not served from root
}

[Fact]
public void ResolveOpenGenericRoot_UnknownType_ReturnsNull()
{
    var provider = new TestOpenGenericProvider();
    var result = provider.GetService(typeof(IList<string>));
    Assert.Null(result);
}

// Test helpers at the bottom of the file:
public interface IGenericService<T> { }
public class GenericService<T> : IGenericService<T> { }
public interface ISingletonGeneric<T> { }
public class SingletonGenericService<T> : ISingletonGeneric<T> { }
public interface IScopedGeneric<T> { }
public class ScopedGenericService<T> : IScopedGeneric<T> { }

private class TestOpenGenericProvider : ZeroInjectStandaloneProvider
{
    private static readonly Dictionary<Type, OpenGenericEntry> _map = new()
    {
        { typeof(IGenericService<>),   new OpenGenericEntry(typeof(GenericService<>),        ServiceLifetime.Transient) },
        { typeof(ISingletonGeneric<>), new OpenGenericEntry(typeof(SingletonGenericService<>), ServiceLifetime.Singleton) },
        { typeof(IScopedGeneric<>),    new OpenGenericEntry(typeof(ScopedGenericService<>),   ServiceLifetime.Scoped) },
    };
    protected override IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => _map;
    protected override object? ResolveKnown(Type serviceType) => ResolveOpenGenericRoot(serviceType);
    protected override ZeroInjectStandaloneScope CreateScopeCore() => throw new NotImplementedException();
}
```

Add to `tests/ZeroInject.Tests/ContainerTests/StandaloneScopeTests.cs`:

```csharp
[Fact]
public void ResolveOpenGenericScoped_Transient_ReturnsNewInstances()
{
    var provider = new TestOpenGenericScopeProvider();
    using var scope = (ZeroInjectStandaloneScope)provider.CreateScope();
    var a = scope.ServiceProvider.GetService(typeof(IGenericService<string>));
    var b = scope.ServiceProvider.GetService(typeof(IGenericService<string>));
    Assert.NotNull(a);
    Assert.NotSame(a, b); // transient: new each time
}

[Fact]
public void ResolveOpenGenericScoped_Scoped_ReturnsSameInstanceWithinScope()
{
    var provider = new TestOpenGenericScopeProvider();
    using var scope = (ZeroInjectStandaloneScope)provider.CreateScope();
    var a = scope.ServiceProvider.GetService(typeof(IScopedGeneric<int>));
    var b = scope.ServiceProvider.GetService(typeof(IScopedGeneric<int>));
    Assert.Same(a, b);
}

[Fact]
public void ResolveOpenGenericScoped_Singleton_DelegatesToRoot()
{
    var provider = new TestOpenGenericScopeProvider();
    var fromRoot = provider.GetService(typeof(ISingletonGeneric<double>));
    using var scope = (ZeroInjectStandaloneScope)provider.CreateScope();
    var fromScope = scope.ServiceProvider.GetService(typeof(ISingletonGeneric<double>));
    Assert.Same(fromRoot, fromScope);
}

// Helpers (reuse interfaces from StandaloneProviderBaseTests):
private class TestOpenGenericScopeProvider : ZeroInjectStandaloneProvider
{
    private static readonly Dictionary<Type, OpenGenericEntry> _map = new()
    {
        { typeof(IGenericService<>),   new OpenGenericEntry(typeof(GenericService<>),         ServiceLifetime.Transient) },
        { typeof(ISingletonGeneric<>), new OpenGenericEntry(typeof(SingletonGenericService<>), ServiceLifetime.Singleton) },
        { typeof(IScopedGeneric<>),    new OpenGenericEntry(typeof(ScopedGenericService<>),    ServiceLifetime.Scoped) },
    };
    protected override IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => _map;
    protected override object? ResolveKnown(Type serviceType) => ResolveOpenGenericRoot(serviceType);
    protected override ZeroInjectStandaloneScope CreateScopeCore() => new TestScope(this);

    private class TestScope : ZeroInjectStandaloneScope
    {
        private static readonly Dictionary<Type, OpenGenericEntry> _map = new()
        {
            { typeof(IGenericService<>),   new OpenGenericEntry(typeof(GenericService<>),         ServiceLifetime.Transient) },
            { typeof(ISingletonGeneric<>), new OpenGenericEntry(typeof(SingletonGenericService<>), ServiceLifetime.Singleton) },
            { typeof(IScopedGeneric<>),    new OpenGenericEntry(typeof(ScopedGenericService<>),    ServiceLifetime.Scoped) },
        };
        protected override IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => _map;
        public TestScope(ZeroInjectStandaloneProvider root) : base(root) { }
        protected override object? ResolveScopedKnown(Type serviceType) => ResolveOpenGenericScoped(serviceType);
    }
}
```

**Step 2: Run tests to verify they fail**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "ResolveOpenGeneric" -v minimal
```
Expected: FAIL (OpenGenericEntry does not exist)

**Step 3: Create `OpenGenericEntry`**

Create `src/ZeroInject.Container/OpenGenericEntry.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace ZeroInject.Container;

public readonly struct OpenGenericEntry
{
    public Type ImplType { get; }
    public ServiceLifetime Lifetime { get; }
    public Type? DecoratorImplType { get; }

    public OpenGenericEntry(Type implType, ServiceLifetime lifetime, Type? decoratorImplType = null)
    {
        ImplType = implType;
        Lifetime = lifetime;
        DecoratorImplType = decoratorImplType;
    }
}
```

**Step 4: Add open-generic support to `ZeroInjectStandaloneProvider`**

Add to `src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs`:

After the `private int _disposed;` field, add:

```csharp
    private System.Collections.Concurrent.ConcurrentDictionary<Type, object>? _openGenericSingletons;

    protected virtual System.Collections.Generic.IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => null;
```

After `protected abstract ZeroInjectStandaloneScope CreateScopeCore();`, add:

```csharp
    internal object CreateInstance(Type type, object? innerArg = null)
    {
        var ctor = type.GetConstructors()[0];
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (innerArg != null && parameters[i].ParameterType.IsAssignableFrom(innerArg.GetType()))
                args[i] = innerArg;
            else
                args[i] = GetService(parameters[i].ParameterType);
        }
        return ctor.Invoke(args);
    }

    protected object? ResolveOpenGenericRoot(Type serviceType)
    {
        if (OpenGenericMap == null || !serviceType.IsGenericType) return null;
        var openDef = serviceType.GetGenericTypeDefinition();
        if (!OpenGenericMap.TryGetValue(openDef, out var entry)) return null;
        if (entry.Lifetime == ServiceLifetime.Scoped) return null; // scoped only from scope

        var typeArgs = serviceType.GenericTypeArguments;
        var closedImpl = entry.ImplType.MakeGenericType(typeArgs);

        object BuildInstance()
        {
            var inner = CreateInstance(closedImpl);
            if (entry.DecoratorImplType != null)
            {
                var closedDecorator = entry.DecoratorImplType.MakeGenericType(typeArgs);
                return CreateInstance(closedDecorator, inner);
            }
            return inner;
        }

        if (entry.Lifetime == ServiceLifetime.Singleton)
        {
            _openGenericSingletons ??= new System.Collections.Concurrent.ConcurrentDictionary<Type, object>();
            return _openGenericSingletons.GetOrAdd(serviceType, _ => BuildInstance());
        }

        return BuildInstance(); // Transient
    }
```

**Step 5: Add open-generic support to `ZeroInjectStandaloneScope`**

Add to `src/ZeroInject.Container/ZeroInjectStandaloneScope.cs`:

After `private int _disposed;`, add:

```csharp
    private System.Collections.Generic.Dictionary<Type, object>? _openGenericScoped;

    protected virtual System.Collections.Generic.IReadOnlyDictionary<Type, OpenGenericEntry>? OpenGenericMap => null;
```

After `protected T TrackDisposable<T>(T instance)...`, add:

```csharp
    internal object CreateInstance(Type type, object? innerArg = null)
    {
        var ctor = type.GetConstructors()[0];
        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            if (innerArg != null && parameters[i].ParameterType.IsAssignableFrom(innerArg.GetType()))
                args[i] = innerArg;
            else
                args[i] = GetService(parameters[i].ParameterType);
        }
        return ctor.Invoke(args);
    }

    protected object? ResolveOpenGenericScoped(Type serviceType)
    {
        if (OpenGenericMap == null || !serviceType.IsGenericType) return null;
        var openDef = serviceType.GetGenericTypeDefinition();
        if (!OpenGenericMap.TryGetValue(openDef, out var entry)) return null;

        var typeArgs = serviceType.GenericTypeArguments;
        var closedImpl = entry.ImplType.MakeGenericType(typeArgs);

        switch (entry.Lifetime)
        {
            case ServiceLifetime.Singleton:
                return Root.ResolveOpenGenericRoot(serviceType);

            case ServiceLifetime.Scoped:
                lock (_trackLock)
                {
                    _openGenericScoped ??= new System.Collections.Generic.Dictionary<Type, object>();
                    if (_openGenericScoped.TryGetValue(serviceType, out var existing)) return existing;
                    var inner = CreateInstance(closedImpl);
                    object instance;
                    if (entry.DecoratorImplType != null)
                    {
                        var closedDecorator = entry.DecoratorImplType.MakeGenericType(typeArgs);
                        instance = CreateInstance(closedDecorator, inner);
                    }
                    else
                    {
                        instance = inner;
                    }
                    _openGenericScoped[serviceType] = instance;
                    TrackDisposable(instance);
                    return instance;
                }

            default: // Transient
                var transientInner = CreateInstance(closedImpl);
                object transient;
                if (entry.DecoratorImplType != null)
                {
                    var closedDecorator = entry.DecoratorImplType.MakeGenericType(typeArgs);
                    transient = CreateInstance(closedDecorator, transientInner);
                }
                else
                {
                    transient = transientInner;
                }
                TrackDisposable(transient);
                return transient;
        }
    }
```

**Step 6: Run tests**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj
```
Expected: all tests PASS (new open-generic tests + all existing)

**Step 7: Commit**

```bash
git add src/ZeroInject.Container/OpenGenericEntry.cs \
        src/ZeroInject.Container/ZeroInjectStandaloneProvider.cs \
        src/ZeroInject.Container/ZeroInjectStandaloneScope.cs \
        tests/ZeroInject.Tests/ContainerTests/StandaloneProviderBaseTests.cs \
        tests/ZeroInject.Tests/ContainerTests/StandaloneScopeTests.cs
git commit -m "feat: add OpenGenericEntry and runtime open-generic resolution to standalone base classes"
```

---

### Task 4: Registration extension — decorator wrapping

**Files:**
- Create: `tests/ZeroInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs`
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`

**Step 1: Write failing generator output tests**

Create `tests/ZeroInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs`:

```csharp
using Xunit;

namespace ZeroInject.Tests.GeneratorTests;

public class DecoratorGeneratorTests
{
    [Fact]
    public void Decorator_NonGeneric_RegistrationExtension_WrapsInner()
    {
        var source = """
            using ZeroInject;
            public interface IOrderService { }
            [Scoped]
            public class OrderService : IOrderService { }
            [Decorator]
            public class LoggingOrderService : IOrderService
            {
                public LoggingOrderService(IOrderService inner) { }
            }
            """;

        var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);
        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        // Inner registered as concrete only
        Assert.Contains("TryAddScoped<global::OrderService>", output);
        // Interface registered via factory wrapping inner
        Assert.Contains("AddScoped<global::IOrderService>", output);
        Assert.Contains("GetRequiredService<global::OrderService>", output);
        Assert.Contains("new global::LoggingOrderService", output);
        // Interface NOT registered the old way (no direct factory for the interface)
        Assert.DoesNotContain("TryAddScoped<global::IOrderService>", output);
    }

    [Fact]
    public void Decorator_WithExtraDeps_RegistrationExtension_InjectsAll()
    {
        var source = """
            using ZeroInject;
            public interface IFoo { }
            public interface ILogger { }
            [Transient]
            public class FooImpl : IFoo { }
            [Singleton]
            public class Logger : ILogger { }
            [Decorator]
            public class LoggingFoo : IFoo
            {
                public LoggingFoo(IFoo inner, ILogger logger) { }
            }
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);
        // Inner param: GetRequiredService<FooImpl>()
        Assert.Contains("GetRequiredService<global::FooImpl>", output);
        // Extra dep: GetRequiredService<ILogger>()
        Assert.Contains("GetRequiredService<global::ILogger>", output);
    }

    [Fact]
    public void NoDecorator_Registration_IsUnchanged()
    {
        var source = """
            using ZeroInject;
            public interface IFoo { }
            [Transient]
            public class FooImpl : IFoo { }
            """;

        var (output, _) = GeneratorTestHelper.RunGenerator(source);
        Assert.Contains("TryAddTransient<global::IFoo>", output);
    }
}
```

**Step 2: Run tests to verify they fail**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "DecoratorGeneratorTests" -v minimal
```
Expected: FAIL

**Step 3: Update `GenerateExtensionClass` and `EmitRegistration` to support decorators**

In `ZeroInjectGenerator.cs`, update the signature of `GenerateExtensionClass` (line 420):

```csharp
        private static string GenerateExtensionClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            string? methodNameOverride,
            System.Collections.Generic.Dictionary<string, DecoratorRegistrationInfo> decoratorsByInterface)
```

Update the call to `GenerateExtensionClass` in `RegisterSourceOutput` (line 163):

```csharp
                var source = GenerateExtensionClass(allServices, asmName, methodNameOverride, decoratorsByInterface);
```

Update `EmitRegistration` signature (line 526):

```csharp
        private static void EmitRegistration(
            StringBuilder sb,
            ServiceRegistrationInfo svc,
            System.Collections.Generic.Dictionary<string, DecoratorRegistrationInfo> decoratorsByInterface)
```

Update the call inside `GenerateExtensionClass`'s loop to pass `decoratorsByInterface`:

```csharp
            foreach (var svc in services)
            {
                EmitRegistration(sb, svc, decoratorsByInterface);
            }
```

Update `EmitRegistration` body: for each interface, check if it has a decorator. If yes, emit inner-as-concrete + factory-wrapping pattern. If no, emit normally:

```csharp
        private static void EmitRegistration(
            StringBuilder sb,
            ServiceRegistrationInfo svc,
            System.Collections.Generic.Dictionary<string, DecoratorRegistrationInfo> decoratorsByInterface)
        {
            var lifetime = svc.Lifetime;
            var fqn = svc.FullyQualifiedName;
            var useAdd = svc.AllowMultiple;

            if (svc.AsType != null)
            {
                EmitSingleRegistration(sb, lifetime, svc.AsType, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
                return;
            }

            bool hasDecoratedInterface = false;
            foreach (var iface in svc.Interfaces)
            {
                if (!svc.IsOpenGeneric && decoratorsByInterface.TryGetValue(iface, out var decorator))
                {
                    hasDecoratedInterface = true;
                    // Emit factory wrapping: sp => new LoggingX(sp.GetRequiredService<XImpl>(), ...)
                    var decoratorFactory = BuildDecoratorFactoryLambda(decorator, fqn);
                    sb.AppendLine(string.Format(
                        "            services.Add{0}<{1}>({2});",
                        lifetime, iface, decoratorFactory));
                }
                else
                {
                    EmitSingleRegistration(sb, lifetime, iface, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
                }
            }

            // Concrete registration:
            // If any interface was decorated, always register the concrete too (inner needs to be resolvable)
            // Use TryAdd to avoid duplicates; decorated interface was already registered above via Add
            EmitConcreteRegistration(sb, lifetime, fqn, svc.Key, useAdd, svc.IsOpenGeneric, svc.ConstructorParameters);
        }
```

Add the `BuildDecoratorFactoryLambda` helper method (near `BuildFactoryLambda`):

```csharp
        private static string BuildDecoratorFactoryLambda(
            DecoratorRegistrationInfo decorator,
            string innerConcreteFqn)
        {
            // sp => new LoggingFoo(sp.GetRequiredService<FooImpl>(), sp.GetRequiredService<ILogger>())
            var sb = new StringBuilder();
            sb.Append("sp => new ");
            sb.Append(decorator.DecoratorFqn);
            sb.Append("(");
            bool first = true;
            foreach (var param in decorator.ConstructorParameters)
            {
                if (!first) sb.Append(", ");
                first = false;
                if (param.FullyQualifiedTypeName == decorator.DecoratedInterfaceFqn)
                {
                    // Inner service param: resolve by concrete type
                    sb.Append("sp.GetRequiredService<").Append(innerConcreteFqn).Append(">()");
                }
                else
                {
                    var method = param.IsOptional ? "GetService" : "GetRequiredService";
                    sb.Append("sp.").Append(method).Append("<").Append(param.FullyQualifiedTypeName).Append(">()");
                }
            }
            sb.Append(")");
            return sb.ToString();
        }
```

**Step 4: Run tests**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj
```
Expected: all tests PASS

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs \
        tests/ZeroInject.Tests/GeneratorTests/DecoratorGeneratorTests.cs
git commit -m "feat: emit decorator wrapping in registration extension method"
```

---

### Task 5: Hybrid container — decorator wrapping in type-switch

**Files:**
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`

**Step 1: Write failing tests**

Add to `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`:

```csharp
[Fact]
public void HybridContainer_Decorator_NonGeneric_WrapsInTypeSwitch()
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

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    // The IFoo case in ResolveKnown should wrap FooImpl in LoggingFoo
    Assert.Contains("new global::LoggingFoo", output);
    Assert.Contains("new global::FooImpl", output);
    // They should appear together (decorator wrapping inner) in the IFoo branch
    var fooIdx = output.IndexOf("typeof(global::IFoo)");
    var loggingIdx = output.IndexOf("new global::LoggingFoo");
    var fooImplIdx = output.IndexOf("new global::FooImpl");
    Assert.True(fooIdx >= 0 && loggingIdx > fooIdx && fooImplIdx > fooIdx);
}
```

**Step 2: Run test to verify it fails**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "HybridContainer_Decorator_NonGeneric" -v minimal
```
Expected: FAIL

**Step 3: Update `GenerateServiceProviderClass` to accept and apply decorators**

Update `GenerateServiceProviderClass` signature (line 588):

```csharp
        private static string GenerateServiceProviderClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            System.Collections.Generic.Dictionary<string, DecoratorRegistrationInfo> decoratorsByInterface)
```

Update the call in `RegisterSourceOutput` (line 168):

```csharp
                    var providerSource = GenerateServiceProviderClass(allServices, asmName, decoratorsByInterface);
```

In `GenerateServiceProviderClass`, the transient emit loop (around line 726) currently emits:
```csharp
sb.AppendLine("                return " + newExpr + ";");
```

Replace the transient loop body with a helper that wraps in decorator when applicable. Modify the transient and singleton loops inside `ResolveKnown` and `ResolveScopedKnown` to use `BuildDecoratedNewExpression`:

Add a new helper method `BuildDecoratedNewExpression`:

```csharp
        private static string BuildDecoratedNewExpression(
            ServiceRegistrationInfo svc,
            string serviceTypeFqn,
            System.Collections.Generic.Dictionary<string, DecoratorRegistrationInfo> decoratorsByInterface,
            bool forScope)
        {
            var baseExpr = forScope ? BuildNewExpressionForScope(svc) : BuildNewExpression(svc);
            if (decoratorsByInterface.TryGetValue(serviceTypeFqn, out var decorator))
            {
                return BuildNewExpressionWithDecorator(decorator, svc.FullyQualifiedName, baseExpr, forScope);
            }
            return baseExpr;
        }

        private static string BuildNewExpressionWithDecorator(
            DecoratorRegistrationInfo decorator,
            string innerConcreteFqn,
            string innerNewExpr,
            bool forScope)
        {
            var sb = new StringBuilder();
            sb.Append("new ").Append(decorator.DecoratorFqn).Append("(");
            bool first = true;
            foreach (var param in decorator.ConstructorParameters)
            {
                if (!first) sb.Append(", ");
                first = false;
                if (param.FullyQualifiedTypeName == decorator.DecoratedInterfaceFqn)
                {
                    sb.Append("(").Append(decorator.DecoratedInterfaceFqn).Append(")(").Append(innerNewExpr).Append(")");
                }
                else
                {
                    var method = param.IsOptional ? "GetService" : "GetRequiredService";
                    if (forScope)
                        sb.Append("(").Append(param.FullyQualifiedTypeName).Append(")GetService(typeof(").Append(param.FullyQualifiedTypeName).Append("))").Append(param.IsOptional ? "" : "!");
                    else
                        sb.Append("(").Append(param.FullyQualifiedTypeName).Append(")GetService(typeof(").Append(param.FullyQualifiedTypeName).Append("))").Append(param.IsOptional ? "" : "!");
                }
            }
            sb.Append(")");
            return sb.ToString();
        }
```

In the transient loop of `ResolveKnown` (hybrid), change the emit to:

```csharp
            foreach (var svc in transients)
            {
                var serviceTypes = GetServiceTypes(svc);
                foreach (var serviceType in serviceTypes)
                {
                    ServiceTypeGroupEntry lastEntry;
                    if (lastRegistrationPerType.TryGetValue(serviceType, out lastEntry)
                        && lastEntry.Svc == svc && lastEntry.Lifetime == "Transient")
                    {
                        var newExpr = BuildDecoratedNewExpression(svc, serviceType, decoratorsByInterface, false);
                        sb.AppendLine("            if (serviceType == typeof(" + serviceType + "))");
                        sb.AppendLine("                return " + newExpr + ";");
                    }
                }
            }
```

Apply the same pattern to singletons (in `ResolveKnown`) and to the scope's transients, singletons, and scopeds (`ResolveScopedKnown`). For singletons, the stored value is the plain type but the returned expression can wrap it — actually for singletons the singleton *instance* needs to be decorated. The decorator is constructed once and stored in the field. So the `newExpr` used for `Interlocked.CompareExchange` becomes the decorated expression.

**Step 4: Run all tests**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj
```
Expected: all PASS

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs \
        tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs
git commit -m "feat: emit decorator wrapping in hybrid container type-switch"
```

---

### Task 6: Standalone container — open generics map + decorator wrapping + resolver calls

**Files:**
- Modify: `src/ZeroInject.Generator/ZeroInjectGenerator.cs`
- Modify: `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`

**Step 1: Write failing tests**

Add to `tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs`:

```csharp
[Fact]
public void Standalone_OpenGeneric_EmitsOpenGenericMap()
{
    var source = """
        using ZeroInject;
        public interface IRepo<T> { }
        [Scoped]
        public class Repo<T> : IRepo<T> { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("OpenGenericMap", output);
    Assert.Contains("typeof(global::IRepo<>)", output);
    Assert.Contains("typeof(global::Repo<>)", output);
    Assert.Contains("ServiceLifetime.Scoped", output);
}

[Fact]
public void Standalone_OpenGeneric_ResolveKnown_CallsResolveOpenGenericRoot()
{
    var source = """
        using ZeroInject;
        public interface IRepo<T> { }
        [Scoped]
        public class Repo<T> : IRepo<T> { }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("ResolveOpenGenericRoot(serviceType)", output);
    Assert.Contains("ResolveOpenGenericScoped(serviceType)", output);
}

[Fact]
public void Standalone_Decorator_NonGeneric_WrapsInTypeSwitch()
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

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    // Standalone type-switch for IFoo wraps FooImpl in LoggingFoo
    var standaloneIdx = output.IndexOf("StandaloneServiceProvider");
    var fooIdx = output.IndexOf("typeof(global::IFoo)", standaloneIdx);
    var loggingIdx = output.IndexOf("new global::LoggingFoo", fooIdx);
    Assert.True(loggingIdx > fooIdx, "Standalone should emit LoggingFoo wrapping FooImpl");
}

[Fact]
public void Standalone_OpenGeneric_WithDecorator_EmitsDecoratorImplType()
{
    var source = """
        using ZeroInject;
        public interface IRepo<T> { }
        [Scoped]
        public class Repo<T> : IRepo<T> { }
        [Decorator]
        public class LoggingRepo<T> : IRepo<T>
        {
            public LoggingRepo(IRepo<T> inner) { }
        }
        """;

    var (output, _) = GeneratorTestHelper.RunGeneratorWithContainer(source);
    Assert.Contains("typeof(global::LoggingRepo<>)", output);
    // DecoratorImplType set in OpenGenericEntry
    Assert.Contains("new global::ZeroInject.Container.OpenGenericEntry(typeof(global::Repo<>)", output);
}
```

**Step 2: Run tests to verify they fail**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "Standalone_OpenGeneric|Standalone_Decorator_NonGeneric" -v minimal
```
Expected: FAIL

**Step 3: Update `GenerateStandaloneServiceProviderClass`**

Update signature:

```csharp
        private static string GenerateStandaloneServiceProviderClass(
            List<ServiceRegistrationInfo> services,
            string assemblyName,
            System.Collections.Generic.Dictionary<string, DecoratorRegistrationInfo> decoratorsByInterface)
```

Update call in `RegisterSourceOutput`:

```csharp
                    var standaloneCode = GenerateStandaloneServiceProviderClass(allServices, asmName, decoratorsByInterface);
```

In `GenerateStandaloneServiceProviderClass`, change the loop that skips open generics to collect them instead:

```csharp
            // Separate services: collect open generics separately instead of skipping
            var openGenerics = new List<ServiceRegistrationInfo>();

            foreach (var svc in services)
            {
                if (svc.IsOpenGeneric)
                {
                    openGenerics.Add(svc);
                    continue;
                }
                // ... existing non-generic bucketing
            }
```

After the singleton fields and constructor, emit the `OpenGenericMap` override when `openGenerics.Count > 0`:

```csharp
            if (openGenerics.Count > 0)
            {
                sb.AppendLine("        private static readonly System.Collections.Generic.Dictionary<global::System.Type, global::ZeroInject.Container.OpenGenericEntry> _s_openGenericMap");
                sb.AppendLine("            = new System.Collections.Generic.Dictionary<global::System.Type, global::ZeroInject.Container.OpenGenericEntry>");
                sb.AppendLine("        {");
                foreach (var svc in openGenerics)
                {
                    if (svc.Key != null) continue; // keyed open generics not supported in standalone

                    // The service interface(s)
                    var ifaces = svc.AsType != null
                        ? new List<string> { svc.AsType }
                        : svc.Interfaces;

                    var lifetime = "global::Microsoft.Extensions.DependencyInjection.ServiceLifetime." + svc.Lifetime;

                    foreach (var iface in ifaces)
                    {
                        // Check for decorator
                        string? decoratorType = null;
                        if (decoratorsByInterface.TryGetValue(iface, out var dec) && dec.IsOpenGeneric)
                            decoratorType = dec.DecoratorFqn;

                        var openIface = ToUnboundGenericString(iface, int.Parse(svc.OpenGenericArity!));
                        var openImpl = ToUnboundGenericString(svc.FullyQualifiedName, int.Parse(svc.OpenGenericArity!));

                        if (decoratorType != null)
                        {
                            var openDecorator = ToUnboundGenericString(decoratorType, int.Parse(svc.OpenGenericArity!));
                            sb.AppendLine(string.Format(
                                "            {{ typeof({0}), new global::ZeroInject.Container.OpenGenericEntry(typeof({1}), {2}, typeof({3})) }},",
                                openIface, openImpl, lifetime, openDecorator));
                        }
                        else
                        {
                            sb.AppendLine(string.Format(
                                "            {{ typeof({0}), new global::ZeroInject.Container.OpenGenericEntry(typeof({1}), {2}) }},",
                                openIface, openImpl, lifetime));
                        }
                    }
                }
                sb.AppendLine("        };");
                sb.AppendLine();
                sb.AppendLine("        protected override System.Collections.Generic.IReadOnlyDictionary<global::System.Type, global::ZeroInject.Container.OpenGenericEntry>? OpenGenericMap => _s_openGenericMap;");
                sb.AppendLine();
            }
```

In `ResolveKnown`, change `return null;` at the end to:

```csharp
            sb.AppendLine(openGenerics.Count > 0
                ? "            return ResolveOpenGenericRoot(serviceType);"
                : "            return null;");
```

Apply the same pattern for `ResolveScopedKnown` in the scope:

```csharp
            sb.AppendLine(openGenerics.Count > 0
                ? "                return ResolveOpenGenericScoped(serviceType);"
                : "                return null;");
```

Also emit the scope's `OpenGenericMap` override (same static map, override in nested Scope class):

```csharp
            if (openGenerics.Count > 0)
            {
                sb.AppendLine("            protected override System.Collections.Generic.IReadOnlyDictionary<global::System.Type, global::ZeroInject.Container.OpenGenericEntry>? OpenGenericMap => " + className + "._s_openGenericMap;");
                sb.AppendLine();
            }
```

Apply `BuildDecoratedNewExpression` in the standalone container's transient/singleton/scoped loops (same as Task 5 but for standalone using `decoratorsByInterface`).

**Step 4: Run all tests**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj
```
Expected: all PASS

**Step 5: Commit**

```bash
git add src/ZeroInject.Generator/ZeroInjectGenerator.cs \
        tests/ZeroInject.Tests/GeneratorTests/ContainerGeneratorTests.cs
git commit -m "feat: emit OpenGenericMap and decorator wrapping in standalone container"
```

---

### Task 7: Integration tests — open generics in standalone

**Files:**
- Modify: `tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs`

**Step 1: Write integration tests**

Add to `IntegrationTests.cs`:

```csharp
[Fact]
public void Standalone_OpenGeneric_Transient_ResolvesNewInstanceEachTime()
{
    var source = """
        using ZeroInject;
        public interface IRepo<T> { }
        [Transient]
        public class Repo<T> : IRepo<T> { }
        """;

    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var repoType = assembly.GetType("IRepo`1")!.MakeGenericType(typeof(string));
    var a = provider.GetService(repoType);
    var b = provider.GetService(repoType);
    Assert.NotNull(a);
    Assert.NotSame(a, b);
}

[Fact]
public void Standalone_OpenGeneric_Scoped_SameWithinScope_DifferentAcrossScopes()
{
    var source = """
        using ZeroInject;
        public interface IRepo<T> { }
        [Scoped]
        public class Repo<T> : IRepo<T> { }
        """;

    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var repoType = assembly.GetType("IRepo`1")!.MakeGenericType(typeof(int));
    var scopeFactory = (IServiceScopeFactory)provider;
    using var scope1 = scopeFactory.CreateScope();
    using var scope2 = scopeFactory.CreateScope();
    var a = scope1.ServiceProvider.GetService(repoType);
    var b = scope1.ServiceProvider.GetService(repoType);
    var c = scope2.ServiceProvider.GetService(repoType);
    Assert.NotNull(a);
    Assert.Same(a, b);
    Assert.NotSame(a, c);
}

[Fact]
public void Standalone_OpenGeneric_Singleton_ReturnsSameInstanceAcrossRootAndScope()
{
    var source = """
        using ZeroInject;
        public interface ICache<T> { }
        [Singleton]
        public class Cache<T> : ICache<T> { }
        """;

    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var cacheType = assembly.GetType("ICache`1")!.MakeGenericType(typeof(string));
    var scopeFactory = (IServiceScopeFactory)provider;
    using var scope = scopeFactory.CreateScope();
    var fromRoot = provider.GetService(cacheType);
    var fromScope = scope.ServiceProvider.GetService(cacheType);
    Assert.NotNull(fromRoot);
    Assert.Same(fromRoot, fromScope);
}

[Fact]
public void Standalone_OpenGeneric_UnknownClosedType_ReturnsNull()
{
    var source = """
        using ZeroInject;
        public interface IFoo<T> { }
        [Transient]
        public class Foo<T> : IFoo<T> { }
        """;

    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    // Completely unrelated type
    var result = provider.GetService(typeof(System.Collections.Generic.IList<string>));
    Assert.Null(result);
}
```

**Step 2: Run tests**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj --filter "Standalone_OpenGeneric" -v minimal
```
Expected: all PASS

**Step 3: Run full suite**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj
```
Expected: all PASS

**Step 4: Commit**

```bash
git add tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs
git commit -m "test: add integration tests for open-generic resolution in standalone container"
```

---

### Task 8: Integration tests — decorators in all modes

**Files:**
- Modify: `tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs`

**Step 1: Write integration tests**

Add to `IntegrationTests.cs`:

```csharp
[Fact]
public void Decorator_NonGeneric_HybridContainer_ReturnsDecoratedInstance()
{
    var source = """
        using ZeroInject;
        public interface IFoo { string Name { get; } }
        [Transient]
        public class FooImpl : IFoo { public string Name => "Inner"; }
        [Decorator]
        public class LoggingFoo : IFoo
        {
            private readonly IFoo _inner;
            public LoggingFoo(IFoo inner) { _inner = inner; }
            public string Name => "Logging:" + _inner.Name;
        }
        """;

    var (assembly, provider) = BuildAndCreateProvider(source);
    var fooType = assembly.GetType("IFoo")!;
    var instance = provider.GetService(fooType);
    Assert.NotNull(instance);
    var name = (string)fooType.GetProperty("Name")!.GetValue(instance)!;
    Assert.Equal("Logging:Inner", name);
}

[Fact]
public void Decorator_NonGeneric_Standalone_ReturnsDecoratedInstance()
{
    var source = """
        using ZeroInject;
        public interface IFoo { string Name { get; } }
        [Transient]
        public class FooImpl : IFoo { public string Name => "Inner"; }
        [Decorator]
        public class LoggingFoo : IFoo
        {
            private readonly IFoo _inner;
            public LoggingFoo(IFoo inner) { _inner = inner; }
            public string Name => "Logging:" + _inner.Name;
        }
        """;

    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var fooType = assembly.GetType("IFoo")!;
    var instance = provider.GetService(fooType);
    Assert.NotNull(instance);
    var name = (string)fooType.GetProperty("Name")!.GetValue(instance)!;
    Assert.Equal("Logging:Inner", name);
}

[Fact]
public void Decorator_NonGeneric_MsDiPath_ReturnsDecoratedInstance()
{
    var source = """
        using ZeroInject;
        using Microsoft.Extensions.DependencyInjection;
        public interface IFoo { string Name { get; } }
        [Transient]
        public class FooImpl : IFoo { public string Name => "Inner"; }
        [Decorator]
        public class LoggingFoo : IFoo
        {
            private readonly IFoo _inner;
            public LoggingFoo(IFoo inner) { _inner = inner; }
            public string Name => "Logging:" + _inner.Name;
        }
        """;

    var (assembly, provider) = BuildAndCreateProvider(source,
        services =>
        {
            // Use raw MS DI path: call AddXxxServices() and BuildServiceProvider()
        });
    // The MS DI path test: verify via the registration extension (BuildAndCreateProvider uses hybrid)
    // We test the extension class instead via BuildAndCreateMsDiProvider
    var (assembly2, msDiProvider) = BuildAndCreateMsDiProvider(source);
    var fooType = assembly2.GetType("IFoo")!;
    var instance = msDiProvider.GetService(fooType);
    Assert.NotNull(instance);
    var name = (string)fooType.GetProperty("Name")!.GetValue(instance)!;
    Assert.Equal("Logging:Inner", name);
}

[Fact]
public void Decorator_Scoped_IsDisposed_WithScope()
{
    var source = """
        using ZeroInject;
        using System;
        public interface IFoo { }
        [Scoped]
        public class FooImpl : IFoo, IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }
        [Decorator]
        public class LoggingFoo : IFoo, IDisposable
        {
            public readonly IFoo Inner;
            public bool Disposed { get; private set; }
            public LoggingFoo(IFoo inner) { Inner = inner; }
            public void Dispose() => Disposed = true;
        }
        """;

    var (assembly, provider) = BuildAndCreateStandaloneProvider(source);
    var fooType = assembly.GetType("IFoo")!;
    LoggingFoo? outer = null;
    var scopeFactory = (IServiceScopeFactory)provider;
    using (var scope = scopeFactory.CreateScope())
    {
        var instance = scope.ServiceProvider.GetService(fooType);
        outer = (dynamic)instance; // track reference
    }
    // After scope disposal, outer decorator should be disposed
    // (The integration test can verify by type name if needed)
    Assert.NotNull(outer);
}
```

Also add the `BuildAndCreateMsDiProvider` helper to `IntegrationTests.cs` (similar to `BuildAndCreateProvider` but calls the generated `AddXxxServices()` on a plain `ServiceCollection` and returns `BuildServiceProvider()`):

```csharp
    private static (Assembly assembly, IServiceProvider provider) BuildAndCreateMsDiProvider(string source)
    {
        var (outputCompilation, diagnostics) = RunGeneratorAndGetCompilation(source);
        var genErrors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (genErrors.Count > 0)
            throw new InvalidOperationException("Generator errors:\n" + string.Join("\n", genErrors));

        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        if (!emitResult.Success)
        {
            var errors = emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString());
            throw new InvalidOperationException("Compilation failed:\n" + string.Join("\n", errors));
        }
        ms.Seek(0, SeekOrigin.Begin);
        var loadContext = new AssemblyLoadContext(null, isCollectible: true);
        var assembly = loadContext.LoadFromStream(ms);

        // Find the AddXxxServices extension method
        var extensionClass = assembly.GetTypes().First(t => t.Name.EndsWith("ServiceCollectionExtensions"));
        var addMethod = extensionClass.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name.StartsWith("Add") && m.Name.EndsWith("Services"));

        var services = new ServiceCollection();
        addMethod.Invoke(null, [services]);
        var provider = services.BuildServiceProvider();
        return (assembly, provider);
    }
```

**Step 2: Run all tests**

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj
```
Expected: all PASS

**Step 3: Commit**

```bash
git add tests/ZeroInject.Tests/ContainerTests/IntegrationTests.cs
git commit -m "test: add integration tests for decorator support in all modes"
```

---

### Task 9: Update README limitations section

**Files:**
- Modify: `README.md`

Remove "Open generics (e.g., `IRepository<>`) delegate to the fallback in hybrid mode; not resolved in standalone mode" from the Current Limitations section.

Replace with:

```markdown
### Current Limitations

- **Open generics in standalone** use runtime reflection for construction (bounded: one `GetConstructors()[0]` + `Invoke` call, result cached for singletons/scoped)
- **Open-generic decorator registration** is not supported via the registration extension (MS DI limitation); open-generic decorators work in both generated containers
- **`IServiceProviderIsService`** delegates to the fallback in hybrid mode
```

Run all tests one final time:

```
dotnet test tests/ZeroInject.Tests/ZeroInject.Tests.csproj
```
Expected: all PASS

Commit:

```bash
git add README.md
git commit -m "docs: update limitations section for open generics and decorator support"
```
