using GitCommands;

namespace GitCommandsTests;
public class PathEqualityComparerTests
{
    private PathEqualityComparer _comparer = null!;

    [SetUp]
    public void Setup()
    {
        _comparer = new PathEqualityComparer();
    }

    // PathEqualityComparer deliberately compares case-INSENSITIVELY only on Windows (matching its
    // case-insensitive filesystem) and only TrimEnd('\\') (never '/'), so these two cases - which
    // rely on both case-folding and backslash/forward-slash interchangeability - only hold on
    // Windows. A same-case, separator-normalising equivalent for POSIX (where the filesystem is
    // case-sensitive) is below.
    [Platform(Include = "Win")]
    [TestCase("C:\\WORK\\GitExtensions\\", "C:/Work/GitExtensions/")]
    [TestCase("\\\\my-pc\\Work\\GitExtensions\\", "//my-pc/WORK/GitExtensions/")]
    public void Equals(string input, string expected)
    {
        true.Should().Be(_comparer.Equals(input, expected));
    }

    [Platform(Exclude = "Win")]
    [TestCase("/work//GitExtensions", "/work/GitExtensions")]
    [TestCase("/work/sub/../GitExtensions", "/work/GitExtensions")]
    public void Equals_posix(string input, string expected)
    {
        true.Should().Be(_comparer.Equals(input, expected));
    }
}
