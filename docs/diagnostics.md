---
id: diagnostics
title: Compiler Diagnostics
slug: /docs/diagnostics
description: ZAI001–ZAI019 Roslyn analyzer rules with triggers, severities, and fix guidance.
sidebar_position: 7
---

# Diagnostics Reference

All diagnostics are emitted at compile time by the Roslyn source generator. Errors (❌) prevent the registration code from being generated. Warnings (⚠️) allow generation to continue but flag potential issues.

## All Diagnostics

| ID | Severity | Description | Cause | Fix |
|----|----------|-------------|-------|-----|
| ZAI001 | ❌ Error | Multiple lifetime attributes on same class | `[Transient]` and `[Singleton]` (or any two lifetime attributes) on the same class | Remove all but one lifetime attribute |
| ZAI002 | ❌ Error | Attribute on non-class type | Applied to a `struct`, `interface`, or `record struct` | Only apply lifetime attributes to non-abstract, non-static classes |
| ZAI003 | ❌ Error | Attribute on abstract or static class | Abstract or static classes cannot be instantiated | Use lifetime attributes on concrete classes only |
| ZAI004 | ❌ Error | `As` type not implemented by the class | `As = typeof(IFoo)` but the class does not implement `IFoo` | Implement the interface or change the `As` type to one the class does implement |
| ZAI005 | ❌ Error | `Key` requires .NET 8+ | The `Key` property is used but the target framework is below .NET 8 | Upgrade to .NET 8+ or remove the `Key` property |
| ZAI006 | ⚠️ Warning | No public constructor | The class has no public constructor; the generator cannot wire dependencies | Add a `public` constructor |
| ZAI007 | ⚠️ Warning | No interfaces (concrete-only registration) | The class implements no non-system interfaces | Either implement an interface or accept concrete-only registration |
| ZAI008 | ⚠️ Warning | Missing `Microsoft.Extensions.DependencyInjection.Abstractions` | The required package is not referenced | Add `<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />` |
| ZAI009 | ❌ Error | Multiple public constructors without `[ActivatorUtilitiesConstructor]` | Ambiguous constructor: the generator does not know which constructor to use | Mark the intended constructor with `[ActivatorUtilitiesConstructor]` |
| ZAI010 | ❌ Error | Constructor parameter is a primitive or value type | `int`, `bool`, `string` (and other value types) cannot be injected via DI | Remove the primitive from the constructor and use the Options pattern instead |
| ZAI011 | ❌ Error | Decorator has no matching interface parameter | A `[Decorator]`-annotated class constructor has no parameter of an interface type that the class also implements | Add a constructor parameter of the decorated interface type |
| ZAI012 | ❌ Error | Decorated interface not registered as a service | The interface the decorator wraps has no registered implementation | Ensure the implementation of the decorated interface is registered (with a lifetime attribute) before the decorator |
| ZAI013 | ⚠️ Warning | Decorator on abstract or static class | A `[Decorator]` or `[DecoratorOf]` attribute is on an abstract or static class | Use decorator attributes on concrete classes only |
| ZAI014 | ❌ Error | Circular dependency detected | Service A depends on B which depends on A (compile-time cycle detection) | Refactor the dependency graph to break the cycle (e.g., introduce a factory, lazy wrapper, or intermediary) |
| ZAI015 | ❌ Error | `[OptionalDependency]` on non-nullable parameter | The generator would emit `GetService<T>()` which can return `null`, but the parameter type is not nullable | Change the parameter type to `T?` (nullable) |
| ZAI016 | ❌ Error | `[DecoratorOf]` interface not implemented by the class | The interface listed in `[DecoratorOf(typeof(IFoo))]` is not implemented by the class | Implement the interface or correct the type argument |
| ZAI017 | ❌ Error | Two decorators share the same `Order` for the same interface | Ambiguous decorator ordering: two `[DecoratorOf]` attributes target the same interface with identical `Order` values | Assign unique `Order` values to each decorator for the same interface |
| ZAI018 | ⚠️ Warning | Open generic has no detected closed usages | An open generic class is registered but no constructor in the assembly takes a closed form of its interface as a parameter; it will not be resolvable from the standalone or hybrid generated container | Ensure at least one constructor parameter of the closed generic type exists in the assembly, or switch to the MS DI extension method mode |
| ZAI019 | ❌ Error | `[Inject]` on a non-settable property | The property has no public setter (or uses `init`); the generator cannot emit a property assignment | Add a `public` setter, or remove `[Inject]` |

## Per-Diagnostic Details

### Registration Errors (ZAI001–ZAI005)

#### ZAI001 — Multiple lifetime attributes

**Title:** Multiple lifetime attributes

**Message:** `Class '{0}' has multiple lifetime attributes; only one of [Transient], [Scoped], or [Singleton] is allowed`

A class must carry exactly one lifetime attribute. Combining two or more lifetime attributes is ambiguous and the generator refuses to produce registration code.

**Triggers ZAI001:**

```csharp
[Transient]
[Singleton]
public class OrderService : IOrderService
{
    // ZAI001: Multiple lifetime attributes
}
```

**Fix:**

```csharp
[Scoped]
public class OrderService : IOrderService
{
    // Only one lifetime attribute
}
```

---

#### ZAI002 — Attribute on non-class type

**Title:** Attribute on non-class type

**Message:** `'{0}' is not a class; service attributes can only be applied to classes`

Lifetime attributes (`[Transient]`, `[Scoped]`, `[Singleton]`) are only valid on reference-type classes. Applying them to a `struct`, `record struct`, or `interface` raises this error.

**Triggers ZAI002:**

```csharp
[Transient]
public struct PaymentRequest
{
    // ZAI002: struct cannot be registered
}
```

**Fix:**

```csharp
// Either use a class:
[Transient]
public class PaymentRequest
{
}

// Or, if a struct is required, remove the attribute and register manually.
```

---

#### ZAI003 — Attribute on abstract or static class

**Title:** Attribute on abstract or static class

**Message:** `Class '{0}' is abstract or static and cannot be registered as a service`

The DI container must instantiate the registered type. Abstract and static classes cannot be instantiated, so the generator rejects them.

**Triggers ZAI003:**

```csharp
[Singleton]
public abstract class NotificationBase : INotificationService
{
    // ZAI003: abstract class
}
```

**Fix:**

```csharp
[Singleton]
public class EmailNotificationService : INotificationService
{
    // Concrete class — can be instantiated
}
```

---

#### ZAI004 — `As` type not implemented by the class

**Title:** As type not implemented

**Message:** `Class '{0}' does not implement '{1}' specified in the As property`

The `As` property on a lifetime attribute tells the generator to register the service under a specific interface. If the class does not actually implement that interface, the registration would be invalid.

**Triggers ZAI004:**

```csharp
[Transient(As = typeof(IPaymentGateway))]
public class StripeClient : IEmailSender
{
    // ZAI004: StripeClient does not implement IPaymentGateway
}
```

**Fix:**

```csharp
[Transient(As = typeof(IPaymentGateway))]
public class StripeClient : IPaymentGateway
{
    // Correct: StripeClient implements IPaymentGateway
}
```

---

#### ZAI005 — `Key` requires .NET 8+

**Title:** Keyed services require .NET 8+

**Message:** `Class '{0}' uses Key property but the target framework does not support keyed services (requires .NET 8+)`

Keyed service registration (`Key = "myKey"`) relies on `IKeyedServiceCollection<,>` which was introduced in `Microsoft.Extensions.DependencyInjection` 8.0. Using it on a project targeting an earlier framework raises this error.

**Triggers ZAI005 (on a net7.0 target):**

```csharp
[Singleton(Key = "primary")]
public class PrimaryCache : ICache
{
    // ZAI005: Key not supported below .NET 8
}
```

**Fix:**

```csharp
// Option 1: upgrade to net8.0 or later in the .csproj
// <TargetFramework>net8.0</TargetFramework>

// Option 2: remove the Key property
[Singleton]
public class PrimaryCache : ICache
{
}
```

---

### Registration Warnings (ZAI006–ZAI008)

#### ZAI006 — No public constructor

**Title:** No public constructor

**Message:** `Class '{0}' has no public constructor; the DI container requires a public constructor to resolve this service`

The source generator selects the constructor to call at resolution time. If no `public` constructor exists it cannot generate valid code and emits this warning (generation continues, but the registration will fail at runtime).

**Triggers ZAI006:**

```csharp
[Scoped]
public class ReportBuilder : IReportBuilder
{
    private ReportBuilder() { }  // ZAI006: no public constructor
}
```

**Fix:**

```csharp
[Scoped]
public class ReportBuilder : IReportBuilder
{
    public ReportBuilder() { }
}
```

---

#### ZAI007 — No interfaces (concrete-only registration)

**Title:** No interfaces implemented

**Message:** `Class '{0}' implements no interfaces and will only be registered as its concrete type`

When a class implements no interfaces, the generator registers it under its own concrete type only. Consumers must request the concrete type rather than an abstraction. This warning flags that the class may benefit from an interface for testability or loose coupling.

**Triggers ZAI007:**

```csharp
[Transient]
public class AuditLogger
{
    // ZAI007: no interfaces — registered as AuditLogger only
}
```

**Fix (preferred):**

```csharp
[Transient]
public class AuditLogger : IAuditLogger
{
    // Registered as IAuditLogger
}
```

**Suppress (intentional concrete registration):**

```csharp
// If you intentionally want a concrete-only registration, suppress the warning:
#pragma warning disable ZAI007
[Transient]
public class AuditLogger { }
#pragma warning restore ZAI007
```

---

#### ZAI008 — Missing `Microsoft.Extensions.DependencyInjection.Abstractions`

**Title:** Missing DI abstractions

**Message:** `Microsoft.Extensions.DependencyInjection.Abstractions is not referenced and generated code will not compile`

The generated registration code emits calls to `IServiceCollection` which lives in `Microsoft.Extensions.DependencyInjection.Abstractions`. Without a reference to this package the generated file will produce compilation errors.

**Triggers ZAI008** — a service class annotated with `[Transient]` in a project that does not reference the abstractions package:

```csharp
// MyService.cs
[Transient]
public class MyService : IMyService
{
    // ZAI008: IServiceCollection is unavailable — abstractions package missing
}
```

```xml
<!-- Missing: no Microsoft.Extensions.DependencyInjection.Abstractions reference -->
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="ZeroAlloc.Inject" Version="*" />
    <PackageReference Include="ZeroAlloc.Inject.Generator" Version="*" PrivateAssets="all" />
    <!-- ZAI008: Microsoft.Extensions.DependencyInjection.Abstractions is NOT referenced -->
  </ItemGroup>
</Project>
```

**Fix** — add the package reference to the project file:

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
```

---

### Constructor Errors (ZAI009–ZAI010)

#### ZAI009 — Multiple public constructors without `[ActivatorUtilitiesConstructor]`

**Title:** Multiple public constructors without [ActivatorUtilitiesConstructor]

**Message:** `Class '{0}' has multiple public constructors; apply [ActivatorUtilitiesConstructor] to the preferred constructor`

The generator must choose a single constructor to call. When more than one `public` constructor exists and none is marked with `[ActivatorUtilitiesConstructor]`, the choice is ambiguous.

**Triggers ZAI009:**

```csharp
[Scoped]
public class UserRepository : IUserRepository
{
    public UserRepository(IDbConnection connection) { }
    public UserRepository(IDbConnection connection, ILogger<UserRepository> logger) { }
    // ZAI009: ambiguous — which constructor to use?
}
```

**Fix:**

```csharp
using Microsoft.Extensions.DependencyInjection;

[Scoped]
public class UserRepository : IUserRepository
{
    public UserRepository(IDbConnection connection) { }

    [ActivatorUtilitiesConstructor]
    public UserRepository(IDbConnection connection, ILogger<UserRepository> logger) { }
}
```

---

#### ZAI010 — Constructor parameter is a primitive or value type

**Title:** Constructor parameter is a primitive/value type

**Message:** `Constructor parameter '{0}' of class '{1}' is a primitive/value type ({2}); use IOptions<T> or a wrapper type instead`

The DI container resolves services by type. Primitive types (`int`, `bool`, `double`, etc.) and value types are not registered as services and cannot be injected. Use the Options pattern to pass configuration values.

**Triggers ZAI010:**

```csharp
[Singleton]
public class RateLimiter : IRateLimiter
{
    public RateLimiter(int maxRequestsPerSecond)  // ZAI010: int is a value type
    {
    }
}
```

**Fix:**

```csharp
public class RateLimiterOptions
{
    public int MaxRequestsPerSecond { get; set; }
}

[Singleton]
public class RateLimiter : IRateLimiter
{
    public RateLimiter(IOptions<RateLimiterOptions> options)
    {
        var maxRps = options.Value.MaxRequestsPerSecond;
    }
}
```

---

### Decorator Errors and Warnings (ZAI011–ZAI013, ZAI016–ZAI017)

#### ZAI011 — Decorator has no matching interface parameter

**Title:** Decorator has no matching interface

**Message:** `Class '{0}' is marked [Decorator] but no constructor parameter type matches any interface it implements`

A `[Decorator]`-annotated class must accept an instance of the interface it decorates as a constructor parameter. If no constructor parameter matches any interface the decorator itself implements, the generator cannot wire the decoration chain.

**Triggers ZAI011:**

```csharp
[Decorator]
public class CachingProductRepository : IProductRepository
{
    // ZAI011: no IProductRepository parameter
    public CachingProductRepository(ILogger<CachingProductRepository> logger) { }
}
```

**Fix:**

```csharp
[Decorator]
public class CachingProductRepository : IProductRepository
{
    public CachingProductRepository(IProductRepository inner, ILogger<CachingProductRepository> logger)
    {
        // inner is the decorated instance
    }
}
```

---

#### ZAI012 — Decorated interface not registered as a service

**Title:** Decorator inner service not found

**Message:** `Class '{0}' is marked [Decorator] for '{1}' but no service implementing that interface is registered in this assembly`

The decorator pattern requires an underlying implementation to wrap. If no class in the assembly is registered with a matching interface, the generator raises this error.

**Triggers ZAI012:**

```csharp
// IProductRepository has no [Transient]/[Scoped]/[Singleton] implementation

[Decorator]
public class CachingProductRepository : IProductRepository
{
    public CachingProductRepository(IProductRepository inner) { }
    // ZAI012: nothing implements IProductRepository
}
```

**Fix:**

```csharp
[Scoped]
public class SqlProductRepository : IProductRepository
{
    // Registered implementation of IProductRepository
}

[Decorator]
public class CachingProductRepository : IProductRepository
{
    public CachingProductRepository(IProductRepository inner) { }
}
```

---

#### ZAI013 — Decorator on abstract or static class

**Title:** Decorator on abstract or static class

**Message:** `Class '{0}' is abstract or static and cannot be used as a decorator`

Just as with regular service registration, a decorator must be a concrete, instantiable class. Applying `[Decorator]` or `[DecoratorOf]` to an abstract or static class triggers this warning.

**Triggers ZAI013:**

```csharp
[Decorator]
public abstract class BaseLoggingDecorator : IOrderService
{
    // ZAI013: abstract decorator
}
```

**Fix:**

```csharp
[Decorator]
public class LoggingOrderService : IOrderService
{
    public LoggingOrderService(IOrderService inner, ILogger<LoggingOrderService> logger) { }
}
```

---

#### ZAI016 — `[DecoratorOf]` interface not implemented by the class

**Title:** [DecoratorOf] interface not implemented

**Message:** `Class '{0}' is marked [DecoratorOf({1})] but does not implement that interface`

`[DecoratorOf(typeof(IFoo))]` explicitly names the interface to decorate. If the class does not implement that interface the decoration contract cannot be fulfilled.

**Triggers ZAI016:**

```csharp
[DecoratorOf(typeof(IOrderService))]
public class LoggingPaymentService : IPaymentService
{
    // ZAI016: LoggingPaymentService does not implement IOrderService
    public LoggingPaymentService(IOrderService inner) { }
}
```

**Fix:**

```csharp
[DecoratorOf(typeof(IOrderService))]
public class LoggingOrderService : IOrderService
{
    public LoggingOrderService(IOrderService inner) { }
}
```

---

#### ZAI017 — Two decorators share the same `Order` for the same interface

**Title:** Duplicate decorator Order

**Message:** `Interface '{0}' has two [DecoratorOf] decorators with the same Order={1}: '{2}' and '{3}'. Orders must be unique per interface.`

When multiple decorators target the same interface, the `Order` property determines the wrapping sequence. Duplicate `Order` values create an ambiguity that the generator cannot resolve.

**Triggers ZAI017:**

```csharp
[DecoratorOf(typeof(IOrderService), Order = 1)]
public class LoggingOrderService : IOrderService
{
    public LoggingOrderService(IOrderService inner) { }
}

[DecoratorOf(typeof(IOrderService), Order = 1)]  // ZAI017: duplicate Order=1
public class ValidationOrderService : IOrderService
{
    public ValidationOrderService(IOrderService inner) { }
}
```

**Fix:**

```csharp
[DecoratorOf(typeof(IOrderService), Order = 1)]
public class ValidationOrderService : IOrderService
{
    public ValidationOrderService(IOrderService inner) { }
}

[DecoratorOf(typeof(IOrderService), Order = 2)]
public class LoggingOrderService : IOrderService
{
    public LoggingOrderService(IOrderService inner) { }
}
```

---

### Dependency Graph Errors (ZAI014–ZAI015)

#### ZAI014 — Circular dependency detected

**Title:** Circular dependency detected

**Message:** `Circular dependency detected: {0}`

The generator performs a static dependency-graph analysis at compile time. If it finds a cycle — service A requires B which eventually requires A — it raises this error and includes the cycle path in the message.

**Triggers ZAI014:**

```csharp
[Scoped]
public class OrderService : IOrderService
{
    public OrderService(IInvoiceService invoices) { }
}

[Scoped]
public class InvoiceService : IInvoiceService
{
    public InvoiceService(IOrderService orders) { }  // ZAI014: cycle
}
```

**Fix — break the cycle with a factory:**

```csharp
[Scoped]
public class OrderService : IOrderService
{
    public OrderService(IInvoiceServiceFactory invoiceFactory) { }
}

[Scoped]
public class InvoiceService : IInvoiceService
{
    public InvoiceService(IOrderService orders) { }
}

[Singleton]
public class InvoiceServiceFactory : IInvoiceServiceFactory
{
    private readonly IServiceProvider _provider;
    public InvoiceServiceFactory(IServiceProvider provider) => _provider = provider;
    public IInvoiceService Create() => _provider.GetRequiredService<IInvoiceService>();
}
```

---

#### ZAI015 — `[OptionalDependency]` on non-nullable parameter

**Title:** [OptionalDependency] on non-nullable parameter

**Message:** `Parameter '{0}' of class '{1}' is marked [OptionalDependency] but its type '{2}' is not nullable; change the parameter type to '{2}?'`

`[OptionalDependency]` causes the generator to emit `provider.GetService<T>()` instead of `provider.GetRequiredService<T>()`. `GetService<T>()` returns `null` when the service is not registered. The parameter type must therefore be nullable so the `null` return value is representable.

**Triggers ZAI015:**

```csharp
[Transient]
public class ReportExporter : IReportExporter
{
    public ReportExporter([OptionalDependency] IEmailSender emailSender)
    // ZAI015: IEmailSender is not nullable
    {
    }
}
```

**Fix:**

```csharp
[Transient]
public class ReportExporter : IReportExporter
{
    public ReportExporter([OptionalDependency] IEmailSender? emailSender)
    {
        // emailSender may be null if IEmailSender is not registered
    }
}
```

---

### Container Warnings (ZAI018–ZAI019)

#### ZAI018 — Open generic has no detected closed usages

**Title:** No closed usages detected for open generic

**Message:** `Open generic '{0}' is registered but no closed usages were detected in this assembly. It will not be resolvable from the standalone or hybrid container.`

ZeroAlloc.Inject's standalone and hybrid container modes generate explicit registrations for each closed form of a generic service. To do so the generator scans constructor parameters across the assembly to find which closed forms (e.g., `IRepository<Order>`, `IRepository<Customer>`) are actually used. If no such usages are found, the open generic cannot be pre-registered and will be unavailable at runtime in those container modes.

The MS DI extension-method mode (`AddMyServices(IServiceCollection services)`) delegates open generic handling to the runtime container, so ZAI018 is only a concern for standalone/hybrid usage.

**Triggers ZAI018:**

```csharp
// IRepository<T> has no constructor parameters consuming any closed form in this assembly

[Scoped]
public class Repository<T> : IRepository<T>
{
    // ZAI018: no IRepository<SomeEntity> detected
}
```

**Fix — ensure at least one constructor takes a closed form:**

```csharp
// Add a service that consumes a closed form of the generic
[Scoped]
public class OrderProcessor : IOrderProcessor
{
    public OrderProcessor(IRepository<Order> orders) { }
    // Now IRepository<Order> is detected and Repository<Order> will be registered
}
```

**Alternative — use MS DI extension mode only:**

If the open generic is intentional and closed types are determined at runtime, suppress the warning and rely on the MS DI extension method:

```csharp
#pragma warning disable ZAI018
[Scoped]
public class Repository<T> : IRepository<T> { }
#pragma warning restore ZAI018
```

---

### Property Injection Errors (ZAI019)

#### ZAI019 — `[Inject]` on a non-settable property {#zai019}

**Title:** `[Inject]` on non-settable property

**Message:** `Property '{0}' of class '{1}' is marked [Inject] but has no public setter; add a public setter or remove [Inject]`

The `[Inject]` attribute tells the generator to set a property after the service is constructed. For the generator to emit `instance.Property = sp.GetRequiredService<T>()`, the property must have a `public` setter. Properties with `init`-only accessors or no setter at all cannot be assigned outside an object initialiser, so the generator rejects them.

**Triggers ZAI019:**

```csharp
[Transient]
public class MyService : IMyService
{
    [Inject]
    public IDep Dep { get; } = null!;        // ZAI019: no setter

    [Inject]
    public IDep Other { get; init; } = null!; // ZAI019: init-only setter
}
```

**Fix — add a `public` setter:**

```csharp
[Transient]
public class MyService : IMyService
{
    [Inject]
    public IDep Dep { get; set; } = null!;   // OK: public setter
}
```

**Alternative — remove `[Inject]` and use constructor injection instead:**

```csharp
[Transient]
public class MyService : IMyService
{
    private readonly IDep _dep;

    public MyService(IDep dep) => _dep = dep;
}
```
