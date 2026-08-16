using GitCommands;

namespace GitCommandsTests;
public class FullPathResolverTests
{
    private readonly string _workingDir = @"c:\dev\repo";
    private FullPathResolver _resolver = null!;

    [SetUp]
    public void Setup()
    {
        _resolver = new FullPathResolver(() => _workingDir);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Resolve_should_return_null_if_path_null_or_illegal_chars(string? path)
    {
        _resolver.Resolve(path).Should().BeNull();
    }

    // "c:\" is only a rooted path per Windows' drive-letter convention (Path.IsPathRooted); off
    // Windows it isn't rooted at all, so FullPathResolver would instead resolve it relative to
    // the working dir - a different (and already-covered) code path, not the same behaviour
    // under test here. Kept Windows-only, with a POSIX-rooted analogue below for the same intent.
    [Platform(Include = "Win")]
    [TestCase(@"c:\")]
    public void Resolve_should_return_original_path_if_rooted(string? path)
    {
        _resolver.Resolve(path).Should().Be(path);
    }

    [Platform(Exclude = "Win")]
    [TestCase("/")]
    public void Resolve_should_return_original_path_if_rooted_posix(string? path)
    {
        _resolver.Resolve(path).Should().Be(path);
    }

    // _workingDir is a Windows drive-letter path, and the expectation is joined with a literal
    // backslash: both only match FullPathResolver's actual (correct, native-separator) output on
    // Windows.
    [Platform(Include = "Win")]
    [TestCase(@"folder\", 10000)]
    public void Resolve_should_return_long_full_path(string dir, int repeats)
    {
        string path = string.Concat(Enumerable.Repeat(dir, repeats)) + "filename.txt";
        _resolver.Resolve(path).Should().Be($"{_workingDir}\\{path}");
    }

    [Platform(Include = "Win")]
    [TestCase(@"file")]
    [TestCase("folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\filename.txt")]
    [TestCase("folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder#\\filename.txt")]
    [TestCase("folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\folder\\filename.txt")]
    public void Resolve_should_return_full_path(string? path)
    {
        _resolver.Resolve(path).Should().Be($"{_workingDir}\\{path}");
    }

    [Platform(Include = "Win")]
    [TestCase("drivers/gpu/drm/nouveau/nvkm/subdev/i2c/aux.c")]
    public void Resolve_handles_system_filenames(string? path)
    {
        _resolver.Resolve(path).Should().Be($"{_workingDir}\\{path!.Replace("/", "\\")}");
    }

    // Every workingDir here is a Windows drive-letter path (some with a trailing '\' or '/'
    // already), so the resolve-and-combine behaviour under test only applies on Windows.
    [Platform(Include = "Win")]
    [TestCase(@"C:\dev\repo")]
    [TestCase(@"C:\dev\repo\")]
    [TestCase(@"C:\dev\repo/")]
    [TestCase(@"C:\dev\c#\repo\")]
    public void Resolve_combines_paths(string? workingDir)
    {
        FullPathResolver resolver = new(() => workingDir!);
        resolver.Resolve("file.txt").Should().Be(Path.Combine(workingDir!, "file.txt").Replace("/", "\\"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public void Resolve_does_not_throw_on_invalid_workingDir(string? workingDir)
    {
        FullPathResolver resolver = new(() => workingDir!);

        // Path.Combine already returns the platform's native separator, so ".Replace("/", "\\")"
        // was a no-op on Windows (nothing to replace) and is dropped rather than kept - on POSIX
        // it would have wrongly forced a Windows-shaped backslash path onto the expectation.
        resolver.Resolve("file.txt").Should().Be(Path.Combine(Environment.CurrentDirectory, "file.txt"));
    }
}
