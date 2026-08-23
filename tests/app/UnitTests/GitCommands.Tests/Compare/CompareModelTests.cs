using GitCommands.Compare;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Compare;

public sealed class CompareModelTests
{
    private static readonly ObjectId A = ObjectId.Parse("aaaa111111111111111111111111111111111111");
    private static readonly ObjectId B = ObjectId.Parse("bbbb222222222222222222222222222222222222");
    private static readonly ObjectId Head = ObjectId.Parse("cccc333333333333333333333333333333333333");
    private static readonly ObjectId Base = ObjectId.Parse("dddd444444444444444444444444444444444444");
    private static readonly ObjectId Zero = ObjectId.Parse("0000000000000000000000000000000000000000");

    [Test]
    public void Merge_base_resolves_for_a_regular_pair()
    {
        CompareRevisions.ResolveMergeBase(A, B, () => Head, (x, y) => Base).Should().Be(Base);
    }

    [Test]
    public void Artificial_revisions_map_to_the_current_head()
    {
        ObjectId? mergeBase = CompareRevisions.ResolveMergeBase(
            ObjectId.WorkTreeId, B, () => Head, (x, y) =>
            {
                x.Should().Be(Head);
                return Base;
            });

        mergeBase.Should().Be(Base);
    }

    [Test]
    public void Merge_base_is_null_for_same_or_unresolvable_sides()
    {
        CompareRevisions.ResolveMergeBase(A, A, () => Head, (_, _) => Base).Should().BeNull();
        CompareRevisions.ResolveMergeBase(ObjectId.WorkTreeId, ObjectId.IndexId, () => Head, (_, _) => Base).Should().BeNull();
        CompareRevisions.ResolveMergeBase(Zero, B, () => Head, (_, _) => Base).Should().BeNull();
        CompareRevisions.ResolveMergeBase(A, B, () => Head, (_, _) => Zero).Should().BeNull();
    }

    [Test]
    public void Effective_base_follows_the_merge_base_option()
    {
        CompareRevisions.ResolveBase(A, Base, compareToMergeBase: true).Should().Be(Base);
        CompareRevisions.ResolveBase(A, Base, compareToMergeBase: false).Should().Be(A);
        CompareRevisions.ResolveBase(A, null, compareToMergeBase: true).Should().Be(A);
    }

    [Test]
    public void Working_directory_gates()
    {
        CompareRevisions.DirDiffAllowed(ObjectId.WorkTreeId).Should().BeFalse();
        CompareRevisions.DirDiffAllowed(A).Should().BeTrue();
        CompareRevisions.CanCompareToWorkingDirectory(ObjectId.WorkTreeId).Should().BeFalse();
        CompareRevisions.CanCompareToWorkingDirectory(A).Should().BeTrue();
    }
}
