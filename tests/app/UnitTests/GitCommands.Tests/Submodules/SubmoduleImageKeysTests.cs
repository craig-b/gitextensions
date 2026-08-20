using GitCommands.Submodules;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Submodules;

public sealed class SubmoduleImageKeysTests
{
    [TestCase(SubmoduleStatus.FastForward, true, SubmoduleImageKeys.SubmoduleRevisionUpDirty)]
    [TestCase(SubmoduleStatus.FastForward, false, SubmoduleImageKeys.SubmoduleRevisionUp)]
    [TestCase(SubmoduleStatus.Rewind, true, SubmoduleImageKeys.SubmoduleRevisionDownDirty)]
    [TestCase(SubmoduleStatus.Rewind, false, SubmoduleImageKeys.SubmoduleRevisionDown)]
    [TestCase(SubmoduleStatus.NewerTime, true, SubmoduleImageKeys.SubmoduleRevisionSemiUpDirty)]
    [TestCase(SubmoduleStatus.NewerTime, false, SubmoduleImageKeys.SubmoduleRevisionSemiUp)]
    [TestCase(SubmoduleStatus.OlderTime, true, SubmoduleImageKeys.SubmoduleRevisionSemiDownDirty)]
    [TestCase(SubmoduleStatus.OlderTime, false, SubmoduleImageKeys.SubmoduleRevisionSemiDown)]
    [TestCase(SubmoduleStatus.Unknown, true, SubmoduleImageKeys.SubmoduleDirty)]
    [TestCase(SubmoduleStatus.Unknown, false, SubmoduleImageKeys.FileStatusModified)]
    public void Both_variants_agree_for_known_statuses(SubmoduleStatus status, bool isDirty, string expected)
    {
        SubmoduleImageKeys.GetMenuImageKey(status, isDirty).Should().Be(expected);
        SubmoduleImageKeys.GetNodeImageKey(status, isDirty).Should().Be(expected);
    }

    [Test]
    public void The_variants_differ_only_for_a_dirty_submodule_with_null_status()
    {
        SubmoduleImageKeys.GetMenuImageKey(status: null, isDirty: true).Should().Be(SubmoduleImageKeys.FolderSubmodule);
        SubmoduleImageKeys.GetNodeImageKey(status: null, isDirty: true).Should().Be(SubmoduleImageKeys.SubmoduleDirty);

        SubmoduleImageKeys.GetMenuImageKey(status: null, isDirty: false).Should().Be(SubmoduleImageKeys.FolderSubmodule);
        SubmoduleImageKeys.GetNodeImageKey(status: null, isDirty: false).Should().Be(SubmoduleImageKeys.FileStatusModified);

        SubmoduleImageKeys.GetNodeImageKey(status: null, isDirty: null).Should().Be(SubmoduleImageKeys.FolderSubmodule);
    }
}
