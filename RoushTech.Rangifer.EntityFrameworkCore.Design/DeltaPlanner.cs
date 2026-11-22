namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using RoushTech.Rangifer.EntityFrameworkCore;

internal static class DeltaPlanner
{
    public static IReadOnlyList<BlockOperation<string>> Plan(IReadOnlyList<ModelBlock>? parent, IReadOnlyList<ModelBlock> child)
    {
        var parentBlocks = parent ?? [];
        var parentTexts = parentBlocks.ToDictionary(block => block.Key, block => block.Text, StringComparer.Ordinal);
        var childKeys = child.Select(block => block.Key).ToList();
        var childKeySet = childKeys.ToHashSet(StringComparer.Ordinal);
        var operations = new List<BlockOperation<string>>();

        foreach (var block in parentBlocks.Where(block => !childKeySet.Contains(block.Key)))
        {
            operations.Add(BlockOperation<string>.Remove(block.Key));
        }

        for (var index = 0; index < child.Count; index++)
        {
            var block = child[index];
            if (!parentTexts.TryGetValue(block.Key, out var parentText) || parentText != block.Text)
            {
                operations.Add(BlockOperation<string>.Set(block.Key, Predecessor(childKeys, index), block.Text));
            }
        }

        // Each move pins the next key behind the already-correct prefix, so one forward pass settles the order.
        var sequence = Replay(parentBlocks, operations);
        for (var index = 0; index < childKeys.Count; index++)
        {
            if (sequence.Keys[index] == childKeys[index])
            {
                continue;
            }

            var move = BlockOperation<string>.Move(childKeys[index], Predecessor(childKeys, index));
            operations.Add(move);
            sequence.Apply(move);
        }

        if (!Matches(Replay(parentBlocks, operations).Blocks, child))
        {
            throw new InvalidOperationException("Rangifer planned a model delta that does not reproduce the migration's model blocks.");
        }

        return operations;
    }

    public static BlockSequence<string> Replay(IEnumerable<ModelBlock> parent, IEnumerable<BlockOperation<string>> operations)
    {
        var sequence = new BlockSequence<string>(parent.Select(block => KeyValuePair.Create(block.Key, block.Text)));
        foreach (var operation in operations)
        {
            sequence.Apply(operation);
        }

        return sequence;
    }

    public static bool Matches(IReadOnlyList<KeyValuePair<string, string>> actual, IReadOnlyList<ModelBlock> expected) =>
        actual.Count == expected.Count
        && actual.Zip(expected).All(pair => pair.First.Key == pair.Second.Key && pair.First.Value == pair.Second.Text);

    private static string? Predecessor(IReadOnlyList<string> keys, int index) => index == 0 ? null : keys[index - 1];
}
