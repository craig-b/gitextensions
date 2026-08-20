using GitCommands.Git;
using GitCommands.Settings.Pages;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class GitConfigAdvancedSettingsPage : GitConfigBaseSettingsPage
{
    private readonly GitConfigAdvancedPageModel _model;
    private readonly CheckBox[] _checkBoxes;

    public GitConfigAdvancedSettingsPage(IServiceProvider serviceProvider)
       : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();

        _model = new GitConfigAdvancedPageModel(GetCurrentSettings);

        // same order as the model's entries
        _checkBoxes =
        [
            checkBoxPullRebase,
            checkBoxFetchPrune,
            checkboxMergeAutoStash,
            checkBoxRebaseAutostash,
            checkBoxRebaseAutosquash,
            checkBoxUpdateRefs,
            checkBoxReReReEnabled,
            checkBoxReReReAutoUpdate,
        ];

        checkBoxUpdateRefs.Visible = GitVersion.Current.SupportUpdateRefs;

        Load += GitConfigAdvancedSettingsPage_Load;
    }

    private IEnumerable<(TriStateSettingsEntry Entry, CheckBox Control)> EntryControls()
        => _model.Entries.Cast<TriStateSettingsEntry>().Zip(_checkBoxes);

    private void GitConfigAdvancedSettingsPage_Load(object? sender, EventArgs e)
    {
        // the model captions already carry the keys; this view keeps its translated
        // captions and appends the key the same way
        foreach ((TriStateSettingsEntry entry, CheckBox checkBox) in EntryControls())
        {
            int keyStart = entry.Caption.LastIndexOf(" [", StringComparison.Ordinal);
            checkBox.Text += entry.Caption[keyStart..];
        }
    }

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((TriStateSettingsEntry entry, CheckBox checkBox) in EntryControls())
        {
            checkBox.CheckState = entry.Value switch
            {
                true => CheckState.Checked,
                false => CheckState.Unchecked,
                null => CheckState.Indeterminate,
            };
        }

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        foreach ((TriStateSettingsEntry entry, CheckBox checkBox) in EntryControls())
        {
            entry.Value = checkBox.CheckState switch
            {
                CheckState.Checked => true,
                CheckState.Unchecked => false,
                _ => null,
            };
        }

        _model.Save();

        base.PageToSettings();
    }
}
