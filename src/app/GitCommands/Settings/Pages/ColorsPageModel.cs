namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the client's colors/theme page. The WinForms css-theme system
///  stays WinForms (per the retirement endgame); the client themes Avalonia-natively via
///  the variant choice.
/// </summary>
public sealed class ColorsPageModel : SettingsPageModel
{
    private static readonly string[] _variants = ["", "Light", "Dark"];

    public ColorsPageModel()
        : base("Colors")
    {
        Groups =
        [
            new SettingsGroup("Theme",
                new ChoiceSettingsEntry(
                    "Theme variant",
                    ["Follow system", "Light", "Dark"],
                    () => Math.Max(0, Array.IndexOf(_variants, AppSettings.ClientThemeVariant)),
                    index => AppSettings.ClientThemeVariant = _variants[index])),
        ];
    }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
