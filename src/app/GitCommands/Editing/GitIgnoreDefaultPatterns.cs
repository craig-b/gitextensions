namespace GitCommands.Editing;

/// <summary>
///  The "add default ignore patterns" logic of the .gitignore dialog: the built-in pattern list,
///  its optional user override file, and the pure computation of what to append to the current
///  editor content.
/// </summary>
public static class GitIgnoreDefaultPatterns
{
    /// <summary>
    ///  A user-provided override for <see cref="Patterns"/>; one pattern per line.
    /// </summary>
    public static readonly string DefaultIgnorePatternsFile = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GitExtensions/DefaultIgnorePatterns.txt");

    public static readonly string[] Patterns =
    [
        "#Ignore thumbnails created by Windows",
        "Thumbs.db",
        "#Ignore files built by Visual Studio",
        "*.obj",
        "*.exe",
        "*.pdb",
        "*.user",
        "*.aps",
        "*.pch",
        "*.vspscc",
        "*_i.c",
        "*_p.c",
        "*.ncb",
        "*.suo",
        "*.tlb",
        "*.tlh",
        "*.bak",
        "*.cache",
        "*.ilk",
        "*.log",
        "[Bb]in",
        "[Dd]ebug*/",
        "*.lib",
        "*.sbr",
        "obj/",
        "[Rr]elease*/",
        "_ReSharper*/",
        "[Tt]est[Rr]esult*",
        ".vs/",
        ".idea/",
        "#Nuget packages folder",
        "packages/"
    ];

    /// <summary>
    ///  The effective pattern list: the override file if it exists, the built-in list otherwise.
    /// </summary>
    public static string[] GetEffectivePatterns()
        => File.Exists(DefaultIgnorePatternsFile) ? File.ReadAllLines(DefaultIgnorePatternsFile) : Patterns;

    /// <summary>
    ///  The patterns from <paramref name="patterns"/> not already present as a line of
    ///  <paramref name="currentContent"/>. Empty means nothing to add.
    /// </summary>
    public static string[] GetPatternsToAdd(string currentContent, string[] patterns)
        => [.. patterns.Except(currentContent.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries))];

    /// <summary>
    ///  Appends <paramref name="patternsToAdd"/> to <paramref name="currentContent"/>, producing
    ///  the exact text the dialog has always produced.
    /// </summary>
    public static string Append(string currentContent, string[] patternsToAdd)
        => $"{currentContent}{Environment.NewLine}{string.Join(Environment.NewLine, patternsToAdd)}{Environment.NewLine}";
}
