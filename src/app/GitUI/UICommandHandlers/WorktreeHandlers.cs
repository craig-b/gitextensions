using GitCommands;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Settings;
using GitUI.CommandsDialogs;
using GitUI.CommandsDialogs.WorktreeDialog;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class WorktreeCreateHandler(GitUICommands commands, IGitExecutorProvider gitExecutorProvider) : IUICommandHandler<Intent.WorktreeCreate>
{
    public bool Execute(Intent.WorktreeCreate command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
        {
            using FormCreateWorktree form = new(commands, command.MainWorktreePath);
            if (form.ShowDialog(owner) != DialogResult.OK)
            {
                return false;
            }

            if (form.OpenWorktree)
            {
                GitModule newModule = new(gitExecutorProvider, form.WorktreeDirectory);
                if (newModule.IsValidGitWorkingDir() && GitUICommands.FindFormBrowse(owner) is FormBrowse browse)
                {
                    browse.SetWorkingDir(Path.GetFullPath(form.WorktreeDirectory));
                }
            }

            return true;
        });
    }
}

internal sealed class WorktreeDeleteHandler(GitUICommands commands) : IUICommandHandler<Intent.WorktreeDelete>
{
    public bool Execute(Intent.WorktreeDelete command, IWin32Window? owner)
    {
        return commands.DoActionOnRepo(owner, action: () =>
        {
            TaskDialogButton result = TaskDialog.ShowDialog(owner!, new TaskDialogPage
            {
                Text = string.Format(TranslatedStrings.DeleteWorktreeConfirmation, command.WorktreePath),
                Caption = TranslatedStrings.DeleteWorktreeCaption,
                Heading = TranslatedStrings.CannotBeUndone,
                Buttons = { TaskDialogButton.Yes, TaskDialogButton.No },
                Icon = TaskDialogIcon.Warning,
                SizeToContent = true
            });

            if (result != TaskDialogButton.Yes)
            {
                return false;
            }

            if (!command.WorktreePath.TryDeleteDirectory(out string? errorMessage))
            {
                TaskDialog.ShowDialog(owner!, new TaskDialogPage
                {
                    Text = $"{string.Format(TranslatedStrings.DeleteWorktreeFailed, command.WorktreePath)}\n{errorMessage}",
                    Caption = TranslatedStrings.Error,
                    Icon = TaskDialogIcon.Error,
                    SizeToContent = true
                });

                return false;
            }

            commands.Execute(new Intent.CommandLineProcess(Command: null, "worktree prune"), owner);
            return true;
        });
    }
}

internal sealed class WorktreeSwitchHandler(ISettings settings) : IUICommandHandler<Intent.WorktreeSwitch>
{
    public bool Execute(Intent.WorktreeSwitch command, IWin32Window? owner)
    {
        if (!settings.DontConfirmSwitchWorktree)
        {
            TaskDialogButton result = TaskDialog.ShowDialog(owner!, new TaskDialogPage
            {
                Text = string.Format(TranslatedStrings.SwitchWorktreeConfirmation, command.WorktreePath),
                Caption = TranslatedStrings.SwitchWorktreeCaption,
                Buttons = { TaskDialogButton.Yes, TaskDialogButton.No },
                Icon = TaskDialogIcon.Information,
                SizeToContent = true
            });

            if (result != TaskDialogButton.Yes)
            {
                return false;
            }
        }

        if (!Directory.Exists(command.WorktreePath))
        {
            return false;
        }

        if (GitUICommands.FindFormBrowse(owner) is FormBrowse browse)
        {
            browse.SetWorkingDir(Path.GetFullPath(command.WorktreePath));
        }

        return true;
    }
}
