using GitCommands.Rewrite;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;

namespace GitCommandsTests.Rewrite;

public sealed class HistoryRewriteModelTests
{
    private static readonly ObjectId Parent = ObjectId.Parse("aaaa111111111111111111111111111111111111");

    [Test]
    public void Sequence_editor_flips_the_first_pick()
    {
        HistoryRewrite.ReplaceFirstPickEditor(RewriteTodoAction.Edit).Should().Be("sed -i -re '0,/pick/s//e/'");
        HistoryRewrite.ReplaceFirstPickEditor(RewriteTodoAction.Reword).Should().Be("sed -i -re '0,/pick/s//r/'");
    }

    [TestCase(CommitKind.Fixup, "fixup! subject line")]
    [TestCase(CommitKind.Squash, "squash! subject line")]
    [TestCase(CommitKind.Amend, "amend! subject line")]
    public void Prefixed_subjects_match_the_autosquash_convention(CommitKind kind, string expected)
    {
        HistoryRewrite.PrefixedSubject(kind, "subject line").Should().Be(expected);
    }

    [Test]
    public void Interactive_rebase_targets_the_parent_with_autostash()
    {
        string command = HistoryRewrite.InteractiveRebaseOntoParent(Parent, supportRebaseMerges: true).ToString();

        command.Should().Contain("rebase");
        command.Should().Contain("-i");
        command.Should().Contain("--autostash");
        command.Should().Contain("--no-autosquash");
        command.Should().Contain(Parent.ToString());
    }

    [Test]
    public void Autosquash_variant_and_root_commit()
    {
        HistoryRewrite.InteractiveRebaseOntoParent(Parent, supportRebaseMerges: true, autoSquash: true).ToString()
            .Should().Contain("--autosquash");
        HistoryRewrite.InteractiveRebaseOntoParent(null, supportRebaseMerges: true).ToString()
            .Should().NotContain(Parent.ToString());
    }
}
