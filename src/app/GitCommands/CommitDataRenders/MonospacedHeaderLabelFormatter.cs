namespace ResourceManager.CommitDataRenders;

/// <summary>
/// Formats the commit information heading labels with spaces.
/// </summary>
public sealed class MonospacedHeaderLabelFormatter : IHeaderLabelFormatter
{
    public string FormatLabel(string label, int desiredLength)
    {
        // M6: returns RAW text (the RichContent serializer encodes); padding is computed on the
        // visible label - the old code padded the HTML-ENCODED label, so a label containing
        // &/</> padded short by the entity overhead. Identical for labels without those chars.
        return (label + ":").PadRight(desiredLength);
    }
}
