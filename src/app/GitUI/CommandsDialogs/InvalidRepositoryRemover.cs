using GitCommands;
using GitCommands.Open;
using GitCommands.UserRepositoryHistory;

namespace GitUI.CommandsDialogs;

/// <summary>
///  Represents an interface for removing invalid repositories.
/// </summary>
public interface IInvalidRepositoryRemover
{
    /// <summary>
    ///  Shows a dialog to remove the provided invalid repository, or all invalid repositories.
    /// </summary>
    /// <param name="repositoryPath">An invalid repository.</param>
    /// <returns><see langword="true"/> if any repositories were removed; otherwise <see langword="false"/>.</returns>
    /// <remarks>The method does not verify that the provided <paramref name="repositoryPath"/> is invalid.</remarks>
    bool ShowDeleteInvalidRepositoryDialog(string repositoryPath);
}

internal sealed class InvalidRepositoryRemover : IInvalidRepositoryRemover
{
    public bool ShowDeleteInvalidRepositoryDialog(string repositoryPath)
    {
        InvalidRepositoryPromptOptions options = InvalidRepositoryPromptOptions.Evaluate(
            ThreadHelper.JoinableTaskFactory.Run(RepositoryHistoryManager.Locals.LoadRecentHistoryAsync).Select(repo => repo.Path),
            GitModule.IsValidGitWorkingDir);

        TaskDialogPage page = new()
        {
            Heading = TranslatedStrings.DirectoryInvalidRepository,
            Caption = TranslatedStrings.Open,
            Icon = TaskDialogIcon.Error,
            Buttons = { TaskDialogButton.Cancel },
            AllowCancel = true,
            SizeToContent = true
        };
        TaskDialogCommandLinkButton btnRemoveSelectedInvalidRepository = new(TranslatedStrings.RemoveSelectedInvalidRepository);
        page.Buttons.Add(btnRemoveSelectedInvalidRepository);

        TaskDialogCommandLinkButton btnRemoveAllInvalidRepositories = new(string.Format(TranslatedStrings.RemoveAllInvalidRepositories, options.InvalidCount));
        if (options.OfferRemoveAll)
        {
            page.Buttons.Add(btnRemoveAllInvalidRepositories);
        }

        TaskDialogButton result = TaskDialog.ShowDialog(page);

        if (result == btnRemoveSelectedInvalidRepository)
        {
            ThreadHelper.JoinableTaskFactory.Run(() => RepositoryHistoryManager.Locals.RemoveRecentAsync(repositoryPath));
            return true;
        }

        if (result == btnRemoveAllInvalidRepositories)
        {
            ThreadHelper.JoinableTaskFactory.Run(() => RepositoryHistoryManager.Locals.RemoveInvalidRepositoriesAsync(repoPath => GitModule.IsValidGitWorkingDir(repoPath)));
            return true;
        }

        return false;
    }
}
