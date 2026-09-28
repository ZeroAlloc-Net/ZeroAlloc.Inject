using System.Collections.Generic;
using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

// Open generics closed over value types: a primitive, a nullable primitive, an enum, a user
// struct and a nullable struct. Each closed form is its own native instantiation under
// NativeAOT, unlike reference-type arguments, which share one canonical body; Nullable<T> is
// the shape dotnet/runtime#134799 hangs on, which is how ZeroAlloc.Cache#182 shipped past a
// smoke that only closed generics over reference types.
public interface IValueSlot<T>
{
    T Value { get; }
    void Set(T value);
    bool Holds(T value);
}

[Transient]
public sealed class ValueSlot<T> : IValueSlot<T>
{
    private T _value = default!;

    public T Value => _value;
    public void Set(T value) => _value = value;
    public bool Holds(T value) => EqualityComparer<T>.Default.Equals(_value, value);
}

public interface IValueRegistry<T>
{
    int Count { get; }
    void Add(T value);
    bool Contains(T value);
}

[Singleton]
public sealed class ValueRegistry<T> : IValueRegistry<T>
{
    private readonly List<T> _values = new();

    public int Count => _values.Count;
    public void Add(T value) => _values.Add(value);
    public bool Contains(T value) => _values.Contains(value);
}

// Takes every closed form as a constructor parameter. That is what the generator scans to emit
// a branch per closed form, and it exercises the generated constructor wiring.
[Transient]
public sealed class ValueTypeConsumer
{
    public ValueTypeConsumer(
        IValueSlot<int> count,
        IValueSlot<int?> limit,
        IValueSlot<SmokeColor> color,
        IValueSlot<SmokeKey> key,
        IValueSlot<SmokeKey?> optionalKey,
        IValueRegistry<int?> limits,
        IValueRegistry<SmokeKey> keys)
    {
        Count = count;
        Limit = limit;
        Color = color;
        Key = key;
        OptionalKey = optionalKey;
        Limits = limits;
        Keys = keys;
    }

    public IValueSlot<int> Count { get; }
    public IValueSlot<int?> Limit { get; }
    public IValueSlot<SmokeColor> Color { get; }
    public IValueSlot<SmokeKey> Key { get; }
    public IValueSlot<SmokeKey?> OptionalKey { get; }
    public IValueRegistry<int?> Limits { get; }
    public IValueRegistry<SmokeKey> Keys { get; }
}
