using GitExtensions.Extensibility;

namespace GitCommands.FileStatus;

/// <summary>Text normalization for the file list's filter box.</summary>
public static class FileStatusFilterText
{
    /// <summary>
    ///  Strips the repository working directory from pasted absolute paths, so filtering by
    ///  a copied full path matches the repo-relative names. Returns null when nothing to strip.
    /// </summary>
    public static string? StripWorkingDirPrefix(string filterText, string workingDir)
    {
        if (filterText.Length <= workingDir.Length)
        {
            return null;
        }

        string posixWorkingDir = PathUtil.ToPosixPath(workingDir);
        string posixFilterText = PathUtil.ToPosixPath(filterText);
        return posixFilterText.StartsWith(posixWorkingDir, StringComparison.InvariantCultureIgnoreCase)
            ? posixFilterText.SubstringAfter(posixWorkingDir, StringComparison.InvariantCultureIgnoreCase)
            : null;
    }
}
