using GitCommands;
using GitCommands.Worktree;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.CommandsDialogs.WorktreeDialog;

public sealed partial class FormCreateWorktree : GitExtensionsDialog
{
    private readonly AsyncLoader _branchesLoader = new();

    private readonly string? _initialDirectoryPath;

    public string WorktreeDirectory => txtWorktreeDirectory.Text;
    public bool OpenWorktree => chkOpenWorktree.Checked;

    public IReadOnlyList<IGitRef>? ExistingBranches { get; set; }

    public FormCreateWorktree(IGitUICommands commands, string? path)
        : base(commands, enablePositionRestore: false)
    {
        InitializeComponent();

        tlpnlMain.AdjustWidthToSize(0, rbCheckoutExistingBranch, rbCreateNewBranch, lblNewWorktreeFolder);
        tlpnlCheckout.AdjustWidthToSize(0, rbCheckoutExistingBranch, rbCreateNewBranch, lblNewWorktreeFolder);

        MinimumSize = new Size(Width, PreferredMinimumHeight);

        InitializeComplete();
        _initialDirectoryPath = path;
    }

    private void FormCreateWorktree_Load(object sender, EventArgs e)
    {
        LoadBranchesAsync();

        UpdateWorktreePathAndValidateWorktreeOptions();

        Task LoadBranchesAsync()
        {
            string selectedBranch = UICommands.Module.GetSelectedBranch();
            ExistingBranches = Module.GetRefs(RefsFilter.Heads);
            cbxBranches.Text = TranslatedStrings.LoadingData;
            ThreadHelper.FileAndForget(async () =>
            {
                await _branchesLoader.LoadAsync(
                    () => ExistingBranches.Where(r => r.Name != selectedBranch).ToList(),
                    list =>
                    {
                        cbxBranches.Text = string.Empty;
                        cbxBranches.DataSource = list;
                        cbxBranches.DisplayMember = nameof(IGitRef.LocalName);
                    });

                await this.SwitchToMainThreadAsync();
                if (cbxBranches.Items.Count == 0)
                {
                    rbCreateNewBranch.Checked = true;
                    rbCheckoutExistingBranch.Enabled = false;
                }
                else
                {
                    rbCheckoutExistingBranch.Checked = true;
                }

                ValidateWorktreeOptions();
            });

            return Task.CompletedTask;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _branchesLoader.Dispose();

            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void cbxBranches_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            CreateWorktree();
        }
    }

    private void btnCreateWorktree_Click(object sender, EventArgs e)
    {
        CreateWorktree();
    }

    private void CreateWorktree()
    {
        string relativePath = Path.GetRelativePath(Module.WorkingDir, WorktreeDirectory).ToPosixPath().Quote();
        string? newBranchOption = WorktreeCreateModel.NewBranchOption(
            rbCreateNewBranch.Checked, txtNewBranchName.Text, (cbxBranches.SelectedItem as GitRef)?.Name);
        DialogResult = UICommands.Execute(new UICmd.GitCommandProcess(WorktreeCreateModel.CreateCommand(key => Module.GetEffectiveSetting(key), relativePath, newBranchOption!)), this) ? DialogResult.OK : DialogResult.None;
    }

    private void ValidateWorktreeOptions()
    {
        cbxBranches.Enabled = rbCheckoutExistingBranch.Checked;
        txtNewBranchName.Enabled = rbCreateNewBranch.Checked;
        btnCreateWorktree.Enabled = WorktreeCreateModel.IsBranchChoiceValid(
                rbCheckoutExistingBranch.Checked,
                cbxBranches.SelectedItem is not null,
                txtNewBranchName.Text,
                ExistingBranches!.Select(b => b.Name))
            && WorktreeCreateModel.IsTargetFolderValid(txtWorktreeDirectory.Text);
    }

    private void txtWorktreeDirectory_TextChanged(object sender, EventArgs e)
    {
        ValidateWorktreeOptions();
    }

    private void UpdateWorktreePathAndValidateWorktreeOptions(object sender, EventArgs e)
        => UpdateWorktreePathAndValidateWorktreeOptions();

    private void UpdateWorktreePathAndValidateWorktreeOptions()
    {
        UpdateWorktreePath();

        ValidateWorktreeOptions();

        return;

        void UpdateWorktreePath()
        {
            txtWorktreeDirectory.Text = WorktreeCreateModel.SuggestDirectory(
                _initialDirectoryPath,
                rbCheckoutExistingBranch.Checked
                    ? ((IGitRef?)cbxBranches.SelectedItem)?.Name ?? string.Empty
                    : txtNewBranchName.Text);
        }
    }
}
