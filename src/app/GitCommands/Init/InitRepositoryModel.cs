namespace GitCommands.Init;

public enum InitValidation
{
    Ok,

    /// <summary>Blank, unrooted, or not a usable directory path (invalid characters, no permission).</summary>
    NotRootedDirectoryPath,

    /// <summary>The path names an existing file; a repository cannot be initialized on a file.</summary>
    PathIsFile,
}

/// <summary>The init dialog's decisions: the seeded directory, validation, and the git-init options.</summary>
public static class InitRepositoryModel
{
    /// <summary>
    ///  The directory the dialog starts with: the explicit argument, else the current repository's
    ///  working directory, else the default clone destination. (Historically this rule was split
    ///  between the intent handler and the form.)
    /// </summary>
    public static string SeedDirectory(
        string? explicitDirectory,
        bool currentRepositoryIsValid,
        string? currentWorkingDir,
        string? defaultCloneDestinationPath)
    {
        string directory = explicitDirectory ?? (currentRepositoryIsValid ? currentWorkingDir ?? "" : "");
        return string.IsNullOrEmpty(directory) ? defaultCloneDestinationPath ?? "" : directory;
    }

    public static InitValidation Validate(string path, Func<string, bool> fileExists)
    {
        if (!IsRootedDirectoryPath(path))
        {
            return InitValidation.NotRootedDirectoryPath;
        }

        return fileExists(path) ? InitValidation.PathIsFile : InitValidation.Ok;
    }

    public static bool IsRootedDirectoryPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // this is going to throw if it's an invalid path (e.g. contains special chars)
            DirectoryInfo info = new(path);

            return Path.IsPathRooted(path.Trim());
        }
        catch (Exception)
        {
            // The code in the try block is expected to throw when the input is not a valid directory path
            // OR when the user does not have the required permission.
            // In both cases we return "false" since the path is not representing a valid "usable" directory.
            // This is also the reason why we are catching all kind of exception here and not IO-related ones.
            return false;
        }
    }

    /// <summary>One checkbox drives both: a "central" repository is created bare AND shared (<c>--shared=all</c>).</summary>
    public static (bool Bare, bool Shared) Options(bool central) => (central, central);
}
