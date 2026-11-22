namespace RoushTech.Rangifer.Tests;

using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore;
using RoushTech.Rangifer.EntityFrameworkCore.Design;
using Operation = RoushTech.Rangifer.EntityFrameworkCore.BlockOperation<string, string>;

public class DeltaPlannerTests
{
    [Test]
    public void UnchangedBlocks_PlanNoOperations()
    {
        var blocks = Blocks("a", "b", "c");

        Assert.That(DeltaPlanner.Plan(Parent(blocks), blocks), Is.Empty);
    }

    [Test]
    public void WithoutParent_SetsEveryBlockInOrder()
    {
        var operations = DeltaPlanner.Plan(null, Blocks("a", "b"));

        Assert.That(operations, Is.EqualTo(new[]
        {
            Operation.Set("a", null, new OpaqueBlock<string, string>("a-text")),
            Operation.Set("b", "a", new OpaqueBlock<string, string>("b-text")),
        }));
    }

    [Test]
    public void ReorderedBlocks_PlanOnlyMoves()
    {
        var operations = DeltaPlanner.Plan(Parent(Blocks("a", "b", "c")), Blocks("c", "a", "b"));

        Assert.That(operations.Select(operation => operation.Kind), Is.All.EqualTo(BlockOperationKind.Move));
    }

    [Test]
    public void EntityBlocks_AreSetByLine()
    {
        var operations = DeltaPlanner.Plan(null, [Entity(Line(1), Line(2))]);

        Assert.That(operations.Single().Value, Is.TypeOf<EntityBlock<string, string>>());
    }

    [Test]
    public void ChangedEntityLine_PlansOneReplace()
    {
        var parent = Entity(Line(1), Line(2), Line(3), Line(4), Line(5));
        var child = Entity(Line(1), Line(2), Line(30), Line(4), Line(5));

        var operations = DeltaPlanner.Plan(Planned(parent), [child]);

        Assert.Multiple(() =>
        {
            Assert.That(operations.Single().Kind, Is.EqualTo(BlockOperationKind.Edit));
            Assert.That(operations.Single().Edit?.Operations, Is.EqualTo(new[] { new MemberOperation<string>(MemberOperationKind.Replace, 2, Line(30)) }));
        });
    }

    [Test]
    public void EntityTheyCannotRebuildExactly_StaysWhole()
    {
        var odd = new ModelBlock("1:Blog", Entity(Line(1)).Text.Replace("    });", "  });", StringComparison.Ordinal));

        var operations = DeltaPlanner.Plan(null, [odd]);

        Assert.That(operations.Single().Value, Is.TypeOf<OpaqueBlock<string, string>>());
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

            var operations = DeltaPlanner.Plan(Parent(parent), child);

            Assert.That(DeltaPlanner.Matches(DeltaPlanner.Replay(Parent(parent), operations).Blocks, child), Is.True, $"iteration {iteration}");
        }
    }

    [Test]
    public void RandomLineEdits_ReplayToTheChild()
    {
        var random = new Random(20261122);
        for (var iteration = 0; iteration < 2_000; iteration++)
        {
            var parentLines = Enumerable.Range(0, random.Next(1, 15)).Select(_ => Line(random.Next(8))).ToList();
            var childLines = parentLines.Where(_ => random.Next(4) != 0).Select(line => random.Next(5) == 0 ? Line(random.Next(8, 20)) : line).ToList();
            for (var added = random.Next(0, 4); added > 0; added--)
            {
                childLines.Insert(random.Next(childLines.Count + 1), Line(random.Next(20)));
            }

            if (childLines.Count == 0)
            {
                childLines.Add(Line(0));
            }

            var parent = Planned(Entity([.. parentLines]));
            var child = new[] { Entity([.. childLines]) };

            var operations = DeltaPlanner.Plan(parent, child);

            Assert.That(DeltaPlanner.Matches(DeltaPlanner.Replay(parent, operations).Blocks, child), Is.True, $"iteration {iteration}");
        }
    }

    private static string Line(int number) => $"b.Property<int>(\"P{number}\")\n    .HasColumnType(\"INTEGER\");";

    private static ModelBlock Entity(params string[] lines) =>
        new("1:Blog", "modelBuilder.Entity(\"Blog\", b =>\n    {\n" + string.Join("\n\n", lines.Select(line => string.Join('\n', line.Split('\n').Select(part => "        " + part)))) + "\n    });");

    private static List<KeyValuePair<string, Block<string, string>>> Planned(ModelBlock block) =>
        [KeyValuePair.Create(block.Key, EntityLines.FromText(block.Key, block.Text))];

    private static List<KeyValuePair<string, Block<string, string>>> Parent(IEnumerable<ModelBlock> blocks) =>
        blocks.Select(block => KeyValuePair.Create(block.Key, (Block<string, string>)new OpaqueBlock<string, string>(block.Text))).ToList();

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
