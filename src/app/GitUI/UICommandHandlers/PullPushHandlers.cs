using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class PullHandler(GitUICommands commands) :
    IUICommandHandler<Intent.Pull>,
    IUICommandHandler<Intent.PullImmediately>
{
    public bool Execute(Intent.Pull command, IWin32Window? owner)
        => Execute(owner, pullOnShow: false, out _, command.RemoteBranch, command.Remote, command.PullAction);

    public bool Execute(Intent.PullImmediately command, IWin32Window? owner)
        => Execute(owner, pullOnShow: true, out _, command.RemoteBranch, command.Remote, command.PullAction);

    public bool Execute(Intent.PullImmediately command, IWin32Window? owner, out bool pullCompleted)
        => Execute(owner, pullOnShow: true, out pullCompleted, command.RemoteBranch, command.Remote, command.PullAction);

    private bool Execute(IWin32Window? owner, bool pullOnShow, out bool pullCompleted, string? remoteBranch, string? remote, GitPullAction pullAction)
    {
        bool pulled = false;

        bool Action()
        {
            using FormPull formPull = new(commands, remoteBranch, remote, pullAction);
            DialogResult dlgResult = pullOnShow
                ? formPull.PullAndShowDialogWhenFailed(owner, remote, pullAction)
                : formPull.ShowDialog(owner);

            if (dlgResult == DialogResult.OK)
            {
                pulled = !formPull.ErrorOccurred;
            }

            return dlgResult == DialogResult.OK;
        }

        bool done = commands.DoActionOnRepo(owner, Action);

        pullCompleted = pulled;

        return done;
    }
}

internal sealed class PushHandler(GitUICommands commands) : IUICommandHandler<Intent.Push>
{
    public bool Execute(Intent.Push command, IWin32Window? owner)
        => Execute(command, owner, out _);

    public bool Execute(Intent.Push command, IWin32Window? owner, out bool pushCompleted)
    {
        bool pushed = false;

        bool Action()
        {
            using FormPush form = new(commands, command.BranchName);
            if (command.ForceWithLease)
            {
                form.CheckForceWithLease();
            }

            DialogResult dlgResult = command.PushOnShow
                ? form.PushAndShowDialogWhenFailed(owner)
                : form.ShowDialog(owner);

            if (dlgResult == DialogResult.OK)
            {
                pushed = !form.ErrorOccurred;
            }

            return dlgResult == DialogResult.OK;
        }

        bool done = commands.DoActionOnRepo(owner, Action);

        pushCompleted = pushed;

        return done;
    }
}
