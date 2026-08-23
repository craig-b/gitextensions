using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Editing;

/// <summary>
///  Presentation model for the dot-file editing dialogs (.gitignore, local exclude,
///  .gitattributes, .mailmap): path resolution, dirty tracking against the loaded content, and
///  saving with the exact semantics the dialogs have always had (trailing newline enforced,
///  written through <see cref="FileInfoExtensions.MakeFileTemporaryWritable"/> in
///  <see cref="GitModule.SystemEncoding"/>).
///
///  The M5.1 MV* pilot: the WinForms views (an EditableFileViewer inside each form) stay
///  unchanged and keep loading the file themselves; this model owns the logic, so it is portable
///  and tested on every platform. Message texts and prompts stay with the views.
/// </summary>
public class RepoDotFileEditor
{
    private readonly Func<string?> _resolvePath;
    private readonly Func<bool> _isBareRepository;
    private readonly bool _createDirectory;

    private RepoDotFileEditor(Func<string?> resolvePath, Func<bool> isBareRepository, bool createDirectory)
    {
        _resolvePath = resolvePath;
        _isBareRepository = isBareRepository;
        _createDirectory = createDirectory;
    }

    /// <summary>
    ///  An editor for a dot file at the root of the work tree (".gitignore", ".gitattributes",
    ///  ".mailmap").
    /// </summary>
    public static RepoDotFileEditor ForWorkTreeFile(IGitModule module, string fileName)
    {
        FullPathResolver resolver = new(() => module.WorkingDir);
        return new(() => resolver.Resolve(fileName), module.IsBareRepository, createDirectory: false);
    }

    /// <summary>
    ///  An editor for the repository-local exclude file (".git/info/exclude"). The "info"
    ///  directory is not guaranteed to exist, so saving creates it.
    /// </summary>
    public static RepoDotFileEditor ForLocalExclude(IGitModule module)
        => new(() => Path.Join(module.ResolveGitInternalPath("info"), "exclude"), module.IsBareRepository, createDirectory: true);

    /// <summary>
    ///  An editor for ".gitignore" proper. Saving creates the directory for parity with the
    ///  historical dialog behaviour (relevant only in edge cases; the work tree root exists).
    /// </summary>
    public static RepoDotFileEditor ForGitIgnore(IGitModule module, bool localExclude)
    {
        if (localExclude)
        {
            return ForLocalExclude(module);
        }

        FullPathResolver resolver = new(() => module.WorkingDir);
        return new(() => resolver.Resolve(".gitignore"), module.IsBareRepository, createDirectory: true);
    }

    /// <summary>
    ///  The resolved path of the file being edited, or null if it cannot be resolved.
    /// </summary>
    public string? FilePath => _resolvePath();

    public bool FileExists => File.Exists(FilePath);

    /// <summary>
    ///  Editing these files requires a working directory; a bare repository has none.
    /// </summary>
    public bool IsSupported => !_isBareRepository();

    /// <summary>
    ///  The content the view last loaded from (or saved to) disk - the baseline dirty tracking
    ///  compares against. Starts empty, matching a dialog opened for a file that does not exist
    ///  yet.
    /// </summary>
    public string OriginalContent { get; private set; } = string.Empty;

    /// <summary>
    ///  The view loads the file itself (it owns rendering and encoding detection); it reports
    ///  the loaded text here to establish the dirty-tracking baseline.
    /// </summary>
    public void NotifyContentLoaded(string content) => OriginalContent = content;

    public bool HasUnsavedChanges(string currentText) => OriginalContent != currentText;

    /// <summary>
    ///  Saves <paramref name="currentText"/> to <see cref="FilePath"/>, enforcing a trailing
    ///  newline, and updates <see cref="OriginalContent"/> on success. Throws on failure
    ///  (including an unresolvable path) - the view owns presenting the error.
    /// </summary>
    public void Save(string currentText)
    {
        string filePath = FilePath ?? throw new InvalidOperationException("The file path could not be resolved.");

        if (!currentText.EndsWith(Environment.NewLine))
        {
            currentText += Environment.NewLine;
        }

        FileInfoExtensions.MakeFileTemporaryWritable(
            filePath,
            path =>
            {
                if (_createDirectory)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                }

                File.WriteAllBytes(path, GitModule.SystemEncoding.GetBytes(currentText));
            });

        OriginalContent = currentText;
    }
}
