using System.IO.Abstractions;
using GitCommands.Git;
using GitCommands.Git.Tag;
using GitExtensions.Extensibility.Git;
using NSubstitute;
using UICmd = GitExtensions.Extensibility.Git.UICommands;

namespace GitCommandsTests.Git.Tag;
public class GitTagControllerTest
{
    private readonly string _workingDir = TestContext.CurrentContext.TestDirectory;
    private string _tagMessageFile = null!;
    private IGitTagController _controller = null!;
    private IFileSystem _fileSystem = null!;
    private IGitUICommands _uiCommands = null!;

    [SetUp]
    public void Setup()
    {
        _tagMessageFile = Path.Combine(_workingDir, "TAGMESSAGE");

        _fileSystem = Substitute.For<IFileSystem>();
        _fileSystem.File.Returns(Substitute.For<FileBase>());

        _uiCommands = Substitute.For<IGitUICommands>();
        _uiCommands.Module.WorkingDir.Returns(_workingDir);
        _uiCommands.Module.GetPathForGitExecution(_tagMessageFile).Returns(_tagMessageFile);

        _controller = new GitTagController(_uiCommands, _fileSystem);
    }

    [Test]
    public void CreateTagWithMessageThrowsIfTheWindowIsNull()
    {
        GitCreateTagArgs args = CreateAnnotatedTagArgs();
        ((Action)(() => _controller.CreateTag(args, parentWindow: null!))).Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void CreateTagWithMessageWritesTagMessageFile()
    {
        GitCreateTagArgs args = CreateAnnotatedTagArgs();

        _controller.CreateTag(args, CreateTestingWindow());

        _fileSystem.File.Received(1).WriteAllText(_tagMessageFile, "hello world");
    }

    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void CreateTagWithMessageDeletesTheTemporaryFileForUiResult(bool uiResult)
    {
        GitCreateTagArgs args = CreateAnnotatedTagArgs();

        _fileSystem.File.Exists(Arg.Is<string>(s => s != null)).Returns(true);

        _uiCommands.Execute(Arg.Is<UICmd.GitCommandLineProcess>(cmd => cmd.Command.Arguments.StartsWith("tag")), Arg.Any<object?>())
            .Returns(uiResult);

        _controller.CreateTag(args, CreateTestingWindow()).Should().Be(uiResult);

        _fileSystem.File.Received(1).Delete(_tagMessageFile);
    }

    [Test]
    public void PassesCreatedArgsAndWindowToCommands()
    {
        GitCreateTagArgs args = CreateAnnotatedTagArgs();
        IWin32Window window = CreateTestingWindow();

        _controller.CreateTag(args, window);

        _uiCommands.Received(1).Execute(Arg.Is<UICmd.GitCommandLineProcess>(cmd => cmd.Command.Arguments.StartsWith("tag")), window);
    }

    private static IWin32Window CreateTestingWindow()
    {
        return Substitute.For<IWin32Window>();
    }

    private static GitCreateTagArgs CreateAnnotatedTagArgs()
    {
        return new GitCreateTagArgs("tagname", ObjectId.Random(), TagOperation.Annotate, "hello world");
    }
}
