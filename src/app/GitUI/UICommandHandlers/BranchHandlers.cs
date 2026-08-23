using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;
using GitUI.HelperDialogs;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class CheckoutBranchHandler(GitUICommands commands) : IUICommandHandler<Intent.CheckoutBranch>
{
    public bool Execute(Intent.CheckoutBranch command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
        {
            using FormCheckoutBranch form = new(commands, command.Branch, command.Remote, command.ContainObjectIds);
            return form.DoDefaultActionOrShow(owner) != DialogResult.Cancel;
        }, preEvent: commands.PreCheckoutBranchEvent, postEvent: commands.PostCheckoutBranchEvent);
    }
}

internal sealed class CheckoutRemoteBranchHandler(GitUICommands commands) : IUICommandHandler<Intent.CheckoutRemoteBranch>
{
    public bool Execute(Intent.CheckoutRemoteBranch command, IWin32Window? owner)
        => commands.Execute(new Intent.CheckoutBranch(command.Branch, Remote: true), owner);
}

internal sealed class CheckoutRevisionHandler(GitUICommands commands) : IUICommandHandler<Intent.CheckoutRevision>
{
    public bool Execute(Intent.CheckoutRevision command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
        {
            using FormCheckoutRevision form = new(commands);
            form.SetRevision(command.Revision);
            return form.ShowDialog(owner) == DialogResult.OK;
        }, preEvent: commands.PreCheckoutRevisionEvent, postEvent: commands.PostCheckoutRevisionEvent);
    }
}

internal sealed class CreateBranchHandler(GitUICommands commands) : IUICommandHandler<Intent.CreateBranch>
{
    public bool Execute(Intent.CreateBranch command, IWin32Window? owner)
    {
        if (commands.Module.IsBareRepository() || command.ObjectId.IsArtificial)
        {
            return false;
        }

        bool Action()
        {
            using FormCreateBranch form = new(commands, command.ObjectId, command.NewBranchNamePrefix);
            return form.ShowDialog(owner) == DialogResult.OK;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class CreateBranchFromHandler(GitUICommands commands) : IUICommandHandler<Intent.CreateBranchFrom>
{
    public bool Execute(Intent.CreateBranchFrom command, IWin32Window? owner)
    {
        ObjectId objectId = commands.Module.RevParse(command.Branch);
        if (objectId.IsZero)
        {
            MessageBoxes.Show($"Branch \"{command.Branch}\" could not be resolved.", TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        return commands.Execute(new Intent.CreateBranch(objectId), owner);
    }
}

internal sealed class DeleteBranchesHandler(GitUICommands commands) : IUICommandHandler<Intent.DeleteBranches>
{
    public bool Execute(Intent.DeleteBranches command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
        {
            using FormDeleteBranch form = new(commands, command.Branches);
            form.ShowDialog(owner);
            return true;
        }, changesRepo: false);
    }
}

internal sealed class DeleteRemoteBranchHandler(GitUICommands commands) : IUICommandHandler<Intent.DeleteRemoteBranch>
{
    public bool Execute(Intent.DeleteRemoteBranch command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
        {
            using FormDeleteRemoteBranch form = new(commands, command.RemoteBranch);
            form.ShowDialog(owner);
            return true;
        }, changesRepo: false);
    }
}

internal sealed class MergeBranchHandler(GitUICommands commands) : IUICommandHandler<Intent.MergeBranch>
{
    public bool Execute(Intent.MergeBranch command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormMergeBranch form = new(commands, command.Branch);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, changesRepo: false);
    }
}

internal sealed class RenameBranchHandler(GitUICommands commands) : IUICommandHandler<Intent.RenameBranch>
{
    public bool Execute(Intent.RenameBranch command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormRenameBranch form = new(commands, command.Branch);
            return form.ShowDialog(owner) == DialogResult.OK;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class ResetCurrentBranchHandler(GitUICommands commands) : IUICommandHandler<Intent.ResetCurrentBranch>
{
    public bool Execute(Intent.ResetCurrentBranch command, IWin32Window? owner)
    {
        ObjectId objectId = commands.Module.RevParse(command.Branch);
        if (objectId.IsZero)
        {
            MessageBoxes.Show($"Branch \"{command.Branch}\" could not be resolved.", TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        using FormResetCurrentBranch form = FormResetCurrentBranch.Create(commands, commands.Module.GetRevision(objectId));
        return form.ShowDialog(owner) == DialogResult.OK;
    }
}

internal sealed class RebaseHandler(GitUICommands commands) : IUICommandHandler<Intent.Rebase>
{
    public bool Execute(Intent.Rebase command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormRebase form = new(commands, command.From, command.To, command.Onto, command.Interactive, command.StartImmediately);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class RebaseWithAdvancedOptionsHandler(GitUICommands commands) : IUICommandHandler<Intent.RebaseWithAdvancedOptions>
{
    public bool Execute(Intent.RebaseWithAdvancedOptions command, IWin32Window? owner)
        => commands.Execute(new Intent.Rebase(command.Onto, command.From), owner);
}

internal sealed class ContinueRebaseHandler(GitUICommands commands) : IUICommandHandler<Intent.ContinueRebase>
{
    public bool Execute(Intent.ContinueRebase command, IWin32Window? owner)
        => commands.Execute(new Intent.Rebase(), owner);
}
