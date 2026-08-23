using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitCommands.Archive;

public enum ArchiveFormat
{
    Zip,
    Tar,
}

/// <summary>The archive dialog's decisions.</summary>
public static class ArchiveModel
{
    public static string FileExtension(ArchiveFormat format) => format is ArchiveFormat.Zip ? "zip" : "tar";

    /// <summary>The historical suggestion: workdir name + revision, plus a single path filter with dots flattened.</summary>
    public static string SuggestFileName(string workingDirName, string? revision, IReadOnlyList<string> pathFilterLines)
    {
        string suggestion = $"{workingDirName}_{revision}";
        if (pathFilterLines.Count == 1 && !string.IsNullOrWhiteSpace(pathFilterLines[0]))
        {
            suggestion += "_" + pathFilterLines[0].Trim().Replace(".", "_");
        }

        return suggestion;
    }

    /// <summary>Path arguments from the dialog's path-filter lines: non-empty lines quoted and joined.</summary>
    public static string PathArgumentsFromLines(IEnumerable<string> lines)
        => string.Join(" ", lines.Select(line => line.QuoteNE()));

    /// <summary>Path arguments from a revision diff: every changed-but-not-deleted file, quoted and joined.</summary>
    public static string PathArgumentsFromChangedFiles(IEnumerable<GitItemStatus> files)
        => string.Join(" ", files.Where(f => !f.IsDeleted).Select(f => f.Name.QuoteNE()));

    /// <summary>The git-archive command line the dialog historically built with string.Format.</summary>
    public static ArgumentString BuildCommand(ArchiveFormat format, string? revision, string outputFilePath, string pathArguments)
        => new GitArgumentBuilder("archive")
        {
            $"--format=\"{FileExtension(format)}\"",
            revision,
            $"--output \"{outputFilePath}\"",
            { !string.IsNullOrWhiteSpace(pathArguments), pathArguments },
        };
}
