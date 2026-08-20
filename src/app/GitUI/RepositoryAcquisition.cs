using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Git.UICommands;

namespace GitUI;

public static class RepositoryAcquisition
{
    /// <summary>
    ///  Executes an intent that can acquire a repository (clone, init, open), forwarding any
    ///  <see cref="IGitUICommands.RepositoryAcquired"/> announcement raised during the execution
    ///  to <paramref name="onAcquired"/>. The subscription lasts only for the call, so the intent
    ///  stays pure data while the caller still learns about the repository it asked for.
    /// </summary>
    public static bool ExecuteWithRepositoryAcquired(this IGitUICommands commands, IUICommand command, object? ownerWindow, EventHandler<GitModuleEventArgs> onAcquired)
    {
        commands.RepositoryAcquired += onAcquired;
        try
        {
            return commands.Execute(command, ownerWindow);
        }
        finally
        {
            commands.RepositoryAcquired -= onAcquired;
        }
    }
}
