using GitCommands;
using GitCommands.Config;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtUtils;
using GitUIPluginInterfaces;
using ResourceManager;

namespace GitUI.UserControls.RevisionGrid;

/// <summary>
///  The two artificial rows the grid shows above HEAD: the working tree (parent: index) and
///  the index (parent: HEAD). Built once per refresh; the grid inserts them just before the
///  checked-out revision, or via <see cref="Graph.RevisionGraph.Insert"/> when HEAD is not
///  in the loaded set.
/// </summary>
public sealed record ArtificialCommits(GitRevision WorkTree, GitRevision Index)
{
    public static ArtificialCommits Create(string userName, string userEmail, ObjectId currentCheckout)
    {
        GitRevision workTreeRev = new(ObjectId.WorkTreeId)
        {
            Author = userName,
            AuthorUnixTime = 0,
            AuthorEmail = userEmail,
            Committer = userName,
            CommitUnixTime = 0,
            CommitterEmail = userEmail,
            Subject = TranslatedStrings.Workspace,
            ParentIds = new[] { ObjectId.IndexId },
            Notes = ""
        };
        GitRevision indexRev = new(ObjectId.IndexId)
        {
            Author = userName,
            AuthorUnixTime = 0,
            AuthorEmail = userEmail,
            Committer = userName,
            CommitUnixTime = 0,
            CommitterEmail = userEmail,
            Subject = TranslatedStrings.Index,
            ParentIds = currentCheckout.IsZero ? null : new[] { currentCheckout },
            Notes = ""
        };

        return new ArtificialCommits(workTreeRev, indexRev);
    }

    public static ArtificialCommits Create(IGitModule module, ObjectId currentCheckout)
        => Create(
            module.GetEffectiveSetting(SettingKeyString.UserName),
            module.GetEffectiveSetting(SettingKeyString.UserEmail),
            currentCheckout);
}

/// <summary>
///  Stash rows prepared for grid display.
///
///  Git stores stashes in 2 or 3 commits. The (first) "stash" commit is listed by
///  git-stash-list, does not include untracked files, which may be stored in the third
///  "untracked" commit. The second "index" commit is ignored (can be seen with reflog).
///  Git creates the "untracked" commit also if there are no changes; these are filtered.
///  Listing "untracked" commits is quite slow, why the number of stashes evaluated for
///  untracked files is capped (<see cref="AppSettings.MaxStashesWithUntrackedFiles"/>).
/// </summary>
public sealed class GridStashes
{
    private GridStashes(
        Dictionary<ObjectId, GitRevision> stashesById,
        ILookup<ObjectId, GitRevision>? stashesByParentId,
        Dictionary<ObjectId, GitRevision>? untrackedByStashId)
    {
        StashesById = stashesById;
        StashesByParentId = stashesByParentId;
        UntrackedByStashId = untrackedByStashId;
    }

    /// <summary>Pending stashes; the weave removes entries as their rows are emitted.</summary>
    internal Dictionary<ObjectId, GitRevision> StashesById { get; }

    /// <summary>Stashes keyed by first parent, the commit they are woven in before; null when the reflog already shows them.</summary>
    internal ILookup<ObjectId, GitRevision>? StashesByParentId { get; }

    /// <summary>The resolved "untracked" companion commit per stash; null when the reflog already shows them.</summary>
    internal Dictionary<ObjectId, GitRevision>? UntrackedByStashId { get; }

    /// <summary>
    ///  Prepares stash rows for weaving: resolves each stash's "untracked" companion commit
    ///  (via <paramref name="resolveUntracked"/>, which filters commits without changes) and
    ///  rewrites stash parents to only the parents shown in the graph. When
    ///  <paramref name="showReflogReferences"/> is set the log already contains all stash
    ///  commits, so only the reflog selector needs attaching and no rows are woven in.
    /// </summary>
    public static GridStashes? Prepare(
        IReadOnlyCollection<GitRevision> stashRevs,
        bool showReflogReferences,
        int maxStashesWithUntrackedFiles,
        Func<IList<ObjectId>, IReadOnlyCollection<GitRevision>> resolveUntracked)
    {
        if (stashRevs.Count == 0)
        {
            return null;
        }

        Dictionary<ObjectId, GitRevision> stashesById = stashRevs.ToDictionary(r => r.ObjectId);

        if (showReflogReferences)
        {
            // The "untracked" commits are already shown in the grid
            return new GridStashes(stashesById, stashesByParentId: null, untrackedByStashId: null);
        }

        ILookup<ObjectId, GitRevision> stashesByParentId = stashRevs
            .Where(r => !r.FirstParentId.IsZero)
            .ToLookup(r => r.FirstParentId);

        // "untracked" commits to insert (parent to "stash" commits)
        Dictionary<ObjectId, ObjectId> untrackedIdByStashId = stashRevs
            .Where(stash => stash.ParentIds!.Count >= 3)
            .Take(maxStashesWithUntrackedFiles)
            .ToDictionary(stash => stash.ObjectId, stash => stash.ParentIds![2]);
        List<ObjectId> untrackedIds = [.. untrackedIdByStashId.Values.Distinct()];
        Dictionary<ObjectId, GitRevision> untrackedRevs = resolveUntracked(untrackedIds)
            .ToDictionary(r => r.ObjectId);
        Dictionary<ObjectId, GitRevision> untrackedByStashId = untrackedIdByStashId
            .Select(e => (e.Key, untrackedRevs.TryGetValue(e.Value, out GitRevision? rev) ? rev : null))
            .Where(e => e.Item2 is not null)
            .ToDictionary(e => e.Key, e => e.Item2!);

        // Remove parents not included ("index" and empty "untracked" commits).
        foreach (GitRevision stash in stashRevs)
        {
            stash.ParentIds = untrackedByStashId.ContainsKey(stash.ObjectId)
                ? [stash.FirstParentId, stash.ParentIds![2]]
                : [stash.FirstParentId];
        }

        return new GridStashes(stashesById, stashesByParentId, untrackedByStashId);
    }
}

/// <summary>
///  Turns the raw git-log revision stream into the grid's display rows: attaches refs,
///  weaves stash rows in before their parent commit (with their "untracked" companions),
///  and inserts the artificial worktree/index rows just before HEAD. This is the row-order
///  decision layer shared by the WinForms grid and any other host; the caller owns
///  threading and feeding the rows to its graph.
/// </summary>
public sealed class GridRowWeaver
{
    private readonly ObjectId _currentCheckout;
    private readonly GridStashes? _stashes;
    private readonly ArtificialCommits? _artificial;
    private readonly ILookup<ObjectId, IGitRef> _refsByObjectId;

    /// <param name="currentCheckout">HEAD; the artificial rows go just before it. Zero (empty repo) puts them first.</param>
    /// <param name="stashes">Prepared stash rows to weave in; null when stashes are hidden or none exist.</param>
    /// <param name="artificial">The artificial pair to insert; null when uncommitted changes are hidden or the repo is bare.</param>
    /// <param name="refsByObjectId">Refs to attach per revision; exclude "refs/stash" when stashes are woven in.</param>
    public GridRowWeaver(
        ObjectId currentCheckout,
        GridStashes? stashes,
        ArtificialCommits? artificial,
        ILookup<ObjectId, IGitRef> refsByObjectId)
    {
        _currentCheckout = currentCheckout;
        _stashes = stashes;
        _artificial = artificial;
        _refsByObjectId = refsByObjectId;
    }

    /// <summary>
    ///  Whether the artificial rows were placed: HEAD has been seen (or the repo is empty).
    ///  When the stream completes without this, the caller inserts them near HEAD's parents
    ///  (see <see cref="TryGetParents"/>) - HEAD can be missing when filtering or limiting
    ///  the number of loaded commits.
    /// </summary>
    public bool HeadIsHandled { get; private set; }

    /// <summary>One batch of log revisions in, display rows in order out.</summary>
    public IReadOnlyList<GitRevision> Weave(IReadOnlyList<GitRevision> revisions)
    {
        const int artificialCommitCount = 2;
        List<GitRevision> revisionsToDisplay = new(capacity: revisions.Count + (_stashes?.StashesById.Count ?? 0) + artificialCommitCount);

        foreach (GitRevision revision in revisions)
        {
            if (_stashes is not null && _stashes.StashesById.Count != 0)
            {
                if (_stashes.StashesById.TryGetValue(revision.ObjectId, out GitRevision? gridStash))
                {
                    revision.ReflogSelector = gridStash.ReflogSelector;

                    // Do not add this again (when the main commit is handled)
                    _stashes.StashesById.Remove(revision.ObjectId);
                }
                else if (_stashes.StashesByParentId?.Contains(revision.ObjectId) is true)
                {
                    foreach (GitRevision stash in _stashes.StashesByParentId[revision.ObjectId])
                    {
                        // Add if not already added (reflogs etc list before parent commit)
                        if (_stashes.StashesById.ContainsKey(stash.ObjectId))
                        {
                            revisionsToDisplay.Add(stash);

                            // Remove current stash from list of stashes to display
                            _stashes.StashesById.Remove(stash.ObjectId);

                            if (_stashes.UntrackedByStashId!.TryGetValue(stash.ObjectId, out GitRevision? untracked))
                            {
                                revisionsToDisplay.Add(untracked);
                            }
                        }
                    }
                }
            }

            // Look up any refs associated with this revision
            revision.Refs = _refsByObjectId[revision.ObjectId].AsReadOnlyList();

            if (!HeadIsHandled && (revision.ObjectId.Equals(_currentCheckout) || _currentCheckout.IsZero))
            {
                // Insert artificial worktree/index just before HEAD (CurrentCheckout).
                // If the grid is filtered and HEAD is not visible, the caller inserts on completion.
                HeadIsHandled = true;
                if (_artificial is not null)
                {
                    revisionsToDisplay.Add(_artificial.WorkTree);
                    revisionsToDisplay.Add(_artificial.Index);
                }
            }

            revisionsToDisplay.Add(revision);
        }

        return revisionsToDisplay;
    }

    /// <summary>
    ///  The first parents of <paramref name="objectId"/>, for attaching the artificial rows
    ///  when HEAD itself is not in the loaded set. Limited to decrease command time (this may
    ///  add seconds in big repos); only the first are interesting to attach to.
    /// </summary>
    public static IEnumerable<ObjectId> TryGetParents(IGitModule module, ObjectId objectId)
    {
        GitArgumentBuilder args = new("rev-list")
        {
            "--max-count=50",
            objectId
        };

        ExecutionResult result = module.GitExecutable.Execute(args, throwOnErrorExit: false);
        foreach (string line in result.StandardOutput.LazySplit('\n'))
        {
            if (ObjectId.TryParse(line, out ObjectId parentId))
            {
                yield return parentId;
            }
        }
    }
}
