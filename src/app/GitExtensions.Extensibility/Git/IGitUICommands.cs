using GitExtensions.Extensibility.Git.UICommands;

namespace GitExtensions.Extensibility.Git;

public interface IGitUICommands : IUICommandBus
{
    event EventHandler<GitUIEventArgs>? PostBrowseInitialize;
    event EventHandler<GitUIPostActionEventArgs>? PostCheckoutBranch;
    event EventHandler<GitUIPostActionEventArgs>? PostCheckoutRevision;
    event EventHandler<GitUIPostActionEventArgs>? PostCommit;
    event EventHandler<GitUIPostActionEventArgs>? PostEditGitIgnore;
    event EventHandler<GitUIEventArgs>? PostRegisterPlugin;
    event EventHandler<GitUIEventArgs>? PostRepositoryChanged;
    event EventHandler<GitUIPostActionEventArgs>? PostSettings;
    event EventHandler<GitUIPostActionEventArgs>? PostUpdateSubmodules;
    event EventHandler<GitUIEventArgs>? PreCheckoutBranch;
    event EventHandler<GitUIEventArgs>? PreCheckoutRevision;
    event EventHandler<GitUIEventArgs>? PreCommit;

    /// <summary>
    ///  Raised when a repository has been acquired on the user's behalf — cloned, initialised,
    ///  or opened — carrying the module for it. This replaces the callback payloads the
    ///  Clone/InitializeRepository/CloneForkFromHoster intents used to carry: callers that want
    ///  to switch to the acquired repository subscribe around <see cref="IUICommandBus.Execute(IUICommand, object?)"/>.
    /// </summary>
    event EventHandler<GitModuleEventArgs>? RepositoryAcquired;

    IBrowseRepo? BrowseRepo { get; set; }

    /// <summary>
    ///  Whether anything is currently listening to <see cref="RepositoryAcquired"/> — dialogs use
    ///  this to skip offering "open the new repository?" when nobody could act on the answer.
    /// </summary>
    bool HasRepositoryAcquiredSubscribers { get; }

    IGitModule Module { get; }

    /// <summary>
    /// RepoChangedNotifier.Notify() should be called after each action that changes repo state
    /// </summary>
    ILockableNotifier RepoChangedNotifier { get; }

    void AddCommitTemplate(string key, Func<string> addingText, object? icon, bool isRegex = false);
    void RemoveCommitTemplate(string key);
    IGitRemoteCommand CreateRemoteCommand();
    bool DoActionOnRepo(Func<bool> action);
    void RaisePostBrowseInitialize(object? ownerWindow);
    void RaisePostRegisterPlugin(object? ownerWindow);

    /// <summary>Announces an acquired repository to <see cref="RepositoryAcquired"/> subscribers.</summary>
    void RaiseRepositoryAcquired(IGitModule gitModule);

    bool RunCommand(IReadOnlyList<string> args);
    IGitUICommands WithGitModule(IGitModule module);
    IGitUICommands WithWorkingDirectory(string? workingDirectory);
}
