namespace RoushTech.Rangifer.EntityFrameworkCore.Design;

using RoushTech.Rangifer.EntityFrameworkCore;

internal static class DeltaPlanner
{
    private const int LineOperationCost = 16;
    private const int EditCost = 40;

    public static IReadOnlyList<TextOperation> Plan(
        IReadOnlyList<KeyValuePair<string, TextBlock>>? parent,
        IReadOnlyList<ModelBlock> child,
        bool lineEdits = true)
    {
        var parentBlocks = parent ?? [];
        var parentByKey = parentBlocks.ToDictionary(block => block.Key, block => block.Value, StringComparer.Ordinal);
        var childKeys = child.Select(block => block.Key).ToList();
        var childKeySet = childKeys.ToHashSet(StringComparer.Ordinal);
        var operations = new List<TextOperation>();

        foreach (var block in parentBlocks.Where(block => !childKeySet.Contains(block.Key)))
        {
            operations.Add(TextOperation.Remove(block.Key));
        }

        for (var index = 0; index < child.Count; index++)
        {
            var block = child[index];
            parentByKey.TryGetValue(block.Key, out var parentBlock);
            if (parentBlock is not null && EntityLines.Text(parentBlock) == block.Text)
            {
                continue;
            }

            var childBlock = lineEdits ? EntityLines.FromText(block.Key, block.Text) : new OpaqueBlock<string, string>(block.Text);
            if (parentBlock is EntityBlock<string, string> parentEntity
                && childBlock is EntityBlock<string, string> childEntity
                && PlanLines(parentEntity.Members, childEntity.Members) is { } edit
                && Cost(edit) < Cost(childEntity))
            {
                operations.Add(TextOperation.EditMembers(block.Key, edit));
                continue;
            }

            operations.Add(TextOperation.Set(block.Key, Predecessor(childKeys, index), childBlock));
        }

        // Each move pins the next key behind the already-correct prefix, so one forward pass settles the order.
        var sequence = Replay(parentBlocks, operations);
        for (var index = 0; index < childKeys.Count; index++)
        {
            if (sequence.Keys[index] == childKeys[index])
            {
                continue;
            }

            var move = TextOperation.Move(childKeys[index], Predecessor(childKeys, index));
            operations.Add(move);
            sequence.Apply(move);
        }

        if (!Matches(Replay(parentBlocks, operations).Blocks, child))
        {
            throw new InvalidOperationException("Rangifer planned a model delta that does not reproduce the migration's model blocks.");
        }

        return operations;
    }

    public static BlockSequence<string, string> Replay(IEnumerable<KeyValuePair<string, TextBlock>> parent, IEnumerable<TextOperation> operations)
    {
        var sequence = new BlockSequence<string, string>(parent, EntityLines.HashMatches);
        foreach (var operation in operations)
        {
            sequence.Apply(operation);
        }

        return sequence;
    }

    public static bool Matches(IReadOnlyList<KeyValuePair<string, TextBlock>> actual, IReadOnlyList<ModelBlock> expected) =>
        actual.Count == expected.Count
        && actual.Zip(expected).All(pair => pair.First.Key == pair.Second.Key && EntityLines.Text(pair.First.Value) == pair.Second.Text);

    // Operations use indexes into the list as it stands when each one runs. Walking the gaps between unchanged
    // lines from the end keeps every index pointing at the parent's original line.
    private static MemberEdit<string>? PlanLines(IReadOnlyList<string> parent, IReadOnlyList<string> child)
    {
        var common = new int[parent.Count + 1, child.Count + 1];
        for (var i = parent.Count - 1; i >= 0; i--)
        {
            for (var j = child.Count - 1; j >= 0; j--)
            {
                common[i, j] = parent[i] == child[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
            }
        }

        var gaps = new List<(int ParentStart, int ParentEnd, int ChildStart, int ChildEnd)>();
        int p = 0, c = 0, gapParent = 0, gapChild = 0;
        while (p < parent.Count || c < child.Count)
        {
            if (p < parent.Count && c < child.Count && parent[p] == child[c] && common[p, c] == common[p + 1, c + 1] + 1)
            {
                AddGap(p, c);
                p++;
                c++;
                gapParent = p;
                gapChild = c;
            }
            else if (c < child.Count && (p == parent.Count || common[p, c + 1] >= common[p + 1, c]))
            {
                c++;
            }
            else
            {
                p++;
            }
        }

        AddGap(p, c);

        var operations = new List<MemberOperation<string>>();
        foreach (var (parentStart, parentEnd, childStart, childEnd) in Enumerable.Reverse(gaps))
        {
            var replaced = Math.Min(parentEnd - parentStart, childEnd - childStart);
            for (var index = parentEnd - 1; index >= parentStart + replaced; index--)
            {
                operations.Add(new(MemberOperationKind.Remove, index, null));
            }

            for (var index = childEnd - 1; index >= childStart + replaced; index--)
            {
                operations.Add(new(MemberOperationKind.Insert, parentStart + replaced, child[index]));
            }

            for (var offset = replaced - 1; offset >= 0; offset--)
            {
                operations.Add(new(MemberOperationKind.Replace, parentStart + offset, child[childStart + offset]));
            }
        }

        return operations.Count == 0 ? null : new MemberEdit<string>(parent.Count, EntityLines.Hash(parent), operations);

        void AddGap(int parentEnd, int childEnd)
        {
            if (parentEnd > gapParent || childEnd > gapChild)
            {
                gaps.Add((gapParent, parentEnd, gapChild, childEnd));
            }
        }
    }

    private static int Cost(MemberEdit<string> edit) =>
        EditCost + edit.Operations.Sum(operation => LineOperationCost + (operation.Value?.Length ?? 0));

    private static int Cost(EntityBlock<string, string> block) =>
        block.Members.Sum(member => LineOperationCost + member.Length);

    private static string? Predecessor(IReadOnlyList<string> keys, int index) => index == 0 ? null : keys[index - 1];
}
