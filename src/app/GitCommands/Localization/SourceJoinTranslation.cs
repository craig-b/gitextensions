using System.Xml.Linq;

namespace GitCommands.Localization;

/// <summary>
///  The §11 source-join: translations for a differently-authored UI are reconstructed by
///  joining its English strings against the xlf catalog's per-unit &lt;source&gt; values.
///  Where one English string has several translations (758 known cases), the most frequent
///  target wins - the per-dialog refinement stays a later option.
/// </summary>
public static class SourceJoinTranslation
{
    /// <summary>source → target for one language file; empty on a missing or unparsable file.</summary>
    public static IReadOnlyDictionary<string, string> LoadCatalog(string xlfFilePath)
    {
        Dictionary<string, Dictionary<string, int>> targetVotes = new(StringComparer.Ordinal);

        try
        {
            XDocument document = XDocument.Load(xlfFilePath);
            foreach (XElement unit in document.Descendants("trans-unit"))
            {
                string? source = unit.Element("source")?.Value;
                string? target = unit.Element("target")?.Value;
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                {
                    continue;
                }

                if (!targetVotes.TryGetValue(source, out Dictionary<string, int>? votes))
                {
                    votes = new Dictionary<string, int>(StringComparer.Ordinal);
                    targetVotes.Add(source, votes);
                }

                votes[target] = votes.GetValueOrDefault(target) + 1;
            }
        }
        catch
        {
            return new Dictionary<string, string>();
        }

        Dictionary<string, string> catalog = new(targetVotes.Count, StringComparer.Ordinal);
        foreach ((string source, Dictionary<string, int> votes) in targetVotes)
        {
            catalog[source] = votes.MaxBy(vote => vote.Value).Key;
        }

        return catalog;
    }

    /// <summary>The selectable languages: every non-plugin xlf in the directory, except English.</summary>
    public static IReadOnlyList<string> FindLanguages(string translationDir)
    {
        try
        {
            return [.. Directory.EnumerateFiles(translationDir, "*.xlf")
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => name is not null && !name.Contains('.') && name != "English")
                .Select(name => name!)
                .Order()];
        }
        catch
        {
            return [];
        }
    }
}

/// <summary>A joined catalog with English fallback; the identity translator when no language is set.</summary>
public sealed class SourceJoinTranslator
{
    public static SourceJoinTranslator Identity { get; } = new(new Dictionary<string, string>());

    private readonly IReadOnlyDictionary<string, string> _catalog;

    public SourceJoinTranslator(IReadOnlyDictionary<string, string> catalog)
    {
        _catalog = catalog;
    }

    public int Count => _catalog.Count;

    public string T(string english)
        => _catalog.TryGetValue(english, out string? translated) && !string.IsNullOrEmpty(translated) ? translated : english;

    /// <summary>Loads the language's catalog from the translation directory (identity for blank/English).</summary>
    public static SourceJoinTranslator Load(string translationDir, string? language)
    {
        if (string.IsNullOrWhiteSpace(language) || language == "English")
        {
            return Identity;
        }

        return new SourceJoinTranslator(SourceJoinTranslation.LoadCatalog(Path.Join(translationDir, $"{language}.xlf")));
    }
}
