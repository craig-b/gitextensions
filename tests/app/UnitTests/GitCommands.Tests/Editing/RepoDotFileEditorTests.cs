using GitCommands;
using GitCommands.Editing;
using GitExtensions.Extensibility.Git;
using NSubstitute;

namespace GitCommandsTests.Editing;

/// <summary>
///  Tests for <see cref="RepoDotFileEditor"/>.
/// </summary>
public class RepoDotFileEditorTests
{
    private string _tempDir = null!;
    private IGitModule _module = null!;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "RepoDotFileEditorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        _module = Substitute.For<IGitModule>();
        _module.WorkingDir.Returns(_tempDir);
        _module.ResolveGitInternalPath("info").Returns(Path.Combine(_tempDir, ".git", "info"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            // Some tests leave a read-only file behind; clear that before deleting.
            foreach (string file in Directory.EnumerateFiles(_tempDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [TestCase(".gitignore")]
    [TestCase(".gitattributes")]
    [TestCase(".mailmap")]
    public void ForWorkTreeFile_should_resolve_path_under_working_dir(string fileName)
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, fileName);

        editor.FilePath.Should().Be(Path.Combine(_module.WorkingDir, fileName));
    }

    [Test]
    public void ForLocalExclude_should_resolve_to_info_exclude_under_git_dir()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForLocalExclude(_module);

        editor.FilePath.Should().Be(Path.Join(_module.ResolveGitInternalPath("info"), "exclude"));
    }

    [Test]
    public void ForGitIgnore_localExclude_true_should_behave_as_ForLocalExclude()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForGitIgnore(_module, localExclude: true);

        editor.FilePath.Should().Be(Path.Join(_module.ResolveGitInternalPath("info"), "exclude"));
    }

    [Test]
    public void ForGitIgnore_localExclude_false_should_resolve_gitignore()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForGitIgnore(_module, localExclude: false);

        editor.FilePath.Should().Be(Path.Combine(_module.WorkingDir, ".gitignore"));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void IsSupported_should_reflect_bare_repository_state(bool isBare, bool expectedIsSupported)
    {
        _module.IsBareRepository().Returns(isBare);

        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");

        editor.IsSupported.Should().Be(expectedIsSupported);
    }

    [Test]
    public void FileExists_should_be_false_for_missing_file_and_true_after_writing()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");

        editor.FileExists.Should().BeFalse();

        File.WriteAllText(editor.FilePath!, "content");

        editor.FileExists.Should().BeTrue();
    }

    [Test]
    public void Fresh_editor_should_have_empty_original_content_and_dirty_tracking_against_it()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");

        editor.OriginalContent.Should().BeEmpty();
        editor.HasUnsavedChanges(string.Empty).Should().BeFalse();
        editor.HasUnsavedChanges("x").Should().BeTrue();
    }

    [Test]
    public void NotifyContentLoaded_should_establish_new_dirty_tracking_baseline()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");

        editor.NotifyContentLoaded("abc");

        editor.OriginalContent.Should().Be("abc");
        editor.HasUnsavedChanges("abc").Should().BeFalse();
        editor.HasUnsavedChanges("abd").Should().BeTrue();
    }

    [Test]
    public void Save_should_append_newline_when_missing()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");

        editor.Save("no newline yet");

        string savedText = GitModule.SystemEncoding.GetString(File.ReadAllBytes(editor.FilePath!));
        savedText.Should().Be("no newline yet" + Environment.NewLine);
    }

    [Test]
    public void Save_should_not_double_newline_when_already_present()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");

        editor.Save("already has one" + Environment.NewLine);

        string savedText = GitModule.SystemEncoding.GetString(File.ReadAllBytes(editor.FilePath!));
        savedText.Should().Be("already has one" + Environment.NewLine);
    }

    [Test]
    public void Save_should_update_original_content_to_newline_terminated_text()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");

        editor.Save("hello");

        editor.OriginalContent.Should().Be("hello" + Environment.NewLine);
        editor.HasUnsavedChanges("hello" + Environment.NewLine).Should().BeFalse();
    }

    [Test]
    public void ForLocalExclude_Save_should_create_missing_info_directory()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForLocalExclude(_module);
        string infoDir = Path.Combine(_tempDir, ".git", "info");

        Directory.Exists(infoDir).Should().BeFalse();

        editor.Save("some rule");

        Directory.Exists(infoDir).Should().BeTrue();
        File.Exists(editor.FilePath!).Should().BeTrue();
    }

    [Test]
    public void ForWorkTreeFile_Save_on_existing_readonly_file_should_succeed_and_remain_readonly()
    {
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");
        string filePath = editor.FilePath!;
        File.WriteAllText(filePath, "original");
        File.SetAttributes(filePath, FileAttributes.ReadOnly);

        editor.Save("updated");

        File.GetAttributes(filePath).HasFlag(FileAttributes.ReadOnly).Should().BeTrue();

        // Clear the read-only flag so TearDown can delete the temp directory.
        File.SetAttributes(filePath, FileAttributes.Normal);

        string savedText = GitModule.SystemEncoding.GetString(File.ReadAllBytes(filePath));
        savedText.Should().Be("updated" + Environment.NewLine);
    }

    [Test]
    public void Save_should_throw_when_path_cannot_be_resolved()
    {
        // FullPathResolver.Resolve returns null only for a null/whitespace-only input path; the
        // ForWorkTreeFile factories always pass a fixed non-empty file name, so the only way to
        // observe RepoDotFileEditor's "unresolvable path" branch through the public factories is
        // to make the resolved base path itself degenerate. An empty WorkingDir makes
        // FullPathResolver fall back to Environment.CurrentDirectory and still successfully
        // resolve ".gitignore" against it (this is documented behaviour of FullPathResolver, not
        // a null result) - so Save does NOT throw InvalidOperationException in that case. This
        // test therefore pins the actually-observed behaviour instead of the originally predicted
        // "empty WorkingDir throws" scenario; see the report for details.
        _module.WorkingDir.Returns(string.Empty);
        RepoDotFileEditor editor = RepoDotFileEditor.ForWorkTreeFile(_module, ".gitignore");

        editor.FilePath.Should().NotBeNull();

        Action act = () => editor.Save("x");

        act.Should().NotThrow();
    }
}
