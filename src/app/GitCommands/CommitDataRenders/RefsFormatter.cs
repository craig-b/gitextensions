using GitCommands;
using GitCommands.RichText;
using ResourceManager;

namespace GitUI.CommitInfo;

public sealed class RefsFormatter
{
    /// <summary>
    /// The number of displayed lines if the list is limited.
    /// </summary>
    private const int MaximumDisplayedLinesIfLimited = 12;

    /// <summary>
    /// The number of displayed refs if the list is limited.
    ///
    /// If limited, the line "[Show all]" and an empty line are added.
    /// Hence the list needs to be limited only if it exceeds MaximumDisplayedRefsIfLimited + 2.
    /// </summary>
    private const int MaximumDisplayedRefsIfLimited = MaximumDisplayedLinesIfLimited - 2;

    private readonly ILinkFactory _linkFactory;

    public RefsFormatter(ILinkFactory linkFactory)
    {
        _linkFactory = linkFactory ?? throw new ArgumentNullException(nameof(linkFactory), "RefsFormatter requires an ILinkFactory instance");
    }

    public RichContent FormatBranches(IEnumerable<string>? branches, bool showAsLinks, bool limit)
    {
        if (branches is null)
        {
            return new RichContent();
        }

        (IReadOnlyList<RichTextSegment> formattedBranches, bool truncated) = FilterAndFormatBranches(branches, showAsLinks, limit);
        return ToContent(formattedBranches, TranslatedStrings.ContainedInBranches, TranslatedStrings.ContainedInNoBranch, "branches", truncated);
    }

    public RichContent FormatTags(IReadOnlyList<string> tags, bool showAsLinks, bool limit)
    {
        if (tags is null)
        {
            return new RichContent();
        }

        bool truncate = limit && tags.Count > MaximumDisplayedLinesIfLimited;
        List<RichTextSegment> formattedTags = [.. FormatTags(truncate ? tags.Take(MaximumDisplayedRefsIfLimited) : tags)];
        return ToContent(formattedTags, TranslatedStrings.ContainedInTags, TranslatedStrings.ContainedInNoTag, "tags", truncate);

        IEnumerable<RichTextSegment> FormatTags(IEnumerable<string> selectedTags)
        {
            return selectedTags.Select(s => showAsLinks ? _linkFactory.CreateTagLink(s ?? string.Empty) : new RichTextSegment(s ?? string.Empty));
        }
    }

    private (IReadOnlyList<RichTextSegment> formattedBranches, bool truncated) FilterAndFormatBranches(IEnumerable<string> branches, bool showAsLinks, bool limit)
    {
        List<RichTextSegment> formattedBranches = [];
        bool truncated = false;

        const string remotesPrefix = "remotes/";

        // Include local branches if explicitly requested or when needed to decide whether to show remotes
        bool getLocal = AppSettings.CommitInfoShowContainedInBranchesLocal ||
                        AppSettings.CommitInfoShowContainedInBranchesRemoteIfNoLocal;

        // Include remote branches if requested
        bool getRemote = AppSettings.CommitInfoShowContainedInBranchesRemote ||
                         AppSettings.CommitInfoShowContainedInBranchesRemoteIfNoLocal;
        bool allowLocal = AppSettings.CommitInfoShowContainedInBranchesLocal;
        bool allowRemote = getRemote;

        foreach (string branch in branches)
        {
            string noPrefixBranch = branch;
            bool branchIsLocal;
            if (getLocal && getRemote)
            {
                // "git branch -a" prefixes remote branches with "remotes/"
                // It is possible to create a local branch named "remotes/origin/something"
                // so this check is not 100% reliable.
                // This shouldn't be a big problem if we're only displaying information.
                // This could be solved by listing local and remote branches separately.
                branchIsLocal = !branch.StartsWith(remotesPrefix);
                if (!branchIsLocal)
                {
                    noPrefixBranch = branch[remotesPrefix.Length..];
                }
            }
            else
            {
                branchIsLocal = !getRemote;
            }

            if ((branchIsLocal && allowLocal) || (!branchIsLocal && allowRemote))
            {
                RichTextSegment branchText = showAsLinks
                    ? _linkFactory.CreateBranchLink(noPrefixBranch)
                    : new RichTextSegment(noPrefixBranch);

                if (limit && formattedBranches.Count == MaximumDisplayedLinesIfLimited)
                {
                    formattedBranches.RemoveRange(MaximumDisplayedRefsIfLimited, MaximumDisplayedLinesIfLimited - MaximumDisplayedRefsIfLimited);
                    truncated = true;
                    break; // from foreach
                }

                formattedBranches.Add(branchText);
            }

            if (branchIsLocal && AppSettings.CommitInfoShowContainedInBranchesRemoteIfNoLocal)
            {
                allowRemote = false;
            }
        }

        return (formattedBranches, truncated);
    }

    private RichContent ToContent(IReadOnlyList<RichTextSegment> formattedRefs, string prefix, string textIfEmpty, string refsType, bool truncated)
    {
        if (formattedRefs.Count == 0)
        {
            return RichContent.From(textIfEmpty);
        }

        RichContent content = new RichContent().AddLine(prefix);
        bool first = true;
        foreach (RichTextSegment segment in formattedRefs)
        {
            if (!first)
            {
                content.AddLine();
            }

            first = false;
            content.Add(segment);
        }

        if (truncated)
        {
            content.AddLine();
            content.Add(_linkFactory.CreateShowAllLink(refsType));
        }

        return content;
    }
}
