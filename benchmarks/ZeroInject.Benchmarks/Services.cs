using ZeroInject;

namespace ZeroInject.Benchmarks;

// Simple service (no dependencies)
public interface ISimpleService
{
    void Execute();
}

[Transient]
public class SimpleService : ISimpleService
{
    public void Execute() { }
}

// Service with one dependency
public interface IServiceWithDep
{
    void Execute();
}

[Transient]
public class ServiceWithDep : IServiceWithDep
{
    private readonly ISimpleService _simple;

    public ServiceWithDep(ISimpleService simple)
    {
        _simple = simple;
    }

    public void Execute() => _simple.Execute();
}

// Service with multiple dependencies
public interface IServiceWithMultipleDeps
{
    void Execute();
}

[Transient]
public class ServiceWithMultipleDeps : IServiceWithMultipleDeps
{
    private readonly ISimpleService _simple;
    private readonly IServiceWithDep _withDep;

    public ServiceWithMultipleDeps(ISimpleService simple, IServiceWithDep withDep)
    {
        _simple = simple;
        _withDep = withDep;
    }

    public void Execute()
    {
        _simple.Execute();
        _withDep.Execute();
    }
}

// Singleton service
public interface ISingletonService
{
    string GetId();
}

[Singleton]
public class SingletonService : ISingletonService
{
    private readonly string _id = Guid.NewGuid().ToString();
    public string GetId() => _id;
}

// Scoped service
public interface IScopedService
{
    string GetId();
}

[Scoped]
public class ScopedService : IScopedService
{
    private readonly string _id = Guid.NewGuid().ToString();
    public string GetId() => _id;
}

// Multiple implementations for IEnumerable benchmarks
public interface IMultiService
{
    void Execute();
}

[Transient(As = typeof(IMultiService), AllowMultiple = true)]
public class MultiServiceA : IMultiService
{
    public void Execute() { }
}

[Transient(As = typeof(IMultiService), AllowMultiple = true)]
public class MultiServiceB : IMultiService
{
    public void Execute() { }
}

[Transient(As = typeof(IMultiService), AllowMultiple = true)]
public class MultiServiceC : IMultiService
{
    public void Execute() { }
}
