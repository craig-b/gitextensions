using GitUI.CommandsDialogs.BrowseDialog;

namespace GitUITests.CommandsDialogs.BrowseDialog;

[TestFixture]
public class ForkReleaseTests
{
    [TestCase("wine-v1", true, 1)]
    [TestCase("wine-v12", true, 12)]
    [TestCase("wine-v0", true, 0)]

    // The scheme before the build number became the whole tag; it must not be mistaken for one.
    [TestCase("wine-v7.2.1.7-2", false, 0)]
    [TestCase("v3.4.5", false, 0)]
    [TestCase("wine-v", false, 0)]
    [TestCase("wine-vx", false, 0)]
    [TestCase("", false, 0)]
    [TestCase(null, false, 0)]
    public void TryGetBuild_reads_only_a_bare_build_number(string? tag, bool expected, int expectedBuild)
    {
        bool actual = ForkRelease.TryGetBuild(tag, out int build);

        actual.Should().Be(expected);
        build.Should().Be(expectedBuild);
    }

    [TestCase("wine-v3", "wine-v4", nameof(UpdateState.UpdateAvailable))]
    [TestCase("wine-v3", "wine-v3", nameof(UpdateState.UpToDate))]

    // Installed ahead of the latest release happens after a release is withdrawn; it is not an update.
    [TestCase("wine-v4", "wine-v3", nameof(UpdateState.UpToDate))]

    // No VERSION file: an overlay from refresh.sh, or any copy not installed from a release.
    [TestCase(null, "wine-v4", nameof(UpdateState.NotFromRelease))]

    // Nothing usable came back, so nothing can be said.
    [TestCase("wine-v3", null, nameof(UpdateState.CheckFailed))]
    [TestCase(null, null, nameof(UpdateState.CheckFailed))]

    // A repository whose newest release is not one of ours.
    [TestCase("wine-v3", "v7.2.1", nameof(UpdateState.CheckFailed))]

    // Build numbers are integers, not strings: 10 is newer than 9.
    [TestCase("wine-v9", "wine-v10", nameof(UpdateState.UpdateAvailable))]
    [TestCase("wine-v10", "wine-v9", nameof(UpdateState.UpToDate))]
    public void Decide_compares_build_numbers(string? installed, string? latest, string expected)
    {
        // UpdateState is internal, so the expectation travels as its name rather than widening the
        // enum's accessibility for the sake of a test signature.
        ForkRelease.Decide(installed, latest).ToString().Should().Be(expected);
    }

    [Test]
    public void Urls_are_built_from_the_configured_repository()
    {
        string repo = ForkRelease.Repo;

        ForkRelease.ReleasesUrl.Should().Be($"https://github.com/{repo}/releases");
        ForkRelease.TagUrl("wine-v5").Should().Be($"https://github.com/{repo}/releases/tag/wine-v5");
    }
}
