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

    IBrowseRepo? BrowseRepo { get; set; }

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
    bool RunCommand(IReadOnlyList<string> args);
    IGitUICommands WithGitModule(IGitModule module);
    IGitUICommands WithWorkingDirectory(string? workingDirectory);
}
