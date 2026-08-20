using GitExtensions.Extensibility;
using GitExtUtils;

namespace GitCommands.FileHistory;

/// <summary>
///  The file-history path-filter construction - the
///  "--follow does not work with --graph" workaround: collect every historical name of the
///  file first, then feed all names as the real path filter.
/// </summary>
public static class FileHistoryPathFilter
{
    /// <summary>Windows commands cap at 32267 characters; git-log itself is ~200.</summary>
    public const int MaxPathFilterLength = 31000;

    /// <summary>
    ///  The historical quoting heuristic: an unquoted spaceless path is quoted; an unquoted
    ///  path with a space - or more than two quote characters - is treated as multiple
    ///  arguments (which --follow cannot take).
    /// </summary>
    public static (string Path, bool MultipleArgs) NormalizeArgument(string path)
    {
        path = path.Trim();
        bool multipleArgs = false;
        if (!path.Any(c => c == '"') && !path.Any(c => c == '\''))
        {
            if (!path.Any(c => c == ' '))
            {
                path = path.Quote();
            }
            else
            {
                multipleArgs = true;
            }
        }
        else if (path.Count(c => c == '"') + path.Count(c => c == '\'') > 2)
        {
            // Basic detection of multiple quoted strings (let the Git command fail for more advanced usage)
            multipleArgs = true;
        }

        return (path, multipleArgs);
    }

    /// <summary>
    ///  Whether the historical-name collection pass runs at all: follow must be on, the path
    ///  must not be a folder (the command line can get very long), and --follow accepts
    ///  exactly one argument.
    /// </summary>
    public static bool ShouldCollectHistoricalNames(string normalizedPath, bool multipleArgs, bool followRenames)
        => followRenames
            && !normalizedPath.EndsWith('/')
            && !normalizedPath.EndsWith("/\"")
            && !multipleArgs;

    public static ArgumentString FindRenamesAndCopiesOptions(bool exactOnly)
        => exactOnly
            ? " --find-renames=\"100%\" --find-copies=\"100%\""
            : " --find-renames --find-copies";

    /// <summary>The name-collection command: one line per historical filename, commits prefixed.</summary>
    public static GitArgumentBuilder FollowNamesCommand(string normalizedPath, string objectIdPrefix, bool exactOnly)
        => new GitArgumentBuilder("log")
        {
            // --name-only will list each filename on a separate line, ending with an empty line
            $"--format=\"{objectIdPrefix}%H\"",
            "--name-only",
            "--follow",
            FindRenamesAndCopiesOptions(exactOnly),
            "--",
            normalizedPath.QuoteIfNotQuotedAndNE()
        };

    /// <summary>
    ///  Joins the collected names into the real path filter. Falls back to the plain path
    ///  when the set is empty (also occurs when Git saw multiple path arguments) or the
    ///  joined filter exceeds <see cref="MaxPathFilterLength"/>.
    /// </summary>
    public static (string Filter, bool TooLong) Combine(string normalizedPath, IReadOnlyCollection<string?> historicalNames)
    {
        if (historicalNames.Count == 0)
        {
            return (normalizedPath, TooLong: false);
        }

        string pathFilter = string.Join("", historicalNames.Select(name => @$" ""{name}"""));
        return pathFilter.Length > MaxPathFilterLength
            ? (normalizedPath, TooLong: true)
            : (pathFilter, TooLong: false);
    }
}

public enum FileHistoryTab
{
    CommitInfo,
    Diff,
    View,
    Blame,
}

/// <summary>Which tabs a selected revision can show, and which tab wins when the current one vanishes.</summary>
public sealed record FileHistoryTabDecision(
    bool ShowCommitInfo,
    bool ShowDiff,
    bool ShowView,
    bool ShowBlame,
    FileHistoryTab? PreferredTab,
    bool FileAvailable)
{
    /// <summary>
    ///  Artificial revisions have no commit info and prefer the diff; an unavailable file
    ///  (or a folder) loses the diff and prefers commit info; view and blame need a real,
    ///  available file - blame additionally needs support (not a submodule).
    /// </summary>
    public static FileHistoryTabDecision Resolve(bool isArtificial, bool isFolder, bool fileExists, bool blameSupported)
    {
        bool fileAvailable = !isFolder && fileExists;
        return new FileHistoryTabDecision(
            ShowCommitInfo: !isArtificial,
            ShowDiff: fileAvailable,
            ShowView: !isArtificial && fileAvailable,
            ShowBlame: !isArtificial && fileAvailable && blameSupported,
            PreferredTab: !fileAvailable ? FileHistoryTab.CommitInfo : isArtificial ? FileHistoryTab.Diff : null,
            FileAvailable: fileAvailable);
    }
}

/// <summary>The file-history window's startup decisions.</summary>
public static class FileHistoryStartup
{
    /// <summary>The dialog's historical input normalization.</summary>
    public static string NormalizeFileName(string fileName) => fileName.RemoveQuotes().ToPosixPath();

    /// <summary>The grid loads immediately only when a load-on-show setting asks for it.</summary>
    public static bool ShouldAutoLoad(bool blameTabSelected, bool loadBlameOnShow, bool loadHistoryOnShow)
        => (blameTabSelected && loadBlameOnShow) || loadHistoryOnShow;

    public static FileHistoryTab InitialTab(bool blameTabExists, bool showBlame)
        => blameTabExists && showBlame ? FileHistoryTab.Blame : FileHistoryTab.Diff;

    /// <summary>"&lt;file&gt;[ (&lt;resolved&gt;)] - ..." - the resolved name only when it differs.</summary>
    public static string BuildTitle(string fileName, string? resolvedFileName)
        => fileName + (!string.IsNullOrEmpty(resolvedFileName) && resolvedFileName != fileName ? $" ({resolvedFileName})" : "");
}
