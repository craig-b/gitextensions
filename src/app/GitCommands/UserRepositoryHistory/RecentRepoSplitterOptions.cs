namespace GitCommands.UserRepositoryHistory;

/// <summary>
///  The settings <see cref="RecentRepoSplitter"/> works from, as an explicit value instead of the
///  mutable property bag + settings-reading constructor it used to be.
/// </summary>
/// <param name="MeasureCaptionWidth">
///  Measures the rendered pixel width of a caption; supplied by the UI, which knows the font.
///  <see langword="null"/> falls back to <see cref="CharBudgetMeasure"/> so width-based shortening
///  still degrades predictably for a host without text measurement (the old default measured
///  everything as zero, which silently disabled shortening).
/// </param>
public sealed record RecentRepoSplitterOptions(
    int MaxTopRepositories,
    bool HideTopRepositoriesFromRecentList,
    ShorteningRecentRepoPathStrategy ShorteningStrategy,
    bool SortTopRepos,
    bool SortRecentRepos,
    int RecentReposComboMinWidth,
    Func<string?, int>? MeasureCaptionWidth = null)
{
    /// <summary>An approximate glyph width matching a ~9pt UI font, for the character-budget fallback.</summary>
    public const int DefaultAverageGlyphWidthPixels = 7;

    /// <summary>A toolkit-free width estimate: characters times an average glyph width.</summary>
    public static Func<string?, int> CharBudgetMeasure(int averageGlyphWidthPixels = DefaultAverageGlyphWidthPixels)
        => caption => (caption?.Length ?? 0) * averageGlyphWidthPixels;

    public static RecentRepoSplitterOptions FromAppSettings(Func<string?, int>? measureCaptionWidth = null)
        => new(
            AppSettings.MaxTopRepositories,
            AppSettings.HideTopRepositoriesFromRecentList.Value,
            AppSettings.ShorteningRecentRepoPathStrategy,
            AppSettings.SortTopRepos,
            AppSettings.SortRecentRepos,
            AppSettings.RecentReposComboMinWidth,
            measureCaptionWidth);
}
