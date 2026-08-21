using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using GitCommands;
using GitCommands.Dashboard;
using GitCommands.UserRepositoryHistory;
using GitExtUtils.GitUI.Theming;
using Microsoft;

namespace GitUI.CommandsDialogs.BrowseDialog;

public partial class FormRecentReposSettings : GitExtensionsForm
{
    private const int MinComboWidthAllowed = 30;
    private IList<Repository>? _repositoryHistory;
    private decimal _previousValue;

    public FormRecentReposSettings()
        : base(enablePositionRestore: true)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        InitializeComponent();
        InitializeComplete();

        _repositoryHistory = ThreadHelper.JoinableTaskFactory.Run(RepositoryHistoryManager.Locals.LoadRecentHistoryAsync);
        LoadSettings();
        RefreshRepos();
    }

    private void LoadSettings()
    {
        RecentReposSettingsSnapshot snapshot = RecentReposSettingsSnapshot.Load();

        SetShorteningStrategy(snapshot.ShorteningStrategy);
        hideTopRepositoriesFromRecentList.Checked = snapshot.HideTopRepositoriesFromRecentList;
        sortTopRepos.Checked = snapshot.SortTopRepos;
        sortRecentRepos.Checked = snapshot.SortRecentRepos;
        comboMinWidthEdit.Value = snapshot.RecentReposComboMinWidth;
        SetNumericUpDownValue(_NO_TRANSLATE_maxRecentRepositories, snapshot.MaxTopRepositories);
        SetNumericUpDownValue(_NO_TRANSLATE_RecentRepositoriesHistorySize, snapshot.RecentRepositoriesHistorySize);

        _previousValue = comboMinWidthEdit.Value;

        return;

        void SetNumericUpDownValue(NumericUpDown control, int value)
        {
            control.Value = Math.Min(Math.Max(control.Minimum, value), control.Maximum);
        }

        void SetShorteningStrategy(ShorteningRecentRepoPathStrategy strategy)
        {
            switch (strategy)
            {
                case ShorteningRecentRepoPathStrategy.None:
                    dontShortenRB.Checked = true;
                    break;
                case ShorteningRecentRepoPathStrategy.MostSignDir:
                    mostSigDirRB.Checked = true;
                    break;
                case ShorteningRecentRepoPathStrategy.MiddleDots:
                    middleDotRB.Checked = true;
                    break;
                default:
                    throw new Exception("Unhandled shortening strategy: " + strategy);
            }
        }
    }

    private void SaveSettings()
    {
        Validates.NotNull(_repositoryHistory);

        CurrentSnapshot().Save();

        ThreadHelper.JoinableTaskFactory.Run(() => RepositoryHistoryManager.Locals.SaveRecentHistoryAsync(_repositoryHistory));
    }

    private RecentReposSettingsSnapshot CurrentSnapshot()
        => new(
            GetShorteningStrategy(),
            hideTopRepositoriesFromRecentList.Checked,
            sortTopRepos.Checked,
            sortRecentRepos.Checked,
            (int)comboMinWidthEdit.Value,
            (int)_NO_TRANSLATE_maxRecentRepositories.Value,
            (int)_NO_TRANSLATE_RecentRepositoriesHistorySize.Value);

    private ShorteningRecentRepoPathStrategy GetShorteningStrategy()
    {
        if (dontShortenRB.Checked)
        {
            return ShorteningRecentRepoPathStrategy.None;
        }
        else if (mostSigDirRB.Checked)
        {
            return ShorteningRecentRepoPathStrategy.MostSignDir;
        }
        else if (middleDotRB.Checked)
        {
            return ShorteningRecentRepoPathStrategy.MiddleDots;
        }
        else
        {
            throw new Exception("Can not figure shortening strategy");
        }
    }

    private void RefreshRepos()
    {
        Validates.NotNull(_repositoryHistory);

        try
        {
            TopLB.BeginUpdate();
            RecentLB.BeginUpdate();

            TopLB.Items.Clear();
            RecentLB.Items.Clear();

            List<RecentRepoInfo> topRepos = [];
            List<RecentRepoInfo> recentRepos = [];

            // The preview renders the UNSAVED snapshot through the very splitter the real menus use.
            RecentRepoSplitter splitter = new(CurrentSnapshot().ToSplitterOptions(
                caption => TextRenderer.MeasureText(caption, TopLB.Font).Width));

            splitter.SplitRecentRepos(_repositoryHistory, topRepos, recentRepos);

            foreach (RecentRepoInfo repo in topRepos)
            {
                TopLB.Items.Add(GetRepositoryListViewItem(repo, repo.Repo.Anchor == Repository.RepositoryAnchor.AnchoredInTop));
            }

            foreach (RecentRepoInfo repo in recentRepos)
            {
                RecentLB.Items.Add(GetRepositoryListViewItem(repo, repo.Repo.Anchor == Repository.RepositoryAnchor.AnchoredInRecent));
            }

            SetComboWidth();
        }
        finally
        {
            TopLB.EndUpdate();
            RecentLB.EndUpdate();
        }
    }

    private static ListViewItem GetRepositoryListViewItem(RecentRepoInfo repo, bool anchored)
    {
        ListViewItem item = new(repo.Caption) { Tag = repo, ToolTipText = repo.Repo.Path };

        if (anchored)
        {
            item.Font = new Font(item.Font, FontStyle.Bold);
        }

        if (!Directory.Exists(repo.Repo.Path))
        {
            item.ForeColor = Color.Red.AdaptForeColor(item.BackColor);
        }

        return item;
    }

    private void SetComboWidth()
    {
        if (comboMinWidthEdit.Value == 0)
        {
            TopLB.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
            RecentLB.AutoResizeColumns(ColumnHeaderAutoResizeStyle.ColumnContent);
        }
        else
        {
            int width = Math.Max(MinComboWidthAllowed, (int)comboMinWidthEdit.Value);
            TopLB.Columns[0].Width = width;
            RecentLB.Columns[0].Width = width;
        }
    }

    private void sortTopRepos_CheckedChanged(object sender, EventArgs e)
    {
        RefreshRepos();
    }

    private void comboMinWidthEdit_ValueChanged(object sender, EventArgs e)
    {
        if (comboMinWidthEdit.Value == _previousValue)
        {
            return;
        }

        comboMinWidthEdit.Value = ComboWidthRule.Snap((int)_previousValue, (int)comboMinWidthEdit.Value);

        _previousValue = comboMinWidthEdit.Value;
        SetComboWidth();
    }

    private void Ok_Click(object sender, EventArgs e)
    {
        SaveSettings();
        Close();
    }

    private void Abort_Click(object sender, EventArgs e)
    {
        Close();
    }

    private void contextMenuStrip1_Opening(object sender, CancelEventArgs e)
    {
        if (GetSelectedRepos(sender, out List<RecentRepoInfo>? repos))
        {
            e.Cancel = false;

            foreach (RecentRepoInfo repo in repos)
            {
                (anchorToTopReposToolStripMenuItem.Enabled, anchorToRecentReposToolStripMenuItem.Enabled, removeAnchorToolStripMenuItem.Enabled) =
                    AnchorCommandAvailability.For(repo.Repo.Anchor);
            }
        }
        else
        {
            e.Cancel = true;
        }
    }

    private bool GetSelectedRepos(object? sender, [NotNullWhen(returnValue: true)] out List<RecentRepoInfo>? repos)
    {
        if (sender is ContextMenuStrip strip)
        {
            sender = strip.SourceControl;
        }
        else if (sender is ToolStripItem item)
        {
            return GetSelectedRepos(item.Owner, out repos);
        }

        ListView? lb;
        if (sender == TopLB)
        {
            lb = TopLB;
        }
        else if (sender == RecentLB)
        {
            lb = RecentLB;
        }
        else
        {
            lb = null;
        }

        repos = [];
        if (lb?.SelectedItems.Count > 0)
        {
            foreach (ListViewItem item in lb.SelectedItems)
            {
                if (item.Tag is RecentRepoInfo repo)
                {
                    repos.Add(repo);
                }
            }
        }

        return repos.Count != 0;
    }

    private void AllRecentLB_DoubleClick(object sender, EventArgs e)
    {
        AnchorToMostRecentRepositories(sender);
    }

    private void TopLB_DoubleClick(object sender, EventArgs e)
    {
        AnchorToLessRecentRepositories(sender);
    }

    private void anchorToMostToolStripMenuItem_Click(object sender, EventArgs e)
    {
        AnchorToMostRecentRepositories(sender);
    }

    private void AnchorToMostRecentRepositories(object sender)
    {
        if (GetSelectedRepos(sender, out List<RecentRepoInfo>? repos))
        {
            foreach (RecentRepoInfo repo in repos)
            {
                repo.Repo.Anchor = Repository.RepositoryAnchor.AnchoredInTop;
            }

            RefreshRepos();
        }
    }

    private void anchorToLessToolStripMenuItem_Click(object sender, EventArgs e)
    {
        AnchorToLessRecentRepositories(sender);
    }

    private void AnchorToLessRecentRepositories(object sender)
    {
        if (GetSelectedRepos(sender, out List<RecentRepoInfo>? repos))
        {
            foreach (RecentRepoInfo repo in repos)
            {
                repo.Repo.Anchor = Repository.RepositoryAnchor.AnchoredInRecent;
            }

            RefreshRepos();
        }
    }

    private void removeAnchorToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (GetSelectedRepos(sender, out List<RecentRepoInfo>? repos))
        {
            foreach (RecentRepoInfo repo in repos)
            {
                repo.Repo.Anchor = Repository.RepositoryAnchor.None;
            }

            RefreshRepos();
        }
    }

    private void removeRecentToolStripMenuItem_Click(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!GetSelectedRepos(sender, out List<RecentRepoInfo>? repos))
        {
            return;
        }

        Validates.NotNull(_repositoryHistory);

        // Like the anchor edits, removal is transactional: it mutates the in-memory list and is
        // persisted by OK's SaveRecentHistoryAsync - Cancel used to keep anchor changes back but
        // let removals through.
        foreach (RecentRepoInfo repo in repos)
        {
            _repositoryHistory.Remove(repo.Repo);
        }

        RefreshRepos();
    }

    private void listView_DrawItem(object sender, DrawListViewItemEventArgs e)
    {
        ListView listView = (ListView)sender;

        Rectangle rowBounds = e.Bounds;
        int leftMargin = e.Item.GetBounds(ItemBoundsPortion.Label).Left;
        Rectangle bounds = new(leftMargin, rowBounds.Top, rowBounds.Width - leftMargin, rowBounds.Height);

        e.Graphics.FillRectangle(SystemBrushes.Window, bounds);
        TextRenderer.DrawText(e.Graphics, e.Item.Text, listView.Font, bounds, SystemColors.ControlText,
                              TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.GlyphOverhangPadding | TextFormatFlags.VerticalCenter);
    }
}
