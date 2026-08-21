using GitCommands;
using GitCommands.Init;
using GitCommands.UserRepositoryHistory;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using ResourceManager;

namespace GitUI.CommandsDialogs;

public partial class FormInit : GitExtensionsDialog
{
    private readonly TranslationString _chooseDirectory =
        new("Please choose a directory.");

    private readonly TranslationString _chooseDirectoryCaption =
        new("Choose directory");

    private readonly TranslationString _chooseDirectoryNotFile =
        new("Cannot initialize a new repository on a file.\nPlease choose a directory.");

    private readonly TranslationString _initMsgBoxCaption =
        new("Create new repository");

    /// <summary>
    ///  Initializes a new instance of the <see cref="FormInit"/> class.
    /// </summary>
    /// <param name="commands">The <see cref="IGitUICommands"/> instance, mainly in its role as <see cref="IServiceProvider"/>.</param>
    /// <param name="dir">The explicit initial directory path, if any.</param>
    public FormInit(IGitUICommands commands, string? dir)
        : base(commands, enablePositionRestore: true)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        InitializeComponent();

        InitializeComplete();

        IList<Repository> repositoryHistory = ThreadHelper.JoinableTaskFactory.Run(RepositoryHistoryManager.Locals.LoadRecentHistoryAsync);
        _NO_TRANSLATE_Directory.DataSource = repositoryHistory;
        _NO_TRANSLATE_Directory.DisplayMember = nameof(Repository.Path);
        _NO_TRANSLATE_Directory.SelectedIndex = -1;
        _NO_TRANSLATE_Directory.Text = InitRepositoryModel.SeedDirectory(
            dir, commands.Module.IsValidGitWorkingDir(), commands.Module.WorkingDir, AppSettings.DefaultCloneDestinationPath);
        _NO_TRANSLATE_Directory.ResizeDropDownWidth();
    }

    private void InitClick(object sender, EventArgs e)
    {
        string directoryPath = _NO_TRANSLATE_Directory.Text;

        switch (InitRepositoryModel.Validate(directoryPath, File.Exists))
        {
            case InitValidation.NotRootedDirectoryPath:
                MessageBoxes.Show(this, _chooseDirectory.Text, _chooseDirectoryCaption.Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;

            case InitValidation.PathIsFile:
                MessageBoxes.Show(this, _chooseDirectoryNotFile.Text, TranslatedStrings.Error, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
        }

        GitModule module = new(UICommands.GetRequiredService<IGitExecutorProvider>(), directoryPath);

        if (!System.IO.Directory.Exists(module.WorkingDir))
        {
            System.IO.Directory.CreateDirectory(module.WorkingDir);
        }

        (bool bare, bool shared) = InitRepositoryModel.Options(Central.Checked);
        MessageBoxes.Show(this, module.Init(bare, shared), _initMsgBoxCaption.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);

        UICommands.RaiseRepositoryAcquired(module);

        ThreadHelper.JoinableTaskFactory.Run(() => RepositoryHistoryManager.Locals.AddAsMostRecentAsync(directoryPath));
        Close();
    }

    private void BrowseClick(object sender, EventArgs e)
    {
        string? userSelectedPath = FolderPicker.PickFolder(this);

        if (userSelectedPath is not null)
        {
            _NO_TRANSLATE_Directory.Text = userSelectedPath;
        }
    }

    internal TestAccessor GetTestAccessor() => new(this);

    internal readonly struct TestAccessor
    {
        private readonly FormInit _form;

        public TestAccessor(FormInit form)
        {
            _form = form;
        }

        public ComboBox DirectoryCombo => _form._NO_TRANSLATE_Directory;

        public bool IsRootedDirectoryPath(string path)
        {
            return InitRepositoryModel.IsRootedDirectoryPath(path);
        }
    }
}
