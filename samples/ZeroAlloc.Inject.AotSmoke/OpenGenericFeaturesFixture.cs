using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

// [Decorator] and Key on open generics closed over value types, and a constructor parameter of a
// concrete closed type. Microsoft DI refuses to close any of them under NativeAOT, so the generated
// registrations and both generated containers have to supply each closed form themselves.
public interface IValueStore<T>
{
    T Value { get; }
    void Set(T value);
    string Describe();
}

[Scoped]
public sealed class ValueStore<T> : IValueStore<T>
{
    private T _value = default!;

    public T Value => _value;
    public void Set(T value) => _value = value;
    public string Describe() => "store";
}

[Decorator]
public sealed class AuditedValueStore<T> : IValueStore<T>
{
    private readonly IValueStore<T> _inner;

    public AuditedValueStore(IValueStore<T> inner) => _inner = inner;

    public IValueStore<T> Inner => _inner;
    public T Value => _inner.Value;
    public void Set(T value) => _inner.Set(value);
    public string Describe() => $"audited({_inner.Describe()})";
}

public interface IKeyedCounter<T>
{
    int Next();
}

[Singleton(Key = "primary")]
public sealed class KeyedCounter<T> : IKeyedCounter<T>
{
    private int _count;

    public int Next() => ++_count;
}

// Surfaces the closed forms as constructor parameters for the generator's closed-usage scan.
// ValueRegistry<long> is the concrete closed type, registered as its own service.
[Transient]
public sealed class OpenGenericFeaturesConsumer
{
    public OpenGenericFeaturesConsumer(IValueStore<int> store, ValueRegistry<long> registry)
    {
        Store = store;
        Registry = registry;
    }

    public IValueStore<int> Store { get; }
    public ValueRegistry<long> Registry { get; }
}

// Stub: only the keyed registration exists, so this is never resolved. It surfaces
// IKeyedCounter<SmokeKey> for the closed-usage scan, which then emits the keyed closed form.
[Transient]
internal sealed class KeyedCounterUsage
{
    public KeyedCounterUsage(IKeyedCounter<SmokeKey> _) { }
}
