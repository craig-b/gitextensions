using GitCommands;
using GitCommands.Settings.Pages;
using GitExtensions.Extensibility.Settings;
using GitExtUtils;
using GitUI.Hotkey;
using GitUI.Shells;
using ResourceManager;
using ResourceManager.Hotkey;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class FormBrowseRepoSettingsPage : SettingsPageWithHeader
{
    private readonly TranslationString _outputHistoryTooltip
        = new("""
              The output displayed in the process dialog and some trace output is retained and shown in the output history.

              - With this set, the output history is displayed in a tab in the lower pane of the Browse Repository window.
              - With this unset, the output history is displayed in a panel docked to the lower left corner of the Browse Repository window.

              Focus the output history and (when displayed as panel) toggle its visibility using the hotkey {0}.
              """);
    private readonly ShellProvider _shellProvider = new();
    private int _cboTerminalPreviousIndex = -1;

    private readonly BrowseRepoPageModel _model = new();

    public FormBrowseRepoSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        cboTerminal.DisplayMember = "Name";
        InitializeComplete();
        string hotkey = serviceProvider.GetRequiredService<IHotkeySettingsManager>()
            .LoadHotkeys(FormBrowse.HotkeySettingsName)
            .GetShortcutDisplay(FormBrowse.Command.FocusOutputHistoryAndToggleIfPanel);
        chkShowOutputHistoryAsTab.ToolTipText = string.Format(_outputHistoryTooltip.Text, hotkey);
    }

    protected override void Init(ISettingsPageHost pageHost)
    {
        base.Init(pageHost);
    }

    protected override void OnRuntimeLoad()
    {
        // align 1st columns across all tables
        tlpnlGeneral.AdjustWidthToSize(0, lblDefaultShell, chkUseBrowseForFileHistory, chkUseDiffViewerForBlame, chkShowFindInCommitFilesGitGrep, chkShowRevisionGridTooltip, chkShowConsoleTab, chkShowGpgInformation);
        tlpnlTabs.AdjustWidthToSize(0, lblDefaultShell, chkUseBrowseForFileHistory, chkUseDiffViewerForBlame, chkShowFindInCommitFilesGitGrep, chkShowRevisionGridTooltip, chkShowConsoleTab, chkShowGpgInformation);

        base.OnRuntimeLoad();
    }

    private IEnumerable<(BoolSettingsEntry Entry, Control Control)> BoolEntryControls =>
    [
        (_model.UseBrowseForFileHistory, chkUseBrowseForFileHistory),
        (_model.UseDiffViewerForBlame, chkUseDiffViewerForBlame),
        (_model.ShowFindInCommitFilesGitGrep, chkShowFindInCommitFilesGitGrep),
        (_model.ShowRevisionGridTooltips, chkShowRevisionGridTooltip),
        (_model.ShowGpgInformation, chkShowGpgInformation),
        (_model.ShowOutputHistoryAsTab, chkShowOutputHistoryAsTab),
    ];

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((BoolSettingsEntry entry, Control control) in BoolEntryControls)
        {
            SettingsPageBindings.SetChecked(control, entry.Value);
        }

        _NO_TRANSLATE_OutputHistoryDepth.Value = _model.OutputHistoryDepth.Value;

        // the console tab and terminal picker are ConEmu (Windows-only) view chrome
        chkShowConsoleTab.Checked = AppSettings.ShowConEmuTab.Value;
        foreach (IShellDescriptor shell in _shellProvider.GetShells())
        {
            cboTerminal.Items.Add(shell);

            if (string.Equals(shell.Name, AppSettings.ConEmuTerminal.Value, StringComparison.InvariantCultureIgnoreCase))
            {
                cboTerminal.SelectedItem = shell;
            }
        }

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        foreach ((BoolSettingsEntry entry, Control control) in BoolEntryControls)
        {
            entry.Value = SettingsPageBindings.GetChecked(control);
        }

        _model.OutputHistoryDepth.Value = (int)_NO_TRANSLATE_OutputHistoryDepth.Value;

        _model.Save();

        AppSettings.ShowConEmuTab.Value = chkShowConsoleTab.Checked;
        AppSettings.ConEmuTerminal.Value = ((IShellDescriptor)cboTerminal.SelectedItem!).Name.ToLowerInvariant();

        base.PageToSettings();
    }

    public static SettingsPageReference GetPageReference()
    {
        return new SettingsPageReferenceByType(typeof(FormBrowseRepoSettingsPage));
    }

    private void cboTerminal_SelectionChangeCommitted(object sender, EventArgs e)
    {
        if (cboTerminal.SelectedItem is not IShellDescriptor shell)
        {
            return;
        }

        if (shell.HasExecutable)
        {
            return;
        }

        MessageBoxes.ShellNotFound(this);
        cboTerminal.SelectedIndex = _cboTerminalPreviousIndex;
    }

    private void cboTerminal_Enter(object sender, EventArgs e)
    {
        _cboTerminalPreviousIndex = cboTerminal.SelectedIndex;
    }
}
