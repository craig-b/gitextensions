using GitUI.CommandsDialogs.BrowseDialog;

namespace GitUITests.CommandsDialogs.BrowseDialog;

[TestFixture]
public class ForkReleaseTests
{
    [TestCase("wine-v7.3.0.4", true, "7.3.0.4")]
    [TestCase("wine-v7.4.0.1", true, "7.4.0.1")]
    [TestCase("wine-v10.0.0.0", true, "10.0.0.0")]

    // Three components is a version but not a release of this fork; the build number is required.
    [TestCase("wine-v7.3.0", false, null)]

    // The two schemes used before the tag stated the version.
    [TestCase("wine-v7.2.1.7-2", false, null)]
    [TestCase("wine-v3", false, null)]

    [TestCase("v7.3.0.4", false, null)]
    [TestCase("wine-v", false, null)]
    [TestCase("", false, null)]
    [TestCase(null, false, null)]
    public void TryGetVersion_requires_a_whole_four_part_version(string? tag, bool expected, string? expectedVersion)
    {
        bool actual = ForkRelease.TryGetVersion(tag, out Version? version);

        actual.Should().Be(expected);
        version?.ToString().Should().Be(expectedVersion);
    }

    [TestCase("wine-v7.3.0.3", "wine-v7.3.0.4", nameof(UpdateState.UpdateAvailable))]
    [TestCase("wine-v7.3.0.4", "wine-v7.3.0.4", nameof(UpdateState.UpToDate))]

    // Installed ahead of the latest release happens after a release is withdrawn; it is not an update.
    [TestCase("wine-v7.3.0.5", "wine-v7.3.0.4", nameof(UpdateState.UpToDate))]

    // The point of versions over a counter: a new upstream base orders correctly even though the
    // build number starts again from one.
    [TestCase("wine-v7.3.0.9", "wine-v7.4.0.1", nameof(UpdateState.UpdateAvailable))]
    [TestCase("wine-v7.4.0.1", "wine-v7.3.0.9", nameof(UpdateState.UpToDate))]

    // Build numbers are numbers, not strings: 10 is newer than 9.
    [TestCase("wine-v7.3.0.9", "wine-v7.3.0.10", nameof(UpdateState.UpdateAvailable))]

    // No VERSION file: an overlay from refresh.sh, or any copy not installed from a release.
    [TestCase(null, "wine-v7.3.0.4", nameof(UpdateState.NotFromRelease))]

    // Nothing usable came back, so nothing can be said.
    [TestCase("wine-v7.3.0.3", null, nameof(UpdateState.CheckFailed))]
    [TestCase(null, null, nameof(UpdateState.CheckFailed))]
    [TestCase("wine-v7.3.0.3", "v7.2.1", nameof(UpdateState.CheckFailed))]
    public void Decide_compares_versions(string? installed, string? latest, string expected)
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
        ForkRelease.TagUrl("wine-v7.3.0.5").Should().Be($"https://github.com/{repo}/releases/tag/wine-v7.3.0.5");
    }
}
