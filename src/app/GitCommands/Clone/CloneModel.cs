using GitExtensions.Extensibility;

namespace GitCommands.Clone;

public enum CloneDestinationState
{
    /// <summary>The destination or subdirectory box is empty or contains invalid path characters.</summary>
    Incomplete,

    /// <summary>The combined directory exists and is not empty.</summary>
    ExistsNotEmpty,

    /// <summary>The combined directory does not exist yet (or exists empty).</summary>
    New,
}

/// <param name="Path">
///  The combined destination path for display; an unfilled half is rendered as its
///  bracketed placeholder caption.
/// </param>
public readonly record struct CloneDestinationPreview(string Path, CloneDestinationState State);

public enum CloneValidation
{
    Ok,
    DestinationMissing,
    DestinationNotRooted,
}

/// <summary>The clone dialog's decisions that are not argument construction (<c>Commands.Clone</c> owns that).</summary>
public static class CloneModel
{
    /// <summary>The live "your repository will be cloned to ..." preview.</summary>
    public static CloneDestinationPreview EvaluateDestination(
        string? destination,
        string? subDirectory,
        string destinationPlaceholder,
        string subDirectoryPlaceholder,
        Func<string, bool> directoryExistsNotEmpty)
    {
        bool destinationUnfilled = string.IsNullOrEmpty(destination) || destination.IndexOfAny(Delimiters.InvalidPathCharsSearchValues) >= 0;
        bool subDirectoryUnfilled = string.IsNullOrEmpty(subDirectory) || subDirectory.IndexOfAny(Delimiters.InvalidPathCharsSearchValues) >= 0;

        string destinationDirectory = destinationUnfilled ? $"[{destinationPlaceholder}]" : destination!;
        string destinationSubDirectory = subDirectoryUnfilled ? $"[{subDirectoryPlaceholder}]" : subDirectory!;
        string path = Path.Combine(destinationDirectory, destinationSubDirectory);

        CloneDestinationState state = destinationUnfilled || subDirectoryUnfilled
            ? CloneDestinationState.Incomplete
            : directoryExistsNotEmpty(path)
                ? CloneDestinationState.ExistsNotEmpty
                : CloneDestinationState.New;

        return new(path, state);
    }

    public static CloneValidation Validate(string? destination)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return CloneValidation.DestinationMissing;
        }

        return Path.IsPathRooted(destination) ? CloneValidation.Ok : CloneValidation.DestinationNotRooted;
    }

    /// <summary>The directory the repository is cloned into; throws if the path is invalid.</summary>
    public static string ResolveTargetDirectory(string destination, string subDirectory)
        => PathUtil.Resolve(Path.Combine(destination, subDirectory));

    /// <summary>
    ///  Shallow clone parameters. Single branch considerations: if neither depth nor a
    ///  single-branch family param is specified, it's like no-single-branch by default; if depth
    ///  is specified, single-branch is assumed. But with single-branch it's really nontrivial to
    ///  switch to another branch in the GUI, and it's very hard in cmdline (obvious choices to
    ///  fetch another branch lead to local repo corruption). So reset it to no-single-branch to
    ///  (a) have the same branches behavior as with full clone, and (b) make it easier for users
    ///  when switching branches.
    /// </summary>
    public static (int? Depth, bool? IsSingleBranch) ShallowOptions(bool downloadFullHistory)
        => downloadFullHistory ? (null, null) : (1, false);
}
