using GitCommands;
using GitCommands.Stash;
using GitExtensions.Extensibility.Git;
using GitExtUtils.GitUI;
using GitUIPluginInterfaces;
using Microsoft;
using ResourceManager;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.CommandsDialogs;

public sealed partial class FormStash : GitModuleForm
{
    private readonly TranslationString _currentWorkingDirChanges = new("Current working directory changes");
    private readonly TranslationString _noStashes = new("There are no stashes.");

    private readonly CancellationTokenSequence _viewChangesSequence = new();
    private readonly AsyncLoader _asyncLoader = new();
    private int _lastSelectedStashIndex = -1;

    public bool ManageStashes { get; set; }
    private GitStash? _currentWorkingDirStashItem;

    public FormStash(IGitUICommands commands, string? initialStash = null)
        : base(commands)
    {
        InitializeComponent();
        Stashed.Bind(() => RefreshAll());
        Stashed.BindContextMenu(View.CherryPickAllChanges, getSupportLinePatching: () => View.SupportLinePatching);
        View.ExtraDiffArgumentsChanged += delegate { StashedSelectedIndexChanged(this, EventArgs.Empty); };
        View.TopScrollReached += FileViewer_TopScrollReached;
        View.BottomScrollReached += FileViewer_BottomScrollReached;
        _lastSelectedStashIndex = StashSelectionPolicy.ParseInitialIndex(initialStash) ?? -1;

        CompleteTheInitialization();
    }

    private void CompleteTheInitialization()
    {
        KeyPreview = true;
        View.EscapePressed += () => DialogResult = DialogResult.Cancel;
        splitContainer1.SplitterDistance = DpiUtil.Scale(280);
        InitializeComplete();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && e.Modifiers == Keys.None)
        {
            Control focusedControl = this.FindFocusedControl();

            switch (focusedControl)
            {
                case ComboBox { DroppedDown: true } comboBox:
                    comboBox.DroppedDown = false;
                    break;
                case TextBoxBase { SelectionLength: > 0 } textBox:
                    textBox.SelectionLength = 0;
                    break;
                default:
                    DialogResult = DialogResult.Cancel;
                    break;
            }

            // do not let the modal form react itself on this preview of the Escape key press
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape && e.Modifiers == Keys.None)
        {
            // do not let the modal form react itself on this preview of the Escape key press
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        _asyncLoader.Dispose();
        if (disposing)
        {
            _viewChangesSequence.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void FormStashFormClosing(object sender, FormClosingEventArgs e)
    {
        AppSettings.StashKeepIndex = StashKeepIndex.Checked;
        AppSettings.IncludeUntrackedFilesInManualStash = chkIncludeUntrackedFiles.Checked;
    }

    private void FormStashLoad(object sender, EventArgs e)
    {
        StashKeepIndex.Checked = AppSettings.StashKeepIndex;
        chkIncludeUntrackedFiles.Checked = AppSettings.IncludeUntrackedFilesInManualStash;

        ResizeStashesWidth();
    }

    private void Initialize()
    {
        List<GitStash> stashedItems = [.. Module.GetStashes(noLocks: false)];

        _currentWorkingDirStashItem = new GitStash(-1, _currentWorkingDirChanges.Text);

        stashedItems.Insert(0, _currentWorkingDirStashItem);

        HotkeysEnabled = true;
        LoadHotkeys(HotkeySettingsName);

        Stashes.Text = "";
        StashMessage.Text = "";
        Stashes.SelectedItem = null;
        Stashes.ComboBox.DisplayMember = nameof(GitStash.Summary);
        Stashes.Items.Clear();
        foreach (GitStash stashedItem in stashedItems)
        {
            Stashes.Items.Add(stashedItem);
        }

        int selection = StashSelectionPolicy.ResolveStartupSelection(_lastSelectedStashIndex, ManageStashes, Stashes.Items.Count);
        if (_lastSelectedStashIndex > 0)
        {
            _lastSelectedStashIndex = -1;
        }
        else if (ManageStashes && Stashes.Items.Count > 1)
        {
            // First load done, show worktree on next refresh
            ManageStashes = false;
        }

        if (selection >= 0)
        {
            Stashes.SelectedIndex = selection;
        }
    }

    private void InitializeSoft()
    {
        GitStash? gitStash = Stashes.SelectedItem as GitStash;

        Stashed.ClearDiffs();

        Loading.Visible = true;
        Loading.IsAnimating = true;
        Stashes.Enabled = false;
        StashMessage.ReadOnly = true;
        if (gitStash is not null)
        {
            StashSelectionCapabilities capabilities = StashSelectionCapabilities.Evaluate(isWorkingDirItem: gitStash == _currentWorkingDirStashItem);
            StashMessage.ReadOnly = !capabilities.MessageEditable;
            Clear.Enabled = capabilities.DropAllowed;
            Apply.Enabled = capabilities.ApplyAllowed;
            Func<IReadOnlyList<GitItemStatus>> loadItems = gitStash == _currentWorkingDirStashItem
                ? () => Module.GetAllChangedFiles()
                : () => Module.GetStashDiffFiles(gitStash.Name);
            _asyncLoader.LoadAsync(loadItems, LoadGitItemStatuses);
        }
    }

    private void FileViewer_TopScrollReached(object? sender, EventArgs e)
    {
        Stashed.SelectPreviousVisibleItem();
        View.ScrollToBottom();
    }

    private void FileViewer_BottomScrollReached(object? sender, EventArgs e)
    {
        Stashed.SelectNextVisibleItem();
        View.ScrollToTop();
    }

    #region Hotkey commands

    public static readonly string HotkeySettingsName = "Stash";

    internal enum Command
    {
        NextStash = 0,
        PreviousStash = 1,
        Refresh = 2
    }

    private bool ChangeSelectedStash(bool next = true)
    {
        if (StashSelectionPolicy.Navigate(Stashes.SelectedIndex, Stashes.Items.Count, next) is not int index)
        {
            return false;
        }

        Stashes.SelectedIndex = index;
        return true;
    }

    protected override bool ExecuteCommand(int cmd)
    {
        switch ((Command)cmd)
        {
            case Command.NextStash: return ChangeSelectedStash(next: true);
            case Command.PreviousStash: return ChangeSelectedStash(next: false);
            case Command.Refresh: RefreshAll(); return true;
            default: return base.ExecuteCommand(cmd);
        }
    }

    #endregion

    private void LoadGitItemStatuses(IReadOnlyList<GitItemStatus> gitItemStatuses)
    {
        GitStash gitStash = (GitStash)Stashes.SelectedItem!;
        if (gitStash == _currentWorkingDirStashItem)
        {
            // FileStatusList has no interface for both worktree<-index, index<-HEAD at the same time
            // Must be handled when displaying
            StashDiffRevisions revisions = StashDiffRevisions.ForWorkingDir(Module.RevParse("HEAD"));
            if (revisions is { First: GitRevision headRev, SplitIndexRevision: GitRevision indexRev })
            {
                List<GitItemStatus> indexItems = [.. gitItemStatuses.Where(item => item.Staged == StagedStatus.Index)];
                List<GitItemStatus> workTreeItems = [.. gitItemStatuses.Where(item => item.Staged != StagedStatus.Index)];
                Stashed.SetStashDiffs(headRev, indexRev, ResourceManager.TranslatedStrings.Index, indexItems, revisions.Second, ResourceManager.TranslatedStrings.Workspace, workTreeItems);
            }
            else
            {
                // Likely a detached head
                Stashed.SetDiffs(revisions.First, revisions.Second, gitItemStatuses);
            }
        }
        else
        {
            ObjectId selectedId = Module.RevParse(gitStash.Name);
            if (selectedId.IsZero)
            {
                throw new InvalidOperationException("selectedId must not be zero");
            }

            StashDiffRevisions revisions = StashDiffRevisions.ForStash(Module.RevParse(gitStash.Name + "^"), selectedId);
            Stashed.SetDiffs(revisions.First, revisions.Second, gitItemStatuses);
        }

        Loading.Visible = false;
        Loading.IsAnimating = false;
        Stashes.Enabled = true;
    }

    private void ResizeStashesWidth()
    {
        const int spacingBetweenLabelAndComboBox = 5;
        int compensationForOddToolbarScaling = DpiUtil.IsFractional ? 1 : 0;
        Stashes.Width = toolStrip1.Width - DpiUtil.Scale(spacingBetweenLabelAndComboBox, ceiling: true) - compensationForOddToolbarScaling - showToolStripLabel.Width;
    }

    private void StashedSelectedIndexChanged(object sender, EventArgs e)
    {
        CancellationToken cancellationToken = _viewChangesSequence.Next();
        this.InvokeAndForget(() => View.ViewChangesAsync(Stashed.SelectedItem, cancellationToken), cancellationToken: cancellationToken);
        EnablePartialStash();
    }

    private void StashClick(object sender, EventArgs e)
    {
        using (WaitCursorScope.Enter())
        {
            string msg = StashSaveMessage.Normalize(StashMessage.Text);
            UICommands.Execute(new UICmd.StashSave(chkIncludeUntrackedFiles.Checked, StashKeepIndex.Checked, msg), this);
            Initialize();
        }
    }

    private void StashSelectedFiles_Click(object sender, EventArgs e)
    {
        using (WaitCursorScope.Enter())
        {
            string msg = StashSaveMessage.Normalize(StashMessage.Text);
            UICommands.Execute(new UICmd.StashSave(chkIncludeUntrackedFiles.Checked, StashKeepIndex.Checked, msg, Stashed.SelectedItems.Select(i => i.Item.Name).ToList()), this);
            Initialize();
        }
    }

    private void ClearClick(object sender, EventArgs e)
    {
        using (new WaitCursorScope())
        {
            string stashName = GetStashName();
            if (!AppSettings.DontConfirmStashDrop)
            {
                TaskDialogPage page = new()
                {
                    Text = TranslatedStrings.AreYouSure,
                    Caption = TranslatedStrings.StashDropConfirmTitle,
                    Heading = TranslatedStrings.CannotBeUndone,
                    Buttons = { TaskDialogButton.Yes, TaskDialogButton.No },
                    Icon = TaskDialogIcon.Information,
                    Verification = new TaskDialogVerificationCheckBox
                    {
                        Text = TranslatedStrings.DontShowAgain
                    },
                    SizeToContent = true
                };

                TaskDialogButton result = TaskDialog.ShowDialog(Handle, page);

                if (result == TaskDialogButton.Yes)
                {
                    _lastSelectedStashIndex = Stashes.SelectedIndex;
                    UICommands.Execute(new UICmd.StashDrop(stashName), this);
                    Initialize();
                }

                if (page.Verification.Checked)
                {
                    AppSettings.DontConfirmStashDrop = true;
                }
            }
            else
            {
                _lastSelectedStashIndex = Stashes.SelectedIndex;
                UICommands.Execute(new UICmd.StashDrop(stashName), this);
                Initialize();
            }
        }
    }

    private string GetStashName()
    {
        return ((GitStash)Stashes.SelectedItem!).Name;
    }

    private void ApplyClick(object sender, EventArgs e)
    {
        UICommands.Execute(new UICmd.StashApply(GetStashName()), this);
        Initialize();
    }

    private void StashesSelectedIndexChanged(object sender, EventArgs e)
    {
        EnablePartialStash();

        using (WaitCursorScope.Enter())
        {
            InitializeSoft();

            if (Stashes.SelectedItem is GitStash gitStash)
            {
                StashMessage.Text = gitStash != _currentWorkingDirStashItem ? gitStash.Message : "";
            }

            if (Stashes.Items.Count == 1)
            {
                StashMessage.Text = _noStashes.Text;
            }
        }
    }

    private void EnablePartialStash()
    {
        StashSelectedFiles.Enabled = StashSelectionCapabilities.PartialStashAllowed(
            isWorkingDirItem: Stashes.SelectedIndex == 0, hasSelectedFiles: Stashed.SelectedItems.Any());
    }

    private void Stashes_DropDown(object sender, EventArgs e)
    {
        Stashes.ResizeDropDownWidth(
            minWidth: Stashes.Size.Width,
            maxWidth: splitContainer1.Width - (2 * showToolStripLabel.Width),
            dpiScaleBounds: false);
    }

    private void RefreshAll(bool force = false)
    {
        if (!force && Stashes.SelectedIndex != 0)
        {
            // Worktree not select, not relevant
            return;
        }

        using (WaitCursorScope.Enter())
        {
            Initialize();
        }
    }

    private void FormStashShown(object sender, EventArgs e)
    {
        // shown when form is first displayed
        RefreshAll(force: true);
    }

    private void splitContainer1_SplitterMoved(object sender, SplitterEventArgs e)
    {
        ResizeStashesWidth();
    }

    private void FormStash_Resize(object sender, EventArgs e)
    {
        ResizeStashesWidth();
    }

    private void View_KeyUp(object sender, KeyEventArgs e)
    {
        // Close Stash form with escape button while pointer focus is in FileViewer(diff view)
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
    }
}
