using GitExtensions.Extensibility.Settings;
using GitUIPluginInterfaces;

namespace GitExtensions.Extensibility.Git.UICommands;

// One record per user-level operation, mirroring the Commands factory for git commands.
// Overload families on IGitUICommands collapse into optional parameters here; the owner
// window is deliberately absent (resolved ambiently by the host).
//
// Not modelled as intents (see cross-platform-plan.md section 13):
//  - ShowModelessForm: passes a Func<Form> form factory, not data - explicit special case.
//  - AddCommitTemplate/RemoveCommitTemplate: Func<string> + Image payload.
//  - AddUpstreamRemote, StartCloneForkFromHoster, StartCreatePullRequest,
//    StartPullRequestsDialog, StartSettingsDialog(IGitPlugin): these reference
//    IRepositoryHostPlugin/IGitPlugin, which stay Windows-bound until M3 clears
//    IGitUICommands/GitUIEventArgs from their signatures (plus IGitPlugin's Image icon);
//    modelling them now would put this file on the probe's exclusion list.

public sealed record AddFiles(string? Files = null) : IUICommand;
public sealed record AddToGitIgnore(bool LocalExclude, IReadOnlyList<string> FilePatterns) : IUICommand;
public sealed record AmendCommit(GitRevision Revision) : IUICommand;
public sealed record ApplyPatch(string? PatchFile = null) : IUICommand;
public sealed record Archive(GitRevision? Revision = null, GitRevision? Revision2 = null, string? Path = null) : IUICommand;
public sealed record BatchFileProcess(string BatchFile) : IUICommand;
public sealed record Browse(BrowseArguments? Args = null) : IUICommand;
public sealed record CheckoutBranch(string Branch = "", bool Remote = false, IReadOnlyList<ObjectId>? ContainObjectIds = null) : IUICommand;
public sealed record CheckoutRemoteBranch(string Branch) : IUICommand;
public sealed record CheckoutRevision(string? Revision = null) : IUICommand;

/// <summary>Zero or one revision opens the cherry-pick dialog for that selection; several revisions cherry-pick sequentially.</summary>
public sealed record CherryPick(IReadOnlyList<GitRevision>? Revisions = null) : IUICommand;

public sealed record CleanupRepository(string? Path = null) : IUICommand;

/// <summary>Wart: <paramref name="GitModuleChanged"/> is a callback, not data - kept so the intent stays usable until handlers raise a proper event.</summary>
public sealed record Clone(string? Url = null, bool OpenedFromProtocolHandler = false, EventHandler<GitModuleEventArgs>? GitModuleChanged = null) : IUICommand;

public sealed record CommandLineProcess(string? Command, ArgumentString Arguments) : IUICommand;
public sealed record Commit(string? CommitMessage = null, bool ShowOnlyWhenChanges = false) : IUICommand;
public sealed record CommitDiff(ObjectId ObjectId) : IUICommand;
public sealed record CompareRevisions : IUICommand;
public sealed record ContinueRebase : IUICommand;
public sealed record CreateBranch(ObjectId ObjectId = default, string? NewBranchNamePrefix = null) : IUICommand;

/// <summary>Resolves <paramref name="Branch"/> to a revision first; shows an error if it does not resolve.</summary>
public sealed record CreateBranchFrom(string Branch) : IUICommand;

public sealed record CreateTag(GitRevision? Revision = null) : IUICommand;
public sealed record DeleteBranches(IReadOnlyList<string> Branches) : IUICommand;
public sealed record DeleteRemoteBranch(string RemoteBranch) : IUICommand;
public sealed record DeleteTag(string? Tag) : IUICommand;
public sealed record EditFile(string? FileName, bool ShowWarning = false, int? LineNumber = null) : IUICommand;
public sealed record EditGitAttributes : IUICommand;
public sealed record EditGitIgnore(bool LocalExcludes) : IUICommand;
public sealed record FileHistory(string FileName, GitRevision? Revision = null, bool FilterByRevision = false, bool ShowBlame = false) : IUICommand;
public sealed record FixupCommit(GitRevision Revision) : IUICommand;
public sealed record FormatPatch : IUICommand;
public sealed record GeneralSettings : IUICommand;
public sealed record GitCommandLineProcess(IGitCommand Command) : IUICommand;
public sealed record GitCommandProcess(ArgumentString Arguments) : IUICommand;

/// <summary>Wart: <paramref name="GitModuleChanged"/> is a callback, not data - see <see cref="Clone"/>.</summary>
public sealed record InitializeRepository(string? Directory = null, EventHandler<GitModuleEventArgs>? GitModuleChanged = null) : IUICommand;

public sealed record MailMap : IUICommand;
public sealed record MergeBranch(string? Branch) : IUICommand;
public sealed record OpenSettings(SettingsPageReference? InitialPage = null) : IUICommand;
public sealed record OpenWithDifftool(IReadOnlyList<GitRevision?> Revisions, string FileName, string? OldFileName, RevisionDiffKind DiffKind, bool IsTracked, string? CustomTool = null) : IUICommand;
public sealed record PluginSettings : IUICommand;
public sealed record Pull(string? RemoteBranch = null, string? Remote = null, GitPullAction PullAction = GitPullAction.None) : IUICommand;

/// <summary>Pulls without showing the dialog (unless settings require it).</summary>
public sealed record PullImmediately(string? RemoteBranch = null, string? Remote = null, GitPullAction PullAction = GitPullAction.None) : IUICommand;

public sealed record Push(bool PushOnShow = false, bool ForceWithLease = false, string? BranchName = null) : IUICommand;

/// <summary>
///  Covers the whole rebase family: plain dialog (defaults), immediate rebase
///  (<c>StartImmediately: true</c>), and interactive rebase (<c>Interactive: true, StartImmediately: true</c>).
///  <see cref="ContinueRebase"/> is separate because it targets no revision.
/// </summary>
public sealed record Rebase(string? Onto = null, string? From = "", string? To = null, bool Interactive = false, bool StartImmediately = false) : IUICommand;

/// <summary>Rebase dialog with the advanced options panel expanded.</summary>
public sealed record RebaseWithAdvancedOptions(string Onto, string From = "") : IUICommand;

public sealed record Remotes(string? PreselectRemote = null, string? PreselectLocal = null) : IUICommand;
public sealed record RenameBranch(string Branch) : IUICommand;
public sealed record RepoSettings : IUICommand;
public sealed record ResetChanges(IReadOnlyCollection<GitItemStatus> WorkTreeFiles, bool OnlyWorkTree) : IUICommand;
public sealed record ResetCurrentBranch(string Branch) : IUICommand;
public sealed record ResolveConflicts(bool OfferCommit = true) : IUICommand;
public sealed record RevertCommit(GitRevision Revision) : IUICommand;
public sealed record SparseWorkingCopy : IUICommand;
public sealed record SquashCommit(GitRevision Revision) : IUICommand;
public sealed record Stash(bool ManageStashes = true, string? InitialStash = null) : IUICommand;
public sealed record StashApply(string StashName) : IUICommand;
public sealed record StashDrop(string StashName) : IUICommand;
public sealed record StashPop(string StashName = "") : IUICommand;
public sealed record StashSave(bool IncludeUntrackedFiles, bool KeepIndex = false, string Message = "", IReadOnlyList<string>? SelectedFiles = null) : IUICommand;
public sealed record StashStaged : IUICommand;
public sealed record Submodules : IUICommand;
public sealed record SyncSubmodules : IUICommand;
public sealed record UpdateSubmodule(string SubmoduleLocalPath, string SubmoduleParentPath) : IUICommand;

/// <summary>Prompts, then updates all submodules without the dialog.</summary>
public sealed record UpdateSubmodules : IUICommand;

public sealed record UpdateSubmodulesDialog(string SubmoduleLocalPath = "") : IUICommand;
public sealed record VerifyDatabase : IUICommand;
public sealed record ViewPatch(string? PatchFile = null) : IUICommand;
public sealed record WorktreeCreate(string MainWorktreePath) : IUICommand;
public sealed record WorktreeDelete(string WorktreePath) : IUICommand;
public sealed record WorktreeSwitch(string WorktreePath) : IUICommand;
