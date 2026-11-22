namespace RoushTech.Rangifer.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

/// <summary>
/// The model-building blocks a migration changed relative to its parent migration. Thin migration
/// designers build their target model by replaying this chain from its base instead of storing a
/// full copy of the model.
/// </summary>
public sealed class ModelDelta
{
    private readonly ModelDelta? parent;
    private readonly List<BlockOperation<Action<ModelBuilder>, Action<EntityTypeBuilder>>> operations = [];
    private readonly Lazy<IReadOnlyList<KeyValuePair<string, Block<Action<ModelBuilder>, Action<EntityTypeBuilder>>>>> blocks;

    public ModelDelta(string migrationId)
    {
        MigrationId = migrationId;
        blocks = new(Resolve);
    }

    public ModelDelta(string migrationId, string parentMigrationId, ModelDelta parent)
        : this(migrationId)
    {
        if (!string.Equals(parent.MigrationId, parentMigrationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Migration '{migrationId}' expects the model delta of '{parentMigrationId}' as its parent, but was given '{parent.MigrationId}'.");
        }

        this.parent = parent;
    }

    public string MigrationId { get; }

    public void Set(string key, string? after, Action<ModelBuilder> build) =>
        Add(BlockOperation<Action<ModelBuilder>, Action<EntityTypeBuilder>>.Set(key, after, new OpaqueBlock<Action<ModelBuilder>, Action<EntityTypeBuilder>>(build)));

    public void SetEntity(string key, string? after, params Action<EntityTypeBuilder>[] members) =>
        Add(BlockOperation<Action<ModelBuilder>, Action<EntityTypeBuilder>>.Set(
            key,
            after,
            new EntityBlock<Action<ModelBuilder>, Action<EntityTypeBuilder>>(EntityKey.EntityName(key), members)));

    public EntityEdit Edit(string key, int parentLineCount, string parentHash)
    {
        var edit = new EntityEdit(this);
        Add(BlockOperation<Action<ModelBuilder>, Action<EntityTypeBuilder>>.EditMembers(
            key,
            new MemberEdit<Action<EntityTypeBuilder>>(parentLineCount, parentHash, edit.Operations)));
        return edit;
    }

    public void Move(string key, string? after) => Add(BlockOperation<Action<ModelBuilder>, Action<EntityTypeBuilder>>.Move(key, after));

    public void Remove(string key) => Add(BlockOperation<Action<ModelBuilder>, Action<EntityTypeBuilder>>.Remove(key));

    public void Apply(ModelBuilder modelBuilder)
    {
        foreach (var (_, block) in blocks.Value)
        {
            switch (block)
            {
                case OpaqueBlock<Action<ModelBuilder>, Action<EntityTypeBuilder>> opaque:
                    opaque.Value(modelBuilder);
                    break;
                case EntityBlock<Action<ModelBuilder>, Action<EntityTypeBuilder>> entity:
                    var entityTypeBuilder = modelBuilder.Entity(entity.EntityName);
                    foreach (var member in entity.Members)
                    {
                        member(entityTypeBuilder);
                    }

                    break;
            }
        }
    }

    internal void EnsureUnresolved()
    {
        if (blocks.IsValueCreated)
        {
            throw new InvalidOperationException($"The model delta for migration '{MigrationId}' has already been resolved.");
        }
    }

    private void Add(BlockOperation<Action<ModelBuilder>, Action<EntityTypeBuilder>> operation)
    {
        EnsureUnresolved();
        operations.Add(operation);
    }

    private IReadOnlyList<KeyValuePair<string, Block<Action<ModelBuilder>, Action<EntityTypeBuilder>>>> Resolve()
    {
        var sequence = new BlockSequence<Action<ModelBuilder>, Action<EntityTypeBuilder>>(parent?.blocks.Value ?? []);

        foreach (var operation in operations)
        {
            try
            {
                sequence.Apply(operation);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"The model delta for migration '{MigrationId}' does not apply on top of '{parent?.MigrationId}': {exception.Message}",
                    exception);
            }
        }

        return sequence.Blocks;
    }
}

/// <summary>
/// Line-level changes to one entity type's model block, by position in the parent migration's lines.
/// </summary>
public sealed class EntityEdit
{
    private readonly ModelDelta delta;

    internal EntityEdit(ModelDelta delta)
    {
        this.delta = delta;
    }

    internal List<MemberOperation<Action<EntityTypeBuilder>>> Operations { get; } = [];

    public EntityEdit Replace(int index, Action<EntityTypeBuilder> line) => Add(MemberOperationKind.Replace, index, line);

    public EntityEdit Insert(int index, Action<EntityTypeBuilder> line) => Add(MemberOperationKind.Insert, index, line);

    public EntityEdit Remove(int index) => Add(MemberOperationKind.Remove, index, null);

    private EntityEdit Add(MemberOperationKind kind, int index, Action<EntityTypeBuilder>? line)
    {
        delta.EnsureUnresolved();
        Operations.Add(new MemberOperation<Action<EntityTypeBuilder>>(kind, index, line));
        return this;
    }
}

internal static class EntityKey
{
    // Entity blocks are keyed "<pass>:<entity name>"; names can contain '#' (owned types), so no suffix is allowed.
    public static string EntityName(string key) =>
        TryEntityName(key) ?? throw new InvalidOperationException($"Model block '{key}' is not an entity type block.");

    public static string? TryEntityName(string key) =>
        key.Length > 2 && key[1] == ':' && key[0] is >= '1' and <= '9' ? key[2..] : null;
}
