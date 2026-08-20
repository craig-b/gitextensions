using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using GitCommands;
using GitCommands.Blame;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitExtUtils;
using GitExtUtils.GitUI.Theming;
using GitUI.Avatars;
using GitUI.CommandsDialogs;
using GitUI.Editor;
using GitUI.HelperDialogs;
using GitUI.Properties;
using GitUI.Theming;
using GitUI.UserControls;
using GitUIPluginInterfaces;
using GitUIPluginInterfaces.RepositoryHosts;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using ResourceManager;

namespace GitUI.Blame;

public sealed partial class BlameControl : GitModuleControl
{
    public event EventHandler<CommandEventArgs>? CommandClick;

    /// <summary>
    /// Raised when the Escape key is pressed (and only when no selection exists, as the default behaviour of escape is to clear the selection).
    /// </summary>
    public event Action? EscapePressed;

    private readonly AsyncLoader _blameLoader = new();
    private int _lineIndex;
    private GitBlameLine? _lastBlameLine;
    private GitBlameLine? _clickedBlameLine;
    private GitBlameCommit? _highlightedCommit;
    private GitBlame? _blame;
    private IRevisionGridInfo? _revisionGridInfo;
    private IRevisionGridFileUpdate? _revisionGridFileUpdate;
    private ObjectId _blameId;
    private string? _fileName;
    private Encoding? _encoding;
    private int _lastTooltipX = int.MinValue;
    private int _lastTooltipY = int.MinValue;
    private GitBlameCommit? _tooltipCommit;
    private bool _changingScrollPosition;
    private IRepositoryHostPlugin? _gitHoster;
    private static readonly Color[] AgeBucketGradientColors = GetAgeBucketGradientColors();
    private static readonly TranslationString _blameActualPreviousRevision = new("&Blame previous revision");
    private static readonly TranslationString _blameVisiblePreviousRevision = new("&Blame previous visible revision");
    private readonly Color _commitHighlightColor;
    private readonly IGitRevisionSummaryBuilder _gitRevisionSummaryBuilder;
    private readonly IGitBlameParser _gitBlameParser;
    private bool _loading;
    private BlamePreviousTarget _blamePreviousTarget;

    public BlameControl()
    {
        InitializeComponent();
        InitializeComplete();

        BlameAuthor.EnableScrollBars(false);
        UpdateShowLineNumbers();
        BlameAuthor.HScrollPositionChanged += BlameAuthor_HScrollPositionChanged;
        BlameAuthor.VScrollPositionChanged += BlameAuthor_VScrollPositionChanged;
        BlameAuthor.MouseMove += BlameAuthor_MouseMove;
        BlameAuthor.MouseLeave += BlameAuthor_MouseLeave;
        BlameAuthor.SelectedLineChanged += SelectedLineChanged;
        BlameAuthor.RequestDiffView += ActiveTextAreaControlDoubleClick;
        BlameAuthor.EscapePressed += () => EscapePressed?.Invoke();
        BlameAuthor.DontMarkGutterSelectedLine();

        BlameFile.VScrollPositionChanged += BlameFile_VScrollPositionChanged;
        BlameFile.SelectedLineChanged += SelectedLineChanged;
        BlameFile.MouseMove += BlameFile_MouseMove;
        BlameFile.EscapePressed += () => EscapePressed?.Invoke();
        BlameFile.EnableAutomaticContinuousScroll = false;

        CommitInfo.CommandClicked += commitInfo_CommandClicked;

        _commitHighlightColor = Application.IsDarkModeEnabled ? AppColor.EditorBackground.GetThemeColor().MakeDarkerBy(-0.06) : SystemColors.ControlLight;
        _gitRevisionSummaryBuilder = new GitRevisionSummaryBuilder();
        _gitBlameParser = new GitBlameParser(() => UICommands.Module);
    }

    public void InitSplitterManager(NestedSplitterManager splitterManager)
    {
        NestedSplitterManager nested = new(splitterManager, Name);
        nested.AddSplitter(splitContainer1, defaultDistance: splitContainer1.Panel1MinSize + 1);
        nested.AddSplitter(splitContainer2, defaultDistance: splitContainer2.Panel1MinSize + 1);
    }

    public void ConfigureRepositoryHostPlugin(IRepositoryHostPlugin? gitHoster)
    {
        _gitHoster = gitHoster;
        (_gitHoster as IRepositoryHostContextMenuProvider)?.ConfigureContextMenu(contextMenu);
    }

    public void UpdateShowLineNumbers()
    {
        BlameAuthor.ShowLineNumbers = AppSettings.BlameShowLineNumbers;
    }

    public int CurrentFileColumn => BlameFile.CurrentFileColumn;
    public int CurrentFileLine => BlameFile.CurrentFileLine;

    public void HideCommitInfo()
    {
        splitContainer1.Panel1Collapsed = true;
        CommitInfo.Visible = false;
        CommitInfo.CommandClicked -= commitInfo_CommandClicked;
    }

    public async Task LoadBlameAsync(GitRevision revision, IReadOnlyList<ObjectId>? children, string fileName, IRevisionGridInfo? revisionGridInfo, IRevisionGridFileUpdate? revisionGridFileUpdate, Control? controlToMask, Encoding encoding, int? initialLine = null, bool force = false, CancellationTokenSequence? cancellationTokenSequence = null)
    {
        ObjectId objectId = revision.ObjectId;

        // refresh only when something changed
        if (!force && objectId == _blameId && fileName == _fileName && revisionGridInfo == _revisionGridInfo && _revisionGridFileUpdate == revisionGridFileUpdate && encoding == _encoding)
        {
            if (initialLine is not null && !_loading)
            {
                BlameFile.GoToLine(initialLine.Value);
            }

            return;
        }

        CancellationToken cancellationToken = cancellationTokenSequence?.Next() ?? default;
        _loading = true;

        int line = BlameTargetLine.Resolve(_clickedBlameLine?.OriginLineNumber, initialLine, sameFile: fileName == _fileName, BlameFile.CurrentFileLine);
        _revisionGridInfo = revisionGridInfo;
        _revisionGridFileUpdate = revisionGridFileUpdate;
        _fileName = fileName;
        _encoding = encoding;

        controlToMask?.Mask();

        // Clear the contents of the viewer while loading
        BlameAuthor.ClearBlameGutter();
        await BlameAuthor.ClearAsync();
        await BlameFile.ClearAsync();

        try
        {
            await _blameLoader.LoadAsync(
                loaderCancellationToken => _blame = Module.Blame(fileName, objectId.ToString(), encoding, lines: null, cancellationToken: loaderCancellationToken.CombineWith(cancellationToken).Token),
                () => ProcessBlame(fileName, revision, children, controlToMask, line, cancellationToken));
        }
        catch (ExternalOperationException ex)
        {
            _blame = null;
            await BlameFile.ViewTextAsync(fileName, ex.Message, cancellationToken: cancellationToken);
        }

        _loading = false;
    }

    private void commitInfo_CommandClicked(object? sender, CommandEventArgs e)
    {
        CommandClick?.Invoke(sender, e);
    }

    private void BlameAuthor_MouseLeave(object? sender, EventArgs e)
    {
        blameTooltip.Hide(this);
    }

    private void BlameAuthor_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!BlameFile.Focused)
        {
            BlameFile.Focus();
        }

        if (_blame is null)
        {
            return;
        }

        _lineIndex = BlameAuthor.GetLineFromVisualPosY(e.Y);

        GitBlameCommit? blameCommit = _lineIndex < _blame.Lines.Count
            ? _blame.Lines[_lineIndex].Commit
            : null;

        HighlightLinesForCommit(blameCommit);

        if (blameCommit is null)
        {
            return;
        }

        int newTooltipX = splitContainer2.SplitterDistance + 60;
        int newTooltipY = e.Y + splitContainer1.SplitterDistance + 20;

        if (_tooltipCommit != blameCommit || Math.Abs(_lastTooltipX - newTooltipX) > 5 || Math.Abs(_lastTooltipY - newTooltipY) > 5)
        {
            _tooltipCommit = blameCommit;
            _lastTooltipX = newTooltipX;
            _lastTooltipY = newTooltipY;
            blameTooltip.Show(blameCommit.ToString(_gitRevisionSummaryBuilder.BuildSummary), this, newTooltipX, newTooltipY);
        }
    }

    private void BlameFile_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_blame is null)
        {
            return;
        }

        int lineIndex = BlameFile.GetLineFromVisualPosY(e.Y);

        GitBlameCommit? blameCommit = lineIndex < _blame.Lines.Count
            ? _blame.Lines[lineIndex].Commit
            : null;

        HighlightLinesForCommit(blameCommit);
    }

    private void HighlightLinesForCommit(GitBlameCommit? commit)
    {
        if (commit == _highlightedCommit)
        {
            return;
        }

        _highlightedCommit = commit;

        BlameAuthor.ClearHighlighting();
        BlameFile.ClearHighlighting();

        if (commit is null)
        {
            return;
        }

        Validates.NotNull(_blame);

        IReadOnlyList<(int Start, int End)> runs = BlameLineRuns.ForCommit(_blame.Lines, commit);
        for (int i = 0; i < runs.Count; i++)
        {
            // The historical behaviour: interior runs use the highlight colour, the last run ControlLight.
            Color color = i < runs.Count - 1 ? _commitHighlightColor : SystemColors.ControlLight;
            BlameAuthor.HighlightLines(runs[i].Start, runs[i].End, color);
            BlameFile.HighlightLines(runs[i].Start, runs[i].End, color);
        }

        BlameAuthor.Refresh();
        BlameFile.Refresh();
    }

    private void SelectedLineChanged(object? sender, SelectedLineEventArgs e)
    {
        int selectedLine = e.SelectedLine;

        if (_blame is null || selectedLine >= _blame.Lines.Count)
        {
            return;
        }

        GitBlameLine newBlameLine = _blame.Lines[selectedLine];

        if (ReferenceEquals(_lastBlameLine?.Commit, newBlameLine.Commit))
        {
            return;
        }

        _lastBlameLine = newBlameLine;
        ObjectId objectId = _lastBlameLine.Commit.ObjectId;
        CommitInfo.Revision = _revisionGridInfo is null ? Module.GetRevision(objectId) : _revisionGridInfo.GetActualRevision(objectId);
    }

    private void BlameAuthor_HScrollPositionChanged(object? sender, EventArgs e)
    {
        BlameAuthor.HScrollPosition = 0;
    }

    private void BlameAuthor_VScrollPositionChanged(object? sender, EventArgs e)
    {
        if (!_changingScrollPosition)
        {
            _changingScrollPosition = true;
            BlameFile.VScrollPosition = BlameAuthor.VScrollPosition;
            _changingScrollPosition = false;
        }

        Rectangle rect = BlameAuthor.ClientRectangle;
        rect = BlameAuthor.RectangleToScreen(rect);
        if (rect.Contains(MousePosition))
        {
            Point p = BlameAuthor.PointToClient(MousePosition);
            MouseEventArgs me = new(0, 0, p.X, p.Y, 0);
            BlameAuthor_MouseMove(this, me);
        }
    }

    private void BlameFile_VScrollPositionChanged(object? sender, EventArgs e)
    {
        if (_changingScrollPosition)
        {
            return;
        }

        _changingScrollPosition = true;
        BlameAuthor.VScrollPosition = BlameFile.VScrollPosition;
        _changingScrollPosition = false;
    }

    private void ProcessBlame(string? filename, GitRevision revision, IReadOnlyList<ObjectId>? children, Control? controlToMask, int lineNumber, CancellationToken cancellationToken = default)
    {
        int avatarSize = BlameAuthor.Font.Height + 1;
        (string gutter, string body, List<GitBlameEntry> avatars) = BuildBlameContents(filename, avatarSize);
        cancellationToken.ThrowIfCancellationRequested();

        BlameAuthor.SetGitBlameGutter(avatars);

        Validates.NotNull(_fileName);

        BlameAuthor.InvokeAndForget(() => BlameAuthor.ViewTextAsync("committer.txt", gutter, cancellationToken: cancellationToken), cancellationToken: cancellationToken);
        BlameFile.InvokeAndForget(() => BlameFile.ViewTextAsync(_fileName, body, cancellationToken: cancellationToken), cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        BlameFile.GoToLine(Math.Min(lineNumber, _blame!.Lines.Count));
        _clickedBlameLine = null;

        _blameId = revision.ObjectId;
        if (CommitInfo.Visible)
        {
            CommitInfo.SetRevisionWithChildren(revision, children);
        }

        controlToMask?.UnMask();
    }

    private (string gutter, string body, List<GitBlameEntry> gitBlameDisplays) BuildBlameContents(string? filename, int avatarSize)
    {
        Validates.NotNull(_blame);

        BlameDisplayOptions options = CurrentDisplayOptions();
        BlameContents contents = BlameGutterModel.Build(_blame, filename, options, CultureInfo.CurrentCulture);

        bool showAuthorAvatar = AppSettings.BlameShowAuthorAvatar;
        List<GitBlameEntry> gitBlameDisplays = showAuthorAvatar && _blame.Lines.Count > 0 ? CalculateBlameGutterData(_blame.Lines) : [];

        if (showAuthorAvatar && _blame.Lines.Count > 0)
        {
            Dictionary<string, Image?> cacheAvatars = [];
            Image noAuthorImage = (Image)new Bitmap(Images.User80, avatarSize, avatarSize);
            for (int index = 0; index < _blame.Lines.Count; index++)
            {
                if (!contents.IsNewCommitLine[index])
                {
                    continue;
                }

                GitBlameLine line = _blame.Lines[index];
                string? authorEmail = line.Commit.AuthorMail?.Trim('<', '>');
                if (authorEmail is not null)
                {
                    if (cacheAvatars.TryGetValue(authorEmail, out Image? avatarImage))
                    {
                        gitBlameDisplays[index].Avatar = avatarImage;
                    }
                    else
                    {
                        Image? avatar = ThreadHelper.JoinableTaskFactory.Run(() => AvatarService.DefaultProvider.GetAvatarAsync(authorEmail, line.Commit.Author, avatarSize));
                        cacheAvatars.Add(authorEmail, avatar);
                        gitBlameDisplays[index].Avatar = avatar;
                    }
                }
                else
                {
                    gitBlameDisplays[index].Avatar = noAuthorImage;
                }
            }
        }

        return (contents.Gutter, contents.Body, gitBlameDisplays);
    }

    private static BlameDisplayOptions CurrentDisplayOptions() => new(
        ShowAuthor: AppSettings.BlameShowAuthor,
        ShowAuthorDate: AppSettings.BlameShowAuthorDate,
        ShowAuthorTime: AppSettings.BlameShowAuthorTime,
        DisplayAuthorFirst: AppSettings.BlameDisplayAuthorFirst,
        ShowOriginalFilePath: AppSettings.BlameShowOriginalFilePath);

    private static string BuildAuthorLine(GitBlameLine line, StringBuilder authorLineBuilder, int lineLength, string dateTimeFormat,
        string? filename, bool showAuthor, bool showAuthorDate, bool showOriginalFilePath, bool displayAuthorFirst)
        => BlameGutterModel.BuildCaption(
            line,
            authorLineBuilder,
            lineLength,
            dateTimeFormat,
            filename,
            new BlameDisplayOptions(showAuthor, showAuthorDate, ShowAuthorTime: false, displayAuthorFirst, showOriginalFilePath));

    private static Color[] GetAgeBucketGradientColors()
    {
        // Color chosen from: https://colorbrewer2.org/#type=sequential&scheme=Greens&n=7
        return Array.ConvertAll([
            Color.FromArgb(247, 252, 245),
            Color.FromArgb(199, 233, 192),
            Color.FromArgb(161, 217, 155),
            Color.FromArgb(116, 196, 118),
            Color.FromArgb(65, 171, 93),
            Color.FromArgb(35, 139, 69),
            Color.FromArgb(0, 68, 27)
        ], c => c.AdaptBackColor());
    }

    public DateTime ArtificialOldBoundary => DateTime.Now.AddYears(-3);

    private List<GitBlameEntry> CalculateBlameGutterData(IReadOnlyList<GitBlameLine> blameLines)
    {
        IReadOnlyList<int> buckets = BlameAgeBuckets.Compute(blameLines, DateTime.Now);
        List<GitBlameEntry> gitBlameDisplays = new(blameLines.Count);
        foreach (int ageBucketIndex in buckets)
        {
            gitBlameDisplays.Add(new GitBlameEntry
            {
                AgeBucketIndex = ageBucketIndex,
                AgeBucketColor = AgeBucketGradientColors[ageBucketIndex]
            });
        }

        return gitBlameDisplays;
    }

    private void ActiveTextAreaControlDoubleClick(object? sender, EventArgs e)
    {
        if (_lastBlameLine is not null
            && TryGetRevision(_lastBlameLine.Commit, out (GitRevision? SelectedRevision, string? Filename) blameInfo))
        {
            BlameRevision(_lastBlameLine.Commit.ObjectId, blameInfo.Filename!, _lastBlameLine);
        }
    }

    private int GetBlameLine()
    {
        if (_blame is null)
        {
            return -1;
        }

        Point position = BlameAuthor.PointToClient(MousePosition);

        int line = BlameAuthor.GetLineFromVisualPosY(position.Y);
        if (line >= _blame.Lines.Count)
        {
            return -1;
        }

        return line;
    }

    private void contextMenu_Opened(object sender, EventArgs e)
    {
        Validates.NotNull(_fileName);
        if (_blameId.IsZero)
        {
            throw new InvalidOperationException("_blameId must not be zero");
        }

        contextMenu.Tag = new GitBlameContext(_fileName, _lineIndex, GetBlameLine(), _blameId);

        if (!TryGetRevision(GetBlameCommit(), out (GitRevision? SelectedRevision, string? Filename) blameinfo))
        {
            blameRevisionToolStripMenuItem.Enabled = false;

            // Ignore if current revision is not visible in grid but parent is.
            blamePreviousRevisionToolStripMenuItem.Enabled = false;
            return;
        }

        blameRevisionToolStripMenuItem.Enabled = true;

        // Get parent for the actual revision, the selected revision may have rewritten parents.
        // The menu will be slightly slower in this situation.
        _blamePreviousTarget = BlamePreviousRevisionModel.Resolve(
            blameinfo.SelectedRevision,
            _revisionGridInfo?.GetActualRevision(blameinfo.SelectedRevision!),
            parentId => parentId is ObjectId parent && _revisionGridInfo?.GetRevision(parent) is not null);

        blamePreviousRevisionToolStripMenuItem.Enabled = _blamePreviousTarget is not BlamePreviousTarget.Disabled;
        blamePreviousRevisionToolStripMenuItem.Text = _blamePreviousTarget is BlamePreviousTarget.VisibleParent
            ? _blameVisiblePreviousRevision.Text
            : _blameActualPreviousRevision.Text;
    }

    private GitBlameCommit? GetBlameCommit()
    {
        int line = (contextMenu.Tag as GitBlameContext)?.BlameLine ?? -1;
        if (line < 0)
        {
            return null;
        }

        Validates.NotNull(_blame);

        return _blame.Lines[line].Commit;
    }

    private void copyLogMessageToolStripMenuItem_Click(object sender, EventArgs e)
    {
        CopyToClipboard(c => c.Summary);
    }

    private void CopyToClipboard(Func<GitBlameCommit, string> formatter)
    {
        GitBlameCommit? commit = GetBlameCommit();

        if (commit is null)
        {
            return;
        }

        ClipboardUtil.TrySetText(formatter(commit));
    }

    private void copyAllCommitInfoToClipboardToolStripMenuItem_Click(object sender, EventArgs e)
    {
        CopyToClipboard(c => c.ToString());
    }

    private void copyCommitHashToClipboardToolStripMenuItem_Click(object sender, EventArgs e)
    {
        CopyToClipboard(c => c.ObjectId.ToString());
    }

    private bool TryGetRevision(GitBlameCommit? blameCommit, [NotNullWhen(returnValue: true)] out (GitRevision? selectedRevision, string? filename) blameInfo)
    {
        if (blameCommit is null)
        {
            blameInfo = (null, null);
            return false;
        }

        blameInfo = (_revisionGridInfo?.GetRevision(blameCommit.ObjectId), blameCommit.FileName);
        return blameInfo.selectedRevision is not null;
    }

    private void blameRevisionToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (!TryGetRevision(GetBlameCommit(), out (GitRevision? SelectedRevision, string? Filename) blameInfo))
        {
            return;
        }

        BlameRevision(blameInfo.SelectedRevision!.ObjectId, blameInfo.Filename!, _lastBlameLine!);
    }

    private void blamePreviousRevisionToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (!TryGetRevision(GetBlameCommit(), out (GitRevision? SelectedRevision, string? Filename) blameInfo))
        {
            return;
        }

        GitRevision? selectedRevision = blameInfo.SelectedRevision;
        if (_blamePreviousTarget is BlamePreviousTarget.ActualParent)
        {
            // Try get actual parent revision, get popup if it does not exist.
            // (The menu should be disabled if previous is not in grid).
            selectedRevision = _revisionGridInfo!.GetActualRevision(selectedRevision!);
        }

        // Origin line of commit selected is final line of the previous blame commit
        int finalLineNumberOfPreviousBlame = _lastBlameLine!.OriginLineNumber;
        int originalLineNumberOfPreviousBlame = _gitBlameParser.GetOriginalLineInPreviousCommit(selectedRevision!, blameInfo.Filename!, finalLineNumberOfPreviousBlame);

        GitBlameLine blameLine = new(_lastBlameLine.Commit, finalLineNumberOfPreviousBlame, originalLineNumberOfPreviousBlame, "Dummy Git blame line used only to store the good 'originLineNumber' value to display and select it");
        BlameRevision(selectedRevision!.FirstParentId, blameInfo.Filename!, blameLine);
    }

    /// <summary>
    /// Blame a specific revision
    /// </summary>
    /// <param name="commitId">the commit id to blame</param>
    /// <param name="filename">the relative path of the file to blame in this commit (because it could have been renamed)</param>
    private void BlameRevision(ObjectId commitId, string filename, GitBlameLine blameLine)
    {
        _clickedBlameLine = blameLine;

        if (_revisionGridFileUpdate is not null)
        {
            if (!_revisionGridFileUpdate.SelectFileInRevision(commitId, RelativePath.From(filename)))
            {
                MessageBoxes.RevisionFilteredInGrid(this, commitId);
            }

            return;
        }

        using FormCommitDiff frm = new(UICommands, commitId);
        frm.ShowDialog(this);
    }

    private void showChangesToolStripMenuItem_Click(object sender, EventArgs e)
    {
        GitBlameCommit? commit = GetBlameCommit();

        if (commit is null)
        {
            return;
        }

        using FormCommitDiff frm = new(UICommands, commit.ObjectId);
        frm.ShowDialog(this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
            _blameLoader.Dispose();
        }

        base.Dispose(disposing);
    }

    internal TestAccessor GetTestAccessor()
        => new(this);

    internal readonly struct TestAccessor
    {
        private readonly BlameControl _control;

        public TestAccessor(BlameControl control)
        {
            _control = control;
        }

        public GitBlame? Blame
        {
            get => _control._blame;
            set => _control._blame = value;
        }

        public FileViewer BlameFile => _control.BlameFile;

        public DateTime ArtificialOldBoundary => _control.ArtificialOldBoundary;

        public void BuildAuthorLine(GitBlameLine line, StringBuilder lineBuilder, int lineLength, string dateTimeFormat, string filename, bool showAuthor, bool showAuthorDate, bool showOriginalFilePath, bool displayAuthorFirst)
            => BlameControl.BuildAuthorLine(line, lineBuilder, lineLength, dateTimeFormat, filename, showAuthor, showAuthorDate, showOriginalFilePath, displayAuthorFirst);

        public (string gutter, string body, List<GitBlameEntry> avatars) BuildBlameContents(string filename) => _control.BuildBlameContents(filename, avatarSize: 10);

        public List<GitBlameEntry> CalculateBlameGutterData(IReadOnlyList<GitBlameLine> blameLines)
            => _control.CalculateBlameGutterData(blameLines);
    }
}
