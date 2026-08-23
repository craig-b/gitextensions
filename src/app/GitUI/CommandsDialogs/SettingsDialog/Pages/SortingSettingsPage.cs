using GitCommands;
using GitCommands.Settings.Pages;
using GitExtUtils.GitUI;
using ResourceManager;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class SortingSettingsPage : SettingsPageWithHeader
{
    private readonly TranslationString _revisionSortWarningTooltip = new("Sorting revisions may delay rendering of the revision graph.");
    private readonly TranslationString _prioBranchNamesTooltip = new("Regex to prioritize branch names in the left panel and commit info.\n" +
        "The branches matching the pattern will be shown before the others.\n" +
        "Separate the priorities with ';'.");
    private readonly TranslationString _prioRemoteNamesTooltip = new("Regex to prioritize remote names in the left panel and commit info.\n" +
        "The remotes matching the pattern will be shown before the others.\n" +
        "Separate the priorities with ';'.");

    private readonly SortingPageModel _model = new();

    public SortingSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();

        // the model owns the choice captions (enum descriptions; these combos are not translated)
        _NO_TRANSLATE_cmbRevisionsSortBy.DataSource = _model.RevisionsSortBy.Choices.ToList();
        _NO_TRANSLATE_cmbBranchesSortBy.DataSource = _model.BranchesSortBy.Choices.ToList();
        _NO_TRANSLATE_cmbBranchesOrder.DataSource = _model.BranchesOrder.Choices.ToList();
    }

    protected override void OnRuntimeLoad()
    {
        base.OnRuntimeLoad();

        ToolTip.SetToolTip(RevisionSortOrderHelp, _revisionSortWarningTooltip.Text);
        ToolTip.SetToolTip(PrioBranchNamesHelp, _prioBranchNamesTooltip.Text);
        ToolTip.SetToolTip(PrioRemoteNamesHelp, _prioRemoteNamesTooltip.Text);
        RevisionSortOrderHelp.Size = DpiUtil.Scale(RevisionSortOrderHelp.Size);
        PrioBranchNamesHelp.Size = DpiUtil.Scale(PrioBranchNamesHelp.Size);
        PrioRemoteNamesHelp.Size = DpiUtil.Scale(PrioRemoteNamesHelp.Size);

        if (!IsSettingsLoaded)
        {
            SettingsToPage();
        }
    }

    protected override void SettingsToPage()
    {
        _model.Load();

        _NO_TRANSLATE_cmbRevisionsSortBy.SelectedIndex = _model.RevisionsSortBy.SelectedIndex;
        _NO_TRANSLATE_cmbBranchesSortBy.SelectedIndex = _model.BranchesSortBy.SelectedIndex;
        _NO_TRANSLATE_cmbBranchesOrder.SelectedIndex = _model.BranchesOrder.SelectedIndex;
        txtPrioBranchNames.Text = _model.PrioritizedBranchNames.Value;
        txtPrioRemoteNames.Text = _model.PrioritizedRemoteNames.Value;

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        _model.RevisionsSortBy.SelectedIndex = _NO_TRANSLATE_cmbRevisionsSortBy.SelectedIndex;
        _model.BranchesSortBy.SelectedIndex = _NO_TRANSLATE_cmbBranchesSortBy.SelectedIndex;
        _model.BranchesOrder.SelectedIndex = _NO_TRANSLATE_cmbBranchesOrder.SelectedIndex;
        _model.PrioritizedBranchNames.Value = txtPrioBranchNames.Text;
        _model.PrioritizedRemoteNames.Value = txtPrioRemoteNames.Text;

        // the model reinitializes the portable TranslatedStrings; this view refreshes its own
        _model.Save();
        TranslatedStrings.Reinitialize();

        base.PageToSettings();
    }

    private void RevisionSortOrderHelp_Click(object sender, EventArgs e)
        => OsShellUtil.OpenUrlInDefaultBrowser(UserManual.UserManual.UrlFor("settings", "sorting-sort-author-date"));
    private void PrioBranchNamesHelp_Click(object sender, EventArgs e)
        => OsShellUtil.OpenUrlInDefaultBrowser(UserManual.UserManual.UrlFor("settings", "sorting-sort-prioritized-branches"));
    private void PrioRemoteNamesHelp_Click(object sender, EventArgs e)
        => OsShellUtil.OpenUrlInDefaultBrowser(UserManual.UserManual.UrlFor("settings", "sorting-sort-prioritized-remotes"));
}
