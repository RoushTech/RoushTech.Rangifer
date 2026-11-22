namespace RoushTech.Rangifer.Tests;

using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore;
using RoushTech.Rangifer.EntityFrameworkCore.Design;

public class DeltaPlannerTests
{
    [Test]
    public void UnchangedBlocks_PlanNoOperations()
    {
        var blocks = Blocks("a", "b", "c");

        Assert.That(DeltaPlanner.Plan(blocks, blocks), Is.Empty);
    }

    [Test]
    public void WithoutParent_SetsEveryBlockInOrder()
    {
        var operations = DeltaPlanner.Plan(null, Blocks("a", "b"));

        Assert.That(operations, Is.EqualTo(new[]
        {
            BlockOperation<string>.Set("a", null, "a-text"),
            BlockOperation<string>.Set("b", "a", "b-text"),
        }));
    }

    [Test]
    public void ReorderedBlocks_PlanOnlyMoves()
    {
        var operations = DeltaPlanner.Plan(Blocks("a", "b", "c"), Blocks("c", "a", "b"));

        Assert.That(operations.Select(operation => operation.Kind), Is.All.EqualTo(BlockOperationKind.Move));
    }

    // Planning verifies its own output, so this only has to drive enough shapes through it to catch a throw.
    [Test]
    public void RandomEdits_ReplayToTheChild()
    {
        var random = new Random(20261115);
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var parent = RandomBlocks(random);
            var child = Mutate(random, parent);

            var operations = DeltaPlanner.Plan(parent, child);

            Assert.That(DeltaPlanner.Matches(DeltaPlanner.Replay(parent, operations).Blocks, child), Is.True, $"iteration {iteration}");
        }
    }

    private static List<ModelBlock> Blocks(params string[] keys) => keys.Select(key => new ModelBlock(key, $"{key}-text")).ToList();

    private static List<ModelBlock> RandomBlocks(Random random) =>
        Enumerable.Range(0, random.Next(0, 12)).Select(index => new ModelBlock($"k{index}", $"text {random.Next(3)}")).ToList();

    private static List<ModelBlock> Mutate(Random random, List<ModelBlock> parent)
    {
        var child = parent.Where(_ => random.Next(5) != 0).ToList();
        for (var index = 0; index < child.Count; index++)
        {
            if (random.Next(4) == 0)
            {
                child[index] = child[index] with { Text = $"changed {random.Next(1_000)}" };
            }
        }

        for (var added = random.Next(0, 4); added > 0; added--)
        {
            child.Insert(random.Next(child.Count + 1), new ModelBlock($"n{random.Next(1_000_000)}", "new"));
        }

        child = child.DistinctBy(block => block.Key).ToList();
        if (random.Next(3) == 0 && child.Count > 1)
        {
            var moved = child[random.Next(child.Count)];
            child.Remove(moved);
            child.Insert(random.Next(child.Count + 1), moved);
        }

        return child;
    }
}
