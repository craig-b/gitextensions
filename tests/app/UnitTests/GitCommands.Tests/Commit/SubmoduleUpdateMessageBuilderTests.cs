using CommonTestUtils;
using GitCommands;
using GitCommands.Commit;
using GitCommands.Git;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Commit;

/// <summary>
///  Integration tests for <see cref="SubmoduleUpdateMessageBuilder"/> against real repositories -
///  the message logic was previously inline in FormCommit's menu handler and untested.
/// </summary>
[NonParallelizable]
public class SubmoduleUpdateMessageBuilderTests
{
    private GitModuleTestHelper _mainHelper = null!;
    private GitModuleTestHelper _subHelper = null!;
    private IGitModule _submoduleInMain = null!;
    private IFullPathResolver _fullPathResolver = null!;

    [SetUp]
    public void Setup()
    {
        _mainHelper = new GitModuleTestHelper("main");
        _subHelper = new GitModuleTestHelper("sub");
        _mainHelper.AddSubmodule(_subHelper, "sub");
        _submoduleInMain = _mainHelper.GetSubmodulesRecursive().Single();
        _fullPathResolver = new FullPathResolver(() => _mainHelper.Module.WorkingDir);
    }

    [TearDown]
    public void TearDown()
    {
        _mainHelper.Dispose();
        _subHelper.Dispose();
    }

    private string? Build(params GitItemStatus[] stagedFiles)
        => SubmoduleUpdateMessageBuilder.Build(
            _mainHelper.Module,
            _mainHelper.Module.GetSubmodulesConfigFile(),
            _fullPathResolver,
            path => new GitModule(new GitExecutorProvider(new GitDirectoryResolver()), path),
            stagedFiles);

    [Test]
    public void Returns_null_when_nothing_staged_is_a_submodule()
    {
        Build(new GitItemStatus("file.txt")).Should().BeNull();
        Build(new GitItemStatus("sub")).Should().BeNull();
    }

    [Test]
    public void Builds_summary_and_log_for_a_staged_submodule_update()
    {
        // Advance the submodule inside the main repository by two commits, then stage the update.
        _submoduleInMain.GitExecutable.GetOutput(@"commit --allow-empty -m ""sub change one""");
        _submoduleInMain.GitExecutable.GetOutput(@"commit --allow-empty -m ""sub change two""");
        _mainHelper.Module.GitExecutable.GetOutput("add sub");

        string? message = Build(new GitItemStatus("sub") { IsSubmodule = true });

        message.Should().NotBeNull();
        message.Should().StartWith("Submodule sub updated");
        message.Should().Contain("Submodule sub:");
        message.Should().Contain("sub change one");
        message.Should().Contain("sub change two");
        message.Should().NotEndWith("\n");
    }
}
