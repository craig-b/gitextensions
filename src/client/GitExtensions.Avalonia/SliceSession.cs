using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitCommands;
using GitCommands.Commit;
using GitCommands.Git;
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

    private (bool Success, string Output) RunGitOperation(ArgumentString arguments)
    {
        ExecutionResult result = _module.GitExecutable.Execute(arguments, throwOnErrorExit: false);
        return (result.ExitedSuccessfully, result.AllOutput);
    }

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

    /// <summary>
    ///  Streams the full log the way the WinForms grid does: RevisionReader.GetLog batches
    ///  revisions through an observer from a detached git-log process.
    /// </summary>
    public void StreamLog(Action<IReadOnlyList<GitRevision>> onBatch, Action onCompleted, Action<Exception> onError, CancellationToken cancellationToken)
    {
        // Refs are looked up per revision, the same way the WinForms grid attaches them.
        ILookup<ObjectId, IGitRef> refsByObjectId = _module.GetRefs(RefsFilter.NoFilter).ToLookup(gitRef => gitRef.ObjectId);

        // The WinForms grid's default branch filter (FilterInfo.GetBranchRevisionFilter): all
        // refs, minus notes/stashes/session refs - so commits reachable only from other
        // branches or unmerged tags (e.g. release tags) have rows to jump to.
        string revisionFilter =
            $"--exclude={GitRefName.RefsNotesPrefix} --exclude={GitRefName.RefsStashPrefix} --exclude={GitRefName.RefsSessionsPrefix}** --all";

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
            pathFilter: "",
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
