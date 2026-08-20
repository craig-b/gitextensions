using GitCommands.Reset;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Reset;

public sealed class ResetModelTests
{
    private static readonly ObjectId Target = ObjectId.Parse("1111111111111111111111111111111111111111");
    private static readonly ObjectId Head = ObjectId.Parse("2222222222222222222222222222222222222222");

    [Test]
    public void Only_hard_reset_requires_confirmation()
    {
        ResetCurrentBranchPolicy.RequiresConfirmation(ResetMode.Hard).Should().BeTrue();

        foreach (ResetMode mode in new[] { ResetMode.Soft, ResetMode.Mixed, ResetMode.Merge, ResetMode.Keep })
        {
            ResetCurrentBranchPolicy.RequiresConfirmation(mode).Should().BeFalse();
        }
    }

    [Test]
    public void Default_mode_protects_a_dirty_working_directory()
    {
        ResetCurrentBranchPolicy.ResolveDefaultMode(isDirtyWorkingDir: true).Should().Be(ResetMode.Soft);
        ResetCurrentBranchPolicy.ResolveDefaultMode(isDirtyWorkingDir: false).Should().Be(ResetMode.Hard);
    }

    [Test]
    public void Submodules_update_only_when_asked_present_and_moving()
    {
        ResetCurrentBranchPolicy.ShouldUpdateSubmodules(true, hasSubmodules: true, Target, Head).Should().BeTrue();

        ResetCurrentBranchPolicy.ShouldUpdateSubmodules(false, hasSubmodules: true, Target, Head).Should().BeFalse();
        ResetCurrentBranchPolicy.ShouldUpdateSubmodules(null, hasSubmodules: true, Target, Head).Should().BeFalse();
        ResetCurrentBranchPolicy.ShouldUpdateSubmodules(true, hasSubmodules: false, Target, Head).Should().BeFalse();
        ResetCurrentBranchPolicy.ShouldUpdateSubmodules(true, hasSubmodules: true, Target, Target).Should().BeFalse();
    }

    [TestCase(ResetMode.Soft, "--soft")]
    [TestCase(ResetMode.Mixed, "--mixed")]
    [TestCase(ResetMode.Keep, "--keep")]
    [TestCase(ResetMode.Merge, "--merge")]
    [TestCase(ResetMode.Hard, "--hard")]
    public void Help_anchor_matches_the_mode(ResetMode mode, string anchor)
    {
        ResetCurrentBranchPolicy.HelpAnchor(mode).Should().Be(anchor);
    }
}
