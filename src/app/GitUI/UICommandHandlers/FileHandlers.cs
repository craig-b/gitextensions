using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Settings;
using GitUI.CommandsDialogs;
using GitUI.Editor;
using Intent = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

internal sealed class EditFileHandler(GitUICommands commands) : IUICommandHandler<Intent.EditFile>
{
    public bool Execute(Intent.EditFile command, IWin32Window? owner)
    {
        using FormEditor formEditor = new(commands, command.FileName, command.ShowWarning, lineNumber: command.LineNumber);
        return !formEditor.IsDisposed && formEditor.ShowDialog() != DialogResult.Cancel;
    }
}

internal sealed class FileHistoryHandler(GitUICommands commands, ISettings settings) : IUICommandHandler<Intent.FileHistory>
{
    public bool Execute(Intent.FileHistory command, IWin32Window? owner)
    {
        bool useBrowseForFileHistory = settings.UseBrowseForFileHistory;
        string arguments = useBrowseForFileHistory ? $"browse {GitUICommands.PathFilterArg}={command.FileName.Quote()}{GetCommitIdArg()}"
            : $"{(command.ShowBlame ? GitUICommands.BlameHistoryCommand : GitUICommands.FileHistoryCommand)} {command.FileName.Quote()}{GetCommitIdArg()} {(command.FilterByRevision ? GitUICommands.FilterByRevisionArg : string.Empty)}";
        GitUICommands.Launch(arguments, commands.Module.WorkingDir);

        return true;

        string GetCommitIdArg()
        {
            if (command.Revision is null)
            {
                return "";
            }

            if (useBrowseForFileHistory)
            {
                return $" -commit={command.Revision.ObjectId}";
            }

            // Avoid a race condition in FormFileHistory selecting an artificial commit.
            // Without a hash passed, it automatically selects the first real revision.
            if (command.Revision.IsArtificial)
            {
                return "";
            }

            return $" {command.Revision.ObjectId}";
        }
    }
}

internal sealed class OpenWithDifftoolHandler(GitUICommands commands) : IUICommandHandler<Intent.OpenWithDifftool>
{
    public bool Execute(Intent.OpenWithDifftool command, IWin32Window? owner)
    {
        // Note: Order in revisions is that first clicked is last in array

        if (!RevisionDiffInfoProvider.TryGet(command.Revisions, command.DiffKind, out string? firstRevision, out string? secondRevision, out string? error))
        {
            MessageBoxes.Show(owner, error, TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else
        {
            commands.Module.OpenWithDifftool(command.FileName, command.OldFileName, firstRevision, secondRevision, isTracked: command.IsTracked, customTool: command.CustomTool);
        }

        return true;
    }
}
