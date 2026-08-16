using ResourceManager;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UserControls.RevisionGrid;

public sealed partial class EmptyRepoControl : GitModuleControl
{
    private readonly TranslationString _repoHasNoCommits = new("This repository does not yet contain any commits.");

    /// <summary>For VS designer.</summary>
    public EmptyRepoControl()
        : this(false)
    {
    }

    public EmptyRepoControl(bool isBareRepository)
    {
        InitializeComponent();
        InitializeComplete();

        lblEmptyRepository.Text = _repoHasNoCommits.Text;

        if (isBareRepository)
        {
            btnEditGitIgnore.Visible = false;
            btnOpenCommitForm.Visible = false;
        }
        else
        {
            btnEditGitIgnore.Click += (_, e) => UICommands.Execute(new UICmd.EditGitIgnore(LocalExcludes: false), this);
            btnOpenCommitForm.Click += (_, e) => UICommands.Execute(new UICmd.Commit(), this);
        }

        Dock = DockStyle.Fill;
    }
}
