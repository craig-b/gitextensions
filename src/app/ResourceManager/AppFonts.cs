using GitCommands;
using GitExtensions.Extensibility;

namespace ResourceManager;

/// <summary>
///  The WinForms view of the configured fonts.
/// </summary>
/// <remarks>
///  <para>
///   <see cref="AppSettings"/> stores fonts as <see cref="FontDescriptor"/>, because
///   <c>System.Drawing.Font</c> is Windows-only and having it on the settings surface made the whole
///   engine unusable off Windows. This type does the conversion for code that still needs a real
///   <c>Font</c>.
///  </para>
///  <para>
///   It lives in ResourceManager rather than GitExtUtils because it needs <see cref="AppSettings"/>,
///   and GitExtUtils sits below GitCommands in the dependency order.
///  </para>
/// </remarks>
public static class AppFonts
{
    public static Font FixedWidth => AppSettings.FixedWidthFont.ToFont();
    public static Font Commit => AppSettings.CommitFont.ToFont();
    public static Font Monospace => AppSettings.MonospaceFont.ToFont();
    public static Font App => AppSettings.Font.ToFont();
    public static Font? ConEmuConsole => AppSettings.ConEmuConsoleFont?.ToFont();

    /// <summary>
    ///  Realises a <see cref="FontDescriptor"/> as a WinForms <see cref="Font"/>.
    /// </summary>
    /// <remarks>
    ///  Falls back to the system UI font if the family is unavailable. <c>new Font(family, ...)</c>
    ///  silently substitutes a default for an unknown family rather than throwing, but an invalid
    ///  size does throw, so that is guarded.
    /// </remarks>
    public static Font ToFont(this FontDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        FontStyle style = (descriptor.Bold ? FontStyle.Bold : FontStyle.Regular)
                        | (descriptor.Italic ? FontStyle.Italic : FontStyle.Regular);
        try
        {
            return new Font(descriptor.FamilyName, descriptor.Size, style);
        }
        catch (ArgumentException)
        {
            return SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
        }
    }

    /// <summary>
    ///  Captures a WinForms <see cref="Font"/> as storable configuration.
    /// </summary>
    public static FontDescriptor ToDescriptor(this Font font)
    {
        ArgumentNullException.ThrowIfNull(font);
        return new FontDescriptor(font.FontFamily.Name, font.Size, font.Bold, font.Italic);
    }
}
