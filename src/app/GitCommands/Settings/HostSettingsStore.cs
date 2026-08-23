namespace GitCommands.Settings;

/// <summary>
///  Host decision the portable core cannot make for itself, set before any
///  <see cref="AppSettings"/> access: which format the app's own settings store uses.
///  The WinForms app stays on the XML dictionary file; the Avalonia client opts into
///  the git-config-style INI store with its own file name — the two stores never share
///  a file, and the client reads no XML (importing WinForms settings is an explicit
///  user action, not infrastructure).
/// </summary>
public static class HostSettingsStore
{
    public static bool UseIniStore { get; set; }

    internal static GitExtSettingsCache CreateCache(string settingsFilePath, bool autoSave)
        => UseIniStore
            ? new IniSettingsCache(settingsFilePath, autoSave)
            : new GitExtSettingsCache(settingsFilePath, autoSave);
}
