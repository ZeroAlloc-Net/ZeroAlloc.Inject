using ZeroAlloc.Inject;

namespace ZeroAlloc.Inject.Sample.UseCases;

// --- Open-generic repository (standalone container resolves this at runtime) ---
public interface IInventory<T>
{
    void Add(T item);
    IReadOnlyList<T> All();
}

[Transient]
public class InMemoryInventory<T> : IInventory<T>
{
    private readonly List<T> _items = [];

    public void Add(T item) => _items.Add(item);
    public IReadOnlyList<T> All() => _items;
}
