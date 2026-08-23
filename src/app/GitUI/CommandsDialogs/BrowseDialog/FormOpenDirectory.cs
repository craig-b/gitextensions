using GitCommands;
using GitCommands.Open;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitExtUtils.GitUI;
using ResourceManager;

namespace GitUI.CommandsDialogs.BrowseDialog;

public partial class FormOpenDirectory : GitExtensionsForm
{
    private readonly TranslationString _warningOpenFailed = new("The selected directory is not a valid git repository.");

    private readonly IGitExecutorProvider _executorProvider;
    private IGitModule? _chosenModule;

    public FormOpenDirectory(IGitExecutorProvider executorProvider, IGitModule? currentModule)
    {
        _executorProvider = executorProvider;

        ThreadHelper.ThrowIfNotOnUIThread();

        InitializeComponent();
        InitializeComplete();

        IList<Repository> repositoryHistory = ThreadHelper.JoinableTaskFactory.Run(RepositoryHistoryManager.Locals.LoadRecentHistoryAsync);
        _NO_TRANSLATE_Directory.DataSource = GetDirectories(currentModule, repositoryHistory);
        _NO_TRANSLATE_Directory.ResizeDropDownWidth();

        Load.Select();
        _NO_TRANSLATE_Directory.Focus();
        _NO_TRANSLATE_Directory.Select();
    }

    protected override void OnRuntimeLoad(EventArgs e)
    {
        base.OnRuntimeLoad(e);

        // scale up for hi DPI
        MaximumSize = DpiUtil.Scale(new Size(800, 116));
        MinimumSize = DpiUtil.Scale(new Size(450, 116));
    }

    private static IReadOnlyList<string> GetDirectories(IGitModule? currentModule, IEnumerable<Repository> repositoryHistory)
        => OpenRepositoryModel.CandidateDirectories(
            AppSettings.DefaultCloneDestinationPath,
            currentModule?.WorkingDir,
            repositoryHistory.Select(r => r.Path),
            AppSettings.RecentWorkingDir,
            EnvironmentConfiguration.GetHomeDir());

    public static IGitModule? OpenModule(IWin32Window? owner, IGitExecutorProvider executorProvider, IGitModule? currentModule)
    {
        using FormOpenDirectory open = new(executorProvider, currentModule);
        open.ShowDialog(owner);
        return open._chosenModule;
    }

    private void LoadClick(object sender, EventArgs e)
    {
        _NO_TRANSLATE_Directory.Text = _NO_TRANSLATE_Directory.Text.Trim();

        _chosenModule = OpenGitRepository(_executorProvider, _NO_TRANSLATE_Directory.Text, RepositoryHistoryManager.Locals);
        if (_chosenModule is not null)
        {
            Close();
            return;
        }

        MessageBoxes.Show(this, _warningOpenFailed.Text, TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void DirectoryKeyPress(object sender, KeyPressEventArgs e)
    {
        if (e.KeyChar == (char)Keys.Enter)
        {
            LoadClick(this, EventArgs.Empty);
        }
    }

    private void folderBrowserButton_Click(object sender, EventArgs e)
    {
        string? userSelectedPath = FolderPicker.PickFolder(this, _NO_TRANSLATE_Directory.Text);
        if (!string.IsNullOrEmpty(userSelectedPath))
        {
            _NO_TRANSLATE_Directory.Text = userSelectedPath;
            Load.PerformClick();
        }
    }

    private void folderGoUpButton_Click(object sender, EventArgs e)
    {
        if (OpenRepositoryModel.ParentOf(_NO_TRANSLATE_Directory.Text) is not string parentPath)
        {
            return;
        }

        _NO_TRANSLATE_Directory.Text = parentPath;
        _NO_TRANSLATE_Directory.Focus();
        _NO_TRANSLATE_Directory.Select(_NO_TRANSLATE_Directory.Text.Length, 0);
        SendKeys.Send(Path.DirectorySeparatorChar.ToString());
    }

    private void _NO_TRANSLATE_Directory_TextChanged(object sender, EventArgs e)
    {
        folderGoUpButton.Enabled = OpenRepositoryModel.CanGoUp(_NO_TRANSLATE_Directory.Text, Directory.Exists);
    }

    private static IGitModule? OpenGitRepository(IGitExecutorProvider executorProvider, string path, ILocalRepositoryManager localRepositoryManager)
    {
        if (OpenRepositoryModel.TryGetOpenablePath(path, Directory.Exists, GitModule.IsValidGitWorkingDir) is not string openablePath)
        {
            return null;
        }

        GitModule chosenModule = new(executorProvider, openablePath);
        ThreadHelper.JoinableTaskFactory.Run(() => localRepositoryManager.AddAsMostRecentAsync(chosenModule.WorkingDir));
        return chosenModule;
    }

    internal TestAccessor GetTestAccessor()
        => new(this);

    internal readonly struct TestAccessor
    {
        private readonly FormOpenDirectory _form;

        public TestAccessor(FormOpenDirectory form)
        {
            _form = form;
        }

        public static IGitModule? OpenGitRepository(IGitExecutorProvider executorProvider, string path, ILocalRepositoryManager localRepositoryManager)
            => FormOpenDirectory.OpenGitRepository(executorProvider, path, localRepositoryManager);
    }
}
