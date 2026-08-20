namespace GitCommands.Settings.Pages;

/// <summary>
///  The settings dialog's page-search rule, shared by every view's find box:
///  a page matches when its title contains the whole search text, or when every
///  space-separated keyword partially matches one of the page's search keywords.
/// </summary>
public static class SettingsPageSearch
{
    /// <summary>
    ///  Whether a page matches the search text. Callers treat blank search text as
    ///  "no filter" and should not pass it here (it would match every page).
    /// </summary>
    public static bool Matches(string searchText, string title, IEnumerable<string> pageKeywords)
    {
        string searchFor = searchText.ToLowerInvariant();

        if (title.Contains(searchFor, StringComparison.InvariantCultureIgnoreCase))
        {
            return true;
        }

        string[] andKeywords = searchFor.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return andKeywords.All(keyword => pageKeywords.Any(pageKeyword => pageKeyword.Contains(keyword, StringComparison.InvariantCultureIgnoreCase)));
    }
}
