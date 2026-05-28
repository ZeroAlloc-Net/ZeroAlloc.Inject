using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

public interface IFoo
{
    string DoStuff(string input);
}

[Transient]
public sealed class Foo : IFoo
{
    public string DoStuff(string input) => $"base:{input}";
}

[Decorator]
public sealed class LoggingFoo : IFoo
{
    private readonly IFoo _inner;
    public LoggingFoo(IFoo inner) => _inner = inner;
    public string DoStuff(string input) => $"[logged] {_inner.DoStuff(input)}";
}
