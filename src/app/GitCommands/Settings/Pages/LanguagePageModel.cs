namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the client's language page (§11 source-join localization).
///  The host supplies the available languages; blank storage means English.
/// </summary>
public sealed class LanguagePageModel : SettingsPageModel
{
    public LanguagePageModel(IReadOnlyList<string> languages)
        : base("Language")
    {
        List<string> choices = ["English", .. languages];
        Groups =
        [
            new SettingsGroup("Application language",
                new ChoiceSettingsEntry(
                    "Language (translations joined from the Git Extensions catalog)",
                    [.. choices],
                    () => Math.Max(0, choices.IndexOf(string.IsNullOrEmpty(AppSettings.Translation) ? "English" : AppSettings.Translation)),
                    index => AppSettings.Translation = choices[index] == "English" ? "" : choices[index])),
        ];
    }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
