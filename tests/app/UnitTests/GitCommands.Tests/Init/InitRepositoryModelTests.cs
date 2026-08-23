using GitCommands.Init;

namespace GitCommandsTests.Init;

public sealed class InitRepositoryModelTests
{
    // OS-rooted paths: DirectoryInfo/Path.IsPathRooted based logic must run on both Windows and Linux.
    private static readonly string _root = Path.GetFullPath(Path.DirectorySeparatorChar.ToString());

    private static string Rooted(params string[] parts) => parts.Aggregate(_root, Path.Combine);

    [Test]
    public void SeedDirectory_prefers_the_explicit_argument()
    {
        InitRepositoryModel.SeedDirectory(
                explicitDirectory: Rooted("explicit"),
                currentRepositoryIsValid: true,
                currentWorkingDir: Rooted("work"),
                defaultCloneDestinationPath: Rooted("defaults"))
            .Should().Be(Rooted("explicit"));
    }

    [Test]
    public void SeedDirectory_empty_explicit_argument_falls_to_the_default()
    {
        InitRepositoryModel.SeedDirectory(
                explicitDirectory: "",
                currentRepositoryIsValid: true,
                currentWorkingDir: Rooted("work"),
                defaultCloneDestinationPath: Rooted("defaults"))
            .Should().Be(Rooted("defaults"), because: "an empty (non-null) argument still bypasses the working dir");
    }

    [Test]
    public void SeedDirectory_uses_the_working_dir_of_a_valid_repository()
    {
        InitRepositoryModel.SeedDirectory(
                explicitDirectory: null,
                currentRepositoryIsValid: true,
                currentWorkingDir: Rooted("work"),
                defaultCloneDestinationPath: Rooted("defaults"))
            .Should().Be(Rooted("work"));
    }

    [TestCase(null)]
    [TestCase("")]
    public void SeedDirectory_valid_repository_with_an_empty_working_dir_falls_to_the_default(string? currentWorkingDir)
    {
        InitRepositoryModel.SeedDirectory(
                explicitDirectory: null,
                currentRepositoryIsValid: true,
                currentWorkingDir,
                defaultCloneDestinationPath: Rooted("defaults"))
            .Should().Be(Rooted("defaults"));
    }

    [Test]
    public void SeedDirectory_invalid_repository_falls_to_the_default()
    {
        InitRepositoryModel.SeedDirectory(
                explicitDirectory: null,
                currentRepositoryIsValid: false,
                currentWorkingDir: Rooted("work"),
                defaultCloneDestinationPath: Rooted("defaults"))
            .Should().Be(Rooted("defaults"));
    }

    [Test]
    public void SeedDirectory_with_nothing_to_offer_is_empty()
    {
        InitRepositoryModel.SeedDirectory(
                explicitDirectory: null,
                currentRepositoryIsValid: false,
                currentWorkingDir: null,
                defaultCloneDestinationPath: null)
            .Should().BeEmpty();
    }

    [Test]
    public void Validate_rejects_an_unrooted_path_without_touching_the_filesystem()
    {
        bool fileChecked = false;

        InitRepositoryModel.Validate(
                Path.Combine("relative", "path"),
                fileExists: _ =>
                {
                    fileChecked = true;
                    return false;
                })
            .Should().Be(InitValidation.NotRootedDirectoryPath);

        fileChecked.Should().BeFalse();
    }

    [Test]
    public void Validate_rejects_a_rooted_path_naming_an_existing_file()
    {
        InitRepositoryModel.Validate(Rooted("repo"), fileExists: _ => true).Should().Be(InitValidation.PathIsFile);
    }

    [Test]
    public void Validate_accepts_a_rooted_path_that_is_not_a_file()
    {
        InitRepositoryModel.Validate(Rooted("repo"), fileExists: _ => false).Should().Be(InitValidation.Ok);
    }

    [Test]
    public void IsRootedDirectoryPath_requires_a_rooted_nonblank_path()
    {
        InitRepositoryModel.IsRootedDirectoryPath("   ").Should().BeFalse();
        InitRepositoryModel.IsRootedDirectoryPath(Path.Combine("relative", "path")).Should().BeFalse();
        InitRepositoryModel.IsRootedDirectoryPath(Rooted("repo")).Should().BeTrue();
    }

    [Test]
    public void Options_central_repository_is_bare_and_shared()
    {
        InitRepositoryModel.Options(central: true).Should().Be((true, true));
    }

    [Test]
    public void Options_personal_repository_is_neither_bare_nor_shared()
    {
        InitRepositoryModel.Options(central: false).Should().Be((false, false));
    }
}
