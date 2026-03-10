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

public interface IOrderService
{
    string PlaceOrder(string item);
}

[Transient]
public class OrderService : IOrderService
{
    private readonly IGreetingService _greetingService;

    public OrderService(IGreetingService greetingService)
    {
        _greetingService = greetingService;
    }

    public string PlaceOrder(string item) => $"Order placed for {item}. {_greetingService.Greet("Customer")}";
}

[Scoped]
public class ScopedWorker
{
    public string DoWork() => "Working...";
}
