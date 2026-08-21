using GitCommands;
using GitCommands.Clone;
using GitCommands.Config;
using GitCommands.Git;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitExtUtils.GitUI.Theming;
using GitUI.HelperDialogs;
using ResourceManager;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.CommandsDialogs;

public partial class FormClone : GitExtensionsDialog
{
    private readonly TranslationString _infoNewRepositoryLocation = new("The repository will be cloned to a new directory located here:" + Environment.NewLine + "{0}");
    private readonly TranslationString _infoDirectoryExists = new("(Directory already exists)");
    private readonly TranslationString _infoDirectoryNew = new("(New directory)");
    private readonly TranslationString _questionOpenRepo = new("The repository has been cloned successfully." + Environment.NewLine + "Do you want to open the new repository \"{0}\" now?");
    private readonly TranslationString _questionOpenRepoCaption = new("Open");
    private readonly TranslationString _branchDefaultRemoteHead = new("(default: remote HEAD)" /* Has a colon, so won't alias with any valid branch name */);
    private readonly TranslationString _branchNone = new("(none: don't checkout after clone)" /* Has a colon, so won't alias with any valid branch name */);
    private readonly TranslationString _errorDestinationNotSupplied = new("You need to specify destination folder.");
    private readonly TranslationString _errorDestinationNotRooted = new("Destination folder must be an absolute path.");
    private readonly TranslationString _errorCloneFailed = new("Clone Failed");

    private readonly bool _openedFromProtocolHandler;
    private readonly string? _url;
    private readonly CancellationTokenSequence _branchLoaderSequence = new();
    private readonly IReadOnlyList<string> _defaultBranchItems;
    private string? _puttySshKey;

    public FormClone(IGitUICommands commands, string? url, bool openedFromProtocolHandler)
        : base(commands, enablePositionRestore: false)
    {
        InitializeComponent();

        MinimumSize = new Size(Width, PreferredMinimumHeight);

        InitializeComplete();
        _openedFromProtocolHandler = openedFromProtocolHandler;
        _url = url;
        _defaultBranchItems = new[] { _branchDefaultRemoteHead.Text, _branchNone.Text };
        _NO_TRANSLATE_Branches.DataSource = _defaultBranchItems;
    }

    protected override void OnRuntimeLoad(EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        base.OnRuntimeLoad(e);

        IList<Repository> repositoryHistory = ThreadHelper.JoinableTaskFactory.Run(RepositoryHistoryManager.Remotes.LoadRecentHistoryAsync);
        _NO_TRANSLATE_From.DataSource = repositoryHistory;
        _NO_TRANSLATE_From.DisplayMember = nameof(Repository.Path);

        IList<Repository> localsHistory = ThreadHelper.JoinableTaskFactory.Run(RepositoryHistoryManager.Locals.LoadRecentHistoryAsync);
        string[] historicPaths = [.. localsHistory.Select(x => x.GetParentPath())
                                              .Where(x => !string.IsNullOrEmpty(x))
                                              .Distinct(StringComparer.CurrentCultureIgnoreCase)];
        _NO_TRANSLATE_To.DataSource = historicPaths;
        _NO_TRANSLATE_To.Text = AppSettings.DefaultCloneDestinationPath;

        string? clipboardText = null;
        try
        {
            // Try to be more helpful to the user: the clipboard text is a potential source URL.
            if (Clipboard.ContainsText(TextDataFormat.Text))
            {
                clipboardText = Clipboard.GetText(TextDataFormat.Text);
            }
        }
        catch
        {
            // We tried.
        }

        CloneSeed seed = CloneSourceSeed.Resolve(
            _url,
            clipboardText,
            AppSettings.DefaultCloneDestinationPath,
            Module.IsValidGitWorkingDir(),
            Module.WorkingDir,
            currentRepositorySuggestedSourceUrl: () =>
            {
                string? remote = CloneSourceSeed.PickSuggestedRemote(
                    Module.GetSetting(string.Format(SettingKeyString.BranchRemote, Module.GetSelectedBranch())),
                    Module.GetRemoteNames());

                string pushUrl = Module.GetSetting(string.Format(SettingKeyString.RemotePushUrl, remote));
                return string.IsNullOrEmpty(pushUrl) ? Module.GetSetting(string.Format(SettingKeyString.RemoteUrl, remote)) : pushUrl;
            },
            Directory.Exists);

        if (seed.Source is not null)
        {
            _NO_TRANSLATE_From.Text = seed.Source;
        }

        if (seed.Destination is not null)
        {
            _NO_TRANSLATE_To.Text = seed.Destination;
        }

        FromTextUpdate(this, EventArgs.Empty);
    }

    private void OkClick(object sender, EventArgs e)
    {
        try
        {
            Cursor = Cursors.Default;
            _branchLoaderSequence.CancelCurrent();

            string destination = _NO_TRANSLATE_To.Text;
            switch (CloneModel.Validate(destination))
            {
                case CloneValidation.DestinationMissing:
                    MessageBoxes.Show(this, _errorDestinationNotSupplied.Text, _errorCloneFailed.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _NO_TRANSLATE_To.Focus();
                    return;

                case CloneValidation.DestinationNotRooted:
                    MessageBoxes.Show(this, _errorDestinationNotRooted.Text, _errorCloneFailed.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _NO_TRANSLATE_To.Focus();
                    return;
            }

            // this will fail if the path is anyhow invalid
            string dirTo = CloneModel.ResolveTargetDirectory(destination, _NO_TRANSLATE_NewDirectory.Text);

            if (!Directory.Exists(dirTo))
            {
                Directory.CreateDirectory(dirTo);
            }

            (int? depth, bool? isSingleBranch) = CloneModel.ShallowOptions(cbDownloadFullHistory.Checked);
            string? branch = CloneBranchSelection.ToBranchArgument(_NO_TRANSLATE_Branches.Text, _branchDefaultRemoteHead.Text, _branchNone.Text);

            ArgumentString cloneCmd = Commands.Clone(_NO_TRANSLATE_From.Text,
                dirTo,
                UICommands.Module.GetPathForGitExecution,
                CentralRepository.Checked,
                cbIntializeAllSubmodules.Checked,
                branch, depth, isSingleBranch);
            using (FormRemoteProcess fromProcess = new(UICommands, cloneCmd))
            {
                string sourceRepo = PathUtil.IsLocalFile(_NO_TRANSLATE_From.Text)
                    ? UICommands.Module.GetPathForGitExecution(_NO_TRANSLATE_From.Text)
                    : _NO_TRANSLATE_From.Text;
                fromProcess.SetUrlTryingToConnect(sourceRepo);
                fromProcess.ShowDialog(this);

                if (fromProcess.ErrorOccurred() || Module.InTheMiddleOfPatch())
                {
                    return;
                }
            }

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await RepositoryHistoryManager.Remotes.AddAsMostRecentAsync(_NO_TRANSLATE_From.Text);
                await RepositoryHistoryManager.Locals.AddAsMostRecentAsync(dirTo);
            });

            if (!string.IsNullOrEmpty(_puttySshKey))
            {
                GitModule clonedGitModule = new(UICommands.GetRequiredService<IGitExecutorProvider>(), dirTo);
                clonedGitModule.SetSetting(string.Format(SettingKeyString.RemotePuttySshKey, "origin"), _puttySshKey);
            }

            switch (ClonePostActionDecision.Decide(_openedFromProtocolHandler, isHostedDialog: ShowInTaskbar == false, UICommands.HasRepositoryAcquiredSubscribers))
            {
                case ClonePostAction.OpenInNewInstance when AskIfNewRepositoryShouldBeOpened(dirTo):
                    Hide();
                    IGitUICommands uiCommands = UICommands.WithWorkingDirectory(dirTo);
                    uiCommands.Execute(new UICmd.Browse(), null);
                    break;

                case ClonePostAction.AnnounceAcquired when AskIfNewRepositoryShouldBeOpened(dirTo):
                    UICommands.RaiseRepositoryAcquired(new GitModule(UICommands.GetRequiredService<IGitExecutorProvider>(), dirTo));
                    break;
            }

            Close();
        }
        catch (Exception ex)
        {
            MessageBoxes.Show(this, "Exception: " + ex.Message, _errorCloneFailed.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool AskIfNewRepositoryShouldBeOpened(string dirTo)
    {
        return MessageBoxes.Show(this, string.Format(_questionOpenRepo.Text, dirTo), _questionOpenRepoCaption.Text,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    private void FromBrowseClick(object sender, EventArgs e)
    {
        string? userSelectedPath = FolderPicker.PickFolder(this, _NO_TRANSLATE_From.Text);

        if (userSelectedPath is not null)
        {
            _NO_TRANSLATE_From.Text = userSelectedPath;
        }

        FromTextUpdate(sender, e);
    }

    private void ToBrowseClick(object sender, EventArgs e)
    {
        string? userSelectedPath = FolderPicker.PickFolder(this, _NO_TRANSLATE_To.Text);

        if (userSelectedPath is not null)
        {
            _NO_TRANSLATE_To.Text = userSelectedPath;
        }

        ToTextUpdate(sender, e);
    }

    private void LoadSshKeyClick(object sender, EventArgs e)
    {
        _puttySshKey = BrowseForPrivateKey.BrowseAndLoad(this);
    }

    private void FormCloneLoad(object sender, EventArgs e)
    {
        if (!GitSshHelpers.IsPlink)
        {
            LoadSSHKey.Visible = false;
        }
    }

    private void FromSelectedIndexChanged(object sender, EventArgs e)
    {
        FromTextUpdate(sender, e);
    }

    private void FromTextUpdate(object sender, EventArgs e)
    {
        string path = PathUtil.GetRepositoryName(_NO_TRANSLATE_From.Text);

        if (path != "")
        {
            _NO_TRANSLATE_NewDirectory.Text = path;
        }

        _NO_TRANSLATE_Branches.DataSource = _defaultBranchItems;
        _NO_TRANSLATE_Branches.Select(0, 0);   // Kill full selection on the default branch text

        ToTextUpdate(sender, e);
    }

    private void ToTextUpdate(object sender, EventArgs e)
    {
        CloneDestinationPreview preview = CloneModel.EvaluateDestination(
            _NO_TRANSLATE_To.Text,
            _NO_TRANSLATE_NewDirectory.Text,
            destinationLabel.Text,
            subdirectoryLabel.Text,
            directoryExistsNotEmpty: path => Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any());

        string newRepositoryLocationInfo = string.Format(_infoNewRepositoryLocation.Text, preview.Path);

        switch (preview.State)
        {
            case CloneDestinationState.Incomplete:
                Info.Text = newRepositoryLocationInfo;
                Info.ForeColor = Color.Red.AdaptForeColor(Info.BackColor);
                break;

            case CloneDestinationState.ExistsNotEmpty:
                Info.Text = $@"{newRepositoryLocationInfo} {_infoDirectoryExists.Text}";
                Info.ForeColor = Color.Red.AdaptForeColor(Info.BackColor);
                break;

            default:
                Info.Text = $@"{newRepositoryLocationInfo} {_infoDirectoryNew.Text}";
                Info.ForeColor = SystemColors.ControlText;
                break;
        }
    }

    private void NewDirectoryTextChanged(object sender, EventArgs e)
    {
        ToTextUpdate(sender, e);
    }

    private void ToSelectedIndexChanged(object sender, EventArgs e)
    {
        ToTextUpdate(sender, e);
    }

    private void UpdateBranches(RemoteProbeStatus status, IReadOnlyList<IGitRef>? refs)
    {
        Cursor = Cursors.Default;

        switch (status)
        {
            case RemoteProbeStatus.HostKeyNotCached:
                if (FormRemoteProcess.AskForCacheHostkey(this, _NO_TRANSLATE_From.Text))
                {
                    LoadBranches();
                }

                break;

            case RemoteProbeStatus.AuthenticationFailed:
                // The authentication failed for want of a key; ask the user to supply one.
                if (FormPuttyError.AskForKey(this, out _))
                {
                    LoadBranches();
                }

                break;

            default:
                (IReadOnlyList<string> items, string? reselect) = CloneBranchSelection.MergeBranchList(
                    _defaultBranchItems, refs!.Select(gitRef => gitRef.LocalName!), _NO_TRANSLATE_Branches.Text);
                _NO_TRANSLATE_Branches.DataSource = items;
                if (reselect is not null)
                {
                    _NO_TRANSLATE_Branches.Text = reselect;
                }

                break;
        }
    }

    private void LoadBranches()
    {
        string from = _NO_TRANSLATE_From.Text;
        Cursor = Cursors.AppStarting;

        CancellationToken cancellationToken = _branchLoaderSequence.Next();
        ThreadHelper.FileAndForget(async () =>
        {
            IReadOnlyList<IGitRef> refs = Module.GetRemoteServerRefs(from, false, true, out string? errorOutput, cancellationToken);

            RemoteProbeStatus status = RemoteProbeOutcome.Classify(errorOutput);
            if (status is RemoteProbeStatus.Error)
            {
                throw new ExternalOperationException(workingDirectory: Module.WorkingDir, innerException: new Exception(errorOutput));
            }

            await this.SwitchToMainThreadAsync(cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                UpdateBranches(status, refs);
            }
        });
    }

    private void Branches_DropDown(object sender, EventArgs e)
    {
        LoadBranches();
    }

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _branchLoaderSequence.Dispose();

            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    internal TestAccessor GetTestAccessor() => new(this);

    internal readonly struct TestAccessor
    {
        private readonly FormClone _form;

        public TestAccessor(FormClone form)
        {
            _form = form;
        }

        public bool TryExtractUrl(string text, out string url) => CloneUrlExtractor.TryExtractUrl(text, out url);
    }
}
