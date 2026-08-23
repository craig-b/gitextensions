using System.Windows.Forms;
using GitCommands;
using GitExtensions.Extensibility;

namespace GitCommandsTests.Settings;

/// <summary>
///  Proves the portable defaults reproduce the WinForms values on Windows — the byte-identical
///  guarantee M1.5 requires. The hosts additionally supply the WinForms values directly at
///  startup, so the app itself never depends on this replication.
/// </summary>
[Platform(Include = "Win")]
internal sealed class AppPathsTests
{
    [Test]
    public void ProductVersion_default_matches_WinForms()
    {
        AppPaths.ProductVersion.Should().Be(Application.ProductVersion);
    }

    [Test]
    public void ApplicationExecutablePath_default_matches_WinForms()
    {
        AppPaths.ApplicationExecutablePath.Should().Be(Application.ExecutablePath);
    }

    [Test]
    public void UserAppDataPath_default_matches_WinForms()
    {
        AppPaths.GetUserAppDataPath().Should().Be(Application.UserAppDataPath);
    }

    [Test]
    public void ApplicationDataPath_is_byte_identical_to_the_pre_refactor_formula()
    {
        if (AppSettings.IsPortable())
        {
            Assert.Inconclusive("Not applicable in a portable installation.");
        }

        AppSettings.ApplicationDataPath.Value.Should().Be(
            Application.UserAppDataPath.Replace(Application.ProductVersion, string.Empty)
                                       .Replace("Git Extensions", "GitExtensions"));
    }
}
