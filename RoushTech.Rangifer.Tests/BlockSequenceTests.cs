namespace RoushTech.Rangifer.Tests;

using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore;

public class BlockSequenceTests
{
    private static BlockSequence<string> Sequence(params string[] keys) =>
        new(keys.Select(key => KeyValuePair.Create(key, key.ToUpperInvariant())));

    [Test]
    public void Set_InsertsNewBlockAfterNamedBlock()
    {
        var sequence = Sequence("a", "c");

        sequence.Apply(BlockOperation<string>.Set("b", "a", "B"));

        Assert.That(sequence.Keys, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void Set_WithoutPredecessor_InsertsFirst()
    {
        var sequence = Sequence("a");

        sequence.Apply(BlockOperation<string>.Set("z", null, "Z"));

        Assert.That(sequence.Keys, Is.EqualTo(new[] { "z", "a" }));
    }

    [Test]
    public void Set_ExistingBlock_ReplacesValueAndPosition()
    {
        var sequence = Sequence("a", "b", "c");

        sequence.Apply(BlockOperation<string>.Set("a", "c", "changed"));

        Assert.That(sequence.Blocks, Is.EqualTo(new[]
        {
            KeyValuePair.Create("b", "B"),
            KeyValuePair.Create("c", "C"),
            KeyValuePair.Create("a", "changed"),
        }));
    }

    [Test]
    public void Move_KeepsValue()
    {
        var sequence = Sequence("a", "b", "c");

        sequence.Apply(BlockOperation<string>.Move("c", null));

        Assert.That(sequence.Blocks, Is.EqualTo(new[]
        {
            KeyValuePair.Create("c", "C"),
            KeyValuePair.Create("a", "A"),
            KeyValuePair.Create("b", "B"),
        }));
    }

    [Test]
    public void Remove_DropsBlock()
    {
        var sequence = Sequence("a", "b");

        sequence.Apply(BlockOperation<string>.Remove("a"));

        Assert.That(sequence.Keys, Is.EqualTo(new[] { "b" }));
    }

    [Test]
    public void OperationsOnMissingBlocks_Throw()
    {
        var sequence = Sequence("a");

        Assert.Multiple(() =>
        {
            Assert.That(() => sequence.Apply(BlockOperation<string>.Remove("missing")), Throws.InvalidOperationException);
            Assert.That(() => sequence.Apply(BlockOperation<string>.Move("missing", null)), Throws.InvalidOperationException);
            Assert.That(() => sequence.Apply(BlockOperation<string>.Set("b", "missing", "B")), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void DuplicateKeys_Throw() =>
        Assert.That(() => Sequence("a", "a"), Throws.InvalidOperationException);
}
