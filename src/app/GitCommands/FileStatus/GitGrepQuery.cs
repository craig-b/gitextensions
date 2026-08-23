using System.Text.RegularExpressions;

namespace GitCommands.FileStatus;

/// <summary>
///  Builds the git-grep search argument the file list passes to the diff calculator: plain
///  text is escaped and wrapped as a quoted <c>-e</c> expression unless the user already
///  supplied one.
/// </summary>
public static partial class GitGrepQuery
{
    [GeneratedRegex(@"(^|\s)-e(\s|\s+['""])", RegexOptions.ExplicitCapture)]
    private static partial Regex ExplicitExpressionRegex { get; }

    public static string BuildSearchArgument(string search)
    {
        if (string.IsNullOrWhiteSpace(search) || ExplicitExpressionRegex.IsMatch(search))
        {
            return search;
        }

        return $@"-e ""{search.Replace(@"\\", @"\\\\")}""";
    }
}
