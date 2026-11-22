namespace RoushTech.Rangifer.EntityFrameworkCore;

internal enum BlockOperationKind
{
    Set,
    Move,
    Remove,
    Edit,
}

internal enum MemberOperationKind
{
    Replace,
    Insert,
    Remove,
}

internal abstract record Block<TOpaque, TMember>;

internal sealed record OpaqueBlock<TOpaque, TMember>(TOpaque Value) : Block<TOpaque, TMember>;

internal sealed record EntityBlock<TOpaque, TMember>(string EntityName, IReadOnlyList<TMember> Members) : Block<TOpaque, TMember>;

internal sealed record MemberOperation<TMember>(MemberOperationKind Kind, int Index, TMember? Value);

internal sealed record MemberEdit<TMember>(int ParentCount, string ParentHash, IReadOnlyList<MemberOperation<TMember>> Operations);

internal sealed record BlockOperation<TOpaque, TMember>(
    BlockOperationKind Kind,
    string Key,
    string? After,
    Block<TOpaque, TMember>? Value,
    MemberEdit<TMember>? Edit)
{
    public static BlockOperation<TOpaque, TMember> Set(string key, string? after, Block<TOpaque, TMember> value) =>
        new(BlockOperationKind.Set, key, after, value, null);

    public static BlockOperation<TOpaque, TMember> Move(string key, string? after) => new(BlockOperationKind.Move, key, after, null, null);

    public static BlockOperation<TOpaque, TMember> Remove(string key) => new(BlockOperationKind.Remove, key, null, null, null);

    public static BlockOperation<TOpaque, TMember> EditMembers(string key, MemberEdit<TMember> edit) =>
        new(BlockOperationKind.Edit, key, null, null, edit);
}

// Shared by the runtime and the design-time tooling so both replay a delta chain identically.
internal sealed class BlockSequence<TOpaque, TMember>
{
    private readonly List<string> order = [];
    private readonly Dictionary<string, Block<TOpaque, TMember>> values = new(StringComparer.Ordinal);
    private readonly Func<IReadOnlyList<TMember>, string, bool>? parentHashMatches;

    // The runtime only has compiled members, so only the design-time tooling can check parent hashes.
    public BlockSequence(IEnumerable<KeyValuePair<string, Block<TOpaque, TMember>>> blocks, Func<IReadOnlyList<TMember>, string, bool>? parentHashMatches = null)
    {
        this.parentHashMatches = parentHashMatches;
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

    public IReadOnlyList<KeyValuePair<string, Block<TOpaque, TMember>>> Blocks =>
        order.Select(key => KeyValuePair.Create(key, values[key])).ToList();

    public void Apply(BlockOperation<TOpaque, TMember> operation)
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
            case BlockOperationKind.Edit:
                RequireExisting(operation.Key);
                values[operation.Key] = EditMembers(operation.Key, values[operation.Key], operation.Edit
                    ?? throw new InvalidOperationException($"Model block '{operation.Key}' is edited without member operations."));
                break;
            default:
                throw new InvalidOperationException($"Unknown model block operation '{operation.Kind}'.");
        }
    }

    private EntityBlock<TOpaque, TMember> EditMembers(string key, Block<TOpaque, TMember> block, MemberEdit<TMember> edit)
    {
        if (block is not EntityBlock<TOpaque, TMember> entity)
        {
            throw new InvalidOperationException($"Model block '{key}' is edited by line, but its parent stores it whole.");
        }

        if (entity.Members.Count != edit.ParentCount)
        {
            throw new InvalidOperationException(
                $"Model block '{key}' is edited by line against a parent with {edit.ParentCount} lines, but the parent has {entity.Members.Count}. An earlier designer was edited by hand.");
        }

        if (parentHashMatches is not null && !parentHashMatches(entity.Members, edit.ParentHash))
        {
            throw new InvalidOperationException(
                $"Model block '{key}' is edited by line against a parent with hash {edit.ParentHash}, but the parent's lines no longer match it. An earlier designer was edited by hand.");
        }

        var members = entity.Members.ToList();
        foreach (var operation in edit.Operations)
        {
            var limit = operation.Kind == MemberOperationKind.Insert ? members.Count : members.Count - 1;
            if (operation.Index < 0 || operation.Index > limit)
            {
                throw new InvalidOperationException($"Model block '{key}' has a line {operation.Kind} at {operation.Index}, outside its {members.Count} lines.");
            }

            switch (operation.Kind)
            {
                case MemberOperationKind.Replace:
                    members[operation.Index] = RequireValue(key, operation);
                    break;
                case MemberOperationKind.Insert:
                    members.Insert(operation.Index, RequireValue(key, operation));
                    break;
                case MemberOperationKind.Remove:
                    members.RemoveAt(operation.Index);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown line operation '{operation.Kind}'.");
            }
        }

        return entity with { Members = members };
    }

    private static TMember RequireValue(string key, MemberOperation<TMember> operation) =>
        operation.Value ?? throw new InvalidOperationException($"Model block '{key}' has a line {operation.Kind} without a value.");

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
