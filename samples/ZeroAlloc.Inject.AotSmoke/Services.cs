using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

public interface IGreeter
{
    string Greet(string name);
}

[Singleton]
public sealed class Greeter : IGreeter
{
    public string Greet(string name) => $"Hello, {name}!";
}

public interface IWelcomeService
{
    string WelcomeUser(string name);
}

[Transient]
public sealed class WelcomeService : IWelcomeService
{
    private readonly IGreeter _greeter;

    // Generator-resolved ctor dependency — the emitted service provider must
    // correctly resolve this chain under AOT without reflection.
    public WelcomeService(IGreeter greeter) => _greeter = greeter;

    public string WelcomeUser(string name) => _greeter.Greet(name);
}
