using System.Diagnostics.CodeAnalysis;
using GitCommands;
using GitCommands.Worktree;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitExtUtils.GitUI;
using ResourceManager;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.CommandsDialogs.WorktreeDialog;

public partial class FormManageWorktree : GitExtensionsDialog
{
    private IReadOnlyList<GitWorktree>? _worktrees;

    public bool ShouldRefreshRevisionGrid { get; private set; }

    public FormManageWorktree(IGitUICommands commands)
        : base(commands, enablePositionRestore: false)
    {
        InitializeComponent();

        Sha1.Width = DpiUtil.Scale(53);
        Worktrees.AutoGenerateColumns = false;

        Path.DataPropertyName = nameof(GitWorktree.Path);
        Type.DataPropertyName = nameof(GitWorktree.HeadType);
        Branch.DataPropertyName = nameof(GitWorktree.Branch);
        Sha1.DataPropertyName = nameof(GitWorktree.Sha1);

        Worktrees.Columns[3].DefaultCellStyle.Font = AppFonts.Monospace;
        Worktrees.Columns[3].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        Worktrees.Select();

        InitializeComplete();
    }

    /// <summary>
    /// If this is not null before showing the dialog the given
    /// remote name will be preselected in the listbox.
    /// </summary>
    public string? PreselectRemoteOnLoad { get; set; }

    protected override void OnRuntimeLoad(EventArgs e)
    {
        base.OnRuntimeLoad(e);

        Initialize();
    }

    private void Initialize()
    {
        _worktrees = Module.GetWorktrees();

        Worktrees.DataSource = _worktrees;

        Font? font = Worktrees.DefaultCellStyle.Font;
        Font deletedFont = new(font?.FontFamily ?? FontFamily.GenericSansSerif, font?.Size ?? 8.25f, (font?.Style ?? FontStyle.Regular) | FontStyle.Strikeout);

        for (int i = 0; i < Worktrees.Rows.Count; i++)
        {
            if (_worktrees[i].IsDeleted)
            {
                Worktrees.Rows[i].DefaultCellStyle.Font = deletedFont;
            }
        }

        buttonPruneWorktrees.Enabled = WorktreeManagePolicy.CanPrune(_worktrees);
    }

    private void buttonPruneWorktrees_Click(object sender, EventArgs e) => PruneWorktrees();

    private void PruneWorktrees()
    {
        UICommands.Execute(new UICmd.CommandLineProcess(Command: null, "worktree prune"), this);
        Initialize();
    }

    private void buttonDeleteSelectedWorktree_Click(object sender, EventArgs e)
    {
        if (!CanActOnSelectedWorkspace(out GitWorktree? workTree))
        {
            return;
        }

        if (UICommands.Execute(new UICmd.WorktreeDelete(workTree.Path), this))
        {
            Initialize();
        }
    }

    private void buttonOpenSelectedWorktree_Click(object sender, EventArgs e)
    {
        OpenSelectedWorktree();
    }

    private void WorktreesOnCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        OpenSelectedWorktree();
    }

    private void Worktrees_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            OpenSelectedWorktree();
        }
    }

    private void OpenSelectedWorktree()
    {
        if (!CanActOnSelectedWorkspace(out GitWorktree? workTree))
        {
            return;
        }

        if (UICommands.Execute(new UICmd.WorktreeSwitch(workTree.Path), this))
        {
            Close();
        }
    }

    private void Worktrees_SelectionChanged(object sender, EventArgs e)
    {
        buttonDeleteSelectedWorktree.Enabled = CanDeleteSelectedWorkspace();
        buttonOpenSelectedWorktree.Enabled = CanActOnSelectedWorkspace(out _);
    }

    private bool CanDeleteSelectedWorkspace()
        => _worktrees is not null && Worktrees.SelectedRows.Count > 0
            && WorktreeManagePolicy.CanDelete(_worktrees, Worktrees.SelectedRows[0].Index, UICommands.Module.WorkingDir);

    private bool CanActOnSelectedWorkspace([NotNullWhen(true)] out GitWorktree? workTree)
    {
        workTree = null;

        if (_worktrees is null || Worktrees.SelectedRows.Count == 0
            || !WorktreeManagePolicy.CanActOn(_worktrees, Worktrees.SelectedRows[0].Index, UICommands.Module.WorkingDir))
        {
            return false;
        }

        workTree = _worktrees[Worktrees.SelectedRows[0].Index];
        return true;
    }

    private void buttonCreateNewWorktree_Click(object sender, EventArgs e)
    {
        string basePath = _worktrees is { Count: > 0 }
            ? _worktrees[0].Path
            : UICommands.Module.WorkingDir;

        if (UICommands.Execute(new UICmd.WorktreeCreate(basePath), this))
        {
            ShouldRefreshRevisionGrid = true;
            Initialize();
        }
    }
}
