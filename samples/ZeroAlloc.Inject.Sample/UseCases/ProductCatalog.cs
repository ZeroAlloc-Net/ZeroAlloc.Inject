using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.Sample.UseCases;

// --- Domain model ---
public record Product(int Id, string Name, decimal Price);

// --- Service contract ---
public interface IProductRepository
{
    IReadOnlyList<Product> GetAll();
    Product? GetById(int id);
}

// --- Implementation ---
[Transient]
public class ProductRepository : IProductRepository
{
    private static readonly List<Product> _products =
    [
        new(1, "Laptop",  1_199.99m),
        new(2, "Mouse",      29.99m),
        new(3, "Keyboard",   79.99m),
    ];

    public IReadOnlyList<Product> GetAll() => _products;
    public Product? GetById(int id) => _products.Find(p => p.Id == id);
}

// --- Decorator: transparent logging wrapper ---
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
