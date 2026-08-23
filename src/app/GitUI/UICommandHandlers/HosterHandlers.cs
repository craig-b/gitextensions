using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitUI.CommandsDialogs.RepoHosting;
using GitUI.HelperDialogs;
using GitUIPluginInterfaces;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class AddUpstreamRemoteHandler(GitUICommands commands) : IUICommandHandler<Intent.AddUpstreamRemote>
{
    public bool Execute(Intent.AddUpstreamRemote command, IWin32Window? owner)
    {
        Hoster.WrapRepoHostingCall(commands, TranslatedStrings.AddUpstreamRemote, command.GitHoster,
            gh =>
            {
                ThreadHelper.FileAndForget(async () =>
                {
                    string? remoteName = await gh.AddUpstreamRemoteAsync();
                    if (!string.IsNullOrEmpty(remoteName))
                    {
                        commands.Execute(new Intent.PullImmediately(null, remoteName, GitPullAction.Fetch), owner);
                    }
                });
            });

        return true;
    }
}

internal sealed class CloneForkFromHosterHandler(GitUICommands commands) : IUICommandHandler<Intent.CloneForkFromHoster>
{
    public bool Execute(Intent.CloneForkFromHoster command, IWin32Window? owner)
    {
        Hoster.WrapRepoHostingCall(commands, TranslatedStrings.ForkCloneRepo, command.GitHoster, gh =>
        {
            using ForkAndCloneForm frm = new(commands, gh);
            frm.ShowDialog(owner);
        });

        return true;
    }
}

internal sealed class CreatePullRequestHandler(GitUICommands commands) : IUICommandHandler<Intent.CreatePullRequest>
{
    public bool Execute(Intent.CreatePullRequest command, IWin32Window? owner)
    {
        IRepositoryHostPlugin? gitHoster = command.GitHoster;

        if (gitHoster is null)
        {
            List<IRepositoryHostPlugin> relevantHosts =
                [.. PluginRegistry.GitHosters.Where(gh => gh.GitModuleIsRelevantToMe())];

            if (relevantHosts.Count == 0)
            {
                MessageBoxes.Show(owner, "Could not find any repo hosts for current working directory", TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return true;
            }

            if (relevantHosts.Count > 1)
            {
                MessageBoxes.Show("StartCreatePullRequest:Selection not implemented!", TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return true;
            }

            gitHoster = relevantHosts[0];
        }

        Hoster.WrapRepoHostingCall(
            commands,
            TranslatedStrings.CreatePullRequest,
            gitHoster,
            gh =>
            {
                CreatePullRequestForm form = new(commands, gh, command.ChooseRemote, command.ChooseBranch)
                {
                    ShowInTaskbar = true
                };

                form.Show(owner);
            });

        return true;
    }
}

internal sealed class PullRequestsHandler(GitUICommands commands) : IUICommandHandler<Intent.PullRequests>
{
    public bool Execute(Intent.PullRequests command, IWin32Window? owner)
    {
        Hoster.WrapRepoHostingCall(commands, TranslatedStrings.ViewPullRequest, command.GitHoster,
            gh =>
            {
                ViewPullRequestsForm frm = new(commands, gh) { ShowInTaskbar = true };
                frm.Show(owner);
            });

        return true;
    }
}

file static class Hoster
{
    internal static void WrapRepoHostingCall(GitUICommands commands, string name, IRepositoryHostPlugin gitHoster, Action<IRepositoryHostPlugin> call)
    {
        if (!gitHoster.ConfigurationOk)
        {
            GitUIEventArgs eventArgs = new(null, commands);
            gitHoster.Execute(eventArgs);
        }

        if (gitHoster.ConfigurationOk)
        {
            try
            {
                call(gitHoster);
            }
            catch (Exception ex)
            {
                MessageBoxes.Show(
                    string.Format("ERROR: {0} failed. Message: {1}\r\n\r\n{2}", name, ex.Message, ex.StackTrace),
                    "Error! :(", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
