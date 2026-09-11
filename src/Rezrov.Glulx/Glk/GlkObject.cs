namespace Rezrov.Glulx.Glk;

/// <summary>
/// One of Glk's opaque objects: a window, a stream, a file reference,
/// or a sound channel.
/// </summary>
/// <remarks>
/// [glk #opaque] A program refers to these by reference and never sees
/// inside them. [glk #opaque_registry] A virtual machine cannot hold a
/// native reference, so each object gets an integer identifier when it
/// is registered, which is what the game holds; zero is never one.
/// [glk #opaque_rocks] The rock is the game's own value, kept for it.
/// </remarks>
public abstract class GlkObject
{
    protected GlkObject(uint rock)
    {
        Rock = rock;
    }

    /// <summary>The identifier the game knows this object by.</summary>
    public uint Id { get; internal set; }

    /// <summary>
    /// [glk #opaque_rocks] The game's value for this object.
    /// </summary>
    public uint Rock { get; }
}

/// <summary>
/// The objects of one class, by identifier.
/// </summary>
/// <remarks>
/// [glk #opaque_iteration] The iterate functions walk every object of
/// a class once, in an order the library chooses. This registry hands
/// out identifiers in creation order and iterates in that order, which
/// is as good as any and easy to reason about in a test.
/// </remarks>
public sealed class GlkRegistry<T>
    where T : GlkObject
{
    private readonly SortedDictionary<uint, T> _objects = [];
    private uint _next = 1;

    public int Count => _objects.Count;

    /// <summary>Every object, in identifier order.</summary>
    public IEnumerable<T> All => _objects.Values;

    /// <summary>Gives the object an identifier and keeps it.</summary>
    public void Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.Id = _next++;
        _objects[item.Id] = item;
    }

    public void Remove(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _objects.Remove(item.Id);
    }

    /// <summary>The object with an identifier, or null for none.</summary>
    public T? Find(uint id) => _objects.TryGetValue(id, out var item) ? item : null;

    /// <summary>
    /// [glk #opaque_iteration] The first object, or the one after
    /// <paramref name="previous"/>, or null when there are no more.
    /// </summary>
    public T? Next(T? previous)
    {
        var after = previous?.Id ?? 0;
        foreach (var (id, item) in _objects)
        {
            if (id > after)
            {
                return item;
            }
        }

        return null;
    }
}
