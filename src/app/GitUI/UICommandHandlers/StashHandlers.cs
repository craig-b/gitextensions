using GitCommands.Git;
using GitExtensions.Extensibility;
using GitUI.CommandsDialogs;
using GitUI.HelperDialogs;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class StashHandler(GitUICommands commands) : IUICommandHandler<Intent.Stash>
{
    public bool Execute(Intent.Stash command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormStash form = new(commands, command.InitialStash) { ManageStashes = command.ManageStashes };
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action, changesRepo: false);
    }
}

internal sealed class StashSaveHandler(GitUICommands commands) : IUICommandHandler<Intent.StashSave>
{
    public bool Execute(Intent.StashSave command, IWin32Window? owner)
    {
        bool Action()
        {
            ArgumentString arguments = Commands.StashSave(command.IncludeUntrackedFiles, command.KeepIndex, command.Message, command.SelectedFiles);
            FormProcess.ShowDialog(owner, commands, arguments, commands.Module.WorkingDir, input: null, useDialogSettings: true);

            // git-stash may have changed commits also if aborted, the grid must be refreshed
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class StashStagedHandler(GitUICommands commands) : IUICommandHandler<Intent.StashStaged>
{
    public bool Execute(Intent.StashStaged command, IWin32Window? owner)
    {
        bool Action()
        {
            FormProcess.ShowDialog(owner, commands, arguments: "stash --staged", commands.Module.WorkingDir, input: null, useDialogSettings: true);

            // git-stash may have changed commits also if aborted, the grid must be refreshed
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class StashPopHandler(GitUICommands commands) : IUICommandHandler<Intent.StashPop>
{
    public bool Execute(Intent.StashPop command, IWin32Window? owner)
    {
        bool Action()
        {
            FormProcess.ShowDialog(owner, commands, arguments: $"stash pop {command.StashName.QuoteNE()}", commands.Module.WorkingDir, input: null, useDialogSettings: true);
            MergeConflictHandler.HandleMergeConflicts(commands, owner, false, false);

            // git-stash may have changed commits also if aborted, the grid must be refreshed
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class StashDropHandler(GitUICommands commands) : IUICommandHandler<Intent.StashDrop>
{
    public bool Execute(Intent.StashDrop command, IWin32Window? owner)
    {
        bool Action()
        {
            FormProcess.ShowDialog(owner, commands, arguments: $"stash drop {command.StashName.Quote()}", commands.Module.WorkingDir, input: null, useDialogSettings: true);

            // git-stash may have changed commits also if aborted, the grid must be refreshed
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class StashApplyHandler(GitUICommands commands) : IUICommandHandler<Intent.StashApply>
{
    public bool Execute(Intent.StashApply command, IWin32Window? owner)
    {
        bool Action()
        {
            FormProcess.ShowDialog(owner, commands, arguments: $"stash apply {command.StashName.Quote()}", commands.Module.WorkingDir, input: null, useDialogSettings: true);
            MergeConflictHandler.HandleMergeConflicts(commands, owner, false, false);

            // git-stash may have changed commits also if aborted, the grid must be refreshed
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}
