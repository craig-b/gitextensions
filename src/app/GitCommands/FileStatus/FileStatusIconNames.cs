namespace GitCommands.FileStatus;

/// <summary>
///  The icon-name keys the portable diff calculator attaches to its groups; the view maps each
///  key to an actual image (FileStatusList's state-image dictionary). The values are the
///  historical WinForms resource names - they are dictionary keys, not resource lookups.
/// </summary>
public static class FileStatusIconNames
{
    public const string Diff = "Diff";
    public const string DiffA = "DiffA";
    public const string DiffB = "DiffB";
    public const string DiffC = "DiffC";
    public const string DiffR = "DiffR";
}
