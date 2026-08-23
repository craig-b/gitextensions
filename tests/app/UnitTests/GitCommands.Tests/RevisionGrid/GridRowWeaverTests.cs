using GitExtensions.Extensibility.Git;
using GitUI.UserControls.RevisionGrid;
using GitUIPluginInterfaces;
using NSubstitute;
using ResourceManager;

namespace GitCommandsTests.RevisionGrid;

[TestFixture]
public class GridRowWeaverTests
{
    private static readonly ILookup<ObjectId, IGitRef> _noRefs = Array.Empty<IGitRef>().ToLookup(r => r.ObjectId);
    private static readonly ObjectId _zeroId = ObjectId.Parse("0000000000000000000000000000000000000000");

    private static GitRevision Commit(ObjectId? parent = null)
        => new(ObjectId.Random())
        {
            ParentIds = parent is null ? null : new ObjectId[] { parent.Value }
        };

    private static GitRevision Stash(ObjectId firstParent, string selector, ObjectId? untrackedParent = null)
        => new(ObjectId.Random())
        {
            ReflogSelector = selector,
            ParentIds = untrackedParent is null
                ? new ObjectId[] { firstParent, ObjectId.Random() }
                : new ObjectId[] { firstParent, ObjectId.Random(), untrackedParent.Value }
        };

    [Test]
    public void Create_builds_the_artificial_pair()
    {
        ObjectId head = ObjectId.Random();

        ArtificialCommits artificial = ArtificialCommits.Create("alice", "alice@example.com", head);

        artificial.WorkTree.ObjectId.Should().Be(ObjectId.WorkTreeId);
        artificial.WorkTree.ParentIds.Should().Equal(ObjectId.IndexId);
        artificial.WorkTree.Subject.Should().Be(TranslatedStrings.Workspace);
        artificial.WorkTree.Author.Should().Be("alice");
        artificial.Index.ObjectId.Should().Be(ObjectId.IndexId);
        artificial.Index.ParentIds.Should().Equal(head);
        artificial.Index.Subject.Should().Be(TranslatedStrings.Index);
    }

    [Test]
    public void Create_with_empty_repo_leaves_the_index_parentless()
    {
        ArtificialCommits artificial = ArtificialCommits.Create("alice", "alice@example.com", _zeroId);

        artificial.Index.ParentIds.Should().BeNull();
    }

    [Test]
    public void Weave_inserts_the_artificial_pair_just_before_head()
    {
        GitRevision head = Commit();
        GitRevision tip = Commit(head.ObjectId);
        ArtificialCommits artificial = ArtificialCommits.Create("alice", "a@example.com", head.ObjectId);
        GridRowWeaver weaver = new(head.ObjectId, stashes: null, artificial, _noRefs);

        IReadOnlyList<GitRevision> rows = weaver.Weave([tip, head]);

        rows.Should().Equal(tip, artificial.WorkTree, artificial.Index, head);
        weaver.HeadIsHandled.Should().BeTrue();
    }

    [Test]
    public void Weave_inserts_the_artificial_pair_first_when_the_repo_is_empty()
    {
        // Zero checkout (unborn HEAD): the artificial rows go before the first revision.
        GitRevision revision = Commit();
        ArtificialCommits artificial = ArtificialCommits.Create("alice", "a@example.com", _zeroId);
        GridRowWeaver weaver = new(_zeroId, stashes: null, artificial, _noRefs);

        IReadOnlyList<GitRevision> rows = weaver.Weave([revision]);

        rows.Should().Equal(artificial.WorkTree, artificial.Index, revision);
    }

    [Test]
    public void Weave_marks_head_handled_even_without_artificial_rows()
    {
        GitRevision head = Commit();
        GridRowWeaver weaver = new(head.ObjectId, stashes: null, artificial: null, _noRefs);

        IReadOnlyList<GitRevision> rows = weaver.Weave([head]);

        rows.Should().Equal(head);
        weaver.HeadIsHandled.Should().BeTrue();
    }

    [Test]
    public void Weave_leaves_head_unhandled_when_head_is_not_streamed()
    {
        // Filtered grid where HEAD is not visible: the caller inserts the artificial rows on
        // completion, attached to HEAD's parents.
        GitRevision head = Commit();
        ArtificialCommits artificial = ArtificialCommits.Create("alice", "a@example.com", head.ObjectId);
        GridRowWeaver weaver = new(head.ObjectId, stashes: null, artificial, _noRefs);

        IReadOnlyList<GitRevision> rows = weaver.Weave([Commit(), Commit()]);

        rows.Should().HaveCount(2);
        rows.Should().NotContain(artificial.WorkTree);
        weaver.HeadIsHandled.Should().BeFalse();
    }

    [Test]
    public void Weave_attaches_refs_to_streamed_revisions()
    {
        GitRevision revision = Commit();
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.ObjectId.Returns(revision.ObjectId);
        GridRowWeaver weaver = new(ObjectId.Random(), stashes: null, artificial: null, new[] { gitRef }.ToLookup(r => r.ObjectId));

        weaver.Weave([revision]);

        revision.Refs.Should().Equal(gitRef);
    }

    [Test]
    public void Weave_inserts_stash_rows_before_their_parent_commit()
    {
        GitRevision parent = Commit();
        GitRevision tip = Commit(parent.ObjectId);
        GitRevision stash = Stash(parent.ObjectId, "stash@{0}");
        GridStashes stashes = GridStashes.Prepare([stash], showReflogReferences: false, maxStashesWithUntrackedFiles: 5, _ => [])!;
        GridRowWeaver weaver = new(ObjectId.Random(), stashes, artificial: null, _noRefs);

        IReadOnlyList<GitRevision> rows = weaver.Weave([tip, parent]);

        rows.Should().Equal(tip, stash, parent);
    }

    [Test]
    public void Weave_inserts_the_untracked_companion_after_its_stash()
    {
        GitRevision parent = Commit();
        GitRevision untracked = Commit();
        GitRevision stash = Stash(parent.ObjectId, "stash@{0}", untracked.ObjectId);
        GridStashes stashes = GridStashes.Prepare([stash], showReflogReferences: false, maxStashesWithUntrackedFiles: 5, _ => [untracked])!;
        GridRowWeaver weaver = new(ObjectId.Random(), stashes, artificial: null, _noRefs);

        IReadOnlyList<GitRevision> rows = weaver.Weave([parent]);

        rows.Should().Equal(stash, untracked, parent);
        stash.ParentIds.Should().Equal(parent.ObjectId, untracked.ObjectId);
    }

    [Test]
    public void Weave_does_not_insert_a_stash_twice_when_its_commit_is_also_streamed()
    {
        // Reflogs etc can list the stash commit itself before its parent: the streamed row
        // takes the reflog selector and the stash is not woven in again at the parent.
        GitRevision parent = Commit();
        GitRevision stash = Stash(parent.ObjectId, "stash@{0}");
        GitRevision streamedStash = new(stash.ObjectId) { ParentIds = new[] { parent.ObjectId } };
        GridStashes stashes = GridStashes.Prepare([stash], showReflogReferences: false, maxStashesWithUntrackedFiles: 5, _ => [])!;
        GridRowWeaver weaver = new(ObjectId.Random(), stashes, artificial: null, _noRefs);

        IReadOnlyList<GitRevision> rows = weaver.Weave([streamedStash, parent]);

        rows.Should().Equal(streamedStash, parent);
        streamedStash.ReflogSelector.Should().Be("stash@{0}");
    }

    [Test]
    public void Weave_skips_the_streamed_duplicate_of_a_woven_stash()
    {
        // Date-order ties can list the parent before the stash tip: the stash (and its
        // untracked companion) is woven in at the parent, and the streamed copies arriving
        // later must be skipped - the graph does not deduplicate rows.
        GitRevision parent = Commit();
        GitRevision untracked = Commit();
        GitRevision stash = Stash(parent.ObjectId, "stash@{0}", untracked.ObjectId);
        GitRevision streamedStash = new(stash.ObjectId) { ParentIds = new ObjectId[] { parent.ObjectId } };
        GitRevision streamedUntracked = new(untracked.ObjectId);
        GridStashes stashes = GridStashes.Prepare([stash], showReflogReferences: false, maxStashesWithUntrackedFiles: 5, _ => [untracked])!;
        GridRowWeaver weaver = new(ObjectId.Random(), stashes, artificial: null, _noRefs);

        IReadOnlyList<GitRevision> first = weaver.Weave([parent]);
        IReadOnlyList<GitRevision> second = weaver.Weave([streamedStash, streamedUntracked]);

        first.Should().Equal(stash, untracked, parent);
        second.Should().BeEmpty();
    }

    [Test]
    public void Prepare_with_reflog_references_only_attaches_selectors()
    {
        // The reflog already shows every stash commit, so no rows are woven in; the streamed
        // rows only need their selector attached.
        GitRevision parent = Commit();
        GitRevision stash = Stash(parent.ObjectId, "stash@{0}");
        int resolveCalls = 0;

        GridStashes stashes = GridStashes.Prepare(
            [stash],
            showReflogReferences: true,
            maxStashesWithUntrackedFiles: 5,
            _ =>
            {
                resolveCalls++;
                return [];
            })!;
        GridRowWeaver weaver = new(ObjectId.Random(), stashes, artificial: null, _noRefs);

        IReadOnlyList<GitRevision> rows = weaver.Weave([parent]);

        rows.Should().Equal(parent);
        resolveCalls.Should().Be(0);
        stash.ParentIds.Should().HaveCount(2, "reflog mode must not rewrite stash parents");
    }

    [Test]
    public void Prepare_rewrites_stash_parents_to_the_displayed_set()
    {
        GitRevision parent = Commit();
        GitRevision emptyUntracked = Commit();
        GitRevision stashWithEmptyUntracked = Stash(parent.ObjectId, "stash@{0}", emptyUntracked.ObjectId);
        GitRevision plainStash = Stash(parent.ObjectId, "stash@{1}");

        // The resolver filters commits without changes: the empty "untracked" commit vanishes.
        GridStashes.Prepare([stashWithEmptyUntracked, plainStash], showReflogReferences: false, maxStashesWithUntrackedFiles: 5, _ => []);

        stashWithEmptyUntracked.ParentIds.Should().Equal(parent.ObjectId);
        plainStash.ParentIds.Should().Equal(parent.ObjectId);
    }

    [Test]
    public void Prepare_caps_the_untracked_lookup()
    {
        GitRevision parent = Commit();
        GitRevision untracked0 = Commit();
        GitRevision untracked1 = Commit();
        GitRevision first = Stash(parent.ObjectId, "stash@{0}", untracked0.ObjectId);
        GitRevision second = Stash(parent.ObjectId, "stash@{1}", untracked1.ObjectId);
        IList<ObjectId>? requested = null;

        GridStashes.Prepare(
            [first, second],
            showReflogReferences: false,
            maxStashesWithUntrackedFiles: 1,
            ids =>
            {
                requested = ids;
                return [untracked0];
            });

        requested.Should().Equal(untracked0.ObjectId);
        first.ParentIds.Should().Equal(parent.ObjectId, untracked0.ObjectId);
        second.ParentIds.Should().Equal(parent.ObjectId);
    }

    [Test]
    public void Prepare_returns_null_without_stashes()
    {
        GridStashes.Prepare([], showReflogReferences: false, maxStashesWithUntrackedFiles: 5, _ => []).Should().BeNull();
    }
}
