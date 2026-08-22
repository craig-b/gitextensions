using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;

namespace GitCommands.Refs;

public enum BatchRefKind
{
    LocalBranch,
    RemoteBranch,
    Tag,
}

public enum BatchRefVerb
{
    Delete,
    Push,
}

public enum BatchRefOutcome
{
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>One ref in the batch dialog: the checkbox row with its observability columns.</summary>
public sealed record BatchRefRow(
    string Name,
    BatchRefKind Kind,
    ObjectId? ObjectId,
    bool IsCurrent,
    string? Upstream,
    string? Subject,
    bool? MergedIntoCurrent);

public sealed record BatchRefRowResult(string Name, BatchRefOutcome Outcome, string? Message = null);

public enum BatchRefCommandKind
{
    DeleteBranches,
    DeleteTags,
    DeleteRemoteCounterparts,
    Push,
}

/// <summary>One git invocation of a batch run and the row names it covers.</summary>
public sealed record BatchRefCommand(BatchRefCommandKind Kind, ArgumentString Arguments, IReadOnlyList<string> RowNames);

/// <summary>
///  The batch-ref operations dialog's engine (context-menu redesign, surface 3): seeds rows
///  from one for-each-ref call, decides per-verb applicability (inapplicable checked rows are
///  reported skipped, never errors), arms destructive runs behind the force gate, builds
///  git-native list commands, and maps their output back to per-row outcomes.
/// </summary>
public static class BatchRefOperations
{
    private const char FieldSeparator = '\u001f';

    /// <summary>The for-each-ref format behind <see cref="ParseRows"/> (%1f = the unit separator).</summary>
    public const string ForEachRefFormat = "%(refname)%1f%(objectname)%1f%(upstream:short)%1f%(HEAD)%1f%(subject)";

    public static string FullRefName(string name, BatchRefKind kind)
        => kind switch
        {
            BatchRefKind.LocalBranch => $"refs/heads/{name}",
            BatchRefKind.RemoteBranch => $"refs/remotes/{name}",
            _ => $"refs/tags/{name}",
        };

    public static ArgumentString ForEachRefCommand(IEnumerable<string> fullRefNames)
        => new GitArgumentBuilder("for-each-ref")
        {
            $"--format=\"{ForEachRefFormat}\"",
            fullRefNames.Select(name => name.Quote())
        };

    /// <summary>Parses the seed rows out of the for-each-ref output; merged state comes from the caller's scan.</summary>
    public static IReadOnlyList<BatchRefRow> ParseRows(string output, Func<string, bool>? isMergedIntoCurrent = null)
    {
        List<BatchRefRow> rows = [];
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = line.Split(FieldSeparator);
            if (fields.Length < 5)
            {
                continue;
            }

            (string name, BatchRefKind kind) = fields[0] switch
            {
                var refName when refName.StartsWith("refs/heads/") => (refName["refs/heads/".Length..], BatchRefKind.LocalBranch),
                var refName when refName.StartsWith("refs/remotes/") => (refName["refs/remotes/".Length..], BatchRefKind.RemoteBranch),
                var refName when refName.StartsWith("refs/tags/") => (refName["refs/tags/".Length..], BatchRefKind.Tag),
                var refName => (refName, BatchRefKind.LocalBranch),
            };

            rows.Add(new BatchRefRow(
                name,
                kind,
                ObjectId.TryParse(fields[1], out ObjectId objectId) ? objectId : null,
                IsCurrent: fields[3] == "*",
                Upstream: string.IsNullOrEmpty(fields[2]) ? null : fields[2],
                Subject: string.IsNullOrEmpty(fields[4]) ? null : fields[4],
                MergedIntoCurrent: kind is BatchRefKind.LocalBranch ? isMergedIntoCurrent?.Invoke(name) : null));
        }

        return rows;
    }

    /// <summary>Whether the verb can act on the row at all (v1: Delete and Push act on local branches and tags).</summary>
    public static bool Applies(BatchRefVerb verb, BatchRefRow row)
        => verb switch
        {
            BatchRefVerb.Delete => (row.Kind is BatchRefKind.LocalBranch && !row.IsCurrent) || row.Kind is BatchRefKind.Tag,
            _ => row.Kind is BatchRefKind.LocalBranch or BatchRefKind.Tag,
        };

    /// <summary>Splits the checked rows: the verb acts on Applicable, Skipped is reported - mixed selections never error.</summary>
    public static (IReadOnlyList<BatchRefRow> Applicable, IReadOnlyList<BatchRefRow> Skipped) Partition(BatchRefVerb verb, IEnumerable<BatchRefRow> checkedRows)
    {
        List<BatchRefRow> applicable = [];
        List<BatchRefRow> skipped = [];
        foreach (BatchRefRow row in checkedRows)
        {
            (Applies(verb, row) ? applicable : skipped).Add(row);
        }

        return (applicable, skipped);
    }

    /// <summary>The force gate: a delete run arms only after an explicit force toggle when any checked branch is unmerged.</summary>
    public static bool RequiresForceDelete(IEnumerable<BatchRefRow> applicable)
        => applicable.Any(row => row.Kind is BatchRefKind.LocalBranch && row.MergedIntoCurrent is false);

    public static IReadOnlyList<BatchRefCommand> BuildDeleteCommands(IReadOnlyList<BatchRefRow> applicable, bool force, bool deleteRemoteCounterparts)
    {
        List<BatchRefCommand> commands = [];

        List<BatchRefRow> branches = [.. applicable.Where(row => row.Kind is BatchRefKind.LocalBranch)];
        if (branches.Count > 0)
        {
            commands.Add(new BatchRefCommand(
                BatchRefCommandKind.DeleteBranches,
                new GitArgumentBuilder("branch") { force ? "-D" : "-d", branches.Select(row => row.Name.Quote()) },
                [.. branches.Select(row => row.Name)]));
        }

        List<BatchRefRow> tags = [.. applicable.Where(row => row.Kind is BatchRefKind.Tag)];
        if (tags.Count > 0)
        {
            commands.Add(new BatchRefCommand(
                BatchRefCommandKind.DeleteTags,
                new GitArgumentBuilder("tag") { "-d", tags.Select(row => row.Name.Quote()) },
                [.. tags.Select(row => row.Name)]));
        }

        if (deleteRemoteCounterparts)
        {
            // One push per remote, deleting the upstream branch of every branch tracking it.
            foreach (IGrouping<string, (BatchRefRow Row, string UpstreamBranch)> remoteGroup in branches
                .Where(row => row.Upstream is not null && row.Upstream.Contains('/'))
                .Select(row => (Row: row, Upstream: row.Upstream!))
                .Select(pair => (pair.Row, Remote: pair.Upstream[..pair.Upstream.IndexOf('/')], UpstreamBranch: pair.Upstream[(pair.Upstream.IndexOf('/') + 1)..]))
                .GroupBy(pair => pair.Remote, pair => (pair.Row, pair.UpstreamBranch)))
            {
                commands.Add(new BatchRefCommand(
                    BatchRefCommandKind.DeleteRemoteCounterparts,
                    new GitArgumentBuilder("push")
                    {
                        "--porcelain",
                        remoteGroup.Key.Quote(),
                        remoteGroup.Select(pair => $":refs/heads/{pair.UpstreamBranch}".Quote())
                    },
                    [.. remoteGroup.Select(pair => pair.Row.Name)]));
            }
        }

        return commands;
    }

    public static BatchRefCommand BuildPushCommand(IReadOnlyList<BatchRefRow> applicable, string remote, bool forceWithLease)
        => new(
            BatchRefCommandKind.Push,
            new GitArgumentBuilder("push")
            {
                "--porcelain",
                { forceWithLease, "--force-with-lease" },
                remote.Quote(),
                applicable.Select(row => $"{FullRefName(row.Name, row.Kind)}:{FullRefName(row.Name, row.Kind)}".Quote())
            },
            [.. applicable.Select(row => row.Name)]);

    /// <summary>
    ///  Maps a command's output back to per-row outcomes. Local deletes report per name
    ///  ("Deleted branch/tag ..." vs. an error line naming the ref); pushes parse the
    ///  --porcelain per-refspec status lines.
    /// </summary>
    public static IReadOnlyList<BatchRefRowResult> ParseResults(BatchRefCommand command, bool success, string output)
        => command.Kind switch
        {
            BatchRefCommandKind.DeleteBranches => ParseDeleteOutput(command, success, output, "Deleted branch"),
            BatchRefCommandKind.DeleteTags => ParseDeleteOutput(command, success, output, "Deleted tag"),
            _ => ParsePushPorcelain(command, success, output),
        };

    private static IReadOnlyList<BatchRefRowResult> ParseDeleteOutput(BatchRefCommand command, bool success, string output, string deletedPrefix)
    {
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return [.. command.RowNames.Select(name =>
        {
            // "Deleted branch feature/x (was abc123)." / "Deleted tag 'v1' (was abc123)"
            if (lines.Any(line => line.StartsWith($"{deletedPrefix} {name} ") || line.StartsWith($"{deletedPrefix} '{name}' ")))
            {
                return new BatchRefRowResult(name, BatchRefOutcome.Succeeded);
            }

            string? errorLine = lines.FirstOrDefault(line => line.Contains($"'{name}'") && line.Contains("error", StringComparison.OrdinalIgnoreCase));
            return errorLine is not null
                ? new BatchRefRowResult(name, BatchRefOutcome.Failed, errorLine)
                : new BatchRefRowResult(name, success ? BatchRefOutcome.Succeeded : BatchRefOutcome.Failed, success ? null : output.Trim());
        })];
    }

    private static IReadOnlyList<BatchRefRowResult> ParsePushPorcelain(BatchRefCommand command, bool success, string output)
    {
        // Porcelain: "<flag>\t<from>:<to>\t<summary>"; '!' = rejected. Counterpart deletes
        // carry the UPSTREAM ref in <to>, so match rows by position, not name.
        string[] statusLines = [.. output.Split('\n')
            .Where(line => line.Contains('\t') && line.Split('\t').Length >= 3)];

        if (command.Kind is BatchRefCommandKind.DeleteRemoteCounterparts && statusLines.Length == command.RowNames.Count)
        {
            return [.. command.RowNames.Select((name, index) => ToResult(name, statusLines[index]))];
        }

        return [.. command.RowNames.Select(name =>
        {
            string? line = statusLines.FirstOrDefault(candidate =>
            {
                string destination = candidate.Split('\t')[1].Split(':').Last();
                return destination.EndsWith($"/{name}") || destination == name;
            });

            return line is not null
                ? ToResult(name, line)
                : new BatchRefRowResult(name, success ? BatchRefOutcome.Succeeded : BatchRefOutcome.Failed, success ? null : output.Trim());
        })];

        static BatchRefRowResult ToResult(string name, string line)
        {
            string[] fields = line.Split('\t');
            string summary = fields.Length >= 3 ? fields[2] : "";
            return line.StartsWith('!')
                ? new BatchRefRowResult(name, BatchRefOutcome.Failed, summary)
                : new BatchRefRowResult(name, BatchRefOutcome.Succeeded, summary);
        }
    }
}
