using System.Net;
using System.Text;

namespace GitCommands.RichText;

[Flags]
public enum RichTextStyle
{
    None = 0,
    Underline = 1,
}

/// <summary>
///  One run of the M6 inline document model: plain text, optionally styled, optionally a link.
///  <see cref="Text"/> and <see cref="LinkTarget"/> are RAW (unencoded) - encoding is the
///  serializer's job.
/// </summary>
public readonly record struct RichTextSegment(string Text, RichTextStyle Style = RichTextStyle.None, string? LinkTarget = null);

/// <summary>
///  The M6 inline document model for commit information: an ordered list of styled runs and
///  links, renderer-agnostic. During the transition the WinForms path serializes it with
///  <see cref="ToXhtml"/> - which reproduces, character for character, the markup the commit-info
///  producers have always emitted (WebUtility.HtmlEncode text, <c>&lt;a href='...'&gt;</c> links
///  in LinkFactory's exact quoting, <c>&lt;u&gt;</c> underline) - so the existing
///  RichTextBox pipeline renders identically. A future client renders the segments directly.
/// </summary>
public sealed class RichContent
{
    private readonly List<RichTextSegment> _segments = [];

    public IReadOnlyList<RichTextSegment> Segments => _segments;

    public bool IsEmpty => _segments.Count == 0;

    public static RichContent From(string? text) => new RichContent().AddText(text);

    /// <summary>Appends plain text. Null or empty is a no-op. May contain newlines.</summary>
    public RichContent AddText(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            _segments.Add(new RichTextSegment(text));
        }

        return this;
    }

    public RichContent AddUnderlined(string text)
    {
        _segments.Add(new RichTextSegment(text, RichTextStyle.Underline));
        return this;
    }

    /// <summary>Appends a link with RAW caption and target (no pre-encoding).</summary>
    public RichContent AddLink(string caption, string target)
    {
        _segments.Add(new RichTextSegment(caption, LinkTarget: target));
        return this;
    }

    /// <summary>Appends <paramref name="text"/> (optional) followed by a newline.</summary>
    public RichContent AddLine(string? text = null)
    {
        AddText(text);
        _segments.Add(new RichTextSegment("\n"));
        return this;
    }

    public RichContent Append(RichContent? other)
    {
        if (other is not null)
        {
            _segments.AddRange(other._segments);
        }

        return this;
    }

    /// <summary>
    ///  Serializes to the exact markup dialect the commit-info producers historically emitted;
    ///  consumed by RichTextBoxXhtmlSupportExtension until the model-driven adapter (M6 stage 3)
    ///  replaces it. Exactness matters: the renderer unit tests assert these strings verbatim.
    /// </summary>
    public string ToXhtml()
    {
        StringBuilder sb = new();

        foreach (RichTextSegment segment in _segments)
        {
            bool underline = segment.Style.HasFlag(RichTextStyle.Underline);
            if (underline)
            {
                sb.Append("<u>");
            }

            if (segment.LinkTarget is not null)
            {
                // Mirrors LinkFactory.CreateLink: <a href='<encoded uri>'><encoded caption></a>
                sb.Append("<a href=").Append(WebUtility.HtmlEncode(segment.LinkTarget).Quote("'")).Append('>')
                  .Append(WebUtility.HtmlEncode(segment.Text)).Append("</a>");
            }
            else
            {
                sb.Append(WebUtility.HtmlEncode(segment.Text));
            }

            if (underline)
            {
                sb.Append("</u>");
            }
        }

        return sb.ToString();
    }

    public string ToPlainText()
    {
        StringBuilder sb = new();
        foreach (RichTextSegment segment in _segments)
        {
            sb.Append(segment.Text);
        }

        return sb.ToString();
    }
}
