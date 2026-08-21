using GitCommands.Clone;

namespace GitCommandsTests.Clone;

public sealed class CloneModelTests
{
    // OS-rooted paths: Path.IsPathRooted/Path.Combine based logic must run on both Windows and Linux.
    private static readonly string _root = Path.GetFullPath(Path.DirectorySeparatorChar.ToString());

    private static string Rooted(params string[] parts) => parts.Aggregate(_root, Path.Combine);

    [Test]
    public void EvaluateDestination_renders_a_blank_destination_as_its_placeholder_and_is_incomplete()
    {
        bool existenceChecked = false;

        CloneDestinationPreview preview = CloneModel.EvaluateDestination(
            destination: "",
            subDirectory: "repo",
            destinationPlaceholder: "destination",
            subDirectoryPlaceholder: "subdirectory",
            directoryExistsNotEmpty: _ =>
            {
                existenceChecked = true;
                return true;
            });

        preview.State.Should().Be(CloneDestinationState.Incomplete);
        preview.Path.Should().Be(Path.Combine("[destination]", "repo"));
        existenceChecked.Should().BeFalse(because: "an incomplete destination is never probed on disk");
    }

    [Test]
    public void EvaluateDestination_treats_invalid_path_characters_as_unfilled()
    {
        // '\0' is in Path.GetInvalidPathChars() (the source of Delimiters.InvalidPathCharsSearchValues)
        // on every OS; most Windows-only offenders like '|' are not portable test data.
        CloneDestinationPreview preview = CloneModel.EvaluateDestination(
            destination: "bad\0dir",
            subDirectory: "repo",
            destinationPlaceholder: "destination",
            subDirectoryPlaceholder: "subdirectory",
            directoryExistsNotEmpty: _ => false);

        preview.State.Should().Be(CloneDestinationState.Incomplete);
        preview.Path.Should().Be(Path.Combine("[destination]", "repo"));
    }

    [Test]
    public void EvaluateDestination_reports_an_existing_nonempty_target()
    {
        string destination = Rooted("clones");
        string? probedPath = null;

        CloneDestinationPreview preview = CloneModel.EvaluateDestination(
            destination,
            subDirectory: "repo",
            destinationPlaceholder: "destination",
            subDirectoryPlaceholder: "subdirectory",
            directoryExistsNotEmpty: path =>
            {
                probedPath = path;
                return true;
            });

        preview.State.Should().Be(CloneDestinationState.ExistsNotEmpty);
        preview.Path.Should().Be(Path.Combine(destination, "repo"));
        probedPath.Should().Be(preview.Path);
    }

    [Test]
    public void EvaluateDestination_reports_a_new_target()
    {
        string destination = Rooted("clones");

        CloneDestinationPreview preview = CloneModel.EvaluateDestination(
            destination,
            subDirectory: "repo",
            destinationPlaceholder: "destination",
            subDirectoryPlaceholder: "subdirectory",
            directoryExistsNotEmpty: _ => false);

        preview.State.Should().Be(CloneDestinationState.New);
        preview.Path.Should().Be(Path.Combine(destination, "repo"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Validate_rejects_a_missing_destination(string? destination)
    {
        CloneModel.Validate(destination).Should().Be(CloneValidation.DestinationMissing);
    }

    [Test]
    public void Validate_rejects_a_relative_destination()
    {
        CloneModel.Validate(Path.Combine("relative", "path")).Should().Be(CloneValidation.DestinationNotRooted);
    }

    [Test]
    public void Validate_accepts_a_rooted_destination()
    {
        CloneModel.Validate(Rooted("clones")).Should().Be(CloneValidation.Ok);
    }

    [Test]
    public void ShallowOptions_full_history_sets_neither_depth_nor_single_branch()
    {
        (int? depth, bool? isSingleBranch) = CloneModel.ShallowOptions(downloadFullHistory: true);

        depth.Should().BeNull();
        isSingleBranch.Should().BeNull();
    }

    [Test]
    public void ShallowOptions_shallow_clone_uses_depth_one_without_single_branch()
    {
        (int? depth, bool? isSingleBranch) = CloneModel.ShallowOptions(downloadFullHistory: false);

        depth.Should().Be(1);
        isSingleBranch.Should().BeFalse();
    }
}
