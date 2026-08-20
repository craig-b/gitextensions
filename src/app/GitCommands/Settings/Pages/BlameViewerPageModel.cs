namespace GitCommands.Settings.Pages;

/// <summary>Presentation model for the blame-viewer settings page.</summary>
public sealed class BlameViewerPageModel : SettingsPageModel
{
    public BlameViewerPageModel()
        : base("Blame viewer")
    {
        Groups =
        [
            new SettingsGroup("Blame settings",
                IgnoreWhitespace = new BoolSettingsEntry("Ignore whitespace",
                    () => AppSettings.IgnoreWhitespaceOnBlame, value => AppSettings.IgnoreWhitespaceOnBlame = value),
                DetectMoveAndCopyInThisFile = new BoolSettingsEntry("Detect moved or copied lines within blamed file",
                    () => AppSettings.DetectCopyInFileOnBlame, value => AppSettings.DetectCopyInFileOnBlame = value),
                DetectMoveAndCopyInAllFiles = new BoolSettingsEntry("Detect moved or copied lines from all files in same commit",
                    () => AppSettings.DetectCopyInAllOnBlame, value => AppSettings.DetectCopyInAllOnBlame = value)),

            new SettingsGroup("Display result settings",
                DisplayAuthorFirst = new BoolSettingsEntry("Display author first",
                    () => AppSettings.BlameDisplayAuthorFirst, value => AppSettings.BlameDisplayAuthorFirst = value),
                ShowAuthor = new BoolSettingsEntry("Show author",
                    () => AppSettings.BlameShowAuthor, value => AppSettings.BlameShowAuthor = value),
                ShowAuthorDate = new BoolSettingsEntry("Show author date",
                    () => AppSettings.BlameShowAuthorDate, value => AppSettings.BlameShowAuthorDate = value),
                ShowAuthorTime = new BoolSettingsEntry("Show author time",
                    () => AppSettings.BlameShowAuthorTime, value => AppSettings.BlameShowAuthorTime = value),
                ShowLineNumbers = new BoolSettingsEntry("Show line numbers",
                    () => AppSettings.BlameShowLineNumbers, value => AppSettings.BlameShowLineNumbers = value),
                ShowOriginalFilePath = new BoolSettingsEntry("Show original file path",
                    () => AppSettings.BlameShowOriginalFilePath, value => AppSettings.BlameShowOriginalFilePath = value),
                ShowAuthorAvatar = new BoolSettingsEntry("Show author avatar",
                    () => AppSettings.BlameShowAuthorAvatar, value => AppSettings.BlameShowAuthorAvatar = value)),
        ];
    }

    public BoolSettingsEntry IgnoreWhitespace { get; }
    public BoolSettingsEntry DetectMoveAndCopyInThisFile { get; }
    public BoolSettingsEntry DetectMoveAndCopyInAllFiles { get; }

    public BoolSettingsEntry DisplayAuthorFirst { get; }
    public BoolSettingsEntry ShowAuthor { get; }
    public BoolSettingsEntry ShowAuthorDate { get; }
    public BoolSettingsEntry ShowAuthorTime { get; }
    public BoolSettingsEntry ShowLineNumbers { get; }
    public BoolSettingsEntry ShowOriginalFilePath { get; }
    public BoolSettingsEntry ShowAuthorAvatar { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
