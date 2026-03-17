---
id: decorators
title: Decorators
slug: /docs/decorators
description: Apply the decorator pattern at compile time with the [Decorator] attribute.
sidebar_position: 4
---

# Decorators

The Decorator pattern lets you layer cross-cutting concerns — logging, caching, tracing, retry logic — around a service without touching its implementation. A decorator implements the same interface as the real service, accepts the inner service as a constructor parameter, and delegates work to it while adding behaviour before or after each call. From the consumer's perspective, nothing changes: they receive an `IProductRepository` and have no idea how many wrappers surround the real one.

The traditional drawback of runtime decorator wiring (used by libraries such as Scrutor) is that the chain is assembled through reflection and is invisible until the application starts. A misconfiguration silently fails or throws at resolution time. ZeroAlloc.Inject wires decorators at compile time. The generator reads your attributes, validates the chain, and emits plain constructor calls — `new LoggingProductRepository(new ProductRepository(...))` — into the generated extension method. Errors such as a decorator with no matching interface parameter (ZAI011), a missing interface implementation (ZAI016), an unresolvable inner service (ZAI012), or a duplicate ordering conflict (ZAI017) surface as build errors, not production incidents.

Because the output is ordinary source code, you can inspect exactly what the generator produced, step through it in a debugger, and publish with Native AOT without any reflection-based ceremony.

---

## `[Decorator]` — Simple Form

`[Decorator]` is the zero-configuration form. Place it on a class and the generator infers the decorated interface automatically: it looks for a constructor parameter whose type is a registered service interface that the class also implements. No extra arguments required.

```csharp
// --- Service contract ---
public interface IProductRepository
{
    IReadOnlyList<Product> GetAll();
    Product? GetById(int id);
}

// --- Real implementation ---
[Transient]
public class ProductRepository : IProductRepository
{
    private static readonly List<Product> _products =
    [
        new(1, "Laptop",   1_199.99m),
        new(2, "Mouse",       29.99m),
        new(3, "Keyboard",    79.99m),
    ];

    public IReadOnlyList<Product> GetAll() => _products;
    public Product? GetById(int id) => _products.Find(p => p.Id == id);
}

// --- Decorator ---
[Decorator]
public class LoggingProductRepository : IProductRepository
{
    private readonly IProductRepository _inner;

    public LoggingProductRepository(IProductRepository inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<Product> GetAll()
    {
        var results = _inner.GetAll();
        Console.WriteLine($"  [log] GetAll → {results.Count} product(s)");
        return results;
    }

    public Product? GetById(int id)
    {
        var result = _inner.GetById(id);
        Console.WriteLine($"  [log] GetById({id}) → {(result is null ? "not found" : result.Name)}");
        return result;
    }
}
```

The generator emits a chain that wraps `ProductRepository` inside `LoggingProductRepository`. Any consumer that resolves `IProductRepository` transparently receives the logging wrapper.

**Constraints for `[Decorator]`:**

- The decorated class must implement the same interface as the service it wraps.
- It must accept the inner service as a constructor parameter of that interface type.
- The inner interface must already be registered as a service; otherwise the generator raises **ZAI012**.
- The generator must be able to unambiguously determine which constructor parameter is the inner service. When the class has multiple interfaces that could match, use `[DecoratorOf]` to be explicit.

---

## `[DecoratorOf]` — Explicit Form

`[DecoratorOf]` names the decorated interface directly, removing any inference ambiguity. It also enables two additional capabilities: controlling `Order` when multiple decorators wrap the same interface, and conditionally applying a decorator based on what is registered in the container.

```csharp
[DecoratorOf(typeof(IProductRepository), Order = 1)]
public class CachingProductRepository : IProductRepository
{
    private readonly IProductRepository _inner;
    private readonly Dictionary<int, Product?> _cache = new();

    public CachingProductRepository(IProductRepository inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<Product> GetAll() => _inner.GetAll();

    public Product? GetById(int id)
    {
        if (!_cache.TryGetValue(id, out var product))
        {
            product = _inner.GetById(id);
            _cache[id] = product;
        }
        return product;
    }
}
```

**When to prefer `[DecoratorOf]` over `[Decorator]`:**

- The class implements multiple interfaces and the generator cannot determine which one to decorate.
- You need to control wrapping order among multiple decorators for the same interface.
- The decorator lives in a different assembly from the real implementation.
- You want to conditionally apply the decorator via `WhenRegistered`.
- Multiple `[DecoratorOf]` attributes on the same class are valid — a single class can decorate several interfaces simultaneously.

---

## Decorator Ordering

When multiple decorators target the same interface, `Order` determines the nesting sequence. The rule is simple: **ascending order, innermost first**. `Order = 1` is closest to the real implementation; the highest `Order` value is the outermost wrapper, and is what callers actually receive when they resolve the interface.

Consider a three-layer stack for a product retrieval service:

```csharp
public interface IProductRetriever
{
    IReadOnlyList<Product> Retrieve(string category);
}

// Real implementation — not a decorator
[Transient]
public class ProductRetriever : IProductRetriever
{
    public IReadOnlyList<Product> Retrieve(string category)
    {
        // ... fetch from database
        return [];
    }
}

// Innermost: runs first, closest to the real implementation
[DecoratorOf(typeof(IProductRetriever), Order = 1)]
public class CachingRetriever : IProductRetriever
{
    private readonly IProductRetriever _inner;

    public CachingRetriever(IProductRetriever inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<Product> Retrieve(string category)
    {
        // Check cache before delegating to the real implementation
        return _inner.Retrieve(category);
    }
}

// Middle layer
[DecoratorOf(typeof(IProductRetriever), Order = 2)]
public class LoggingRetriever : IProductRetriever
{
    private readonly IProductRetriever _inner;

    public LoggingRetriever(IProductRetriever inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<Product> Retrieve(string category)
    {
        Console.WriteLine($"  [log] Retrieve({category})");
        return _inner.Retrieve(category);
    }
}

// Outermost: the caller receives this one
[DecoratorOf(typeof(IProductRetriever), Order = 3)]
public class TracingRetriever : IProductRetriever
{
    private readonly IProductRetriever _inner;

    public TracingRetriever(IProductRetriever inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<Product> Retrieve(string category)
    {
        // Start trace span, then delegate down the chain
        return _inner.Retrieve(category);
    }
}
```

The generator produces a chain equivalent to:

```csharp
new TracingRetriever(
    new LoggingRetriever(
        new CachingRetriever(
            new ProductRetriever()
        )
    )
)
```

The resolution flow:

```mermaid
flowchart LR
    Caller -->|resolves IProductRetriever| TracingRetriever["TracingRetriever (Order=3)"]
    TracingRetriever -->|wraps| LoggingRetriever["LoggingRetriever (Order=2)"]
    LoggingRetriever -->|wraps| CachingRetriever["CachingRetriever (Order=1)"]
    CachingRetriever -->|wraps| RealRetriever["ProductRetriever (real impl)"]
    style TracingRetriever fill:#4a90d9,color:#fff
    style LoggingRetriever fill:#4a90d9,color:#fff
    style CachingRetriever fill:#4a90d9,color:#fff
    style RealRetriever fill:#27ae60,color:#fff
```

If two decorators share the same `Order` for the same interface, the generator raises **ZAI017** at build time — there is no ambiguous runtime tie-breaking.

---

## Conditional Decorators with `WhenRegistered`

The `WhenRegistered` property gates a decorator on whether a specified type is present in the `IServiceCollection` at the moment the generated extension method is called.

```csharp
[DecoratorOf(typeof(IProductRetriever), Order = 3, WhenRegistered = typeof(TracingOptions))]
public class TracingRetriever : IProductRetriever
{
    private readonly IProductRetriever _inner;

    public TracingRetriever(IProductRetriever inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<Product> Retrieve(string category)
    {
        // Emit a trace span only when tracing is configured
        return _inner.Retrieve(category);
    }
}
```

The generated registration looks roughly like:

```csharp
// Generated — simplified for illustration
if (services.Any(d => d.ServiceType == typeof(TracingOptions)))
{
    services.TryAddTransient<IProductRetriever>(sp =>
        new TracingRetriever(sp.GetRequiredService<IProductRetriever>()));
}
```

**Key characteristics:**

- The check is a single O(n) scan of the `ServiceDescriptor` list at startup, not at every resolution. Once the extension method returns, the decision is permanent.
- If `TracingOptions` is not registered, the decorator is silently skipped and the next lower-order decorator becomes the outermost wrapper. There is no runtime penalty.
- This pattern cleanly supports environment-aware stacks: register `TracingOptions` in development or staging environments only, and the tracing decorator is wired in automatically without `#if` directives or conditional logic in `Program.cs`.

---

## Optional Dependencies with `[OptionalDependency]`

A decorator sometimes needs a dependency that may not be registered — most commonly a logger. Marking a constructor parameter with `[OptionalDependency]` tells the generator to emit `sp.GetService<T>()` (which returns `null` when not registered) rather than `sp.GetRequiredService<T>()` (which throws).

```csharp
[DecoratorOf(typeof(IProductRetriever), Order = 2)]
public class LoggingRetriever : IProductRetriever
{
    private readonly IProductRetriever _inner;
    private readonly ILogger<LoggingRetriever>? _logger;

    public LoggingRetriever(
        IProductRetriever inner,
        [OptionalDependency] ILogger<LoggingRetriever>? logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public IReadOnlyList<Product> Retrieve(string category)
    {
        _logger?.LogInformation("Retrieving products in category {Category}", category);
        return _inner.Retrieve(category);
    }
}
```

**Rules and diagnostics:**

- `[OptionalDependency]` is a parameter-level attribute, not a class-level one.
- The parameter **must** be declared as a nullable reference type (e.g., `ILogger?`). If the parameter is non-nullable, the generator raises **ZAI015** at build time — there is no point emitting `GetService<T>()` into a non-nullable slot.
- Any number of constructor parameters may be marked optional, not just one.

---

## Real-World Example: Three-Layer Product Catalog Stack

The following shows a complete, production-style registration for a product catalog service. The real implementation hits the database; a caching layer sits directly above it; a logging layer sits above that.

```csharp
using ZeroAlloc.Inject;
using Microsoft.Extensions.Logging;

namespace Catalog.Infrastructure;

public record Product(int Id, string Name, decimal Price);

public interface IProductCatalog
{
    IReadOnlyList<Product> GetByCategory(string category);
    Product? FindById(int id);
}

// --- Layer 1: Real implementation (database access) ---
[Scoped]
public class SqlProductCatalog : IProductCatalog
{
    // Accepts IDbConnection or similar via DI
    public IReadOnlyList<Product> GetByCategory(string category)
    {
        // Query database
        return [];
    }

    public Product? FindById(int id)
    {
        // Query database
        return null;
    }
}

// --- Layer 2: Caching (Order = 1, innermost decorator) ---
// Runs before the real implementation is consulted.
// On a cache hit, the database call never happens.
[DecoratorOf(typeof(IProductCatalog), Order = 1)]
public class CachingProductCatalog : IProductCatalog
{
    private readonly IProductCatalog _inner;
    private readonly Dictionary<string, IReadOnlyList<Product>> _categoryCache = new();
    private readonly Dictionary<int, Product?> _idCache = new();

    public CachingProductCatalog(IProductCatalog inner)
    {
        _inner = inner;
    }

    public IReadOnlyList<Product> GetByCategory(string category)
    {
        if (!_categoryCache.TryGetValue(category, out var cached))
        {
            cached = _inner.GetByCategory(category);
            _categoryCache[category] = cached;
        }
        return cached;
    }

    public Product? FindById(int id)
    {
        if (!_idCache.TryGetValue(id, out var cached))
        {
            cached = _inner.FindById(id);
            _idCache[id] = cached;
        }
        return cached;
    }
}

// --- Layer 3: Logging (Order = 2, outermost decorator) ---
// Wraps CachingProductCatalog. Sees every call from consumers.
// ILogger is optional — if not registered, the decorator still works silently.
[DecoratorOf(typeof(IProductCatalog), Order = 2)]
public class LoggingProductCatalog : IProductCatalog
{
    private readonly IProductCatalog _inner;
    private readonly ILogger<LoggingProductCatalog>? _logger;

    public LoggingProductCatalog(
        IProductCatalog inner,
        [OptionalDependency] ILogger<LoggingProductCatalog>? logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public IReadOnlyList<Product> GetByCategory(string category)
    {
        _logger?.LogDebug("GetByCategory({Category}) requested", category);
        var result = _inner.GetByCategory(category);
        _logger?.LogDebug("GetByCategory({Category}) returned {Count} product(s)", category, result.Count);
        return result;
    }

    public Product? FindById(int id)
    {
        _logger?.LogDebug("FindById({Id}) requested", id);
        var result = _inner.FindById(id);
        _logger?.LogDebug("FindById({Id}) → {Name}", id, result?.Name ?? "not found");
        return result;
    }
}
```

The generated extension method wires this up as:

```csharp
// Conceptual — actual output uses factory lambdas
new LoggingProductCatalog(          // caller receives this (Order = 2)
    new CachingProductCatalog(      // checks in-memory cache (Order = 1)
        new SqlProductCatalog()     // queries the database only on cache miss
    ),
    sp.GetService<ILogger<LoggingProductCatalog>>()  // optional
)
```

**What happens at runtime for each layer:**

1. A consumer resolves `IProductCatalog` and calls `GetByCategory("electronics")`.
2. `LoggingProductCatalog` logs the request and delegates to its inner service.
3. `CachingProductCatalog` checks the in-memory dictionary. On the first call, it misses and delegates to the real implementation. On subsequent calls for the same category, it returns the cached list immediately without reaching the database.
4. `SqlProductCatalog` queries the database and returns results only when the cache misses.

---

## Diagnostics Quick Reference

The generator reports decorator-related problems as build errors or warnings. None of them reach runtime.

| ID | Description |
|----|-------------|
| ZAI011 | Decorator has no matching interface parameter — the generator could not find a constructor parameter whose type is a registered service interface that the class also implements |
| ZAI012 | Decorated interface not registered as a service — the interface being wrapped has no registered implementation |
| ZAI013 | `[Decorator]` or `[DecoratorOf]` applied to an abstract or static class |
| ZAI016 | `[DecoratorOf]` interface not implemented by the class — the explicitly named interface is not in the class's interface list |
| ZAI017 | Two decorators for the same interface share the same `Order` value — ordering is unambiguous by design |
