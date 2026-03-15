using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Inject.Sample.UseCases;

// ============================================================
// ZeroAlloc.Inject Sample — E-Commerce use cases
// ============================================================
// This program exercises the three integration modes:
//   1. MS DI extension method  (AddZeroAllocInjectSampleServices)
//   2. Hybrid ZeroAlloc.Inject container  (BuildZeroAllocInjectServiceProvider)
//   3. Standalone ZeroAlloc.Inject provider  (new ...StandaloneServiceProvider())

Console.WriteLine("=== Use case 1: Product catalog (MS DI extension, decorator) ===");
{
    var services = new ServiceCollection();
    services.AddZeroAllocInjectSampleServices();
    using var provider = services.BuildServiceProvider();

    var repo = provider.GetRequiredService<IProductRepository>();
    var all = repo.GetAll();          // goes through LoggingProductRepository
    var one = repo.GetById(2);
    Console.WriteLine($"  Found: {one?.Name}");
}

Console.WriteLine();
Console.WriteLine("=== Use case 2: Order processing (hybrid container, scoped) ===");
{
    var services = new ServiceCollection();
    services.AddZeroAllocInjectSampleServices();
    var provider = services.BuildZeroAllocInjectServiceProvider();
    var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

    using var scope = scopeFactory.CreateScope();
    var orderSvc = scope.ServiceProvider.GetRequiredService<IOrderService>();
    orderSvc.AddToCart(1, 1);   // 1 Laptop
    orderSvc.AddToCart(2, 2);   // 2 Mice
    orderSvc.Checkout();
}

Console.WriteLine();
Console.WriteLine("=== Use case 3: Notifications (singleton email gateway) ===");
{
    var services = new ServiceCollection();
    services.AddZeroAllocInjectSampleServices();
    using var provider = services.BuildServiceProvider();

    var notifier = provider.GetRequiredService<INotificationService>();
    notifier.NotifyOrderPlaced("customer@example.com", 1_259.97m);

    // Singleton: same gateway instance across different resolutions
    var a = provider.GetRequiredService<IEmailGateway>();
    var b = provider.GetRequiredService<IEmailGateway>();
    Console.WriteLine($"  Same gateway instance: {ReferenceEquals(a, b)}");
}

Console.WriteLine();
Console.WriteLine("=== Use case 4: Inventory (standalone provider, open generics) ===");
{
    using var provider = new ZeroAlloc.Inject.Generated.ZeroAllocInjectSampleStandaloneServiceProvider();

    // Open generic resolved at runtime via MakeGenericType
    var inventoryType = typeof(IInventory<Product>);
    var inventory = (IInventory<Product>)provider.GetRequiredService(inventoryType);

    inventory.Add(new Product(10, "Headphones", 199.99m));
    inventory.Add(new Product(11, "Webcam",      89.99m));

    Console.WriteLine($"  Items in inventory: {inventory.All().Count}");
    foreach (var item in inventory.All())
        Console.WriteLine($"    - {item.Name}: {item.Price:C}");
}
