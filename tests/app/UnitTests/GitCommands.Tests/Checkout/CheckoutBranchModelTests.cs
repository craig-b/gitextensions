using GitCommands;
using GitCommands.Checkout;
using GitCommands.Git;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Checkout;

public sealed class CheckoutBranchModelTests
{
    [Test]
    public void Options_build_the_checkout_command()
    {
        CheckoutBranchOptions options = new("origin/feature", IsRemote: true, LocalChangesAction.Merge, CheckoutNewBranchMode.Create, "feature");

        options.ToCommand().Arguments.ToString().Should().Be("checkout --merge -b \"feature\" --track \"origin/feature\"");
    }

    [Test]
    public void Remote_branch_names_resolve_with_tracking_and_suggestion()
    {
        RemoteBranchCheckoutNames names = RemoteBranchCheckoutNames.Resolve(
            "origin/feature/x",
            ["origin", "upstream"],
            getLocalTrackingBranchName: (remote, branch) => "feature/x",
            localBranchExists: name => name == "feature/x");

        names.RemoteName.Should().Be("origin");
        names.LocalBranchName.Should().Be("feature/x");
        names.SuggestedNewBranchName.Should().Be("origin_feature/x");
        names.LocalBranchExists.Should().BeTrue();
    }

    [Test]
    public void Suggested_name_uniquifies_with_the_historical_seed()
    {
        HashSet<string> existing = ["main", "origin_main", "origin_main_2"];

        RemoteBranchCheckoutNames names = RemoteBranchCheckoutNames.Resolve(
            "origin/main",
            ["origin"],
            getLocalTrackingBranchName: (_, _) => "main",
            localBranchExists: existing.Contains);

        names.SuggestedNewBranchName.Should().Be("origin_main_3");
        names.LocalBranchExists.Should().BeTrue();
    }

    [Test]
    public void Fast_forward_check_compares_the_merge_base()
    {
        ObjectId local = ObjectId.Parse("aaaa000000000000000000000000000000000000");
        ObjectId other = ObjectId.Parse("bbbb000000000000000000000000000000000000");

        ResetBranchFastForwardCheck.Evaluate(local, mergeBaseId: local).IsFastForward.Should().BeTrue();

        ResetBranchFastForwardCheck notFastForward = ResetBranchFastForwardCheck.Evaluate(local, mergeBaseId: other);
        notFastForward.IsFastForward.Should().BeFalse();
        notFastForward.MergeBaseDisplay.Should().Be(other.ToShortString());

        ResetBranchFastForwardCheck.Evaluate(local, ObjectId.Parse(new string('0', 40))).MergeBaseDisplay.Should().Be("merge base");
    }

    [Test]
    public void Local_changes_downgrade_when_there_is_nothing_to_protect()
    {
        CheckoutLocalChangesPolicy.Resolve(LocalChangesAction.Stash, setAsDefaultRequested: false, dialogVisible: true, useDefaultActionSetting: false, hasUncommittedChanges: true)
            .EffectiveAction.Should().Be(LocalChangesAction.Stash);

        CheckoutLocalChangesPolicy.Resolve(LocalChangesAction.Stash, setAsDefaultRequested: false, dialogVisible: true, useDefaultActionSetting: false, hasUncommittedChanges: false)
            .EffectiveAction.Should().Be(LocalChangesAction.DontChange);

        CheckoutLocalChangesPolicy.Resolve(LocalChangesAction.Merge, setAsDefaultRequested: false, dialogVisible: false, useDefaultActionSetting: false, hasUncommittedChanges: true)
            .EffectiveAction.Should().Be(LocalChangesAction.DontChange, because: "a dialog-less checkout without the use-default setting must not touch changes");

        CheckoutLocalChangesPolicy.Resolve(LocalChangesAction.Merge, setAsDefaultRequested: false, dialogVisible: false, useDefaultActionSetting: true, hasUncommittedChanges: true)
            .EffectiveAction.Should().Be(LocalChangesAction.Merge);
    }

    [Test]
    public void Reset_never_persists_as_the_default_action()
    {
        CheckoutLocalChangesPolicy.Resolve(LocalChangesAction.Reset, setAsDefaultRequested: true, dialogVisible: true, useDefaultActionSetting: false, hasUncommittedChanges: true)
            .PersistAsDefault.Should().BeFalse();

        CheckoutLocalChangesPolicy.Resolve(LocalChangesAction.Stash, setAsDefaultRequested: true, dialogVisible: true, useDefaultActionSetting: false, hasUncommittedChanges: true)
            .PersistAsDefault.Should().BeTrue();
    }

    [Test]
    public void Dialog_shows_unless_a_safe_direct_checkout_is_possible()
    {
        CheckoutBranchCandidates.ShouldShowDialog(alwaysShowDialogSetting: false, localBranchSelected: true, hasUncommittedChanges: false, useDefaultActionSetting: false)
            .Should().BeFalse();
        CheckoutBranchCandidates.ShouldShowDialog(alwaysShowDialogSetting: true, localBranchSelected: true, hasUncommittedChanges: false, useDefaultActionSetting: false)
            .Should().BeTrue();
        CheckoutBranchCandidates.ShouldShowDialog(alwaysShowDialogSetting: false, localBranchSelected: false, hasUncommittedChanges: false, useDefaultActionSetting: false)
            .Should().BeTrue(because: "remote checkouts always show the dialog");
        CheckoutBranchCandidates.ShouldShowDialog(alwaysShowDialogSetting: false, localBranchSelected: true, hasUncommittedChanges: true, useDefaultActionSetting: false)
            .Should().BeTrue();
        CheckoutBranchCandidates.ShouldShowDialog(alwaysShowDialogSetting: false, localBranchSelected: true, hasUncommittedChanges: true, useDefaultActionSetting: true)
            .Should().BeFalse();
    }

    [Test]
    public void Branch_intersection_filters_detached_heads_and_HEAD_pseudo_branches()
    {
        ObjectId first = ObjectId.Parse("aaaa000000000000000000000000000000000000");
        ObjectId second = ObjectId.Parse("bbbb000000000000000000000000000000000000");

        Dictionary<ObjectId, string[]> containing = new()
        {
            [first] = ["main", "feature/x", "origin/HEAD", "(HEAD detached at 1234567)"],
            [second] = ["main", "origin/HEAD"],
        };

        IReadOnlyList<string> result = CheckoutBranchCandidates.IntersectBranchesContainingCommits(
            [first, second], objectId => containing[objectId]);

        result.Should().Equal("main");
    }
}
