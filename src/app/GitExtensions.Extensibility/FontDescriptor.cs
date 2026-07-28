namespace GitExtensions.Extensibility;

/// <summary>
///  A font as configuration data, independent of any UI toolkit.
/// </summary>
/// <remarks>
///  <para>
///   This exists because <c>System.Drawing.Font</c> is Windows-only, and it was the single type
///   standing between the settings layer and a platform-neutral core. <c>SettingsSource</c> exposed
///   <c>Font</c>, which made <c>IGitModule</c> unusable off Windows, which in turn blocked most of
///   <c>GitCommands</c>.
///  </para>
///  <para>
///   The persisted format is unchanged: <see cref="FontParser"/> already serialised to
///   <c>family;size;_IC_;bold;italic</c>, which was always toolkit-neutral. Only the in-memory type
///   was the problem, so existing stored settings keep working.
///  </para>
/// </remarks>
/// <param name="FamilyName">Font family name, e.g. <c>Consolas</c>.</param>
/// <param name="Size">Size in points.</param>
public sealed record FontDescriptor(string FamilyName, float Size, bool Bold = false, bool Italic = false)
{
    /// <summary>
    ///  A monospaced default that does not depend on the host toolkit resolving a system font.
    /// </summary>
    /// <remarks>
    ///  The previous defaults used <c>SystemFonts.MessageBoxFont</c>, which throws off Windows.
    ///  Callers that want the platform's UI font should resolve it in the UI layer, where the
    ///  toolkit can answer that question.
    /// </remarks>
    public static FontDescriptor DefaultMonospace { get; } = new("Consolas", 10f);

    public override string ToString()
        => $"{FamilyName}, {Size}pt{(Bold ? ", Bold" : "")}{(Italic ? ", Italic" : "")}";
}
