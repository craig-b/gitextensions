using System.Text;
using System.Text.RegularExpressions;
using GitCommands.RichText;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
using Microsoft;

namespace ResourceManager.CommitDataRenders;

/// <summary>
/// Provides the ability to render commit information.
/// </summary>
public interface ICommitDataHeaderRenderer
{
    /// <summary>
    /// Gets the plain text for the clipboard - without tabs, relative date, children and parents.
    /// </summary>
    string GetPlainText(string header);

    /// <summary>
    /// Generate header.
    /// </summary>
    RichContent Render(CommitData commitData, bool showRevisionsAsLinks);

    /// <summary>
    /// Generate header.
    /// </summary>
    string RenderPlain(CommitData commitData);
}

/// <summary>
/// Renders commit information in a tabular format with data columns aligned with spaces.
/// </summary>
public sealed partial class CommitDataHeaderRenderer : ICommitDataHeaderRenderer
{
    private readonly IHeaderLabelFormatter _labelFormatter;
    private readonly IDateFormatter _dateFormatter;
    private readonly IHeaderRenderStyleProvider _headerRendererStyleProvider;
    private readonly ILinkFactory? _linkFactory;

    [GeneratedRegex(@"[ \t]+", RegexOptions.ExplicitCapture)]
    private static partial Regex SpacesRegex { get; }
    [GeneratedRegex(@"(?<reltime>\n[^:]+: ).* ago \((?<unit>[^)]+)\)", RegexOptions.ExplicitCapture)]
    private static partial Regex RemoveAgoRegex { get; }

    public CommitDataHeaderRenderer(IHeaderLabelFormatter labelFormatter, IDateFormatter dateFormatter, IHeaderRenderStyleProvider headerRendererStyleProvider, ILinkFactory? linkFactory)
    {
        _labelFormatter = labelFormatter;
        _dateFormatter = dateFormatter;
        _headerRendererStyleProvider = headerRendererStyleProvider;
        _linkFactory = linkFactory;
    }

    public string GetPlainText(string header)
    {
        string children = $"({TranslatedStrings.GetChildren(1)})|({TranslatedStrings.GetChildren(2)})|({TranslatedStrings.GetChildren(10)})";
        string parents = $"({TranslatedStrings.GetParents(1)})|({TranslatedStrings.GetParents(2)})|({TranslatedStrings.GetParents(10)})";
        header = SpacesRegex.Replace(header, " ");
        header = RemoveAgoRegex.Replace(header, "$1$2");
        header = Regex.Replace(header, @$"\n({children}|{parents})[^\n]*", "");
        return header;
    }

    /// <summary>
    /// Generate header.
    /// </summary>
    public RichContent Render(CommitData commitData, bool showRevisionsAsLinks)
    {
        ArgumentNullException.ThrowIfNull(commitData);

        bool isArtificial = commitData.ObjectId.IsArtificial;
        bool authorIsCommitter = string.Equals(commitData.Author, commitData.Committer, StringComparison.CurrentCulture);
        bool datesEqual = commitData.AuthorDate.EqualsExact(commitData.CommitDate);
        int padding = _headerRendererStyleProvider.GetMaxWidth();
        string authorEmail = GetEmail(commitData.Author);

        Validates.NotNull(_linkFactory);

        RichContent header = new();
        bool firstLine = true;

        StartLine(TranslatedStrings.Author);
        header.Add(_linkFactory.CreateLink(commitData.Author, "mailto:" + authorEmail));

        if (!isArtificial)
        {
            StartLine(datesEqual ? TranslatedStrings.Date : TranslatedStrings.AuthorDate);
            header.AddText(_dateFormatter.FormatDateAsRelativeLocal(commitData.AuthorDate));
        }

        if (!authorIsCommitter)
        {
            string committerEmail = GetEmail(commitData.Committer);
            StartLine(TranslatedStrings.Committer);
            header.Add(_linkFactory.CreateLink(commitData.Committer, "mailto:" + committerEmail));
        }

        if (!isArtificial)
        {
            if (!datesEqual)
            {
                StartLine(TranslatedStrings.CommitDate);
                header.AddText(_dateFormatter.FormatDateAsRelativeLocal(commitData.CommitDate));
            }

            StartLine(TranslatedStrings.CommitHash);
            header.AddText(commitData.ObjectId.ToString());
        }

        if (commitData.ChildIds is not null && commitData.ChildIds.Count != 0)
        {
            StartLine(TranslatedStrings.GetChildren(commitData.ChildIds.Count));
            RenderObjectIds(header, commitData.ChildIds, showRevisionsAsLinks);
        }

        IReadOnlyList<ObjectId>? parentIds = commitData.ParentIds;
        if (parentIds?.Count > 0)
        {
            StartLine(TranslatedStrings.GetParents(parentIds.Count));
            RenderObjectIds(header, parentIds, showRevisionsAsLinks);
        }

        return header;

        void StartLine(string label)
        {
            if (!firstLine)
            {
                header.AddLine();
            }

            firstLine = false;
            header.AddText(_labelFormatter.FormatLabel(label, padding));
        }
    }

    /// <summary>
    /// Generate header.
    /// </summary>
    public string RenderPlain(CommitData commitData)
    {
        ArgumentNullException.ThrowIfNull(commitData);

        bool authorIsCommitter = string.Equals(commitData.Author, commitData.Committer, StringComparison.CurrentCulture);
        bool datesEqual = commitData.AuthorDate.EqualsExact(commitData.CommitDate);
        int padding = _headerRendererStyleProvider.GetMaxWidth();

        StringBuilder header = new();
        header.AppendLine(_labelFormatter.FormatLabel(TranslatedStrings.Author, padding) + commitData.Author);
        header.AppendLine(_labelFormatter.FormatLabel(datesEqual ? TranslatedStrings.Date : TranslatedStrings.AuthorDate, padding) + _dateFormatter.FormatDateAsRelativeLocal(commitData.AuthorDate));
        if (!authorIsCommitter)
        {
            header.AppendLine(_labelFormatter.FormatLabel(TranslatedStrings.Committer, padding) + commitData.Committer);
        }

        if (!datesEqual)
        {
            header.AppendLine(_labelFormatter.FormatLabel(TranslatedStrings.CommitDate, padding) + _dateFormatter.FormatDateAsRelativeLocal(commitData.CommitDate));
        }

        header.Append(_labelFormatter.FormatLabel(TranslatedStrings.CommitHash, padding) + commitData.ObjectId);

        return header.ToString();
    }

    private static string GetEmail(string? author)
    {
        if (string.IsNullOrEmpty(author))
        {
            return "";
        }

        int ind = author.IndexOf('<');
        if (ind == -1)
        {
            return "";
        }

        ++ind;
        return author[ind..author.LastIndexOf('>')];
    }

    private void RenderObjectIds(RichContent header, IEnumerable<ObjectId> objectIds, bool showRevisionsAsLinks)
    {
        Validates.NotNull(_linkFactory);
        bool first = true;
        foreach (ObjectId id in objectIds)
        {
            if (!first)
            {
                header.AddText(" ");
            }

            first = false;
            if (showRevisionsAsLinks)
            {
                header.Add(_linkFactory.CreateCommitLink(id));
            }
            else
            {
                header.AddText(id.ToShortString());
            }
        }
    }
}
