using GitCommands.Git;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Settings;
using GitUI.CommandsDialogs;
using GitUI.HelperDialogs;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class SubmodulesHandler(GitUICommands commands) : IUICommandHandler<Intent.Submodules>
{
    public bool Execute(Intent.Submodules command, IWin32Window? owner)
    {
        bool Action()
        {
            using FormSubmodules form = new(commands);
            form.ShowDialog(owner);
            return true;
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class SyncSubmodulesHandler(GitUICommands commands) : IUICommandHandler<Intent.SyncSubmodules>
{
    public bool Execute(Intent.SyncSubmodules command, IWin32Window? owner)
    {
        bool Action()
        {
            return FormProcess.ShowDialog(owner, commands, arguments: Commands.SubmoduleSync(""), commands.Module.WorkingDir, input: null, useDialogSettings: true);
        }

        return commands.DoActionOnRepo(owner, Action);
    }
}

internal sealed class UpdateSubmodulesDialogHandler(GitUICommands commands) : IUICommandHandler<Intent.UpdateSubmodulesDialog>
{
    public bool Execute(Intent.UpdateSubmodulesDialog command, IWin32Window? owner)
    {
        bool Action()
        {
            return FormProcess.ShowDialog(owner, commands, arguments: Commands.SubmoduleUpdate(command.SubmoduleLocalPath), commands.Module.WorkingDir, input: null, useDialogSettings: true);
        }

        return commands.DoActionOnRepo(owner, Action, postEvent: commands.PostUpdateSubmodulesEvent);
    }
}

internal sealed class UpdateSubmoduleHandler(GitUICommands commands) : IUICommandHandler<Intent.UpdateSubmodule>
{
    public bool Execute(Intent.UpdateSubmodule command, IWin32Window? owner)
    {
        bool Action()
        {
            // Execute the submodule update comment from the submodule's parent directory
            return FormProcess.ShowDialog(owner, commands, arguments: Commands.SubmoduleUpdate(command.SubmoduleLocalPath), command.SubmoduleParentPath, null, true);
        }

        return commands.DoActionOnRepo(owner, Action, postEvent: commands.PostUpdateSubmodulesEvent);
    }
}

internal sealed class UpdateSubmodulesHandler(GitUICommands commands, ISettings settings) : IUICommandHandler<Intent.UpdateSubmodules>
{
    public bool Execute(Intent.UpdateSubmodules command, IWin32Window? owner)
    {
        if (!commands.Module.HasSubmodules())
        {
            return true;
        }

        bool updateSubmodules = settings.UpdateSubmodulesOnCheckout ?? (settings.DontConfirmUpdateSubmodulesOnCheckout ?? MessageBoxes.ConfirmUpdateSubmodules(owner));

        if (updateSubmodules)
        {
            commands.Execute(new Intent.UpdateSubmodulesDialog(), owner);
        }

        return true;
    }
}
