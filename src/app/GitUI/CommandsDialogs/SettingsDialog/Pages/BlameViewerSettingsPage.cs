using GitCommands.Settings.Pages;
using GitExtensions.Extensibility.Settings;
using ResourceManager;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class BlameViewerSettingsPage : SettingsPageWithHeader
{
    private readonly TranslationString _blameWarningTooltip = new("Could prevent blame to calculate the accurate line number when blaming previous revisions.");

    private readonly BlameViewerPageModel _model = new();

    public BlameViewerSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();
        cbDetectMoveAndCopyInThisFile.ToolTipText = _blameWarningTooltip.Text;
        cbDetectMoveAndCopyInAllFiles.ToolTipText = _blameWarningTooltip.Text;
    }

    private IEnumerable<(BoolSettingsEntry Entry, Control Control)> EntryControls =>
    [
        (_model.IgnoreWhitespace, cbIgnoreWhitespace),
        (_model.DetectMoveAndCopyInThisFile, cbDetectMoveAndCopyInThisFile),
        (_model.DetectMoveAndCopyInAllFiles, cbDetectMoveAndCopyInAllFiles),
        (_model.DisplayAuthorFirst, cbDisplayAuthorFirst),
        (_model.ShowAuthor, cbShowAuthor),
        (_model.ShowAuthorDate, cbShowAuthorDate),
        (_model.ShowAuthorTime, cbShowAuthorTime),
        (_model.ShowLineNumbers, cbShowLineNumbers),
        (_model.ShowOriginalFilePath, cbShowOriginalFilePath),
        (_model.ShowAuthorAvatar, cbShowAuthorAvatar),
    ];

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((BoolSettingsEntry entry, Control control) in EntryControls)
        {
            SettingsPageBindings.SetChecked(control, entry.Value);
        }

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        foreach ((BoolSettingsEntry entry, Control control) in EntryControls)
        {
            entry.Value = SettingsPageBindings.GetChecked(control);
        }

        _model.Save();

        base.PageToSettings();
    }

    public static SettingsPageReference GetPageReference()
    {
        return new SettingsPageReferenceByType(typeof(BlameViewerSettingsPage));
    }
}
