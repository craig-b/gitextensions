using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;
using GitUI.HelperDialogs;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class CommandLineProcessHandler(GitUICommands commands) : IUICommandHandler<Intent.CommandLineProcess>
{
    public bool Execute(Intent.CommandLineProcess command, IWin32Window? owner)
    {
        return FormProcess.ShowDialog(owner, commands, command.Arguments, commands.Module.WorkingDir, input: null, useDialogSettings: true, process: command.Command);
    }
}

internal sealed class GitCommandLineProcessHandler(GitUICommands commands) : IUICommandHandler<Intent.GitCommandLineProcess>
{
    public bool Execute(Intent.GitCommandLineProcess command, IWin32Window? owner)
    {
        bool success = command.Command.AccessesRemote
            ? FormRemoteProcess.ShowDialog(owner, commands, command.Command.Arguments)
            : FormProcess.ShowDialog(owner, commands, arguments: command.Command.Arguments, commands.Module.WorkingDir, input: null, useDialogSettings: true);

        if (success && command.Command.ChangesRepoState)
        {
            commands.RepoChangedNotifier.Notify();
        }

        return success;
    }
}

internal sealed class GitCommandProcessHandler(GitUICommands commands) : IUICommandHandler<Intent.GitCommandProcess>
{
    public bool Execute(Intent.GitCommandProcess command, IWin32Window? owner)
    {
        return FormProcess.ShowDialog(owner, commands, command.Arguments, commands.Module.WorkingDir, input: null, useDialogSettings: true);
    }
}

internal sealed class BatchFileProcessHandler(GitUICommands commands) : IUICommandHandler<Intent.BatchFileProcess>
{
    public bool Execute(Intent.BatchFileProcess command, IWin32Window? owner)
    {
        string tempFile = Path.Join(Path.GetTempPath(), $"GitExtensions-{Guid.NewGuid():N}.cmd");

        try
        {
            using (StreamWriter writer = new(tempFile))
            {
                writer.WriteLine("@prompt $G");
                writer.Write(command.BatchFile);
            }

            FormProcess.ShowDialog(owner: null, commands, arguments: $"/C \"{tempFile}\"", commands.Module.WorkingDir, input: null, useDialogSettings: true, process: "cmd.exe");
        }
        finally
        {
            File.Delete(tempFile);
        }

        return true;
    }
}
