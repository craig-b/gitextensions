using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitCommands;
using GitCommands.Branch;
using GitCommands.Commit;
using GitCommands.Git;
using GitCommands.Git.Extensions;
using GitCommands.Git.Tag;
using GitCommands.Merge;
using GitCommands.LeftPanel;
using GitCommands.RichText;
using GitExtUtils;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
using GitUI;
using GitUI.Editor.Diff;
using ResourceManager;
using ResourceManager.CommitDataRenders;

namespace GitExtensions.Avalonia;

/// <summary>
///  The slice's engine facade: everything below this line is the portable core, consumed exactly
///  as the WinForms app consumes it - GitModule + RevisionReader for the log, CommitDataManager +
///  the M6 renderers for commit info (RichContent), and the M4 highlight services for the diff
///  (StyledSpan). No WinForms, no Avalonia - this class could serve any client.
/// </summary>
public sealed class SliceSession
{
    private readonly GitModule _module;
    private readonly CommitDataManager _commitDataManager;
    private readonly ICommitDataHeaderRenderer _headerRenderer;
    private readonly ICommitDataBodyRenderer _bodyRenderer;

    internal GitModule Module => _module;

    public SliceSession(string repositoryPath)
    {
        _module = new GitModule(new GitExecutorProvider(new GitDirectoryResolver()), repositoryPath);
        _commitDataManager = new CommitDataManager(() => _module);
        LinkFactory linkFactory = new();
        _headerRenderer = new CommitDataHeaderRenderer(new MonospacedHeaderLabelFormatter(), new DateFormatter(), new MonospacedHeaderRenderStyleProvider(), linkFactory);
        _bodyRenderer = new CommitDataBodyRenderer(() => _module, linkFactory);
    }

    public string WorkingDir => _module.WorkingDir;

    public bool IsValidRepository => _module.IsValidGitWorkingDir();

    public ObjectId CurrentCheckout => _module.GetCurrentCheckout();

    public string SelectedBranch => _module.GetSelectedBranch();

    public ObjectId? ResolveRef(string refName) => _module.RevParse(refName) is { IsZero: false } objectId ? objectId : null;

    public bool IsMergeCommitPending => !_module.RevParse("MERGE_HEAD").IsZero;

    public bool InConflictedMerge => _module.InTheMiddleOfConflictedMerge();

    public bool InPatch => _module.InTheMiddleOfPatch();

    /// <summary>The current conflicts (stage 1/2/3 triples).</summary>
    public Task<List<ConflictData>> GetConflictsAsync() => _module.GetConflictsAsync();

    /// <summary>Resolves a conflict to one side: checkout-index --stage=N then git add.</summary>
    public Task<bool> ResolveConflictSideAsync(string fileName, GitCommands.Conflicts.ConflictSide side)
        => Task.Run(() => _module.HandleConflictSelectSide(fileName, side switch
        {
            GitCommands.Conflicts.ConflictSide.Base => "BASE",
            GitCommands.Conflicts.ConflictSide.Local => "LOCAL",
            _ => "REMOTE",
        }));

    /// <summary>Resolves a delete/modify conflict by removing the file.</summary>
    public Task<(bool Success, string Output)> RemoveConflictedFileAsync(string fileName)
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("rm") { "--", fileName.Quote() }));

    /// <summary>Marks a conflict resolved (git add).</summary>
    public Task<(bool Success, string Output)> StageConflictedFileAsync(string fileName)
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("add") { "--", fileName.Quote() }));

    public bool IsDetachedHead => _module.IsDetachedHead();

    public bool InRebase => _module.InTheMiddleOfRebase();

    /// <summary>
    ///  The status-bar push target: the model resolves where the branch would push, the view
    ///  words the annotations - same split as the WinForms dialog.
    /// </summary>
    public BranchPushTarget PushTarget => BranchPushTarget.Resolve(
        _module.GetRefs(RefsFilter.Heads).FirstOrDefault(r => r.LocalName == SelectedBranch),
        _module.GetRemoteNames(),
        SelectedBranch);

    /// <summary>
    ///  The left panel's ref hierarchy: local branches, remote branches, and tags, each shaped
    ///  by the portable RefTreeBuilder with the user's priority settings applied.
    /// </summary>
    public (IReadOnlyList<RefTreeNode> Branches, IReadOnlyList<RefTreeNode> Remotes, IReadOnlyList<RefTreeNode> Tags) GetRefPanel()
    {
        IReadOnlyList<IGitRef> refs = _module.GetRefs(RefsFilter.NoFilter);
        string currentBranch = SelectedBranch;

        IReadOnlyList<Remote> remotes = ThreadHelper.JoinableTaskFactory.Run(_module.GetRemotesAsync);
        GitCommands.Remotes.ConfigFileRemoteSettingsManager remotesManager = new(() => _module);

        return (
            RefTreeBuilder.Build(refs.Where(r => r.IsHead), r => r.LocalName, AppSettings.PrioritizedBranchNames, currentBranch, RefTreeNodeKind.LocalBranch),
            RemoteTreeBuilder.Build(
                [.. refs.Where(r => r.IsRemote)],
                remotes,
                remotesManager.GetDisabledRemotes(),
                AppSettings.PrioritizedBranchNames,
                AppSettings.PrioritizedRemoteNames),
            RefTreeBuilder.Build(refs.Where(r => r.IsTag), r => r.LocalName, prioritySetting: "", leafKind: RefTreeNodeKind.Tag));
    }

    /// <summary>The stash rows for the left panel, with each stash's commit resolved so log jumps work.</summary>
    public IReadOnlyList<StashTreeNode> GetStashPanel()
        => StashTreeBuilder.Build(_module.GetStashes()
            .Select(stash => new GitRevision(_module.RevParse(stash.Name))
            {
                ReflogSelector = stash.Name,
                Subject = stash.Message,
            }));

    /// <summary>The worktree rows for the left panel.</summary>
    public IReadOnlyList<WorktreeTreeNode> GetWorktreePanel()
        => WorktreeTreeBuilder.Build(_module.GetWorktrees(), _module.WorkingDir);

    /// <summary>Creates a worktree with the model's command (worktree.useRelativePaths seeded when unset).</summary>
    public Task<(bool Success, string Output)> CreateWorktreeAsync(string directory, string newBranchOption)
        => Task.Run(() =>
        {
            string relativePath = Path.GetRelativePath(_module.WorkingDir, directory).ToPosixPath().Quote();
            return RunGitOperation(GitCommands.Worktree.WorktreeCreateModel.CreateCommand(
                key => _module.GetEffectiveSetting(key), relativePath, newBranchOption));
        });

    public Task<(bool Success, string Output)> RemoveWorktreeAsync(string worktreePath, bool force)
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("worktree") { "remove", { force, "--force" }, worktreePath.Quote() }));

    public Task<(bool Success, string Output)> PruneWorktreesAsync()
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("worktree") { "prune" }));

    /// <summary>Adds a submodule (portable Commands.AddSubmodule).</summary>
    public Task<(bool Success, string Output)> AddSubmoduleAsync(string remotePath, string localPath, string branch, bool force)
        => Task.Run(() => RunGitOperation(Commands.AddSubmodule(remotePath, localPath, branch, force)));

    /// <summary>Updates all submodules recursively.</summary>
    public Task<(bool Success, string Output)> UpdateSubmodulesAsync()
        => Task.Run(() => RunGitOperation(Commands.SubmoduleUpdate(name: null)));

    private (bool Success, string Output) RunGitOperation(ArgumentString arguments)
    {
        ExecutionResult result = _module.GitExecutable.Execute(arguments, throwOnErrorExit: false);
        return (result.ExitedSuccessfully, result.AllOutput);
    }

    public Task<(bool Success, string Output)> CherryPickAsync(ObjectId commitId)
        => Task.Run(() => RunGitOperation(Commands.CherryPick(commitId, commit: true, arguments: "")));

    public Task<(bool Success, string Output)> RevertAsync(ObjectId commitId)
        => Task.Run(() => RunGitOperation(Commands.Revert(commitId, autoCommit: true, parentIndex: 0)));

    public Task<(bool Success, string Output)> MergeAsync(string refName)
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("merge") { refName.QuoteNE() }));

    /// <summary>Runs a merge built from the portable MergeBranchOptions model.</summary>
    public Task<(bool Success, string Output)> MergeWithOptionsAsync(MergeBranchOptions options)
        => Task.Run(() => RunGitOperation(options.ToArguments(mergeMessagePath: null, _module.GetPathForGitExecution)));

    /// <summary>Rebases the current branch onto the given ref (plain, non-interactive).</summary>
    public Task<(bool Success, string Output)> RebaseAsync(string onto)
        => Task.Run(() => RunGitOperation(Commands.Rebase(new Commands.RebaseOptions { BranchName = onto })));

    /// <summary>Runs an arbitrary process in the repo (the scripts engine's runner).</summary>
    public Task<(bool Success, string Output)> RunProcessAsync(string fileName, string arguments)
        => Task.Run(() =>
        {
            System.Diagnostics.ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = _module.WorkingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode == 0, output);
        });

    /// <summary>The user's git aliases for the palette (name, expansion).</summary>
    public Task<IReadOnlyList<(string Name, string Expansion)>> GetGitAliasesAsync()
        => Task.Run<IReadOnlyList<(string, string)>>(() =>
        {
            (bool success, string output) = RunGitOperation(new GitArgumentBuilder("config") { "--get-regexp", "^alias\\." });
            return success ? GitCommands.Scripts.GitAliasParser.Parse(output) : [];
        });

    /// <summary>Runs git with extra environment variables (the sequence-editor flows need them).</summary>
    public Task<(bool Success, string Output)> RunGitWithEnvAsync(GitExtensions.Extensibility.ArgumentString arguments, IReadOnlyDictionary<string, string> environment)
        => Task.Run(() =>
        {
            System.Diagnostics.ProcessStartInfo startInfo = new()
            {
                FileName = _module.GitExecutable.Command,
                Arguments = $"{_module.GitExecutable.PrefixArguments}{arguments}",
                WorkingDirectory = _module.WorkingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach ((string name, string value) in environment)
            {
                startInfo.Environment[name] = value;
            }

            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode == 0, output);
        });

    /// <summary>Edit/reword via interactive rebase; reword injects the new message through GIT_EDITOR.</summary>
    public async Task<(bool Success, string Output)> RewriteCommitAsync(GitRevision revision, GitCommands.Rewrite.RewriteTodoAction action, string? rewordMessage)
    {
        Dictionary<string, string> environment = new()
        {
            [GitCommands.Rewrite.HistoryRewrite.SequenceEditorVariable] = GitCommands.Rewrite.HistoryRewrite.ReplaceFirstPickEditor(action),
        };

        string? messageFile = null;
        if (action is GitCommands.Rewrite.RewriteTodoAction.Reword && rewordMessage is not null)
        {
            messageFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ge-reword-{Guid.NewGuid():N}.txt");
            await System.IO.File.WriteAllTextAsync(messageFile, rewordMessage);
            environment[GitCommands.Rewrite.HistoryRewrite.EditorVariable] = $"cp '{messageFile}'";
        }

        try
        {
            return await RunGitWithEnvAsync(
                GitCommands.Rewrite.HistoryRewrite.InteractiveRebaseOntoParent(revision.FirstParentId, _module.GitVersion.SupportRebaseMerges),
                environment);
        }
        finally
        {
            if (messageFile is not null && System.IO.File.Exists(messageFile))
            {
                System.IO.File.Delete(messageFile);
            }
        }
    }

    /// <summary>Folds fixup!/squash!/amend! commits into their targets (autosquash, todo accepted verbatim).</summary>
    public Task<(bool Success, string Output)> AutosquashFoldAsync(GitRevision targetRevision)
        => RunGitWithEnvAsync(
            GitCommands.Rewrite.HistoryRewrite.InteractiveRebaseOntoParent(targetRevision.FirstParentId, _module.GitVersion.SupportRebaseMerges, autoSquash: true),
            new Dictionary<string, string>
            {
                [GitCommands.Rewrite.HistoryRewrite.SequenceEditorVariable] = GitCommands.Rewrite.HistoryRewrite.AcceptTodoEditor,
            });

    public Task<(bool Success, string Output)> ContinueRebaseAsync()
        => Task.Run(() => RunGitOperation(Commands.ContinueRebase()));

    public Task<(bool Success, string Output)> AbortRebaseAsync()
        => Task.Run(() => RunGitOperation(Commands.AbortRebase()));

    /// <summary>Resets the current branch to the given commit with the chosen mode.</summary>
    public Task<(bool Success, string Output)> ResetAsync(ResetMode mode, ObjectId commitId)
        => Task.Run(() => RunGitOperation(Commands.Reset(mode, commitId.ToString())));

    /// <summary>Exports a revision with git-archive (whole tree; the model builds the command).</summary>
    public Task<(bool Success, string Output)> ArchiveAsync(GitCommands.Archive.ArchiveFormat format, string revision, string outputFilePath)
        => Task.Run(() => RunGitOperation(GitCommands.Archive.ArchiveModel.BuildCommand(format, revision, outputFilePath, pathArguments: "")));

    /// <summary>The merged-branch scan feeding the delete-branch preflight.</summary>
    public Task<MergedBranchScan> GetMergedBranchScanAsync()
        => Task.Run(() => MergedBranchScan.Parse(_module.GetMergedBranches()));

    public Task<(bool Success, string Output)> CreateBranchAtAsync(string branchName, ObjectId commitId, bool checkout)
        => Task.Run(() => RunGitOperation(Commands.Branch(branchName, commitId, checkout)));

    public Task<(bool Success, string Output)> RenameBranchAsync(string oldName, string newName)
        => Task.Run(() => RunGitOperation(Commands.RenameBranch(oldName, newName)));

    public Task<(bool Success, string Output)> DeleteBranchAsync(string branchName, bool force = false)
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("branch") { force ? "-D" : "-d", branchName.QuoteNE() }));

    public Task<(bool Success, string Output)> DeleteTagAsync(string tagName)
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("tag") { "-d", tagName.QuoteNE() }));

    /// <summary>Creates a tag from the portable GitCreateTagArgs (the GitTagController message-file protocol).</summary>
    public Task<(bool Success, string Output)> CreateTagAsync(GitCreateTagArgs args)
        => Task.Run(() =>
        {
            string? messageFile = null;
            if (args.Operation.CanProvideMessage())
            {
                messageFile = System.IO.Path.Join(_module.WorkingDirGitDir, "TAGMESSAGE");
                System.IO.File.WriteAllText(messageFile, args.TagMessage);
            }

            try
            {
                return RunGitOperation(Commands.CreateTag(args, messageFile, _module.GetPathForGitExecution).Arguments);
            }
            finally
            {
                if (messageFile is not null && System.IO.File.Exists(messageFile))
                {
                    System.IO.File.Delete(messageFile);
                }
            }
        });

    public Task<(bool Success, string Output)> PushTagAsync(string remote, string tagName)
        => Task.Run(() => RunGitOperation(Commands.PushTag(remote, tagName, all: false)));

    /// <summary>The remote a created tag is offered to push to.</summary>
    public string ResolveTagPushRemote() => TagDialogModel.ResolvePushRemote(_module.GetCurrentRemote());

    public Task<(bool Success, string Output)> StashApplyAsync(string reflogSelector)
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("stash") { "apply", reflogSelector.QuoteNE() }));

    public Task<(bool Success, string Output)> StashDropAsync(string reflogSelector)
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("stash") { "drop", reflogSelector.QuoteNE() }));

    public bool InBisect => _module.InTheMiddleOfBisect();

    public Task<(bool Success, string Output)> StartBisectAsync()
        => Task.Run(() => RunGitOperation(Commands.StartBisect()));

    public Task<(bool Success, string Output)> ContinueBisectAsync(GitBisectOption option)
        => Task.Run(() => RunGitOperation(Commands.ContinueBisect(option)));

    public Task<(bool Success, string Output)> StopBisectAsync()
        => Task.Run(() => RunGitOperation(Commands.StopBisect()));

    /// <summary>Points a ref at a commit (the reset-another-branch primitive).</summary>
    public Task<(bool Success, string Output)> UpdateRefAsync(string refCompleteName, ObjectId targetId)
        => Task.Run(() => RunGitOperation(Commands.UpdateRef(refCompleteName, targetId)));

    /// <summary>The reset-another-branch candidate list for a revision, model-ordered, with the preselect.</summary>
    public (IReadOnlyList<IGitRef> Candidates, string? DefaultName) GetResetAnotherBranchCandidates(GitRevision revision)
    {
        List<IGitRef> revisionRemotes = [.. (revision.Refs ?? []).Where(r => r.IsRemote)];
        IReadOnlyList<IGitRef> candidates = GitCommands.Reset.ResetAnotherBranchCandidates.Build(
            _module.GetRefs(RefsFilter.Heads), revisionRemotes, SelectedBranch, revision.ObjectId);
        return (candidates, GitCommands.Reset.ResetAnotherBranchCandidates.ResolveDefault(candidates, revisionRemotes));
    }

    public bool IsBareRepository => _module.IsBareRepository();

    /// <summary>Runs a pull/fetch built from the portable PullOptions model.</summary>
    public Task<(bool Success, string Output)> PullWithOptionsAsync(GitCommands.Pull.PullOptions options)
        => Task.Run(() => RunGitOperation(options.ToArguments(_module)));

    /// <summary>Fetches the default remote (plain "git fetch", via the module's FetchCmd).</summary>
    public Task<(bool Success, string Output)> FetchAsync()
        => Task.Run(() => RunGitOperation(_module.FetchCmd(remote: null, remoteBranch: null, localBranch: null)));

    /// <summary>Pulls the current branch from its tracking remote (or the default remote).</summary>
    public Task<(bool Success, string Output)> PullAsync(bool rebase)
    {
        var (remote, remoteBranch, _) = ResolvePushSpec();
        if (remote is null)
        {
            return Task.FromResult((false, "No remote configured."));
        }

        return Task.Run(() => RunGitOperation(_module.PullCmd(remote, remoteBranch, rebase)));
    }

    /// <summary>Pushes the current branch to its tracking remote, or creates it on the default remote.</summary>
    public Task<(bool Success, string Output)> PushAsync(bool forceWithLease)
    {
        var (remote, remoteBranch, track) = ResolvePushSpec();
        if (remote is null)
        {
            return Task.FromResult((false, "No remote configured."));
        }

        ArgumentString arguments = Commands.Push(
            remote,
            SelectedBranch,
            remoteBranch,
            forceWithLease ? ForcePushOptions.ForceWithLease : ForcePushOptions.DoNotForce,
            track,
            recursiveSubmodules: 0);

        return Task.Run(() => RunGitOperation(arguments));
    }

    public IReadOnlyList<string> GetRemoteNames() => [.. _module.GetRemoteNames()];

    public IReadOnlyList<string> GetLocalBranchNames() => [.. _module.GetRefs(RefsFilter.Heads).Select(gitRef => gitRef.Name)];

    /// <summary>The portable remotes manager (the same one FormRemotes uses).</summary>
    public GitCommands.Remotes.IConfigFileRemoteSettingsManager CreateRemotesManager()
        => new GitCommands.Remotes.ConfigFileRemoteSettingsManager(() => _module);

    /// <summary>The push dialog's prefill: tracking remote/branch, or the default remote with tracking setup.</summary>
    public (string? Remote, string? RemoteBranch, bool Track) GetPushDefaults() => ResolvePushSpec();

    /// <summary>Runs a push built from the dialog's explicit choices (portable Commands.Push).</summary>
    public Task<(bool Success, string Output)> PushWithOptionsAsync(string remote, string localBranch, string? remoteBranch, ForcePushOptions force, bool track)
        => Task.Run(() => RunGitOperation(Commands.Push(remote, localBranch, remoteBranch, force, track, recursiveSubmodules: 0)));

    /// <summary>The same resolution as <see cref="PushTarget"/>, but with the parts separated for command building.</summary>
    private (string? Remote, string? RemoteBranch, bool Track) ResolvePushSpec()
    {
        string currentBranch = SelectedBranch;
        IGitRef? branchRef = _module.GetRefs(RefsFilter.Heads).FirstOrDefault(r => r.LocalName == currentBranch);

        if (branchRef is not null && !string.IsNullOrEmpty(branchRef.TrackingRemote) && !string.IsNullOrEmpty(branchRef.MergeWith))
        {
            return (branchRef.TrackingRemote, branchRef.MergeWith, Track: false);
        }

        IReadOnlyList<string> remotes = [.. _module.GetRemoteNames()];
        string? defaultRemote = remotes.FirstOrDefault(r => r == "origin") ?? remotes.OrderBy(r => r).FirstOrDefault();
        return (defaultRemote, currentBranch, Track: true);
    }

    public Task<(bool Success, string Output)> CheckoutBranchAsync(string branchName, LocalChangesAction localChanges = LocalChangesAction.DontChange)
        => Task.Run(() => RunGitOperation(Commands.Checkout(branchName, localChanges)));

    /// <summary>Whether the working tree has uncommitted changes (the checkout dialog's gate).</summary>
    public Task<bool> IsDirtyAsync()
        => Task.Run(() => _module.IsDirtyDir());

    public Task<(bool Success, string Output)> StashSaveAsync()
        => Task.Run(() => RunGitOperation(Commands.StashSave(
            AppSettings.IncludeUntrackedFilesInAutoStash, keepIndex: false, message: "", selectedFiles: null)));

    public Task<(bool Success, string Output)> StashPopAsync()
        => Task.Run(() => RunGitOperation(new GitArgumentBuilder("stash") { "pop" }));

    public Task<(bool Success, string Output)> CreateBranchAsync(string branchName, bool checkout)
        => Task.Run(() => RunGitOperation(Commands.Branch(branchName, CurrentCheckout, checkout)));

    /// <summary>
    ///  The work-tree status, partitioned exactly as FormCommit.LoadUnstagedOutput does:
    ///  work-tree changes and status-only errors are unstaged, index changes are staged.
    /// </summary>
    public (IReadOnlyList<GitItemStatus> Unstaged, IReadOnlyList<GitItemStatus> Staged) GetWorkTreeStatus(CancellationToken cancellationToken)
    {
        IReadOnlyList<GitItemStatus> allChangedFiles = _module.GetAllChangedFilesWithSubmodulesStatus(cancellationToken);

        List<GitItemStatus> unstagedFiles = [];
        List<GitItemStatus> stagedFiles = [];
        foreach (GitItemStatus fileStatus in allChangedFiles)
        {
            if (fileStatus.Staged == StagedStatus.WorkTree || fileStatus.IsStatusOnly)
            {
                unstagedFiles.Add(fileStatus);
            }
            else if (fileStatus.Staged == StagedStatus.Index)
            {
                stagedFiles.Add(fileStatus);
            }
        }

        return (unstagedFiles, stagedFiles);
    }

    public (bool Success, string Output) StageFiles(IReadOnlyList<GitItemStatus> files)
    {
        bool success = _module.StageFiles(files, out string output);
        return (success, output);
    }

    public void UnstageFiles(IReadOnlyList<GitItemStatus> files)
        => _module.BatchUnstageFiles(files);

    /// <summary>
    ///  Commits through the same portable pipeline as the WinForms dialog: the message goes
    ///  through CommitMessageManager (COMMITMESSAGE file, second-line rule), the arguments
    ///  through Commands.Commit.
    /// </summary>
    public async Task<(bool Success, string Output)> CommitAsync(string message, bool amend, bool resetAuthor, bool allowEmpty)
    {
        CommitMessageManager commitMessageManager = new(new TraceUserInteraction(), _module.WorkingDirGitDir, _module.CommitEncoding);
        await commitMessageManager.WriteCommitMessageToFileAsync(
            message,
            CommitMessageType.Normal,
            usingCommitTemplate: false,
            ensureCommitMessageSecondLineEmpty: AppSettings.EnsureCommitMessageSecondLineEmpty);

        ArgumentString arguments = Commands.Commit(
            amend,
            signOff: false,
            author: "",
            useExplicitCommitMessage: true,
            commitMessageManager.CommitMessagePath,
            _module.GetPathForGitExecution,
            allowEmpty: allowEmpty,
            resetAuthor: resetAuthor);

        ExecutionResult result = _module.GitExecutable.Execute(arguments, throwOnErrorExit: false);
        if (result.ExitedSuccessfully)
        {
            await commitMessageManager.ResetCommitMessageAsync();
        }

        return (result.ExitedSuccessfully, result.AllOutput);
    }

    /// <summary>
    ///  The revision's changed files, grouped exactly as the WinForms file list shows them -
    ///  the real FileStatusDiffCalculator, so merge commits get per-parent groups.
    /// </summary>
    public IReadOnlyList<FileStatusWithDescription> GetRevisionFileGroups(GitRevision revision, CancellationToken cancellationToken)
    {
        FileStatusDiffCalculator calculator = new(() => _module);
        calculator.SetDiff([revision], headId: default(ObjectId), allowMultiDiff: false);
        return calculator.Calculate(prevList: [], refreshDiff: true, refreshGrep: false, cancellationToken);
    }

    /// <summary>
    ///  One file's diff between two revisions of a diff group, through the M4 highlight pipeline.
    /// </summary>
    public (string Text, IReadOnlyList<StyledSpan> Spans, DiffLinesInfo LineNumbers) GetRevisionFileDiff(ObjectId? firstId, ObjectId secondId, GitItemStatus file)
    {
        GitArgumentBuilder args;
        if (firstId is null)
        {
            // Root commit: show the whole file as added.
            args = new GitArgumentBuilder("show")
            {
                "--format=",
                "--patch",
                "--color=always",
                "--no-ext-diff",
                secondId.ToString(),
                "--",
                file.Name.QuoteNE()
            };
        }
        else
        {
            args = new GitArgumentBuilder("diff")
            {
                "--no-ext-diff",
                "--color=always",
                "--find-renames",
                "--find-copies",
                firstId.ToString(),
                secondId.ToString(),
                "--",
                file.Name.QuoteNE(),
                { !string.IsNullOrEmpty(file.OldName), file.OldName.QuoteNE() }
            };
        }

        string text = _module.GitExecutable.GetOutput(args, outputEncoding: _module.LogOutputEncoding, stripAnsiEscapeCodes: false);

        PatchHighlightService highlightService = new(ref text, useGitColoring: true);
        IReadOnlyList<StyledSpan> spans = highlightService.GetHighlighting();
        DiffLinesInfo lineNumbers = DiffLineNumAnalyzer.Analyze(text, spans, isCombinedDiff: false);
        return (text, spans, lineNumbers);
    }

    /// <summary>
    ///  The work-tree or index diff of one file, through the same M4 highlight pipeline as
    ///  revision diffs.
    /// </summary>
    public (string Text, IReadOnlyList<StyledSpan> Spans, DiffLinesInfo LineNumbers) GetWorkTreeFileDiff(GitItemStatus file, bool staged)
    {
        string text;
        if (file.IsNew && !file.IsTracked && !staged)
        {
            // Untracked: git diff knows nothing about the file; render its content as an
            // all-added diff (exit code 1 is "differences found", not an error).
            GitArgumentBuilder noIndexArgs = new("diff")
            {
                "--no-ext-diff",
                "--color=always",
                "--no-index",
                "--",
                "/dev/null",
                file.Name.QuoteNE()
            };

            text = _module.GitExecutable.Execute(noIndexArgs, outputEncoding: _module.LogOutputEncoding, stripAnsiEscapeCodes: false, throwOnErrorExit: false).StandardOutput;
        }
        else
        {
            GitArgumentBuilder args = new("diff")
            {
                "--no-ext-diff",
                "--color=always",
                { staged, "--cached" },
                "--",
                file.Name.QuoteNE()
            };

            text = _module.GitExecutable.GetOutput(args, outputEncoding: _module.LogOutputEncoding, stripAnsiEscapeCodes: false);
        }

        PatchHighlightService highlightService = new(ref text, useGitColoring: true);
        IReadOnlyList<StyledSpan> spans = highlightService.GetHighlighting();
        DiffLinesInfo lineNumbers = DiffLineNumAnalyzer.Analyze(text, spans, isCombinedDiff: false);
        return (text, spans, lineNumbers);
    }

    private sealed class TraceUserInteraction : IUserInteraction
    {
        public Task ShowErrorAsync(string text, string caption, CancellationToken cancellationToken = default)
        {
            Console.Error.WriteLine($"[commit] {caption}: {text}");
            return Task.CompletedTask;
        }
    }

    private const string FollowNamesPrefix = "????";

    /// <summary>
    ///  The file-history path filter with the --follow workaround (collect all historical
    ///  names first), plus the per-commit filename cache for later resolution.
    /// </summary>
    public (string PathFilter, IReadOnlyDictionary<ObjectId, string> FileByCommit) BuildFileHistoryFilter(string fileName)
    {
        (string path, bool multipleArgs) = GitCommands.FileHistory.FileHistoryPathFilter.NormalizeArgument(fileName);
        if (!GitCommands.FileHistory.FileHistoryPathFilter.ShouldCollectHistoricalNames(path, multipleArgs, AppSettings.FollowRenamesInFileHistory))
        {
            return (path, new Dictionary<ObjectId, string>());
        }

        GitArgumentBuilder args = GitCommands.FileHistory.FileHistoryPathFilter.FollowNamesCommand(
            path, FollowNamesPrefix, AppSettings.FollowRenamesInFileHistoryExactOnly);
        ExecutionResult result = _module.GitExecutable.Execute(args, outputEncoding: GitModule.LosslessEncoding, throwOnErrorExit: false);
        if (!result.ExitedSuccessfully)
        {
            return (path, new Dictionary<ObjectId, string>());
        }

        var (names, fileByCommit) = GitCommands.FileHistory.FileHistoryPathFilter.ParseFollowNamesOutput(
            result.StandardOutput.LazySplit('\n').Select(GitModule.ReEncodeFileNameFromLossless).WhereNotNull(),
            FollowNamesPrefix);
        (string filter, bool tooLong) = GitCommands.FileHistory.FileHistoryPathFilter.Combine(path, [.. names]);
        if (tooLong)
        {
            Console.Error.WriteLine("[file-history] path filter too long, following disabled for this file");
        }

        return (filter, fileByCommit);
    }

    /// <summary>Streams the file-filtered log (FilterInfo's --parents/--full-history/--simplify-merges rules).</summary>
    public void StreamFileLog(string pathFilter, Action<IReadOnlyList<GitRevision>> onBatch, Action onCompleted, Action<Exception> onError, CancellationToken cancellationToken)
        => StreamLogCore($"{DefaultRevisionFilter} {BuildFileHistoryRevisionFilter()}", pathFilter, onBatch, onCompleted, onError, cancellationToken);

    private static string BuildFileHistoryRevisionFilter()
    {
        string filter = "--parents";
        if (AppSettings.FullHistoryInFileHistory)
        {
            filter += " --full-history";
            if (AppSettings.SimplifyMergesInFileHistory)
            {
                filter += " --simplify-merges";
            }
        }

        return filter;
    }

    /// <summary>The blame of a file at a revision (portable GitBlame; the model builds the gutter).</summary>
    public GitBlame GetBlame(string fileName, ObjectId objectId, CancellationToken cancellationToken)
        => _module.Blame(fileName, objectId.ToString(), _module.FilesEncoding, lines: null, cancellationToken: cancellationToken);

    public GitRevision GetRevision(ObjectId objectId) => _module.GetRevision(objectId);

    /// <summary>The changed files between two revisions (the compare window's list).</summary>
    public IReadOnlyList<GitItemStatus> GetDiffFilesBetween(ObjectId firstId, ObjectId secondId, CancellationToken cancellationToken)
        => _module.GetDiffFilesWithUntracked(
            firstId == ObjectId.WorkTreeId ? null : firstId.ToString(),
            secondId == ObjectId.WorkTreeId ? null : secondId.ToString(),
            GitExtensions.Extensibility.Git.StagedStatus.None,
            cancellationToken: cancellationToken);

    /// <summary>The compare window's merge base (model rules over Module.GetMergeBase).</summary>
    public ObjectId? ResolveMergeBase(ObjectId firstId, ObjectId secondId)
        => GitCommands.Compare.CompareRevisions.ResolveMergeBase(firstId, secondId, () => CurrentCheckout, _module.GetMergeBase);

    /// <summary>The blame-previous line mapping (GitBlameParser's diff-chunk walk).</summary>
    public int GetOriginalLineInPreviousCommit(GitRevision blamedRevision, string fileName, int line)
        => new GitCommands.Blame.GitBlameParser(() => _module).GetOriginalLineInPreviousCommit(blamedRevision, fileName, line);

    /// <summary>The file's content at a revision, or null when the blob does not exist there.</summary>
    public string? GetFileTextAtRevision(string fileName, ObjectId objectId)
    {
        ObjectId blobId = _module.GetFileBlobHash(fileName, objectId);
        return blobId.IsZero ? null : _module.GetFileText(blobId, _module.FilesEncoding, stripAnsiEscapeCodes: true);
    }

    /// <summary>The file-availability half of the tab decision (worktree file for artificial revisions).</summary>
    public bool FileExistsAtRevision(string fileName, GitRevision revision)
        => revision.IsArtificial
            ? File.Exists(Path.Combine(WorkingDir, fileName))
            : !_module.GetFileBlobHash(fileName, revision.ObjectId).IsZero;

    /// <summary>
    ///  Streams the full log the way the WinForms grid does: RevisionReader.GetLog batches
    ///  revisions through an observer from a detached git-log process.
    /// </summary>
    public void StreamLog(Action<IReadOnlyList<GitRevision>> onBatch, Action onCompleted, Action<Exception> onError, CancellationToken cancellationToken)
        => StreamLogCore(DefaultRevisionFilter, pathFilter: "", onBatch, onCompleted, onError, cancellationToken);

    // The WinForms grid's default branch filter (FilterInfo.GetBranchRevisionFilter): all
    // refs, minus notes/stashes/session refs - so commits reachable only from other
    // branches or unmerged tags (e.g. release tags) have rows to jump to.
    private static string DefaultRevisionFilter =>
        $"--exclude={GitRefName.RefsNotesPrefix} --exclude={GitRefName.RefsStashPrefix} --exclude={GitRefName.RefsSessionsPrefix}** --all";

    private void StreamLogCore(string revisionFilter, string pathFilter, Action<IReadOnlyList<GitRevision>> onBatch, Action onCompleted, Action<Exception> onError, CancellationToken cancellationToken)
    {
        // Refs are looked up per revision, the same way the WinForms grid attaches them.
        ILookup<ObjectId, IGitRef> refsByObjectId = _module.GetRefs(RefsFilter.NoFilter).ToLookup(gitRef => gitRef.ObjectId);

        RevisionReader reader = new(_module, allBodies: false);
        reader.GetLog(
            new BatchObserver(
                revisions =>
                {
                    foreach (GitRevision revision in revisions)
                    {
                        revision.Refs = [.. refsByObjectId[revision.ObjectId]];
                    }

                    onBatch(revisions);
                },
                onCompleted,
                onError),
            revisionFilter: revisionFilter,
            pathFilter: pathFilter,
            hasNotes: false,
            autostashLabel: "autostash",
            cancellationToken);
    }

    private sealed class BatchObserver : IObserver<IReadOnlyList<GitRevision>>
    {
        private readonly Action<IReadOnlyList<GitRevision>> _onBatch;
        private readonly Action _onCompleted;
        private readonly Action<Exception> _onError;

        public BatchObserver(Action<IReadOnlyList<GitRevision>> onBatch, Action onCompleted, Action<Exception> onError)
        {
            _onBatch = onBatch;
            _onCompleted = onCompleted;
            _onError = onError;
        }

        public void OnNext(IReadOnlyList<GitRevision> value) => _onBatch(value);

        public void OnCompleted() => _onCompleted();

        public void OnError(Exception error) => _onError(error);
    }

    public IReadOnlyList<GitRevision> GetLog(int maxCount, CancellationToken cancellationToken)
    {
        GitArgumentBuilder args = new("rev-list")
        {
            $"--max-count={maxCount}",
            "HEAD"
        };

        List<ObjectId> ids = [.. _module.GitExecutable.GetOutput(args)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ObjectId.Parse)];

        RevisionReader reader = new(_module, allBodies: true);
        IReadOnlyCollection<GitRevision> revisions = reader.GetRevisionsFromList(ids, cancellationToken);

        // GetRevisionsFromList does not guarantee input order; restore rev-list (topological) order.
        Dictionary<ObjectId, GitRevision> byId = revisions.ToDictionary(revision => revision.ObjectId);
        return [.. ids.Where(byId.ContainsKey).Select(id => byId[id])];
    }

    public (RichContent Header, RichContent Body) GetCommitInfo(GitRevision revision)
    {
        CommitData data = _commitDataManager.CreateFromRevision(revision, children: null);
        return (_headerRenderer.Render(data, showRevisionsAsLinks: true),
                _bodyRenderer.Render(data, showRevisionsAsLinks: true));
    }

    public (string Text, IReadOnlyList<StyledSpan> Spans, DiffLinesInfo LineNumbers) GetDiff(GitRevision revision)
    {
        GitArgumentBuilder args = new("show")
        {
            "--format=",
            "--patch",
            "--color=always",
            "--no-ext-diff",
            revision.ObjectId.ToString()
        };

        // stripAnsiEscapeCodes must be false to match --color=always: the real GitModule pairs
        // them the same way (stripAnsiEscapeCodes: !useGitColoring) - stripping would leave the
        // git-coloring highlight path with nothing to parse.
        string text = _module.GitExecutable.GetOutput(args, outputEncoding: _module.LogOutputEncoding, stripAnsiEscapeCodes: false);

        PatchHighlightService highlightService = new(ref text, useGitColoring: true);
        IReadOnlyList<StyledSpan> spans = highlightService.GetHighlighting();
        DiffLinesInfo lineNumbers = DiffLineNumAnalyzer.Analyze(text, spans, isCombinedDiff: false);
        return (text, spans, lineNumbers);
    }
}
