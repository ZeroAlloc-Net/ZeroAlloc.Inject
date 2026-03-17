---
id: testing
title: Testing
slug: /docs/testing
description: Test DI-registered services in isolation and with a real container in integration tests.
sidebar_position: 9
---

# Testing

ZeroAlloc.Inject generates registration and resolution code at compile time. This means the most important thing to test is your service logic — not the wiring. Most tests should never touch the DI container at all. This guide explains when and how to involve the container in tests, and how to set up each container mode for integration testing.

---

## Unit Testing Without the Container

Services annotated with `[Transient]`, `[Scoped]`, or `[Singleton]` are ordinary classes. You can instantiate them directly in a unit test by passing test doubles (fakes, stubs, or mocks) to the constructor. No container, no registration, no overhead.

```csharp
public interface IOrderRepository
{
    Order? GetById(int id);
}

[Transient]
public class OrderService : IOrderService
{
    private readonly IOrderRepository _repository;

    public OrderService(IOrderRepository repository)
    {
        _repository = repository;
    }

    public Order GetOrThrow(int id)
        => _repository.GetById(id) ?? throw new KeyNotFoundException($"Order {id} not found.");
}

// Unit test — no container involved
public class OrderServiceTests
{
    [Fact]
    public void GetOrThrow_WhenOrderExists_ReturnsOrder()
    {
        var fakeRepository = new FakeOrderRepository(new Order(1, "Pending"));
        var service = new OrderService(fakeRepository);  // direct constructor call

        var result = service.GetOrThrow(1);

        Assert.Equal(1, result.Id);
    }

    [Fact]
    public void GetOrThrow_WhenOrderMissing_Throws()
    {
        var fakeRepository = new FakeOrderRepository(null);
        var service = new OrderService(fakeRepository);

        Assert.Throws<KeyNotFoundException>(() => service.GetOrThrow(99));
    }
}
```

This pattern works regardless of which container mode you choose — the attributes and lifetimes only matter at runtime.

> **Tip:** Keep unit tests free of the container. Reserve DI-based tests for verifying that the wiring itself is correct.

---

## Integration Testing with the MS DI Extension Method

When you want to verify that services wire up and resolve correctly end-to-end, use the generated `AddXxxServices()` extension method to populate a real `IServiceCollection` and then call `BuildServiceProvider()`.

The generated method name is derived from the assembly name: dots are removed and `Services` is appended. For example, `MyApp.Domain` generates `AddMyAppDomainServices()`. You can find the exact name by checking the generated source in your IDE (look under `Dependencies > Analyzers > ZeroAlloc.Inject.Generator`) or by checking the output of `dotnet build`.

```csharp
public class OrderServiceIntegrationTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddMyAppDomainServices();  // generated extension method
        return services.BuildServiceProvider();
    }

    [Fact]
    public void OrderService_CanBeResolved()
    {
        using var provider = BuildProvider();

        var service = provider.GetRequiredService<IOrderService>();

        Assert.NotNull(service);
    }

    [Fact]
    public void Transient_CreatesNewInstanceEachResolution()
    {
        using var provider = BuildProvider();

        var first  = provider.GetRequiredService<IOrderService>();
        var second = provider.GetRequiredService<IOrderService>();

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Scoped_ReturnsSameInstanceWithinScope()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var first  = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var second = scope.ServiceProvider.GetRequiredService<IOrderService>();

        Assert.Same(first, second);
    }
}
```

### Replacing services for testing

Use `IServiceCollection` to swap out real implementations for test doubles before building the provider. Because the generated method uses `TryAdd` semantics, registrations you add *before* calling the generated method take priority:

```csharp
var services = new ServiceCollection();
services.AddScoped<IOrderRepository, FakeOrderRepository>(); // registered first — wins
services.AddMyAppDomainServices();                           // TryAdd skips IOrderRepository
using var provider = services.BuildServiceProvider();
```

To override a registration added by the generated method, add it *after* and use `Add` (not `TryAdd`):

```csharp
var services = new ServiceCollection();
services.AddMyAppDomainServices();
// Replace the real repository with a fake
services.AddScoped<IOrderRepository, FakeOrderRepository>(); // won't work — already registered
// Instead, remove and re-add:
var descriptor = services.Single(d => d.ServiceType == typeof(IOrderRepository));
services.Remove(descriptor);
services.AddScoped<IOrderRepository, FakeOrderRepository>();
```

---

## Integration Testing with the Hybrid Container

When you use `BuildZeroAllocInjectServiceProvider()` or `ZeroAllocInjectServiceProviderFactory` in your application, set up tests the same way but call the same extension after building the fallback collection:

```csharp
public class HybridContainerIntegrationTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddMyAppDomainServices();
        return services.BuildZeroAllocInjectServiceProvider(); // hybrid mode
    }

    [Fact]
    public void OrderService_ResolvesViaGeneratedTypeSwitch()
    {
        using var provider = BuildProvider();
        var service = provider.GetRequiredService<IOrderService>();
        Assert.NotNull(service);
    }

    [Fact]
    public void Scoped_LifetimeIsRespected()
    {
        using var provider = BuildProvider();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var fromA = scopeA.ServiceProvider.GetRequiredService<IOrderService>();
        var fromB = scopeB.ServiceProvider.GetRequiredService<IOrderService>();

        Assert.NotSame(fromA, fromB);
    }
}
```

`BuildZeroAllocInjectServiceProvider()` is an extension method on `IServiceCollection` provided by the `ZeroAlloc.Inject.Container` package.

---

## Integration Testing with the Standalone Container

The standalone container is instantiated directly with `new`. No `ServiceCollection`, no `BuildServiceProvider()`. Use it in tests exactly as you would in application code:

> **Note:** The generated standalone container class is `internal sealed`, so it is not directly accessible from a separate test project. You have two options:
>
> - Add `[assembly: InternalsVisibleTo("YourTestProject")]` to the main project's `AssemblyInfo.cs` or any code file, which grants the test project access to all internal types.
> - Use reflection to instantiate the type without needing access to its name at compile time:
>   ```csharp
>   var providerType = typeof(SomeTypeInMainProject).Assembly
>       .GetType("ZeroAlloc.Inject.Generated.MyAppDomainStandaloneServiceProvider");
>   using var provider = (IServiceProvider)Activator.CreateInstance(providerType)!;
>   ```

```csharp
public class StandaloneContainerIntegrationTests
{
    [Fact]
    public void OrderService_ResolvesFromStandaloneProvider()
    {
        using var provider = new MyAppDomainStandaloneServiceProvider();

        var service = provider.GetRequiredService<IOrderService>();

        Assert.NotNull(service);
    }

    [Fact]
    public async Task ScopedService_DisposedWithScope()
    {
        using var provider = new MyAppDomainStandaloneServiceProvider();

        await using var scope = provider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();

        Assert.NotNull(repo);
        // scope.DisposeAsync() called here — tracked disposables are cleaned up
    }
}
```

The generated class name follows the pattern `<AssemblyName>StandaloneServiceProvider` in the `ZeroAlloc.Inject.Generated` namespace. For an assembly named `MyApp.Domain` the generated type is `ZeroAlloc.Inject.Generated.MyAppDomainStandaloneServiceProvider`.

Because the standalone provider has no fallback, services that are not annotated with ZeroAlloc.Inject attributes return `null`. This is useful in tests for verifying that only the expected services are registered:

```csharp
[Fact]
public void UnregisteredService_ReturnsNull()
{
    using var provider = new MyAppDomainStandaloneServiceProvider();

    var service = provider.GetService<IUnregisteredService>();

    Assert.Null(service);
}
```

---

## Testing with Generator Output (Roslyn-level tests)

The test suite in this repository uses a helper (`GeneratorTestHelper`) that runs the source generator against an in-memory compilation and asserts on the emitted source text. This technique is useful when you want to verify that a specific attribute combination produces or suppresses a diagnostic, or emits the expected registration calls.

```csharp
// Example: assert that [Transient] on a class with one interface generates TryAddTransient
var source = """
    using ZeroAlloc.Inject;
    namespace TestApp;

    public interface IMyService { }

    [Transient]
    public class MyService : IMyService { }
    """;

var (output, diagnostics) = GeneratorTestHelper.RunGenerator(source);

Assert.DoesNotContain("Error", diagnostics.AsEnumerable().Select(d => d.Severity.ToString()));
Assert.Contains("TryAddTransient<global::TestApp.IMyService>", output);
```

Use `GeneratorTestHelper.RunGeneratorWithContainer(source)` when the generated output depends on the `ZeroAlloc.Inject.Container` package (for example, when testing standalone or hybrid provider emission).

---

## Tips for Fast Test Setup

- **Prefer direct construction over the container.** Unit tests that call `new OrderService(fakeRepo)` run in microseconds. Tests that build a `ServiceProvider` pay a one-time startup cost per test or test class.
- **Share a single provider across a test class.** Use a class-level `IServiceProvider` fixture or a static factory method so the provider is built once and reused across all tests in the class.
- **Use the standalone container for pure service tests.** It has a ~5 ns startup cost and no MS DI runtime dependency. For tests that don't need framework services (`IOptions<T>`, `ILogger`, etc.), the standalone container is the fastest option.
- **Use `ValidateScopes = true` and `ValidateOnBuild = true` for integration tests.** These options, available on `ServiceProviderOptions` when calling `BuildServiceProvider()`, catch captive dependency bugs (scoped service injected into singleton) at container-build time rather than at runtime.

```csharp
var provider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateScopes = true,
    ValidateOnBuild = true,
});
```

- **Do not use the hybrid container when the standalone container is sufficient.** The hybrid container's ~4,477 ns build cost is negligible in production, but in a test suite with hundreds of tests it adds up. If your tests don't exercise framework services, use the standalone container or the plain `BuildServiceProvider()`.
