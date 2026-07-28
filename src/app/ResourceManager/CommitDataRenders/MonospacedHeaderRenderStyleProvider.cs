using GitCommands;
using GitExtensions.Extensibility.Extensions;

namespace ResourceManager.CommitDataRenders;

/// <summary>
/// Renders commit information in a tabular format with data columns aligned with spaces.
/// </summary>
public sealed class MonospacedHeaderRenderStyleProvider : IHeaderRenderStyleProvider
{
    private readonly int _maxLength;

    public MonospacedHeaderRenderStyleProvider()
    {
        string[] strings =
        [
            TranslatedStrings.Author,
            TranslatedStrings.AuthorDate,
            TranslatedStrings.Committer,
            TranslatedStrings.CommitDate,
            TranslatedStrings.CommitHash,
            TranslatedStrings.GetChildren(10), // assume text for plural case is longer
            TranslatedStrings.GetParents(10)
        ];

        _maxLength = strings.Select(s => s.Length).Max() + 2;
    }

    public Font GetFont(Graphics g)
    {
        if (!AppFonts.App.IsFixedWidth(g))
        {
            return new Font(FontFamily.GenericMonospace, AppFonts.App.Size);
        }

        return AppFonts.App;
    }

    public int GetMaxWidth() => _maxLength;

    public IEnumerable<int> GetTabStops() => [];
}
