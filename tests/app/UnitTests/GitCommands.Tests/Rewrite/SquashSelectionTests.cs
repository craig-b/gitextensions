using GitCommands.Rewrite;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitCommandsTests.Rewrite;

public sealed class SquashSelectionTests
{
    private static IReadOnlyList<GitRevision> Chain(int count)
    {
        // Build head -> parent -> ... newest-first, linked by ParentIds.
        List<GitRevision> revisions = [];
        GitRevision? child = null;
        for (int i = 0; i < count + 1; i++)
        {
            GitRevision revision = new(ObjectId.Random()) { Subject = $"commit {count - 1 - i}" };
            if (child is not null)
            {
                child.ParentIds = [revision.ObjectId];
            }

            revisions.Add(revision);
            child = revision;
        }

        // The extra tail revision only exists to give the oldest selected commit a parent.
        return revisions[..count];
    }

    [Test]
    public void Valid_contiguous_selection_ending_at_head_passes()
    {
        IReadOnlyList<GitRevision> selection = Chain(3);

        SquashSelection.Validate(selection, selection[0].ObjectId).Should().BeNull();
        SquashSelection.ResetTarget(selection).Should().Be(selection[^1].FirstParentId);
        SquashSelection.CombinedMessage(selection).Should().Be("commit 0\n\ncommit 1\n\ncommit 2");
    }

    [Test]
    public void Selection_not_at_head_or_with_gaps_is_rejected()
    {
        IReadOnlyList<GitRevision> selection = Chain(3);

        SquashSelection.Validate(selection, ObjectId.Random()).Should().Contain("HEAD");
        SquashSelection.Validate(selection, headCommit: null).Should().Contain("HEAD");
        SquashSelection.Validate([selection[0]], selection[0].ObjectId).Should().Contain("two commits");

        // Drop the middle commit: the chain breaks.
        SquashSelection.Validate([selection[0], selection[2]], selection[0].ObjectId).Should().Contain("contiguous");
    }

    [Test]
    public void Root_commit_cannot_be_squashed_into()
    {
        GitRevision head = new(ObjectId.Random());
        GitRevision root = new(ObjectId.Random());
        head.ParentIds = [root.ObjectId];

        SquashSelection.Validate([head, root], head.ObjectId).Should().Contain("root");
    }
}
