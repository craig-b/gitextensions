using GitExtUtils.GitUI.Theming;
using GitUI.Theming;

namespace GitCommandsTests.Editor.Diff;

/// <summary>
///  Installs the default light theme for every fixture in this namespace: the expected colors in
///  these tests (the AnsiEscapeUtilitiesTestBase palette, SystemColors.Window backgrounds, the
///  colors FixGitTerminalColors substitutes) are all written against
///  <see cref="ThemeSettings.Default"/> - the built-in light theme from AppColorDefaults, which is
///  also what these classes see in production before ThemeModule.Load has installed a parsed
///  theme. Installing it explicitly keeps the fixtures order-independent of any other test that
///  changes the process-wide theme statics.
/// </summary>
[SetUpFixture]
public class DefaultLightThemeSetupFixture
{
    [OneTimeSetUp]
    public void InstallDefaultLightTheme()
    {
        AppColorExtension.ThemeSettings = ThemeSettings.Default;
        ColorHelper.ThemeSettings = ThemeSettings.Default;
    }
}
