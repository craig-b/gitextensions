namespace GitCommands.Clone;

/// <summary>What the clone dialog's From/To boxes start out with; <see langword="null"/> leaves a box alone.</summary>
public readonly record struct CloneSeed(string? Source, string? Destination);

/// <summary>
///  The clone dialog's source/destination seeding cascade (FormClone's historical rules):
///  a URL argument is the source; an existing-directory argument is the destination; otherwise
///  try the clipboard for a URL, then fall back to the current repository's remote URL (in the
///  hope the new clone is hosted on the same server) with the current repository's parent as
///  destination; a still-empty destination lands on the parent of the current working directory
///  (or the directory itself when it is not a repository).
/// </summary>
public static class CloneSourceSeed
{
    public static CloneSeed Resolve(
        string? urlArgument,
        string? clipboardText,
        string? defaultCloneDestinationPath,
        bool currentRepositoryIsValid,
        string? currentWorkingDir,
        Func<string?> currentRepositorySuggestedSourceUrl,
        Func<string, bool> directoryExists)
    {
        string? source = null;
        string? destination = string.IsNullOrWhiteSpace(defaultCloneDestinationPath) ? null : defaultCloneDestinationPath;

        if (PathUtil.CanBeGitURL(urlArgument))
        {
            source = urlArgument;
        }
        else
        {
            if (!string.IsNullOrEmpty(urlArgument) && directoryExists(urlArgument))
            {
                destination = urlArgument;
            }

            if (clipboardText is not null && CloneUrlExtractor.TryExtractUrl(clipboardText, out string possibleUrl))
            {
                source = possibleUrl;
            }

            if (string.IsNullOrWhiteSpace(source) && currentRepositoryIsValid)
            {
                string? suggestedUrl = currentRepositorySuggestedSourceUrl();
                source = suggestedUrl;

                if (!string.IsNullOrWhiteSpace(suggestedUrl)
                    && string.IsNullOrWhiteSpace(destination)
                    && !string.IsNullOrWhiteSpace(currentWorkingDir))
                {
                    destination = TryGetParent(currentWorkingDir) ?? destination;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(destination) && !string.IsNullOrWhiteSpace(currentWorkingDir))
        {
            if (currentRepositoryIsValid)
            {
                if (Path.GetPathRoot(currentWorkingDir) != currentWorkingDir)
                {
                    destination = TryGetParent(currentWorkingDir) ?? destination;
                }
            }
            else
            {
                destination = currentWorkingDir;
            }
        }

        return new(source, destination);

        static string? TryGetParent(string path)
        {
            try
            {
                return Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar));
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    ///  Which remote's URL to suggest as a clone source: the current branch's remote, else a
    ///  remote literally named "origin" (case-insensitive), else the first remote.
    /// </summary>
    public static string? PickSuggestedRemote(string? currentBranchRemote, IReadOnlyList<string> remoteNames)
    {
        if (!string.IsNullOrEmpty(currentBranchRemote))
        {
            return currentBranchRemote;
        }

        if (remoteNames.Any(name => name.Equals("origin", StringComparison.InvariantCultureIgnoreCase)))
        {
            return "origin";
        }

        return remoteNames.Count > 0 ? remoteNames[0] : null;
    }
}
