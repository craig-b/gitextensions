using CommonTestUtils;
using GitCommands.FileHistory;

namespace GitCommandsTests.FileHistory;
// Exact-casing resolution is a Windows concern (8.3 short-path round-trip); on other
// platforms TryGetExactPath returns the input unchanged.
[Platform(Include = "Win")]
public sealed class ExactPathResolverTests
{
    [TestCase(@"Does not exist")]
    [TestCase("")]
    [TestCase(" ")]
    public void TryGetExactPathName_Should_return_null_on_not_existing_file(string path)
    {
        string lowercasePath = path.ToLower();
        bool isExistingOnFileSystem = ExactPathResolver.TryGetExactPath(lowercasePath, out string? exactPath);

        isExistingOnFileSystem.Should().BeFalse();
        exactPath.Should().BeNull();
    }

    [Test]
    public void TryGetExactPathName_Should_handle_network_path()
    {
        string path = @"\\" + Environment.MachineName.ToLower() + @"\c$\Windows\System32";

        string lowercasePath = path.ToLower();
        bool isExistingOnFileSystem = ExactPathResolver.TryGetExactPath(lowercasePath, out string? exactPath);

        isExistingOnFileSystem.Should().BeTrue();

        exactPath.Should().Be(path);
    }

    [TestCase("Folder1\\file1.txt", true, true)]
    [TestCase("FOLDER1\\file1.txt", true, false)]
    [TestCase("fOLDER1\\file1.txt", true, false)]
    [TestCase("Folder2\\file1.txt", false, false)]
    public void TryGetExactPathName_should_check_if_path_matches_case(string relativePath, bool isResolved, bool doesMatch)
    {
        using GitModuleTestHelper repo = new();

        // Create a file
        string notUsed = repo.CreateFile(Path.Combine(repo.TemporaryPath, "Folder1"), "file1.txt", "bla");

        string expected = Path.Combine(repo.TemporaryPath, relativePath);

        ExactPathResolver.TryGetExactPath(expected, out string? exactPath).Should().Be(isResolved);
        if (doesMatch)
        {
            exactPath.Should().Be(expected);
        }
        else
        {
            exactPath.Should().NotBe(expected);
        }
    }
}
