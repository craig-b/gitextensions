using GitCommands;
using GitCommands.Settings.Pages;
using GitExtensions.Extensibility.Settings;
using GitExtUtils.GitUI;
using GitUI.Avatars;
using ResourceManager;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class AppearanceSettingsPage : SettingsPageWithHeader
{
    private const string _spellingWikiURL = "https://github.com/gitextensions/gitextensions/wiki/Spelling";
    private const string _translationsWikiURL = "https://github.com/gitextensions/gitextensions/wiki/Translations";

    private readonly TranslationString _noDictFile = new("None");
    private readonly TranslationString _noDictFilesFound = new("No dictionary files found in: {0}");
    private readonly TranslationString _noImageServiceTooltip = new($"A default image, if the provider has no image for the email address.\r\n\r\nClick this info icon for more details.");
    private readonly TranslationString _avatarProviderTooltip = new($"The avatar provider defines the source for user-defined avatar images.\r\nThe \"Default\" provider uses GitHub and Gravatar,\r\nthe \"Custom\" provider allows you to set custom provider URLs and\r\n\"None\" disables user-defined avatars.\r\n\r\nClick this info icon for more details.");

    private readonly AppearancePageModel _model = new();

    public AppearanceSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();

        // the model owns the choice orders; this view keeps its translated captions
        AvatarProvider.DataSource = _model.AvatarProvider.Choices.ToList();
        _NO_TRANSLATE_NoImageService.DataSource = _model.AvatarFallbackType.Choices.ToList();
    }

    protected override void OnRuntimeLoad()
    {
        base.OnRuntimeLoad();

        ToolTip.SetToolTip(_NO_TRANSLATE_NoImageService, _noImageServiceTooltip.Text);
        ToolTip.SetToolTip(pictureAvatarHelp, _noImageServiceTooltip.Text);
        ToolTip.SetToolTip(avatarProviderHelp, _avatarProviderTooltip.Text);
        pictureAvatarHelp.Size = DpiUtil.Scale(pictureAvatarHelp.Size);
        avatarProviderHelp.Size = DpiUtil.Scale(avatarProviderHelp.Size);

        // align 1st columns across all tables
        tlpnlGeneral.AdjustWidthToSize(0, truncateLongFilenames, lblCacheDays, lblNoImageService, lblLanguage, lblSpellingDictionary);
        tlpnlAuthor.AdjustWidthToSize(0, truncateLongFilenames, lblCacheDays, lblNoImageService, lblLanguage, lblSpellingDictionary);
        tlpnlLanguage.AdjustWidthToSize(0, truncateLongFilenames, lblCacheDays, lblNoImageService, lblLanguage, lblSpellingDictionary);

        // align 2nd columns across all tables
        truncatePathMethod.AdjustWidthToFitContent();
        Language.AdjustWidthToFitContent();
        tlpnlGeneral.AdjustWidthToSize(1, truncatePathMethod, _NO_TRANSLATE_NoImageService, Language);
        tlpnlAuthor.AdjustWidthToSize(1, truncatePathMethod, _NO_TRANSLATE_NoImageService, Language);
        tlpnlLanguage.AdjustWidthToSize(1, truncatePathMethod, _NO_TRANSLATE_NoImageService, Language);
    }

    public static SettingsPageReference GetPageReference()
    {
        return new SettingsPageReferenceByType(typeof(AppearanceSettingsPage));
    }

    private IEnumerable<(BoolSettingsEntry Entry, Control Control)> BoolEntryControls =>
    [
        (_model.ShowRelativeDate, chkShowRelativeDate),
        (_model.ShowRepoCurrentBranch, chkShowRepoCurrentBranch),
        (_model.EnableAutoScale, chkEnableAutoScale),
        (_model.ShowAuthorAvatarInCommitGraph, ShowAuthorAvatarInCommitGraph),
        (_model.ShowAuthorAvatarInCommitInfo, ShowAuthorAvatarInCommitInfo),
    ];

    protected override void SettingsToPage()
    {
        _model.Load();

        foreach ((BoolSettingsEntry entry, Control control) in BoolEntryControls)
        {
            SettingsPageBindings.SetChecked(control, entry.Value);
        }

        // registry-backed toggle for the Visual Studio plugin - Windows-only view chrome
        chkShowCurrentBranchInVisualStudio.Checked = AppSettings.ShowCurrentBranchInVisualStudio;

        truncatePathMethod.SelectedIndex = _model.TruncateLongFilenames.SelectedIndex;
        _NO_TRANSLATE_DaysToCacheImages.Value = _model.AvatarImageCacheDays.Value;
        AvatarProvider.SelectedIndex = _model.AvatarProvider.SelectedIndex;
        _NO_TRANSLATE_NoImageService.SelectedIndex = _model.AvatarFallbackType.SelectedIndex;
        txtCustomAvatarTemplate.Text = _model.CustomAvatarTemplate.Value;
        ManageAvatarOptionsDisplay();

        Language.Items.Clear();
        Language.Items.AddRange(AppearancePageModel.GetAvailableLanguages());
        Language.Text = _model.Language.Value;

        Dictionary.Items.Clear();
        Dictionary.Items.Add(_noDictFile.Text);
        if (_model.Dictionary.Value.Equals("none", StringComparison.InvariantCultureIgnoreCase))
        {
            Dictionary.SelectedIndex = 0;
        }
        else
        {
            string dictionaryFile = string.Concat(Path.Join(AppSettings.GetDictionaryDir(), _model.Dictionary.Value), ".dic");
            if (File.Exists(dictionaryFile))
            {
                Dictionary.Items.Add(_model.Dictionary.Value);
                Dictionary.Text = _model.Dictionary.Value;
            }
            else
            {
                Dictionary.SelectedIndex = 0;
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

        // clearing the avatar cache is this view's job; decide before the model saves
        bool shouldClearCache =
            (int)AppSettings.AvatarProvider != AvatarProvider.SelectedIndex
            || (int)AppSettings.AvatarFallbackType != _NO_TRANSLATE_NoImageService.SelectedIndex
            || AppSettings.CustomAvatarTemplate != txtCustomAvatarTemplate.Text;

        AppSettings.ShowCurrentBranchInVisualStudio = chkShowCurrentBranchInVisualStudio.Checked;

        _model.TruncateLongFilenames.SelectedIndex = truncatePathMethod.SelectedIndex;
        _model.AvatarImageCacheDays.Value = (int)_NO_TRANSLATE_DaysToCacheImages.Value;
        _model.AvatarProvider.SelectedIndex = AvatarProvider.SelectedIndex;
        _model.AvatarFallbackType.SelectedIndex = _NO_TRANSLATE_NoImageService.SelectedIndex;
        _model.CustomAvatarTemplate.Value = txtCustomAvatarTemplate.Text;
        _model.Language.Value = Language.Text;
        _model.Dictionary.Value = Dictionary.SelectedIndex == 0 ? "none" : Dictionary.Text;

        // the model reinitializes the portable TranslatedStrings; this view refreshes its own
        _model.Save();
        TranslatedStrings.Reinitialize();

        if (shouldClearCache)
        {
            new AvatarControl().ClearCache();
        }

        base.PageToSettings();
    }

    private void Dictionary_DropDown(object sender, EventArgs e)
    {
        try
        {
            string currentDictionary = Dictionary.Text;

            Dictionary.Items.Clear();
            Dictionary.Items.Add(_noDictFile.Text);
            foreach (string name in AppearancePageModel.GetAvailableDictionaries())
            {
                Dictionary.Items.Add(name);
            }

            Dictionary.Text = currentDictionary;
        }
        catch
        {
            MessageBoxes.Show(this, string.Format(_noDictFilesFound.Text, AppSettings.GetDictionaryDir()), TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearImageCache_Click(object sender, EventArgs e)
    {
        ThreadHelper.FileAndForget(AvatarService.CacheCleaner.ClearCacheAsync);
    }

    private void helpTranslate_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OsShellUtil.OpenUrlInDefaultBrowser(_translationsWikiURL);
    }

    private void downloadDictionary_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OsShellUtil.OpenUrlInDefaultBrowser(_spellingWikiURL);
    }

    private void pictureAvatarHelp_Click(object sender, EventArgs e)
         => OsShellUtil.OpenUrlInDefaultBrowser(UserManual.UserManual.UrlFor("settings", "author-images-avatar-fallback"));

    private void customAvatarHelp_Click(object sender, EventArgs e)
        => OsShellUtil.OpenUrlInDefaultBrowser(UserManual.UserManual.UrlFor("settings", "author-images-avatar-provider"));

    private void AvatarProvider_SelectedIndexChanged(object sender, EventArgs e)
    {
        ManageAvatarOptionsDisplay();
    }

    private void ManageAvatarOptionsDisplay()
    {
        bool showCustomTemplate = AvatarProvider.SelectedIndex == (int)GitCommands.AvatarProvider.Custom;

        lblCustomAvatarTemplate.Visible = showCustomTemplate;
        txtCustomAvatarTemplate.Visible = showCustomTemplate;
    }
}
