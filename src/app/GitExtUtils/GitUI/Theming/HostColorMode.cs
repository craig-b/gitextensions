namespace GitExtUtils.GitUI.Theming;

/// <summary>
///  Portable stand-in for <c>System.Windows.Forms.Application.IsDarkModeEnabled</c>: whether the
///  host application runs in a dark color mode. Installed by the WinForms host — ThemeModule.Load
///  sets it immediately after Application.SetColorMode, the only place the color mode changes.
///  Defaults to light mode, which is also what Application.IsDarkModeEnabled reports before
///  SetColorMode is called.
/// </summary>
public static class HostColorMode
{
    public static bool IsDark { get; set; }
}
