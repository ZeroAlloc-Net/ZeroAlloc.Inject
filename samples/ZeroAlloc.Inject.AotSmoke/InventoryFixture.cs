using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.AotSmoke;

public interface IInventory<T>
{
    string Describe();
}

[Transient]
public sealed class InMemoryInventory<T> : IInventory<T>
{
    public string Describe() => $"InMemoryInventory<{typeof(T).Name}>";
}

public sealed record Product(int Id, string Name);

// Stub: surfaces IInventory<Product> as a ctor parameter so the generator's
// compile-time closed-usage scan emits a typeof(IInventory<Product>) branch
// in the type-switch resolver. Internal because it's never resolved at
// runtime — its only purpose is to exist as a syntactic touchpoint for the
// generator. Per docs/advanced.md:248-256, without this ZAI018 fires and
// the closed form is not resolvable from the standalone/hybrid container.
[Transient]
internal sealed class InventoryConsumer
{
    public InventoryConsumer(IInventory<Product> _) { }
}
