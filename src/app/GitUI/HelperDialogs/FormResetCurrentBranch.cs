using GitCommands;
using GitCommands.Git;
using GitCommands.Reset;
using GitExtensions.Extensibility.Git;
using GitExtUtils.GitUI.Theming;
using GitUIPluginInterfaces;
using ResourceManager;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.HelperDialogs;

public partial class FormResetCurrentBranch : GitModuleForm
{
    private readonly TranslationString _branchInfo = new("Reset branch '{0}' to revision:");
    private readonly TranslationString _resetHardWarning = new("You are about to discard ALL local changes, are you sure?");
    private readonly TranslationString _resetCaption = new("Reset branch");

    public enum ResetType
    {
        Soft,
        Mixed,
        Keep,
        Merge,
        Hard
    }

    public static FormResetCurrentBranch Create(IGitUICommands commands, GitRevision revision, ResetType resetType = ResetType.Soft)
        => new(commands, revision ?? throw new NotSupportedException(TranslatedStrings.NoRevision), resetType);

    private FormResetCurrentBranch(IGitUICommands commands, GitRevision revision, ResetType resetType)
        : base(commands)
    {
        Revision = revision;

        InitializeComponent();
        Soft.SetForeColorForBackColor();
        Mixed.SetForeColorForBackColor();
        Keep.ForeColor = Mixed.ForeColor;
        Merge.ForeColor = Mixed.ForeColor;
        Hard.SetForeColorForBackColor();
        InitializeComplete();

        switch (resetType)
        {
            case ResetType.Soft:
                Soft.Checked = true;
                break;
            case ResetType.Mixed:
                Mixed.Checked = true;
                break;
            case ResetType.Keep:
                Keep.Checked = true;
                break;
            case ResetType.Merge:
                Merge.Checked = true;
                break;
            case ResetType.Hard:
                Hard.Checked = true;
                break;
        }
    }

    public GitRevision Revision { get; }

    private void FormResetCurrentBranch_Load(object sender, EventArgs e)
    {
        _NO_TRANSLATE_BranchInfo.Text = string.Format(_branchInfo.Text, Module.GetSelectedBranch());
        commitSummaryUserControl1.Revision = Revision;
    }

    private ResetMode SelectedResetMode
        => Soft.Checked ? ResetMode.Soft
        : Mixed.Checked ? ResetMode.Mixed
        : Hard.Checked ? ResetMode.Hard
        : Merge.Checked ? ResetMode.Merge
        : ResetMode.Keep;

    private void Ok_Click(object sender, EventArgs e)
    {
        bool updateSubmodules = ResetCurrentBranchPolicy.ShouldUpdateSubmodules(
            AppSettings.UpdateSubmodulesOnCheckout, Module.HasSubmodules(), Revision.ObjectId, Module.GetCurrentCheckout());

        ResetMode mode = SelectedResetMode;

        if (ResetCurrentBranchPolicy.RequiresConfirmation(mode)
            && MessageBoxes.Show(this, _resetHardWarning.Text, _resetCaption.Text, MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) != DialogResult.Yes)
        {
            return;
        }

        ObjectId currentCheckout = Module.GetCurrentCheckout();
        bool success = FormProcess.ShowDialog(this, UICommands, arguments: Commands.Reset(mode, Revision.Guid, quiet: false), Module.WorkingDir, input: null, useDialogSettings: true);

        if (mode is ResetMode.Hard && success && currentCheckout != Revision.ObjectId)
        {
            UICommands.Execute(new UICmd.UpdateSubmodules(), this);
        }

        if (updateSubmodules)
        {
            UICommands.Execute(new UICmd.UpdateSubmodulesDialog(), this);
        }

        UICommands.RepoChangedNotifier.Notify();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Cancel_Click(object sender, EventArgs e)
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void FormResetCurrentBranch_HelpButtonClicked(object sender, System.ComponentModel.CancelEventArgs e)
    {
        string? helpSection = default;
        if (Soft.Checked)
        {
            helpSection = "--soft";
        }
        else if (Mixed.Checked)
        {
            helpSection = "--mixed";
        }
        else if (Keep.Checked)
        {
            helpSection = "--keep";
        }
        else if (Merge.Checked)
        {
            helpSection = "--merge";
        }
        else if (Hard.Checked)
        {
            helpSection = "--hard";
        }

        OsShellUtil.OpenUrlInDefaultBrowser(@$"https://git-scm.com/docs/git-reset#Documentation/git-reset.txt-{helpSection}");
    }
}
