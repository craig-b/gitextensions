using GitCommands.Utils;
using GitExtensions.Extensibility.Translations;
using ResourceManager;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the appearance settings page. The avatar-cache clearing on
///  provider change, the help links, the dictionary drop-down scan, and the
///  registry-backed "show current branch in Visual Studio" toggle stay view-side;
///  <see cref="GetAvailableLanguages"/> and <see cref="GetAvailableDictionaries"/> feed
///  any view's pickers.
/// </summary>
public sealed class AppearancePageModel : SettingsPageModel
{
    /// <summary>The stored enum value per truncate choice index (the page's historical order).</summary>
    private static readonly TruncatePathMethod[] _truncatePathMethods =
        [TruncatePathMethod.None, TruncatePathMethod.Compact, TruncatePathMethod.TrimStart, TruncatePathMethod.FileNameOnly];

    public AppearancePageModel()
        : base("Appearance")
    {
        Groups =
        [
            new SettingsGroup("General",
                ShowRelativeDate = new BoolSettingsEntry("Show relative date instead of full date",
                    () => AppSettings.RelativeDate, value => AppSettings.RelativeDate = value),
                ShowRepoCurrentBranch = new BoolSettingsEntry("Show current branch names in the dashboard and the recent repositories dropdown menu",
                    () => AppSettings.ShowRepoCurrentBranch, value => AppSettings.ShowRepoCurrentBranch = value),
                EnableAutoScale = new BoolSettingsEntry("Auto scale user interface when high DPI is used",
                    () => AppSettings.EnableAutoScale, value => AppSettings.EnableAutoScale = value),
                TruncateLongFilenames = new ChoiceSettingsEntry("Truncate long filenames", ["None", "Compact", "Trim start", "Filename only"],
                    () => Math.Max(0, Array.IndexOf(_truncatePathMethods, AppSettings.TruncatePathMethod)),
                    index => AppSettings.TruncatePathMethod = _truncatePathMethods[index])),

            new SettingsGroup("Author images",
                ShowAuthorAvatarInCommitGraph = new BoolSettingsEntry("Show author's avatar column in the commit graph",
                    () => AppSettings.ShowAuthorAvatarColumn, value => AppSettings.ShowAuthorAvatarColumn = value),
                ShowAuthorAvatarInCommitInfo = new BoolSettingsEntry("Show author's avatar in the commit info view",
                    () => AppSettings.ShowAuthorAvatarInCommitInfo, value => AppSettings.ShowAuthorAvatarInCommitInfo = value),
                AvatarImageCacheDays = new NumberSettingsEntry("Cache images (days)",
                    minimum: 0, maximum: 400, increment: 1,
                    () => AppSettings.AvatarImageCacheDays, value => AppSettings.AvatarImageCacheDays = value),
                AvatarProvider = new ChoiceSettingsEntry("Avatar provider", DescriptionsOf<AvatarProvider>(),
                    () => (int)AppSettings.AvatarProvider, index => AppSettings.AvatarProvider = (AvatarProvider)index),
                AvatarFallbackType = new ChoiceSettingsEntry("Fallback generated avatar style", DescriptionsOf<AvatarFallbackType>(),
                    () => (int)AppSettings.AvatarFallbackType, index => AppSettings.AvatarFallbackType = (AvatarFallbackType)index),
                CustomAvatarTemplate = new StringSettingsEntry("Custom avatar template",
                    () => AppSettings.CustomAvatarTemplate, value => AppSettings.CustomAvatarTemplate = value)),

            new SettingsGroup("Language",
                Language = new StringSettingsEntry("Language (restart required)",
                    () => AppSettings.Translation, value => AppSettings.Translation = value),
                Dictionary = new StringSettingsEntry("Dictionary for spelling checker",
                    () => AppSettings.Dictionary, value => AppSettings.Dictionary = value)),
        ];
    }

    public BoolSettingsEntry ShowRelativeDate { get; }
    public BoolSettingsEntry ShowRepoCurrentBranch { get; }
    public BoolSettingsEntry EnableAutoScale { get; }
    public ChoiceSettingsEntry TruncateLongFilenames { get; }

    public BoolSettingsEntry ShowAuthorAvatarInCommitGraph { get; }
    public BoolSettingsEntry ShowAuthorAvatarInCommitInfo { get; }
    public NumberSettingsEntry AvatarImageCacheDays { get; }
    public ChoiceSettingsEntry AvatarProvider { get; }
    public ChoiceSettingsEntry AvatarFallbackType { get; }
    public StringSettingsEntry CustomAvatarTemplate { get; }

    public StringSettingsEntry Language { get; }
    public StringSettingsEntry Dictionary { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }

    public override void Save()
    {
        base.Save();

        // the language choice feeds the cached translated strings
        TranslatedStrings.Reinitialize();
    }

    /// <summary>"English" plus every installed translation.</summary>
    public static string[] GetAvailableLanguages()
        => ["English", .. Translator.GetAllTranslations()];

    /// <summary>
    ///  The dictionary names installed beside the app ("none" disables spellcheck).
    ///  May throw if the dictionary directory is unreadable; callers present that.
    /// </summary>
    public static string[] GetAvailableDictionaries()
        => [.. Directory.GetFiles(AppSettings.GetDictionaryDir(), "*.dic", SearchOption.TopDirectoryOnly)
            .Select(fileName => Path.GetFileNameWithoutExtension(fileName))];

    private static string[] DescriptionsOf<T>() where T : Enum
        => [.. EnumHelper.GetValues<T>().Select(value => value.GetDescription())];
}
