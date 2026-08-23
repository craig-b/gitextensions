using GitCommands.Config;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Conflicts;

/// <summary>A conflict's side, numbered exactly like "checkout-index --stage=N".</summary>
public enum ConflictSide
{
    Base = 1,
    Local = 2,
    Remote = 3,
}

public enum ConflictKind
{
    ChangedBothSides,
    AddedBothSides,
    DeletedLocallyModifiedRemotely,
    ModifiedLocallyDeletedRemotely,
    Unknown,
}

/// <summary>What a resolution choice does to the file.</summary>
public enum ConflictOutcome
{
    TakeLocal,
    TakeRemote,
    TakeBase,
    DeleteFile,
}

public static class ConflictClassifier
{
    /// <summary>The dialog's (base, local, remote)-existence switch.</summary>
    public static ConflictKind Classify(in ConflictData conflict)
        => (Exists(conflict.Base.Filename), Exists(conflict.Local.Filename), Exists(conflict.Remote.Filename)) switch
        {
            (true, true, true) => ConflictKind.ChangedBothSides,
            (false, true, true) => ConflictKind.AddedBothSides,
            (true, false, true) => ConflictKind.DeletedLocallyModifiedRemotely,
            (true, true, false) => ConflictKind.ModifiedLocallyDeletedRemotely,
            _ => ConflictKind.Unknown,
        };

    /// <summary>
    ///  The mergetool orchestration buckets, each reversed so prompts show the grid's order
    ///  (the historical Insert(0, ...)).
    /// </summary>
    public static ConflictSelectionBuckets Bucket(IReadOnlyList<ConflictData> conflicts)
    {
        List<ConflictData> deletedLocally = [];
        List<ConflictData> deletedRemotely = [];
        List<ConflictData> remaining = [];

        foreach (ConflictData conflict in conflicts)
        {
            if (!Exists(conflict.Local.Filename) && Exists(conflict.Remote.Filename))
            {
                deletedLocally.Insert(0, conflict);
            }
            else if (Exists(conflict.Local.Filename) && !Exists(conflict.Remote.Filename))
            {
                deletedRemotely.Insert(0, conflict);
            }
            else
            {
                remaining.Insert(0, conflict);
            }
        }

        return new ConflictSelectionBuckets(deletedLocally, deletedRemotely, remaining);
    }

    private static bool Exists(string? filename) => !string.IsNullOrEmpty(filename);
}

public sealed record ConflictSelectionBuckets(
    IReadOnlyList<ConflictData> DeletedLocallyModifiedRemotely,
    IReadOnlyList<ConflictData> ModifiedLocallyDeletedRemotely,
    IReadOnlyList<ConflictData> Remaining);

/// <summary>
///  Rebase inverts the ours/theirs labels but never the stage numbers: during a rebase the
///  local stage carries "theirs" and the remote stage "ours".
/// </summary>
public readonly record struct ConflictSideLabels(string LocalLabel, string RemoteLabel)
{
    public static ConflictSideLabels Resolve(bool inTheMiddleOfRebase, string ours, string theirs)
        => inTheMiddleOfRebase
            ? new ConflictSideLabels(LocalLabel: theirs, RemoteLabel: ours)
            : new ConflictSideLabels(LocalLabel: ours, RemoteLabel: theirs);
}

/// <summary>What happens when the last conflict is resolved.</summary>
public readonly record struct ConflictCompletionDecision(bool ShouldUpdateSubmodules, bool ShouldOfferCommit, bool ShouldClose)
{
    /// <summary>
    ///  Only fires when conflicts existed and are now gone; the commit offer is suppressed
    ///  mid-rebase and mid-patch (the caller drives continuation there).
    /// </summary>
    public static ConflictCompletionDecision Evaluate(
        bool stillConflicted,
        bool thereWereConflicts,
        bool inTheMiddleOfPatch,
        bool inTheMiddleOfRebase,
        bool offerCommit)
    {
        if (stillConflicted || !thereWereConflicts)
        {
            return new ConflictCompletionDecision(ShouldUpdateSubmodules: false, ShouldOfferCommit: false, ShouldClose: false);
        }

        return new ConflictCompletionDecision(
            ShouldUpdateSubmodules: true,
            ShouldOfferCommit: !inTheMiddleOfPatch && !inTheMiddleOfRebase && offerCommit,
            ShouldClose: true);
    }
}

/// <summary>The resolved mergetool: which tool, and whether GE can launch it directly.</summary>
public sealed record MergeToolConfiguration(string? Tool, string? Command, string? Path)
{
    public bool SupportsDirectLaunch => !string.IsNullOrWhiteSpace(Command) && !string.IsNullOrWhiteSpace(Path);

    /// <summary>
    ///  The dialog's discovery: merge.guitool (when supported) else merge.tool, then the
    ///  tool's cmd/path, the kdiff3 back-compat defaults, and the Windows-only ".exe" split
    ///  separating the executable from its arguments.
    /// </summary>
    public static MergeToolConfiguration Resolve(Func<string, string?> getConfigValue, bool supportsGuiMergeTool, bool isWindows)
    {
        string? mergetool = supportsGuiMergeTool ? getConfigValue(SettingKeyString.MergeToolKey) : null;
        if (string.IsNullOrEmpty(mergetool))
        {
            mergetool = getConfigValue(SettingKeyString.MergeToolNoGuiKey);
        }

        if (string.IsNullOrEmpty(mergetool))
        {
            return new MergeToolConfiguration(Tool: null, Command: null, Path: null);
        }

        string? command = getConfigValue($"mergetool.{mergetool}.cmd");
        string? path = getConfigValue($"mergetool.{mergetool}.path");

        // Temporary compatibility with GE <3.3
        if (mergetool == "kdiff3")
        {
            if (string.IsNullOrEmpty(path))
            {
                path = "kdiff3";
            }

            if (string.IsNullOrEmpty(command))
            {
                command = "\"$BASE\" \"$LOCAL\" \"$REMOTE\" -o \"$MERGED\"";
            }
        }

        if (isWindows && command is not null)
        {
            // This only works when on Windows....
            const string executablePattern = ".exe";
            int idx = command.IndexOf(executablePattern);
            if (idx >= 0)
            {
                path = command[..(idx + executablePattern.Length + 1)].Trim('"', ' ');
                command = command[(idx + executablePattern.Length + 1)..];
            }
        }

        return new MergeToolConfiguration(mergetool, command, path);
    }
}

public static class MergeToolArguments
{
    /// <summary>The $BASE/$LOCAL/$REMOTE/$MERGED substitution.</summary>
    public static string Substitute(string command, string? baseFile, string? localFile, string? remoteFile, string mergedFile)
        => command
            .Replace("$BASE", baseFile)
            .Replace("$LOCAL", localFile)
            .Replace("$REMOTE", remoteFile)
            .Replace("$MERGED", mergedFile);

    /// <summary>The per-tool 2-way rewriting used when the conflict has no base.</summary>
    public static string To2Way(string tool, string arguments)
        => tool.ToLowerInvariant() switch
        {
            "kdiff3" or "diffmerge" or "smerge" => arguments.Replace("\"$BASE\"", ""),
            "tortoisemerge" => arguments
                .Replace("-base:\"$BASE\"", "").Replace("/base:\"$BASE\"", "")
                .Replace("mine:\"$LOCAL\"", "base:\"$LOCAL\""),
            _ => arguments,
        };
}

/// <summary>
///  The exit-code × file-changed success matrix: a clean exit with a touched file stages;
///  the ambiguous combinations (failed-but-touched, clean-but-untouched) ask the user.
/// </summary>
public readonly record struct MergeToolResultDecision(bool ShouldStage, bool ShouldAskUser)
{
    public static MergeToolResultDecision Evaluate(int exitCode, bool exitedSuccessfully, bool fileChanged)
        => new(
            ShouldStage: exitedSuccessfully && fileChanged,
            ShouldAskUser: (exitCode == 1 && fileChanged) || (exitCode == 0 && !fileChanged));
}

/// <summary>Which outcomes each conflict kind offers (the three TaskDialog buttons).</summary>
public static class ConflictResolutionChoices
{
    public static IReadOnlyList<ConflictOutcome> For(ConflictKind kind)
        => kind switch
        {
            ConflictKind.AddedBothSides =>
                [ConflictOutcome.TakeLocal, ConflictOutcome.TakeRemote, ConflictOutcome.DeleteFile],
            ConflictKind.DeletedLocallyModifiedRemotely =>
                [ConflictOutcome.DeleteFile, ConflictOutcome.TakeRemote, ConflictOutcome.TakeBase],
            ConflictKind.ModifiedLocallyDeletedRemotely =>
                [ConflictOutcome.TakeLocal, ConflictOutcome.DeleteFile, ConflictOutcome.TakeBase],
            _ =>
                [ConflictOutcome.TakeLocal, ConflictOutcome.TakeRemote, ConflictOutcome.TakeBase],
        };
}
