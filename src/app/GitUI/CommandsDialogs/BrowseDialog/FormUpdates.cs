using System.ComponentModel;
using GitCommands;
using ResourceManager;

namespace GitUI.CommandsDialogs.BrowseDialog;

public partial class FormUpdates : GitExtensionsDialog
{
    #region Translation
    private readonly TranslationString _newVersionAvailable = new("There is a new version {0} of Git Extensions available");
    private readonly TranslationString _noUpdatesFound = new("No updates found");
    private readonly TranslationString _checkFailed = new("Could not check for updates.");
    private readonly TranslationString _notFromRelease = new("This copy was not installed from a release. The latest release is {0}.");
    private readonly TranslationString _updating = new("Closing Git Extensions to install {0}...");
    private readonly TranslationString _confirmHeading = new("Update and restart");
    private readonly TranslationString _confirmMessage = new("Git Extensions will close, update to {0} and open again.\n\nClose any other Git Extensions windows now: the update waits for them, and gives up without changing anything if they stay open.");
    private readonly TranslationString _updateFailedToStart = new("The update could not be started.");
    #endregion

    private readonly CancellationTokenSource _cancellation = new();
    private readonly string? _repository;
    private IWin32Window? _ownerWindow;
    private UpdateState _state = UpdateState.CheckFailed;
    private string _latestTag = string.Empty;
    private string _updateProgram = string.Empty;

    public FormUpdates(Version currentVersion, string? repository = null)
        : base(commands: null, enablePositionRestore: false)
    {
        _repository = string.IsNullOrWhiteSpace(repository) ? null : repository;

        InitializeComponent();
        InitializeComplete();

        progressBar1.Visible = true;
        progressBar1.Style = ProgressBarStyle.Marquee;
    }

    public void SearchForUpdatesAndShow(IWin32Window ownerWindow, bool alwaysShow)
    {
        _ownerWindow = ownerWindow;
        ThreadHelper.FileAndForget(SearchForUpdatesAsync);
        if (alwaysShow)
        {
            ShowDialog(ownerWindow);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _cancellation.Cancel();
        base.OnFormClosed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // We need to override ProcessCmdKey as mnemonics on labels do not behave the same as buttons
        if (keyData == (Keys.Alt | Keys.L))
        {
            LaunchUrl(LaunchType.ChangeLog);
        }
        else if (keyData == (Keys.Alt | Keys.D))
        {
            LaunchUrl(LaunchType.DirectDownload);
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private async Task SearchForUpdatesAsync()
    {
        // Whatever happens, reach Done. The check this replaced could return from several places
        // without doing so, and then the dialog searched for updates until it was closed.
        string? latest = null;
        try
        {
            latest = await ForkRelease.GetLatestTagAsync(_cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        string? installed = WineUpdater.TryLocate(out string updateProgram, out string installRoot)
            ? WineUpdater.ReadInstalledTag(installRoot)
            : null;

        _updateProgram = updateProgram;
        _latestTag = latest ?? string.Empty;
        _state = ForkRelease.Decide(installed, latest);

        await DoneAsync();
    }

    private async Task DoneAsync()
    {
        await this.SwitchToMainThreadAsync();

        progressBar1.Visible = false;
        linkChangeLog.Visible = _state != UpdateState.UpToDate;

        switch (_state)
        {
            case UpdateState.UpdateAvailable:
                UpdateLabel.Text = string.Format(_newVersionAvailable.Text, _latestTag);
                linkDirectDownload.Visible = true;

                // The button only appears where the update can actually be carried out: a release
                // install with a reachable Linux side. Everywhere else the download link is the
                // honest offer.
                if (_updateProgram.Length > 0)
                {
                    btnUpdateNow.Visible = true;
                    btnUpdateNow.Focus();
                }
                else
                {
                    linkDirectDownload.Focus();
                }

                if (!Visible && _ownerWindow is not null)
                {
                    await ShowDialogAsync(_ownerWindow);
                }

                break;

            case UpdateState.UpToDate:
                UpdateLabel.Text = _noUpdatesFound.Text;
                break;

            case UpdateState.NotFromRelease:
                // A build overlaid by refresh.sh compares as older than every release, so without
                // this it would offer an update on every weekly check and none of them would apply.
                UpdateLabel.Text = string.Format(_notFromRelease.Text, _latestTag);
                break;

            default:
                UpdateLabel.Text = _checkFailed.Text;
                break;
        }
    }

    private void LaunchUrl(LaunchType launchType)
    {
        switch (launchType)
        {
            case LaunchType.ChangeLog:
                OsShellUtil.OpenUrlInDefaultBrowser(ForkRelease.ReleasesUrl);
                break;

            case LaunchType.DirectDownload:
                OsShellUtil.OpenUrlInDefaultBrowser(
                    _latestTag.Length > 0 ? ForkRelease.TagUrl(_latestTag) : ForkRelease.ReleasesUrl);
                break;
        }
    }

    private void linkChangeLog_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) => LaunchUrl(LaunchType.ChangeLog);

    private void linkDirectDownload_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) => LaunchUrl(LaunchType.DirectDownload);

    private void btnUpdateNow_Click(object sender, EventArgs e)
    {
        ThreadHelper.FileAndForget(UpdateNowAsync);
    }

    private async Task UpdateNowAsync()
    {
        await this.SwitchToMainThreadAsync();

        if (!MessageBoxes.Confirm(this, string.Format(_confirmMessage.Text, _latestTag), _confirmHeading.Text))
        {
            return;
        }

        btnUpdateNow.Enabled = false;
        linkChangeLog.Visible = false;
        linkDirectDownload.Visible = false;
        UpdateLabel.Text = string.Format(_updating.Text, _latestTag);
        progressBar1.Visible = true;

        (bool started, string message) = await WineUpdater.StartAsync(_updateProgram, _latestTag, _repository);

        await this.SwitchToMainThreadAsync();

        if (started)
        {
            // The helper waits for this process to go before it touches anything.
            Close();
            Application.Exit();
            return;
        }

        progressBar1.Visible = false;
        btnUpdateNow.Enabled = true;
        linkChangeLog.Visible = true;
        linkDirectDownload.Visible = true;
        UpdateLabel.Text = message.Length > 0 ? message : _updateFailedToStart.Text;
    }

    internal enum LaunchType
    {
        ChangeLog,
        DirectDownload
    }

    internal TestAccessor GetTestAccessor() => new(this);

    internal readonly struct TestAccessor
    {
        private readonly FormUpdates _form;

        public TestAccessor(FormUpdates form)
        {
            _form = form;
        }

        public UpdateState State
        {
            get => _form._state;
            set => _form._state = value;
        }

        public string LatestTag
        {
            get => _form._latestTag;
            set => _form._latestTag = value;
        }

        public string UpdateProgram
        {
            set => _form._updateProgram = value;
        }

        public string LabelText => _form.UpdateLabel.Text;

        public Button UpdateNowButton => _form.btnUpdateNow;

        public LinkLabel DirectDownloadLink => _form.linkDirectDownload;

        // A control that was never added to the form still answers Visible and Text quite happily,
        // so every other assertion here passes against a dialog that shows nothing at all.
        public Control? LabelParent => _form.UpdateLabel.Parent;

        public Control? ProgressBarParent => _form.progressBar1.Parent;

        public Control? ChangeLogLinkParent => _form.linkChangeLog.Parent;

        public Task RenderAsync() => _form.DoneAsync();
    }
}
