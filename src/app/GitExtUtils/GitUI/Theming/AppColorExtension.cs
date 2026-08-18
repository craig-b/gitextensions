using GitExtUtils.GitUI.Theming;

namespace GitUI.Theming;

public static class AppColorExtension
{
    /// <summary>
    ///  The active theme settings, installed by the host (ThemeModule.Load in GitUI) at startup.
    ///  Defaults to <see cref="ThemeSettings.Default"/>, i.e. the invariant light theme.
    /// </summary>
    public static ThemeSettings ThemeSettings { private get; set; } = ThemeSettings.Default;

    public static Color GetThemeColor(this AppColor name)
    {
        Color themeColor = ThemeSettings.Theme.GetColor(name);

        return themeColor is { IsEmpty: false }
            ? themeColor
            : ThemeSettings.InvariantTheme.GetColor(name);
    }
}
