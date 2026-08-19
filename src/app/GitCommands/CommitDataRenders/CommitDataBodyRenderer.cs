using GitCommands.RichText;
using GitExtensions.Extensibility.Extensions;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace ResourceManager.CommitDataRenders;

/// <summary>
/// Provides the ability to render the body of a commit message.
/// </summary>
public interface ICommitDataBodyRenderer
{
    /// <summary>
    /// Render the body of a commit message.
    /// </summary>
    RichContent Render(CommitData commitData, bool showRevisionsAsLinks);
}

/// <summary>
/// Renders the body of a commit message.
/// </summary>
public sealed class CommitDataBodyRenderer : ICommitDataBodyRenderer
{
    private readonly Func<IGitModule> _getModule;
    private readonly ILinkFactory _linkFactory;

    public CommitDataBodyRenderer(Func<IGitModule> getModule, ILinkFactory linkFactory)
    {
        _getModule = getModule;
        _linkFactory = linkFactory;
    }

    /// <summary>
    /// Render the body of a commit message.
    /// </summary>
    public RichContent Render(CommitData commitData, bool showRevisionsAsLinks)
    {
        ArgumentNullException.ThrowIfNull(commitData);

        // M6: the hash regex now runs over the RAW body - the old code matched over the
        // HTML-ENCODED body, where an entity adjacent to a hash could shift a match boundary.
        // Identical for bodies without &/</>/quotes next to hash candidates.
        string body = (UIExtensions.FormatBodyAndNotes(commitData.Body, commitData.Notes) ?? "").Trim();

        RichContent content = new();

        if (!showRevisionsAsLinks)
        {
            return content.AddText(body);
        }

        int position = 0;
        foreach (System.Text.RegularExpressions.Match match in GitRevision.Sha1HashShortRegex.Matches(body))
        {
            content.AddText(body[position..match.Index]);
            AddHashCandidate(content, match.Value);
            position = match.Index + match.Length;
        }

        content.AddText(body[position..]);
        return content;
    }

    private void AddHashCandidate(RichContent content, string hash)
    {
        IGitModule module = _getModule();

        if (module is null || !module.TryResolvePartialCommitId(hash, out ObjectId fullHash))
        {
            content.AddText(hash);
            return;
        }

        content.Add(_linkFactory.CreateCommitLink(fullHash, hash, true));
    }
}
