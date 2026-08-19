using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GitCommands;
using GitCommands.Commit;
using GitCommands.Git;
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
        RevisionReader reader = new(_module, allBodies: false);
        reader.GetLog(
            new BatchObserver(onBatch, onCompleted, onError),
            revisionFilter: "HEAD",
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
