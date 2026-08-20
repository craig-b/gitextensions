using GitCommands.Archive;
using GitExtensions.Extensibility.Git;

namespace GitCommandsTests.Archive;

public sealed class ArchiveModelTests
{
    [Test]
    public void File_extension_matches_the_format()
    {
        ArchiveModel.FileExtension(ArchiveFormat.Zip).Should().Be("zip");
        ArchiveModel.FileExtension(ArchiveFormat.Tar).Should().Be("tar");
    }

    [Test]
    public void Suggestion_combines_workdir_and_revision()
    {
        ArchiveModel.SuggestFileName("repo", "abc123", []).Should().Be("repo_abc123");
    }

    [Test]
    public void Suggestion_appends_a_single_path_filter_with_dots_flattened()
    {
        ArchiveModel.SuggestFileName("repo", "abc123", ["src/file.cs"]).Should().Be("repo_abc123_src/file_cs");
        ArchiveModel.SuggestFileName("repo", "abc123", ["a", "b"]).Should().Be("repo_abc123");
        ArchiveModel.SuggestFileName("repo", "abc123", ["  "]).Should().Be("repo_abc123");
    }

    [Test]
    public void Path_arguments_quote_lines_and_skip_empties()
    {
        ArchiveModel.PathArgumentsFromLines(["src/a", "", "docs"]).Should().Be("\"src/a\"  \"docs\"");
    }

    [Test]
    public void Changed_file_paths_skip_deleted_files()
    {
        GitItemStatus kept = new("kept.txt");
        GitItemStatus deleted = new("gone.txt") { IsDeleted = true };

        ArchiveModel.PathArgumentsFromChangedFiles([kept, deleted]).Should().Be("\"kept.txt\"");
    }

    [Test]
    public void Command_matches_the_historical_shape()
    {
        ArchiveModel.BuildCommand(ArchiveFormat.Zip, "abc123", "/tmp/out.zip", "\"src\"").ToString()
            .Should().Be("archive --format=\"zip\" abc123 --output \"/tmp/out.zip\" \"src\"");
        ArchiveModel.BuildCommand(ArchiveFormat.Tar, "abc123", "/tmp/out.tar", "").ToString()
            .Should().Be("archive --format=\"tar\" abc123 --output \"/tmp/out.tar\"");
    }
}
