using GitUI.CommandsDialogs;
using GitUI.CommandsDialogs.SettingsDialog;
using GitUI.CommandsDialogs.SettingsDialog.Pages;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class OpenSettingsHandler(GitUICommands commands) : IUICommandHandler<Intent.OpenSettings>
{
    public bool Execute(Intent.OpenSettings command, IWin32Window? owner)
    {
        bool Action()
        {
            return FormSettings.ShowSettingsDialog(commands, owner, command.InitialPage)
                is DialogResult.OK;
        }

        return commands.DoActionOnRepo(owner, Action, requiresValidWorkingDir: false, postEvent: commands.PostSettingsEvent);
    }
}

internal sealed class GeneralSettingsHandler(GitUICommands commands) : IUICommandHandler<Intent.GeneralSettings>
{
    public bool Execute(Intent.GeneralSettings command, IWin32Window? owner)
        => commands.Execute(new Intent.OpenSettings(GeneralSettingsPage.GetPageReference()), owner);
}

internal sealed class RepoSettingsHandler(GitUICommands commands) : IUICommandHandler<Intent.RepoSettings>
{
    public bool Execute(Intent.RepoSettings command, IWin32Window? owner)
        => commands.Execute(new Intent.OpenSettings(GitConfigSettingsPage.GetPageReference()), owner);
}

internal sealed class PluginSettingsHandler(GitUICommands commands) : IUICommandHandler<Intent.PluginSettings>
{
    public bool Execute(Intent.PluginSettings command, IWin32Window? owner)
        => commands.Execute(new Intent.OpenSettings(PluginsSettingsGroup.GetPageReference()), owner);
}
