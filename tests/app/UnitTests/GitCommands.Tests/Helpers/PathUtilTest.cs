using GitCommands;

namespace GitCommandsTests.Helpers;
public class PathUtilTest
{
    [TestCase('a', true)]
    [TestCase('z', true)]
    [TestCase('A', true)]
    [TestCase('Z', true)]
    [TestCase('0', true)]
    [TestCase('9', true)]
    [TestCase('-', true)]
    [TestCase('_', true)]
    [TestCase('.', true)]
    [TestCase('/', true)]
    [TestCase('!', true)]
    [TestCase('}', true)]
    [TestCase('@', true)]
    [TestCase(' ', false)]
    [TestCase('~', false)]
    [TestCase('^', false)]
    [TestCase(':', false)]
    [TestCase('\0', false)]
    [TestCase('\t', false)]
    [TestCase('\n', false)]
    [TestCase('\r', false)]
    [TestCase('\x7F', false)]
    public void IsValidPathChar_should_return_expected(char c, bool expected)
    {
        PathUtil.IsValidPathChar(c).Should().Be(expected);
    }

    [Test]
    public void IsValidPathChar_should_return_false_for_all_invalid_path_chars()
    {
        foreach (char c in Path.GetInvalidPathChars())
        {
            PathUtil.IsValidPathChar(c).Should().BeFalse($"character U+{(int)c:X4} should be invalid");
        }
    }

    [Test]
    public void IsValidPathChar_should_accept_all_ascii_letters_and_digits()
    {
        for (char c = 'a'; c <= 'z'; c++)
        {
            PathUtil.IsValidPathChar(c).Should().BeTrue();
        }

        for (char c = 'A'; c <= 'Z'; c++)
        {
            PathUtil.IsValidPathChar(c).Should().BeTrue();
        }

        for (char c = '0'; c <= '9'; c++)
        {
            PathUtil.IsValidPathChar(c).Should().BeTrue();
        }
    }

    [Test]
    public void ToPosixPathTest()
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            "C:/Work/GitExtensions/".Should().Be("C:/Work/GitExtensions/".ToPosixPath());
            "C:/Work/GitExtensions/".Should().Be("C:\\Work\\GitExtensions\\".ToPosixPath());
        }
        else
        {
            "C:/Work/GitExtensions/".Should().Be("C:/Work/GitExtensions/".ToPosixPath());
            "C:\\Work\\GitExtensions\\".Should().Be("C:\\Work\\GitExtensions\\".ToPosixPath());
            "/var/tmp/".Should().Be("/var/tmp/".ToPosixPath());
        }
    }

    [Test]
    public void ToNativePathTest()
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            "C:\\Work\\GitExtensions\\".Should().Be("C:\\Work\\GitExtensions\\".ToNativePath());
            "C:\\Work\\GitExtensions\\".Should().Be("C:/Work/GitExtensions/".ToNativePath());
            "\\\\my-pc\\Work\\GitExtensions\\".Should().Be("\\\\my-pc\\Work\\GitExtensions\\".ToNativePath());
        }
        else
        {
            "C:\\Work\\GitExtensions\\".Should().Be("C:\\Work\\GitExtensions\\".ToNativePath());
            "/Work/GitExtensions/".Should().Be("/Work/GitExtensions/".ToNativePath());
            "//server/share/".Should().Be("//server/share/".ToNativePath());
        }
    }

    [Test]
    public void ToWslPathTest()
    {
        // ToMountPath (which both ToWslPath and ToCygwinPath forward to) runs ToPosixPath first,
        // which only converts the NATIVE separator; the forward-slash input is already portable
        // (works identically either way), the backslash one only matches on Windows.
        PathUtil.ToWslPath(null).Should().BeNull();
        "/mnt/c/Work/GitExtensions/".Should().Be(@"C:/Work/GitExtensions/".ToWslPath());
        if (OperatingSystem.IsWindows())
        {
            "/mnt/c/Work/GitExtensions/".Should().Be(@"C:\Work\GitExtensions\".ToWslPath());
        }

        "/var/tmp/".Should().Be(@"/var/tmp/".ToWslPath());
    }

    [Test]
    public void ToCygwinPathTest()
    {
        PathUtil.ToCygwinPath(null).Should().BeNull();
        "/cygdrive/c/Work/GitExtensions/".Should().Be(@"C:/Work/GitExtensions/".ToCygwinPath());
        if (OperatingSystem.IsWindows())
        {
            "/cygdrive/c/Work/GitExtensions/".Should().Be(@"C:\Work\GitExtensions\".ToCygwinPath());
        }

        "/var/tmp/".Should().Be(@"/var/tmp/".ToCygwinPath());
    }

    [Test]
    public void ToMountPathTest()
    {
        const string prefix = "PrEfiX";
        PathUtil.ToMountPath(null, prefix).Should().BeNull();
        "".Should().Be(@"".ToMountPath(prefix));
        "C".Should().Be(@"C".ToMountPath(prefix));
        $".:".Should().Be(@".:".ToMountPath(prefix));
        $"{prefix}c".Should().Be(@"C:".ToMountPath(prefix));
        $"{prefix}c_".Should().Be(@"C:_".ToMountPath(prefix));
        $"{prefix}c/".Should().Be(@"C:/".ToMountPath(prefix));
        $"{prefix}c/Work/GitExtensions/".Should().Be(@"C:/Work/GitExtensions/".ToMountPath(prefix));
        "/var/tmp/".Should().Be(@"/var/tmp/".ToMountPath(prefix));

        if (OperatingSystem.IsWindows())
        {
            $"{prefix}c/".Should().Be(@"C:\".ToMountPath(prefix));
            $"{prefix}c/folder".Should().Be(@"C:\folder".ToMountPath(prefix));
            $"{prefix}c/Work/GitExtensions/".Should().Be(@"C:\Work\GitExtensions\".ToMountPath(prefix));
        }
    }

    [Test]
    public void EnsureTrailingPathSeparatorTest()
    {
        ((string?)null).EnsureTrailingPathSeparator().Should().BeNull();
        "".Should().Be("".EnsureTrailingPathSeparator());

        if (Path.DirectorySeparatorChar == '\\')
        {
            "C\\".Should().Be("C".EnsureTrailingPathSeparator());
            "C:\\".Should().Be("C:".EnsureTrailingPathSeparator());
            "C:\\".Should().Be("C:\\".EnsureTrailingPathSeparator());
            "C:\\Work\\GitExtensions\\".Should().Be("C:\\Work\\GitExtensions".EnsureTrailingPathSeparator());
            "C:\\Work\\GitExtensions\\".Should().Be("C:\\Work\\GitExtensions\\".EnsureTrailingPathSeparator());
            "C:/Work/GitExtensions/".Should().Be("C:/Work/GitExtensions/".EnsureTrailingPathSeparator());
            "\\".Should().Be("\\".EnsureTrailingPathSeparator());
            "/".Should().Be("/".EnsureTrailingPathSeparator());
        }
        else
        {
            "/".Should().Be("/".EnsureTrailingPathSeparator());
            "/Work/GitExtensions/".Should().Be("/Work/GitExtensions".EnsureTrailingPathSeparator());
            "/Work/GitExtensions/".Should().Be("/Work/GitExtensions/".EnsureTrailingPathSeparator());
            "/Work/GitExtensions\\/".Should().Be("/Work/GitExtensions\\".EnsureTrailingPathSeparator());
        }
    }

    [Test]
    public void RemoveTrailingPathSeparatorTest()
    {
        ((string?)null).RemoveTrailingPathSeparator().Should().BeNull();
        "".Should().Be("".RemoveTrailingPathSeparator());

        char s = Path.DirectorySeparatorChar;

        "C:".Should().Be($"C:{s}".RemoveTrailingPathSeparator());
        "foo".Should().Be("foo".RemoveTrailingPathSeparator());
        "foo".Should().Be($"foo{s}".RemoveTrailingPathSeparator());
        $"foo{s}bar".Should().Be($"foo{s}bar".RemoveTrailingPathSeparator());
        $"foo{s}bar".Should().Be($"foo{s}bar{s}".RemoveTrailingPathSeparator());

        "foo".Should().Be("foo/".RemoveTrailingPathSeparator());
        "foo/bar".Should().Be("foo/bar".RemoveTrailingPathSeparator());
        "foo/bar".Should().Be("foo/bar/".RemoveTrailingPathSeparator());
    }

    [Test]
    public void IsLocalFileTest()
    {
        true.Should().Be(PathUtil.IsLocalFile("\\\\my-pc\\Work\\GitExtensions"));
        true.Should().Be(PathUtil.IsLocalFile("//my-pc/Work/GitExtensions"));
        true.Should().Be(PathUtil.IsLocalFile("C:\\Work\\GitExtensions"));
        true.Should().Be(PathUtil.IsLocalFile("C:\\Work\\GitExtensions\\"));
        true.Should().Be(PathUtil.IsLocalFile("/Work/GitExtensions"));
        true.Should().Be(PathUtil.IsLocalFile("/Work/GitExtensions/"));
        false.Should().Be(PathUtil.IsLocalFile("ssh://domain\\user@serverip/cache/git/something/something.git"));
    }

    [Test]
    public void GetFileNameTest()
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            "GitExtensions".Should().Be(PathUtil.GetFileName("\\\\my-pc\\Work\\GitExtensions"));
            "GitExtensions".Should().Be(PathUtil.GetFileName("C:\\Work\\GitExtensions"));
            "".Should().Be(PathUtil.GetFileName("C:\\Work\\GitExtensions\\"));
        }
        else
        {
            "GitExtensions".Should().Be(PathUtil.GetFileName("//my-pc/Work/GitExtensions"));
            "GitExtensions".Should().Be(PathUtil.GetFileName("/Work/GitExtensions"));
            "".Should().Be(PathUtil.GetFileName("/Work/GitExtensions/"));
        }
    }

    [Test]
    public void GetRepositoryNameTest()
    {
        "gitextensions".Should().Be(PathUtil.GetRepositoryName("https://github.com/gitextensions/gitextensions.git"));
        "gitextensions".Should().Be(PathUtil.GetRepositoryName("https://github.com/jeffqc/gitextensions"));
        "test".Should().Be(PathUtil.GetRepositoryName("git://mygitserver/git/test.git"));
        "test".Should().Be(PathUtil.GetRepositoryName("ssh://mygitserver/git/test.git"));
        "test".Should().Be(PathUtil.GetRepositoryName("ssh://john.doe@mygitserver/git/test.git"));
        "MyAwesomeRepo".Should().Be(PathUtil.GetRepositoryName("ssh://john-abraham.doe@mygitserver/git/MyAwesomeRepo.git"));
        "somerepo".Should().Be(PathUtil.GetRepositoryName("git@anotherserver.mysubnet.com:project/somerepo.git"));
        "somerepo".Should().Be(PathUtil.GetRepositoryName("http://anotherserver.mysubnet.com/project/somerepo.git"));
        "Hello Günter".Should().Be(PathUtil.GetRepositoryName("http://anotherserver.mysubnet.com/project/Hello+G%C3%BCnter.git"));
        "Hello Günter".Should().Be(PathUtil.GetRepositoryName("ssh://anotherserver.mysubnet.com/project/Hello+G%C3%BCnter.git"));
        "Hello Günter".Should().Be(PathUtil.GetRepositoryName("git://anotherserver.mysubnet.com/project/Hello+G%C3%BCnter.git"));
        "Hello Günter".Should().Be(PathUtil.GetRepositoryName("git@anotherserver.mysubnet.com:project/Hello+G%C3%BCnter.git"));

        "".Should().Be(PathUtil.GetRepositoryName(""));
        "".Should().Be(PathUtil.GetRepositoryName(null));
        if (Path.DirectorySeparatorChar == '\\')
        {
            "my_repo".Should().Be(PathUtil.GetRepositoryName(@"C:\dev\my_repo"));
            "Hello+G%C3%BCnter".Should().Be(PathUtil.GetRepositoryName(@"C:\dev\Hello+G%C3%BCnter"));
            "gitextensions".Should().Be(PathUtil.GetRepositoryName(@"\\networkshare\folder1\folder2\gitextensions"));
        }
        else
        {
            "my_repo".Should().Be(PathUtil.GetRepositoryName(@"/dev/my_repo"));
            "Hello+G%C3%BCnter".Should().Be(PathUtil.GetRepositoryName(@"/dev/Hello+G%C3%BCnter"));
            "gitextensions".Should().Be(PathUtil.GetRepositoryName(@"//networkshare/folder1/folder2/gitextensions"));
        }
    }

    [Platform(Include = "Win")]
    [TestCase(null, "")]
    [TestCase("", "")]
    [TestCase(" ", "")]
    [TestCase("c:", "")]
    [TestCase("C:\\", "C:\\")]
    [TestCase("a:\\folder\\filename.txt", "a:\\folder\\filename.txt")]
    [TestCase("a:\\folder\\..\\filename.txt", "a:\\filename.txt")]
    [TestCase("file:///C:/Test%20Project.exe", "C:\\Test Project.exe")]
    [TestCase("C:\\Progra~1\\", "C:\\Program Files\\")]
    [TestCase("C:\\Progra~1", "C:\\Program Files")]
    [TestCase("\\\\folder\\filename.txt", "\\\\folder\\filename.txt")]
    [TestCase("a:\\\\folder/filename.txt", "a:\\folder\\filename.txt")]
    [TestCase(@"c:\folder#\filename.txt", @"c:\folder#\filename.txt")]
    [TestCase(@"C:\WORK\..\WORK\.\GitExtensions\", @"C:\WORK\GitExtensions\")]
    [TestCase(@"\\my-pc\Work\.\GitExtensions\", @"\\my-pc\Work\GitExtensions\")]
    [TestCase(@"\\wsl$\Ubuntu\home\jack\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    [TestCase(@"\\Wsl$\Ubuntu\home\jack\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    [TestCase(@"\\w$\work\", "")]
    public void NormalizePath(string? path, string expected)
    {
        PathUtil.NormalizePath(path!).Should().Be(expected);
    }

    [Platform(Include = "Win")]
    [TestCase("", "")]
    [TestCase(" ", " ")]
    [TestCase("c:", "c:")]
    [TestCase("C:\\", "C:\\")]
    [TestCase(@"\\wsl$\Ubuntu\home\jack\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    [TestCase(@"\\Wsl.LoCALhosT\Ubuntu\home\jack\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    [TestCase(@"\\wsl.localhost\Ubuntu\home\jack\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    public void NormalizeWslPath(string? path, string expected)
    {
        PathUtil.NormalizeWslPath(path!).Should().Be(expected);
    }

    [TestCase(@"C:\WORK\GitExtensions\", @"C:\WORK\GitExtensions\")]
    [TestCase(@"\\my-pc\Work\GitExtensions\", @"\\my-pc\Work\GitExtensions\")]
    [TestCase(@"\\wsl$\Ubuntu\home\jack\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    [TestCase(@"\\wsl$\Ubuntu\home\jack\.\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    [TestCase(@"\\WSL$\Ubuntu\home\jack\.\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    public void Resolve(string? path, string expected)
    {
        PathUtil.Resolve(path!).Should().Be(expected);
    }

    [TestCase(@"C:\WORK\", @"GitExtensions\", @"C:\WORK\GitExtensions\")]
    [TestCase(@"C:\WORK\", @" file .txt ", @"C:\WORK\ file .txt ")]
    [TestCase(@"\\wsl$\", @"Ubuntu\home\jack\work\", @"\\wsl$\Ubuntu\home\jack\work\")]
    public void Resolve(string? path, string relativePath, string expected)
    {
        PathUtil.Resolve(path!, relativePath).Should().Be(expected);
    }

    [TestCase(@"\\w$\work\", typeof(UriFormatException))]
    [TestCase(@":$\work\", typeof(UriFormatException))]
    [TestCase(@"C:", typeof(UriFormatException))]
    [TestCase(@"\\wsl$", typeof(UriFormatException))]
    [TestCase(null, typeof(ArgumentException))]
    [TestCase("", typeof(ArgumentException))]
    [TestCase(" ", typeof(ArgumentException))]
    public void Resolve(string? input, Type expectedException)
    {
        Assert.Throws(expectedException, () => PathUtil.Resolve(input!));
    }

    [TestCase(@"\\wsl$\Ubuntu\work\..\GitExtensions\", @"\\wsl$\Ubuntu\GitExtensions\")]
    [TestCase(@"\\Wsl$\Ubuntu\work\..\GitExtensions\", @"\\wsl$\Ubuntu\GitExtensions\")]
    [TestCase(@"\\WSL$\Ubuntu\work\..\GitExtensions\", @"\\wsl$\Ubuntu\GitExtensions\")]
    public void ResolveWsl(string? path, string expected)
    {
        PathUtil.ResolveWsl(path!).Should().Be(expected);
    }

    [TestCase(@"\\w$\work\", typeof(ArgumentException))]
    [TestCase(@":$\work\", typeof(ArgumentException))]
    [TestCase(@"C:\work\", typeof(ArgumentException))]
    [TestCase(null, typeof(ArgumentException))]
    [TestCase("", typeof(ArgumentException))]
    [TestCase(" ", typeof(ArgumentException))]
    public void ResolveWsl(string? input, Type expectedException)
    {
        Assert.Throws(expectedException, () => PathUtil.ResolveWsl(input!));
    }

    [TestCase(@"C:\work\..\GitExtensions\", false)]
    [TestCase(@"\\Wsl$\Ubuntu\work\..\GitExtensions\", false)]
    [TestCase(@"\\wsl.localhost\Ubuntu\work\..\GitExtensions\", true)]
    [TestCase(@"\\wsl.localhost/Ubuntu\work\..\GitExtensions\", false)]
    public void IsWslLocalhostPath(string? path, bool expected)
    {
        PathUtil.TestAccessor.IsWslLocalhostPrefixPath(path!).Should().Be(expected);
    }

    [TestCase(@"\\Wsl$\Ubuntu\work\..\GitExtensions\", true, true)]
    [TestCase(@"\\wsl$\Ubuntu\work\..\GitExtensions\", true, true)]
    [TestCase(@"C:\work\..\GitExtensions\", false, false)]
    [TestCase(@"\\Wsl$/Ubuntu\work\..\GitExtensions\", false, false)]
    [TestCase(@"\\Wsl.localhost\GitExtensions\", true, false)]
    public void IsWslPath(string? path, bool expected, bool expectedPrefix)
    {
        PathUtil.IsWslPath(path).Should().Be(expected);
        PathUtil.TestAccessor.IsWslPrefixPath(path!).Should().Be(expectedPrefix);
    }

    [TestCase(@"\\Wsl$\Ubuntu\work\..\GitExtensions\", "Ubuntu")]
    [TestCase(@"\\wsl$\Ubuntu/work/../GitExtensions", "Ubuntu")]
    [TestCase(@"\\wsl$\Ubuntu-20.04\work\..\GitExtensions\", "Ubuntu-20.04")]
    [TestCase(@"C:\work\..\GitExtensions\", "")]
    public void GetWslDistro(string? path, string expected)
    {
        PathUtil.GetWslDistro(path).Should().Be(expected);
    }

    [TestCase(@"\\wsl$/Ubuntu/work/../GitExtensions", "")]
    public void GetWslDistro_unexpected_usage(string? path, string expected)
    {
        PathUtil.GetWslDistro(path).Should().Be(expected);
    }

    // wslDistro is "" in every case here, so GetPathForGitExecution/GetWindowsPath both take
    // their "no WSL" branch - plain path.ToPosixPath()/ToNativePath(), native-separator
    // dependent despite the wsl$-shaped content of the first case (that text is never actually
    // interpreted as a UNC prefix when distro is empty). [TestCase] can't hold
    // Path.DirectorySeparatorChar directly, so normalise the input at the top of the method
    // instead (a no-op on Windows); "expected" is always posix-shaped ('/') already, portable
    // as-is.
    [TestCase(@"\\wsl$\Ubuntu\work\..\GitExtensions\", "", @"//wsl$/Ubuntu/work/../GitExtensions/")]
    [TestCase(@"C:\work\..\GitExtensions\", "", @"C:/work/../GitExtensions/")]
    [TestCase(@"work\..\GitExtensions\", "", @"work/../GitExtensions/")]
    public void GetPathForGitExecution_GetWindowsPath_default(string? path, string wslDistro, string expected)
    {
        path = path?.Replace('\\', Path.DirectorySeparatorChar);

        PathUtil.GetPathForGitExecution(path, wslDistro).Should().Be(expected);
        PathUtil.GetWindowsPath(expected, wslDistro).Should().Be(path);
    }

    // No "\\wsl$\" UNC prefix in the input, so - same as the default-branch method above -
    // GetWslDistro finds nothing and this falls through to the drive-letter/plain ToPosixPath
    // branches: native-separator dependent, not actually WSL-UNC-specific despite wslDistro
    // being set. Normalise the input the same way.
    [TestCase(@"C:\work\..\GitExtensions\", "Ubuntu", @"/mnt/c/work/../GitExtensions/")]
    [TestCase(@"work\..\GitExtensions\", "Ubuntu", @"work/../GitExtensions/")]
    public void GetPathForGitExecution_wsl(string? path, string wslDistro, string expected)
    {
        path = path?.Replace('\\', Path.DirectorySeparatorChar);

        PathUtil.GetPathForGitExecution(path, wslDistro).Should().Be(expected);
    }

    // These DO carry a "\\wsl$\" UNC prefix, so GetWslDistro matches and GetPathForGitExecution
    // slices off the prefix and posix-converts the remainder with ToPosixPath - genuinely
    // native-separator dependent for that remainder, same as above, but WslPrefix itself
    // (src/app/GitCommands/PathUtil.cs) is a hardcoded backslash UNC constant representing an
    // actual Windows path syntax (how Windows accesses a WSL distro's filesystem), not something
    // a native-separator substitution can meaningfully translate off Windows. Genuinely
    // Windows/WSL-semantic, matching this file's existing NormalizePath/NormalizeWslPath
    // [Platform(Include = "Win")] precedent for the same reason.
    [Platform(Include = "Win")]
    [TestCase(@"\\Wsl$\Ubuntu\work\..\GitExtensions\", "Ubuntu", @"/work/../GitExtensions/")]
    [TestCase(@"\\wsl$\Ubuntu\work/../GitExtensions", "Ubuntu", @"/work/../GitExtensions")]
    [TestCase(@"\\wsl$\Ubuntu-20.04\work\..\GitExtensions\", "Ubuntu-20.04", @"/work/../GitExtensions/")]
    public void GetPathForGitExecution_wsl_uncPrefix(string? path, string wslDistro, string expected)
    {
        PathUtil.GetPathForGitExecution(path, wslDistro).Should().Be(expected);
    }

    [Platform(Include = "Win")]
    [TestCase(@"\\wsl$/Ubuntu/work/../GitExtensions", "Ubuntu", @"//wsl$/Ubuntu/work/../GitExtensions")]
    [TestCase(@"\\wsl$\Ubuntu-20.04\work\..\GitExtensions\", "Ubuntu", @"//wsl$/Ubuntu-20.04/work/../GitExtensions/")]
    public void GetPathForGitExecution_unexpected_usage(string? path, string wslDistro, string expected)
    {
        PathUtil.GetPathForGitExecution(path, wslDistro).Should().Be(expected);
    }

    // Mostly opposite to GetRepoPath_wsl - same "\\wsl$\" UNC prefix reasoning as
    // GetPathForGitExecution_wsl_uncPrefix above.
    [Platform(Include = "Win")]
    [TestCase(@"\\wsl$\Ubuntu\work\..\GitExtensions\", "Ubuntu", @"/work/../GitExtensions/")]
    [TestCase(@"\\wsl$\Ubuntu\work\..\GitExtensions", "Ubuntu", @"/work/../GitExtensions")]
    [TestCase(@"\\wsl$\Ubuntu-20.04\work\..\GitExtensions\", "Ubuntu-20.04", @"/work/../GitExtensions/")]
    [TestCase(@"\\wsl$\Ubuntu\work\..\GitExtensions\", "Ubuntu", @"work/../GitExtensions/")]
    public void GetWindowsPath_wsl_uncPrefix(string expected, string wslDistro, string? path)
    {
        PathUtil.GetWindowsPath(path, wslDistro).Should().Be(expected);
    }

    // No UNC prefix - drive-letter branch of GetWindowsPath, native-separator dependent (same
    // reasoning as GetPathForGitExecution_wsl above).
    [TestCase(@"C:\work\..\GitExtensions\", "Ubuntu", @"/mnt/c/work/../GitExtensions/")]
    public void GetWindowsPath_wsl(string expected, string wslDistro, string? path)
    {
        expected = expected.Replace('\\', Path.DirectorySeparatorChar);

        PathUtil.GetWindowsPath(path, wslDistro).Should().Be(expected);
    }

    [Platform(Include = "Win")]
    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("a:\\folder\\filename.txt")]
    [TestCase("a:\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\filename.txt")]
    [TestCase("file:////folder/filename.txt")]
    [TestCaseSource(nameof(GetInvalidPaths))]
    public void TryFindFullPath_not_throw_if_file_not_exist(string? fileName)
    {
        PathUtil.TryFindFullPath(fileName!, out _).Should().BeFalse();
    }

    private static IEnumerable<string> GetInvalidPaths()
    {
        if (Path.DirectorySeparatorChar == '\\')
        {
            yield return @"c::\word";
            yield return "\"c:\\word\t\\\"";
            yield return @".c:\Programs\";
            yield return "c:\\Programs\\Get\"\\";
        }
        else
        {
            // I am not able to figure out any invalid (giving exception) path under mono
        }
    }

    [Test]
    public void GetDisplayPath()
    {
        // GetDisplayPath itself compares against PathUtil.UserProfilePath (a static field
        // captured once, at process start), not a fresh Environment.GetFolderPath(UserProfile)
        // call - and some other test in the suite may have since called
        // EnvironmentConfiguration.SetEnvironmentVariables(), which reassigns the process's HOME
        // (observed to flake this test off Windows: Environment.GetFolderPath re-reads live HOME
        // there, so it can diverge from the value UserProfilePath latched at startup, depending
        // on test execution order). Building the input from UserProfilePath directly - the same
        // source GetDisplayPath itself reads - keeps this test's outcome independent of that
        // ordering, on every platform, without changing what's actually under test.
        string home = PathUtil.UserProfilePath;

        // Path.Combine(home, "SomePath") joins with the native separator, so the "~"-substituted
        // result carries that same native separator - only literally "\" on Windows.
        PathUtil.GetDisplayPath(Path.Combine(home, "SomePath")).Should().Be($"~{Path.DirectorySeparatorChar}SomePath");

        // Not under UserProfilePath on any platform (this literal has no bearing on the current
        // OS's profile path), so GetDisplayPath returns it unchanged regardless of platform.
        PathUtil.GetDisplayPath("c:\\SomePath").Should().Be("c:\\SomePath");
    }

    // Path.GetDirectoryName (which FindAncestors walks up via) normalises separators to the
    // NATIVE one on Windows regardless of which separator the input used - e.g. "/foo/bar" ->
    // "\foo" there - so these hardcoded backslash expectations only match on Windows even though
    // the '/' input itself is portable. [TestCase] arguments must be compile-time constants, so
    // the "\\"-literals are normalised to the native separator at the top of the method (a no-op
    // on Windows) rather than in the attributes themselves.
    [TestCase("/foo/bar", new[] { "\\foo\\", "\\" })]
    [TestCase("/foo/bar/", new[] { "\\foo\\", "\\" })]
    [TestCase("/foo", new[] { "\\" })]
    [TestCase("/foo/", new[] { "\\" })]
    [TestCase("/", new string[0])]
    public void FindAncestors(string? path, string[] expected)
    {
        expected = Array.ConvertAll(expected, e => e.Replace('\\', Path.DirectorySeparatorChar));

        PathUtil.FindAncestors(path!).ToArray().Should().Equal(expected);
    }

    // Drive-letter paths: off Windows, "\" isn't a separator at all, so "C:\foo\bar" is a single
    // path segment with no ancestors to walk - a different (and correct) result off Windows, not
    // a portable expectation to translate.
    [Platform(Include = "Win")]
    [TestCase("C:\\foo\\bar", new[] { "C:\\foo\\", "C:\\" })]
    [TestCase("C:\\", new string[0])]
    public void FindAncestors_driveLetter(string? path, string[] expected)
    {
        PathUtil.FindAncestors(path!).ToArray().Should().Equal(expected);
    }

    private static IEnumerable<TestCaseData> InvalidFolders
    {
        get
        {
            yield return new TestCaseData(null);
            yield return new TestCaseData("");
            yield return new TestCaseData(@"A:\does\not\exist");
        }
    }

    [Test, TestCaseSource(nameof(InvalidFolders))]
    public void DeleteWithExtremePrejudice_should_return_true_for_empty_input_or_absent_folders(string? path)
    {
        path.TryDeleteDirectory(out string? errorMessage).Should().BeTrue();
        errorMessage.Should().BeNull();
    }

    [Test]
    public void DeleteWithExtremePrejudice_should_return_true_if_delete_successful()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempPath);
        Directory.Exists(tempPath).Should().BeTrue();

        tempPath.TryDeleteDirectory(out string? errorMessage).Should().BeTrue();
        errorMessage.Should().BeNull();
        Directory.Exists(tempPath).Should().BeFalse();
    }

    [Test]
    public void TryFindShellPath_should_not_throw_for_nonexistent_shell()
    {
        // This exercises the null-safety guards in TryFindShellPath:
        // - GetEnvironmentVariable("ProgramW6432") may return null
        // - AppSettings.LinuxToolsDir may return ""
        // The method should handle all these gracefully without throwing.
        bool result = PathUtil.TryFindShellPath("nonexistent_shell_1234567890.exe", out string? shellPath);
        result.Should().BeFalse();
        shellPath.Should().BeNull();
    }

    [TestCase(@"C:\Users\Acker Liu\Git\bash.exe", @"""C:\Users\Acker Liu\Git\bash.exe""")]
    [TestCase(@"C:\Program Files\Git\bash.exe", @"""C:\Program Files\Git\bash.exe""")]
    [TestCase(@"C:\NoSpaces\bash.exe", @"""C:\NoSpaces\bash.exe""")]
    [TestCase(null, "")]
    public void Quote_should_handle_paths_with_spaces(string? path, string expected)
    {
        path.Quote().Should().Be(expected);
    }

    [TestCase(@"C:\Users\Acker Liu\Git\bash.exe", @"""C:\Users\Acker Liu\Git\bash.exe""")]
    [TestCase(null, null)]
    [TestCase("", "")]
    public void QuoteNE_should_handle_paths_with_spaces(string? path, string? expected)
    {
        path.QuoteNE().Should().Be(expected);
    }

    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase(" ", false)]
    [TestCase("blah", false)]
    [TestCase("https://github.com/gitextensions/gitextensions", true)]
    [TestCase("https://github.com/gitextensions/gitextensions.git", true)]
    [TestCase("github.com/gitextensions/gitextensions.git", true)]
    [TestCase("HTTPS://MYPRIVATEGITHUB.COM:8080/LOUDREPO.GIT", true)]
    [TestCase("git://myurl/myrepo.git", true)]
    [TestCase("git@github.com:gitextensions/gitextensions.git", true)]
    [TestCase("git@github.com:gitextensions/gitextensions.git/", true)]
    [TestCase("https://github.com/gitextensions/gitextensions/issues/12210#issuecomment-2672903979", false)]
    [TestCase("https://github.com/gitextensions/gitextensions?query=param", false)]
    [TestCase("github.com/gitextensions", false)]
    [TestCase("github.com/gitextensions/gitextensions/pull/9018", false)]
    [TestCase("http://", false)]
    [TestCase("HTTPS://www", true)]
    [TestCase("git://", true)]
    [TestCase("file:", false)]
    [TestCase("SSH:", true)]
    [TestCase("SSH", false)]
    [TestCase("https://myhost:12368/", true)]
    public void CanBeGitURL(string? url, bool expected)
    {
        PathUtil.CanBeGitURL(url).Should().Be(expected);
    }
}
