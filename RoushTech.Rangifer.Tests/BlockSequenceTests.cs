namespace RoushTech.Rangifer.Tests;

using NUnit.Framework;
using RoushTech.Rangifer.EntityFrameworkCore;
using Operation = RoushTech.Rangifer.EntityFrameworkCore.BlockOperation<string, string>;

public class BlockSequenceTests
{
    private static BlockSequence<string, string> Sequence(params string[] keys) =>
        new(keys.Select(key => KeyValuePair.Create(key, Opaque(key.ToUpperInvariant()))));

    private static Block<string, string> Opaque(string value) => new OpaqueBlock<string, string>(value);

    private static BlockSequence<string, string> Entity(params string[] lines) =>
        new([KeyValuePair.Create("1:E", (Block<string, string>)new EntityBlock<string, string>("E", lines))], (members, hash) => string.Join(",", members) == hash);

    private static IReadOnlyList<string> Lines(BlockSequence<string, string> sequence) =>
        ((EntityBlock<string, string>)sequence.Blocks.Single().Value).Members;

    private static Operation Edit(int parentCount, string parentHash, params MemberOperation<string>[] operations) =>
        Operation.EditMembers("1:E", new MemberEdit<string>(parentCount, parentHash, operations));

    [Test]
    public void Set_InsertsNewBlockAfterNamedBlock()
    {
        var sequence = Sequence("a", "c");

        sequence.Apply(Operation.Set("b", "a", Opaque("B")));

        Assert.That(sequence.Keys, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void Set_WithoutPredecessor_InsertsFirst()
    {
        var sequence = Sequence("a");

        sequence.Apply(Operation.Set("z", null, Opaque("Z")));

        Assert.That(sequence.Keys, Is.EqualTo(new[] { "z", "a" }));
    }

    [Test]
    public void Set_ExistingBlock_ReplacesValueAndPosition()
    {
        var sequence = Sequence("a", "b", "c");

        sequence.Apply(Operation.Set("a", "c", Opaque("changed")));

        Assert.That(sequence.Blocks, Is.EqualTo(new[]
        {
            KeyValuePair.Create("b", Opaque("B")),
            KeyValuePair.Create("c", Opaque("C")),
            KeyValuePair.Create("a", Opaque("changed")),
        }));
    }

    [Test]
    public void Move_KeepsValue()
    {
        var sequence = Sequence("a", "b", "c");

        sequence.Apply(Operation.Move("c", null));

        Assert.That(sequence.Blocks, Is.EqualTo(new[]
        {
            KeyValuePair.Create("c", Opaque("C")),
            KeyValuePair.Create("a", Opaque("A")),
            KeyValuePair.Create("b", Opaque("B")),
        }));
    }

    [Test]
    public void Remove_DropsBlock()
    {
        var sequence = Sequence("a", "b");

        sequence.Apply(Operation.Remove("a"));

        Assert.That(sequence.Keys, Is.EqualTo(new[] { "b" }));
    }

    [Test]
    public void OperationsOnMissingBlocks_Throw()
    {
        var sequence = Sequence("a");

        Assert.Multiple(() =>
        {
            Assert.That(() => sequence.Apply(Operation.Remove("missing")), Throws.InvalidOperationException);
            Assert.That(() => sequence.Apply(Operation.Move("missing", null)), Throws.InvalidOperationException);
            Assert.That(() => sequence.Apply(Operation.Set("b", "missing", Opaque("B"))), Throws.InvalidOperationException);
        });
    }

    [Test]
    public void DuplicateKeys_Throw() =>
        Assert.That(() => Sequence("a", "a"), Throws.InvalidOperationException);

    [Test]
    public void Edit_AppliesLineOperationsInOrder()
    {
        var sequence = Entity("a", "b", "c", "d");

        sequence.Apply(Edit(
            4,
            "a,b,c,d",
            new(MemberOperationKind.Remove, 3, null),
            new(MemberOperationKind.Insert, 2, "x"),
            new(MemberOperationKind.Replace, 0, "A")));

        Assert.That(Lines(sequence), Is.EqualTo(new[] { "A", "b", "x", "c" }));
    }

    [Test]
    public void Edit_WithWrongParentLineCount_Throws() =>
        Assert.That(
            () => Entity("a", "b").Apply(Edit(3, "a,b", new MemberOperation<string>(MemberOperationKind.Remove, 0, null))),
            Throws.InvalidOperationException.With.Message.Contains("edited by hand"));

    [Test]
    public void Edit_WithWrongParentHash_Throws() =>
        Assert.That(
            () => Entity("a", "changed").Apply(Edit(2, "a,b", new MemberOperation<string>(MemberOperationKind.Remove, 0, null))),
            Throws.InvalidOperationException.With.Message.Contains("edited by hand"));

    [Test]
    public void Edit_OutsideTheLines_Throws() =>
        Assert.That(
            () => Entity("a").Apply(Edit(1, "a", new MemberOperation<string>(MemberOperationKind.Replace, 1, "x"))),
            Throws.InvalidOperationException);

    [Test]
    public void Edit_OfWholeBlock_Throws() =>
        Assert.That(
            () => Sequence("a").Apply(Operation.EditMembers("a", new MemberEdit<string>(0, "", []))),
            Throws.InvalidOperationException.With.Message.Contains("stores it whole"));
}
