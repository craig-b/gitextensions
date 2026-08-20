using System.ComponentModel;
using GitCommands;
using GitCommands.Settings.Pages;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Settings;
using ResourceManager;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class GeneralSettingsPage : SettingsPageWithHeader
{
    private readonly TranslationString _openPullDialog = new("Open pull dialog");
    private readonly TranslationString _pullMerge = new("Pull - merge");
    private readonly TranslationString _pullRebase = new("Pull - rebase");
    private readonly TranslationString _fetch = new("Fetch");
    private readonly TranslationString _fetchAll = new("Fetch all");
    private readonly TranslationString _fetchAndPruneAll = new("Fetch and prune all");

    private readonly GeneralPageModel _model = new();

    public GeneralSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        InitializeComponent();
        InitializeComplete();

        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime || GitModuleForm.IsUnitTestActive)
        {
            return;
        }

        IList<Repository> repositoryHistory = ThreadHelper.JoinableTaskFactory.Run(RepositoryHistoryManager.Locals.LoadRecentHistoryAsync);
        string[] historicPaths = [.. repositoryHistory.Select(x => x.GetParentPath())
                                                  .Where(x => !string.IsNullOrEmpty(x))
                                                  .Distinct(StringComparer.CurrentCultureIgnoreCase)];
        cbDefaultCloneDestination.Items.AddRange(historicPaths);

        // the model owns the choice order; this view supplies the translated captions
        var pullActions = GeneralPageModel.PullActionValues
            .Select(value => new { Key = CaptionFor(value), Value = value })
            .ToArray();
        cboDefaultPullAction.DisplayMember = "Key";
        cboDefaultPullAction.ValueMember = "Value";
        cboDefaultPullAction.DataSource = pullActions;
        cboDefaultPullAction.SelectedIndex = 0;

        TranslationString CaptionFor(GitPullAction value)
            => value switch
            {
                GitPullAction.Merge => _pullMerge,
                GitPullAction.Rebase => _pullRebase,
                GitPullAction.Fetch => _fetch,
                GitPullAction.FetchAll => _fetchAll,
                GitPullAction.FetchPruneAll => _fetchAndPruneAll,
                _ => _openPullDialog,
            };
    }

    public static SettingsPageReference GetPageReference()
    {
        return new SettingsPageReferenceByType(typeof(GeneralSettingsPage));
    }

    protected override void OnRuntimeLoad()
    {
        base.OnRuntimeLoad();

        // align 1st columns across all tables
        tlpnlBehaviour.AdjustWidthToSize(0, lblDefaultCloneDestination);
        tlpnlTelemetry.AdjustWidthToSize(0, lblDefaultCloneDestination);
    }

    private void SetSubmoduleStatus()
    {
        chkShowSubmoduleStatusInBrowse.Enabled = chkShowGitStatusInToolbar.Checked || chkShowGitStatusForArtificialCommits.Checked;
        chkShowSubmoduleStatusInBrowse.Checked = chkShowSubmoduleStatusInBrowse.Enabled && chkShowSubmoduleStatusInBrowse.Checked;
    }

    private IEnumerable<(BoolSettingsEntry Entry, CheckBox Control)> BoolEntryControls =>
    [
        (_model.ShowGitStatusInToolbar, chkShowGitStatusInToolbar),
        (_model.ShowGitStatusForArtificialCommits, chkShowGitStatusForArtificialCommits),
        (_model.ShowSubmoduleStatus, chkShowSubmoduleStatusInBrowse),
        (_model.ShowStashCount, chkShowStashCountInBrowseWindow),
        (_model.ShowAheadBehindData, chkShowAheadBehindDataInBrowseWindow),
        (_model.CheckForUncommittedChanges, chkCheckForUncommittedChangesInCheckoutBranch),
        (_model.CloseProcessDialog, chkCloseProcessDialog),
        (_model.ShowGitCommandLine, chkShowGitCommandLine),
        (_model.UseHistogramDiffAlgorithm, chkUseHistogramDiffAlgorithm),
        (_model.IncludeUntrackedFilesInAutoStash, chkStashUntrackedFiles),
        (_model.FollowRenamesInFileHistory, chkFollowRenamesInFileHistory),
        (_model.FollowRenamesExactOnly, chkFollowRenamesInFileHistoryExact),
        (_model.StartWithRecentWorkingDir, chkStartWithRecentWorkingDir),
        (_model.Telemetry, chkTelemetry),
    ];

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((BoolSettingsEntry entry, CheckBox control) in BoolEntryControls)
        {
            control.Checked = entry.Value;
        }

        chkUpdateModules.CheckState = ToCheckboxState(_model.UpdateSubmodulesOnCheckout.Value);
        lblCommitsLimit.Checked = _model.CommitsLimit.Enabled;
        _NO_TRANSLATE_MaxCommits.Value = _model.CommitsLimit.Number;
        _NO_TRANSLATE_MaxCommits.Enabled = _model.CommitsLimit.Enabled;
        RevisionGridQuickSearchTimeout.Value = _model.QuickSearchTimeout.Value;
        cbDefaultCloneDestination.Text = _model.DefaultCloneDestination.Value;
        cboDefaultPullAction.SelectedIndex = _model.DefaultPullAction.SelectedIndex;
        SetSubmoduleStatus();

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        foreach ((BoolSettingsEntry entry, CheckBox control) in BoolEntryControls)
        {
            entry.Value = control.Checked;
        }

        _model.UpdateSubmodulesOnCheckout.Value = ToBoolean(chkUpdateModules.CheckState);
        _model.CommitsLimit.Enabled = lblCommitsLimit.Checked;
        _model.CommitsLimit.Number = (int)_NO_TRANSLATE_MaxCommits.Value;
        _model.QuickSearchTimeout.Value = (int)RevisionGridQuickSearchTimeout.Value;
        _model.DefaultCloneDestination.Value = cbDefaultCloneDestination.Text;
        _model.DefaultPullAction.SelectedIndex = cboDefaultPullAction.SelectedIndex;

        _model.Save();

        base.PageToSettings();
    }

    private static CheckState ToCheckboxState(bool? booleanValue)
    {
        if (!booleanValue.HasValue)
        {
            return CheckState.Indeterminate;
        }

        return booleanValue == true ? CheckState.Checked : CheckState.Unchecked;
    }

    private static bool? ToBoolean(CheckState state)
    {
        if (state == CheckState.Indeterminate)
        {
            return null;
        }

        return state == CheckState.Checked;
    }

    private void DefaultCloneDestinationBrowseClick(object sender, EventArgs e)
    {
        string? userSelectedPath = FolderPicker.PickFolder(this, cbDefaultCloneDestination.Text);

        if (userSelectedPath is not null)
        {
            cbDefaultCloneDestination.Text = userSelectedPath;
        }
    }

    private void ShowGitStatus_CheckedChanged(object sender, EventArgs e)
    {
        SetSubmoduleStatus();
    }

    private void LlblTelemetryPrivacyLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OsShellUtil.OpenUrlInDefaultBrowser(@"https://github.com/gitextensions/gitextensions/blob/master/setup/assets/PrivacyPolicy.md");
    }

    private void lblCommitsLimit_CheckedChanged(object sender, EventArgs e)
    {
        _NO_TRANSLATE_MaxCommits.Enabled = lblCommitsLimit.Checked;
    }
}
