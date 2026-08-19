using System.Collections.Generic;
using System.Linq;
using GitCommands;
using GitCommands.Git;
using GitCommands.RichText;
using GitExtUtils;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
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
