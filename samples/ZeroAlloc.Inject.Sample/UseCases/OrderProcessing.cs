using ZeroAlloc.Inject;
using ZeroAlloc.Inject.Sample.UseCases;

namespace ZeroAlloc.Inject.Sample.UseCases;

// --- Scoped order context (one per request) ---
public interface IOrderContext
{
    void AddLine(Product product, int qty);
    decimal Total { get; }
    void Print();
}

[Scoped]
public class OrderContext : IOrderContext
{
    private readonly List<(Product Product, int Qty)> _lines = [];

    public void AddLine(Product product, int qty) => _lines.Add((product, qty));

    public decimal Total => _lines.Sum(l => l.Product.Price * l.Qty);

    public void Print()
    {
        foreach (var (product, qty) in _lines)
            Console.WriteLine($"  {qty}x {product.Name} @ {product.Price:C} = {product.Price * qty:C}");
        Console.WriteLine($"  Total: {Total:C}");
    }
}

// --- Order service (transient, depends on scoped context + product repo) ---
public interface IOrderService
{
    void AddToCart(int productId, int qty);
    void Checkout();
}

[Transient]
public class OrderService : IOrderService
{
    private readonly IProductRepository _products;
    private readonly IOrderContext _context;

    public OrderService(IProductRepository products, IOrderContext context)
    {
        _products = products;
        _context  = context;
    }

    public void AddToCart(int productId, int qty)
    {
        var product = _products.GetById(productId)
            ?? throw new InvalidOperationException($"Product {productId} not found.");
        _context.AddLine(product, qty);
    }

    public void Checkout()
    {
        Console.WriteLine("  Order summary:");
        _context.Print();
    }
}
