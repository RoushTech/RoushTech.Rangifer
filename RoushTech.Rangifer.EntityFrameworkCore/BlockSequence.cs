namespace RoushTech.Rangifer.EntityFrameworkCore;

internal enum BlockOperationKind
{
    Set,
    Move,
    Remove,
}

internal sealed record BlockOperation<T>(BlockOperationKind Kind, string Key, string? After, T? Value)
{
    public static BlockOperation<T> Set(string key, string? after, T value) => new(BlockOperationKind.Set, key, after, value);

    public static BlockOperation<T> Move(string key, string? after) => new(BlockOperationKind.Move, key, after, default);

    public static BlockOperation<T> Remove(string key) => new(BlockOperationKind.Remove, key, null, default);
}

// Shared by the runtime and the design-time tooling so both replay a delta chain identically.
internal sealed class BlockSequence<T>
{
    private readonly List<string> order = [];
    private readonly Dictionary<string, T> values = new(StringComparer.Ordinal);

    public BlockSequence()
    {
    }

    public BlockSequence(IEnumerable<KeyValuePair<string, T>> blocks)
    {
        foreach (var (key, value) in blocks)
        {
            if (!values.TryAdd(key, value))
            {
                throw new InvalidOperationException($"Model block '{key}' appears more than once.");
            }

            order.Add(key);
        }
    }

    public IReadOnlyList<string> Keys => order;

    public IReadOnlyList<KeyValuePair<string, T>> Blocks => order.Select(key => KeyValuePair.Create(key, values[key])).ToList();

    public void Apply(BlockOperation<T> operation)
    {
        switch (operation.Kind)
        {
            case BlockOperationKind.Set:
                if (operation.Value is not { } value)
                {
                    throw new InvalidOperationException($"Model block '{operation.Key}' is set without a value.");
                }

                order.Remove(operation.Key);
                Insert(operation.Key, operation.After);
                values[operation.Key] = value;
                break;
            case BlockOperationKind.Move:
                RequireExisting(operation.Key);
                order.Remove(operation.Key);
                Insert(operation.Key, operation.After);
                break;
            case BlockOperationKind.Remove:
                RequireExisting(operation.Key);
                order.Remove(operation.Key);
                values.Remove(operation.Key);
                break;
            default:
                throw new InvalidOperationException($"Unknown model block operation '{operation.Kind}'.");
        }
    }

    private void RequireExisting(string key)
    {
        if (!values.ContainsKey(key))
        {
            throw new InvalidOperationException($"Model block '{key}' does not exist.");
        }
    }

    private void Insert(string key, string? after)
    {
        if (after is null)
        {
            order.Insert(0, key);
            return;
        }

        var index = order.IndexOf(after);
        if (index < 0)
        {
            throw new InvalidOperationException($"Model block '{key}' is placed after '{after}', which does not exist.");
        }

        order.Insert(index + 1, key);
    }
}
