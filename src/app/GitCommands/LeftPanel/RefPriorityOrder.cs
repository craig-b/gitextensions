using System.Text.RegularExpressions;

namespace GitCommands.LeftPanel;

/// <summary>
///  Orders refs by the user's priority-regex setting - "regex1;regex2;..." sorts matches of
///  regex1 first, then regex2, non-matches last, stable within each bucket (extracted from
///  BaseRefTree.OrderByPriority). Used for the left panel's prioritized
///  branches and remotes.
/// </summary>
public static class RefPriorityOrder
{
    /// <typeparam name="T">The type to prioritize, e.g. IGitRef.</typeparam>
    /// <param name="references">The branches or remotes to prioritize.</param>
    /// <param name="keySelector">Function in T to get the sorter.</param>
    /// <param name="setting">String with regexes with priorities separated by semicolon.</param>
    /// <returns>The resorted references.</returns>
    public static IEnumerable<T> OrderByPriority<T>(IReadOnlyList<T> references, Func<T, string> keySelector, string setting)
    {
        // Sort prio branches first (if set) with the compile cache (no need to instantiate)
        string[] regexes = [.. setting.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(regex => $"^({regex})$")];

        if (regexes.Length == 0)
        {
            return references;
        }

        // A lot of regexes will push out entries from the static regex cache, increasing the time a lot (several hundred ms).
        // (Switching the regex to the outer loop will decrease the effect but the code structure is simpler this way).
        // The static .NET regex cache must be able to include at least all regexes in the loop,
        // ideally also all common use in a "refresh" loop, so increase the size.
        // A few usages in branches vs remote in this method, CommitInfo adds usage for
        // split length of PrioritizedBranchNames (for remotes) and PrioritizedRemoteNames,
        // a few usages in submodule status processing etc.
        // This check should probably be done in a central location only at startup.
        const int additionalRegexCacheEntries = 10;
        if ((regexes.Length * 2) + additionalRegexCacheEntries > Regex.CacheSize)
        {
            Regex.CacheSize = (regexes.Length * 2) + additionalRegexCacheEntries;
        }

        Dictionary<string, int> orderByNodeKey = [];
        foreach (T node in references)
        {
            int currentOrder = 0;
            foreach (string regex in regexes)
            {
                string key = keySelector(node);
                if (Regex.IsMatch(key, regex, RegexOptions.ExplicitCapture))
                {
                    orderByNodeKey[key] = currentOrder;
                    break;
                }

                currentOrder++;
            }
        }

        // Order by the sort match, with no match last as int.Max
        return references.OrderBy(node => orderByNodeKey.GetValueOrDefault(keySelector(node), int.MaxValue));
    }
}
