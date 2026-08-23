using GitCommands;
using GitCommands.Settings.Pages;
using GitExtensions.Extensibility.Settings;
using ResourceManager;

namespace GitUI.CommandsDialogs.SettingsDialog.Pages;

public partial class GitSettingsPage : SettingsPageWithHeader
{
    private readonly TranslationString _envIsSetToString = new("{0} is set to: {1}");
    private readonly TranslationString _envIsNotSetString = new("{0} is not set.");

    private readonly GitPathsPageModel _model = new();

    public GitSettingsPage(IServiceProvider serviceProvider)
        : base(serviceProvider)
    {
        InitializeComponent();
        InitializeComplete();
    }

    public static SettingsPageReference GetPageReference()
    {
        return new SettingsPageReferenceByType(typeof(GitSettingsPage));
    }

    public override void OnPageShown()
    {
        GitPath.Text = AppSettings.GitCommandValue;
        LinuxToolsDir.Text = AppSettings.LinuxToolsDir;
    }

    protected override void SettingsToPage()
    {
        (string envName, string? envValue, bool configEnvIsSet) = GitPathsPageModel.GetEffectiveConfigEnvironment();
        string additionalText = configEnvIsSet
            ? ""
            : $"    ({string.Format(_envIsNotSetString.Text, "%GIT_CONFIG_GLOBAL%")})";

        homeIsSetToLabel.Text = string.Format(_envIsSetToString.Text, $"%{envName}%", envValue) + additionalText;

        _model.Load();
        GitPath.Text = _model.GitCommand.Value;
        LinuxToolsDir.Text = _model.LinuxToolsDir.Value;

        base.SettingsToPage();
    }

    protected override void PageToSettings()
    {
        _model.GitCommand.Value = GitPath.Text;
        _model.LinuxToolsDir.Value = LinuxToolsDir.Text;
        _model.Save();

        base.PageToSettings();
    }

    private void BrowseGitPath_Click(object sender, EventArgs e)
    {
        CheckSettingsLogic.SolveGitCommand(GitPath.Text.Trim());

        using OpenFileDialog browseDialog = new()
        {
            FileName = AppSettings.GitCommandValue,
            Filter = "Git.cmd (git.cmd)|git.cmd|Git.exe (git.exe)|git.exe|Git (git)|git"
        };
        if (browseDialog.ShowDialog(this) == DialogResult.OK)
        {
            GitPath.Text = browseDialog.FileName;
        }
    }

    private void BrowseLinuxToolsDir_Click(object sender, EventArgs e)
    {
        CheckSettingsLogic.SolveLinuxToolsDir(LinuxToolsDir.Text.Trim());

        string? userSelectedPath = FolderPicker.PickFolder(this, AppSettings.LinuxToolsDir);

        if (userSelectedPath is not null)
        {
            LinuxToolsDir.Text = userSelectedPath;
        }
    }

    private void GitPath_TextChanged(object sender, EventArgs e)
    {
        // If user pastes text or types in the box be sure to validate and save in the settings.
        CheckSettingsLogic.SolveGitCommand(GitPath.Text.Trim());
    }

    private void LinuxToolsDir_TextChanged(object sender, EventArgs e)
    {
        // If user pastes text or types in the box be sure to validate and save in the settings.
        CheckSettingsLogic.SolveLinuxToolsDir(LinuxToolsDir.Text.Trim());
    }

    private void downloadGitForWindows_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        OsShellUtil.OpenUrlInDefaultBrowser(@"https://github.com/gitextensions/gitextensions/wiki/Application-Dependencies#git");
    }

    private void ChangeHomeButton_Click(object sender, EventArgs e)
    {
        PageHost.SaveAll();
        using (FormFixHome frm = new())
        {
            frm.ShowDialog(this);
        }

        PageHost.LoadAll();

        // TODO?: rescan
    }
}
