namespace GitExtUtils.GitUI.Theming;

/// <summary>
///  The WinForms-bound half of the theming extensions.
/// </summary>
/// <remarks>
///  <para>
///   These members were moved out of <see cref="ColorHelper"/> because they were the only ones
///   requiring System.Windows.Forms (<c>Control</c>, <c>ToolStripItem</c>, <c>ButtonBase</c>) or
///   System.Drawing.Common (<c>Bitmap</c>). Everything left in <see cref="ColorHelper"/> operates on
///   <see cref="Color"/>, which is cross-platform.
///  </para>
///  <para>
///   The split is along the line the theming system already implies: the colour model is
///   platform-neutral, and applying colours to widgets is not. Keeping these here means the colour
///   arithmetic can be consumed by a non-WinForms client, and it lets the portability probe cover
///   <c>ColorHelper</c>.
///  </para>
///  <para>
///   Deliberately the same namespace as <see cref="ColorHelper"/>: extension methods resolve by
///   namespace, so all 72 existing call sites compile unchanged.
///  </para>
/// </remarks>
public static class ThemeWinFormsExtensions
{
    /// <summary>
    ///  The default <see cref="ThemeId"/> for the current Windows app colour mode.
    /// </summary>
    /// <remarks>
    ///  Moved from <see cref="ThemeId"/>: it queries <c>Application.SystemColorMode</c>, which is a
    ///  platform capability rather than a property of the identifier.
    /// </remarks>
    public static ThemeId ColorModeThemeId
        => Application.SystemColorMode == SystemColorMode.Dark
            ? ThemeId.DefaultDark
            : ThemeId.DefaultLight;

    /// <summary>
    ///  Maps <see cref="Theme.IsDark"/> onto the WinForms colour mode.
    /// </summary>
    public static SystemColorMode ToSystemColorMode(this Theme theme)
        => theme.IsDark ? SystemColorMode.Dark : SystemColorMode.Classic;

    public static void SetForeColorForBackColor(this Control control)
        => control.ForeColor = control.ForeColor.AdaptForeColor(control.BackColor);

    public static void AdaptImageLightness(this ToolStripItem item) =>
        item.Image = ((Bitmap?)item.Image)?.AdaptLightness();

    public static void AdaptImageLightness(this ButtonBase button) =>
        button.Image = ((Bitmap?)button.Image)?.AdaptLightness();

    public static Bitmap AdaptLightness(this Bitmap original)
    {
        if (ColorHelper.IsDefaultTheme)
        {
            return original;
        }

        Bitmap clone = (Bitmap)original.Clone();
        new LightnessCorrection(clone).Execute();
        return clone;
    }
}
