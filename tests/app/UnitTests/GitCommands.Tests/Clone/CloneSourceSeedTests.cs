using GitCommands.Clone;

namespace GitCommandsTests.Clone;

public sealed class CloneSourceSeedTests
{
    // OS-rooted paths: the Path.GetPathRoot/GetDirectoryName based logic must run on both Windows and Linux.
    private static readonly string _root = Path.GetFullPath(Path.DirectorySeparatorChar.ToString());

    private static string Rooted(params string[] parts) => parts.Aggregate(_root, Path.Combine);

    private static CloneSeed Resolve(
        string? urlArgument = null,
        string? clipboardText = null,
        string? defaultCloneDestinationPath = null,
        bool currentRepositoryIsValid = false,
        string? currentWorkingDir = null,
        Func<string?>? currentRepositorySuggestedSourceUrl = null,
        Func<string, bool>? directoryExists = null)
        => CloneSourceSeed.Resolve(
            urlArgument,
            clipboardText,
            defaultCloneDestinationPath,
            currentRepositoryIsValid,
            currentWorkingDir,
            currentRepositorySuggestedSourceUrl ?? (() => null),
            directoryExists ?? (_ => false));

    [Test]
    public void Url_argument_becomes_the_source()
    {
        CloneSeed seed = Resolve(urlArgument: "https://host/repo.git");

        seed.Source.Should().Be("https://host/repo.git");
        seed.Destination.Should().BeNull();
    }

    [Test]
    public void Url_argument_suppresses_the_suggested_remote_lookup()
    {
        bool consulted = false;

        CloneSeed seed = Resolve(
            urlArgument: "https://host/repo.git",
            currentRepositoryIsValid: true,
            currentWorkingDir: Rooted("work", "repo"),
            currentRepositorySuggestedSourceUrl: () =>
            {
                consulted = true;
                return "https://other/suggested.git";
            });

        consulted.Should().BeFalse();
        seed.Source.Should().Be("https://host/repo.git");
    }

    [Test]
    public void Existing_directory_argument_becomes_the_destination()
    {
        string directory = Rooted("existing", "dir");
        string? seenByExists = null;

        CloneSeed seed = Resolve(
            urlArgument: directory,
            directoryExists: path =>
            {
                seenByExists = path;
                return true;
            });

        seed.Source.Should().BeNull();
        seed.Destination.Should().Be(directory);
        seenByExists.Should().Be(directory);
    }

    [Test]
    public void Clipboard_clone_command_line_yields_the_url_as_source()
    {
        CloneSeed seed = Resolve(clipboardText: "git clone https://host/repo.git && cd x");

        seed.Source.Should().Be("https://host/repo.git");
    }

    [Test]
    public void Clipboard_source_suppresses_the_suggested_remote_lookup()
    {
        bool consulted = false;

        CloneSeed seed = Resolve(
            clipboardText: "git clone https://host/repo.git && cd x",
            currentRepositoryIsValid: true,
            currentWorkingDir: Rooted("work", "repo"),
            currentRepositorySuggestedSourceUrl: () =>
            {
                consulted = true;
                return "https://other/suggested.git";
            });

        consulted.Should().BeFalse();
        seed.Source.Should().Be("https://host/repo.git");
    }

    [Test]
    public void Suggested_url_becomes_the_source_and_the_working_dir_parent_the_destination()
    {
        CloneSeed seed = Resolve(
            currentRepositoryIsValid: true,
            currentWorkingDir: Rooted("work", "repo"),
            currentRepositorySuggestedSourceUrl: () => "https://other/suggested.git");

        seed.Source.Should().Be("https://other/suggested.git");
        seed.Destination.Should().Be(Rooted("work"));
    }

    [Test]
    public void Suggested_url_does_not_override_the_default_clone_destination()
    {
        CloneSeed seed = Resolve(
            defaultCloneDestinationPath: Rooted("defaults"),
            currentRepositoryIsValid: true,
            currentWorkingDir: Rooted("work", "repo"),
            currentRepositorySuggestedSourceUrl: () => "https://other/suggested.git");

        seed.Source.Should().Be("https://other/suggested.git");
        seed.Destination.Should().Be(Rooted("defaults"), because: "the parent-of-working-dir rule only fills a still-blank destination");
    }

    [Test]
    public void Blank_destination_falls_back_to_the_working_dir_parent_for_a_valid_repository()
    {
        CloneSeed seed = Resolve(
            currentRepositoryIsValid: true,
            currentWorkingDir: Rooted("work", "repo"));

        seed.Source.Should().BeNull();
        seed.Destination.Should().Be(Rooted("work"));
    }

    [Test]
    public void Blank_destination_falls_back_to_the_working_dir_itself_when_it_is_not_a_repository()
    {
        CloneSeed seed = Resolve(
            currentRepositoryIsValid: false,
            currentWorkingDir: Rooted("work", "repo"));

        seed.Destination.Should().Be(Rooted("work", "repo"));
    }

    [Test]
    public void Destination_stays_null_when_the_working_dir_is_a_path_root()
    {
        CloneSeed seed = Resolve(
            currentRepositoryIsValid: true,
            currentWorkingDir: _root);

        seed.Destination.Should().BeNull(because: "a path root has no parent to suggest");
    }

    [Test]
    public void Empty_inputs_yield_an_empty_seed()
    {
        CloneSeed seed = Resolve();

        seed.Source.Should().BeNull();
        seed.Destination.Should().BeNull();
    }

    [Test]
    public void PickSuggestedRemote_prefers_the_current_branch_remote()
    {
        CloneSourceSeed.PickSuggestedRemote("upstream", ["origin", "other"]).Should().Be("upstream");
    }

    [Test]
    public void PickSuggestedRemote_matches_origin_case_insensitively()
    {
        CloneSourceSeed.PickSuggestedRemote(currentBranchRemote: null, ["other", "ORIGIN"])
            .Should().Be("origin", because: "the literal name is returned, not the list entry's casing");
    }

    [Test]
    public void PickSuggestedRemote_falls_back_to_the_first_remote()
    {
        CloneSourceSeed.PickSuggestedRemote(currentBranchRemote: "", ["alpha", "beta"]).Should().Be("alpha");
    }

    [Test]
    public void PickSuggestedRemote_returns_null_without_remotes()
    {
        CloneSourceSeed.PickSuggestedRemote(currentBranchRemote: null, []).Should().BeNull();
    }
}
