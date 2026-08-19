using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GitCommands.Commit;

/// <summary>
///  Expands the commit-template placeholder syntax <c>{{regex}}[groupIndex]</c> against the
///  current branch name (extracted from FormCommit.ReplaceMessage).
///  Faithful to the original semantics: placeholders are expanded left to right, a missing
///  match or group expands to the empty string, and an exception mid-way (e.g. an invalid
///  user pattern) is traced and the partially-expanded message is returned.
/// </summary>
public static partial class CommitMessageTemplateExpander
{
    /// <summary>
    /// Regex to find message replace pattern: {{ group1 }}[ group2 ]
    /// </summary>
    [GeneratedRegex(@"\{\{(?<pattern>.*?)\}\}(?:\[(?<index>\d+)\])?", RegexOptions.ExplicitCapture)]
    private static partial Regex ReplaceMessageRegex();

    public static string Expand(string message, Func<string> getCurrentBranchName)
    {
        try
        {
            foreach (Match regexMatch in ReplaceMessageRegex().Matches(message))
            {
                string pattern = regexMatch.Groups["pattern"].Value;
                int groupIndex = 1;

                if (int.TryParse(regexMatch.Groups["index"].ValueSpan, out int parsedIndex))
                {
                    groupIndex = parsedIndex;
                }

                Regex regex = new(pattern);
                string currentBranchName = getCurrentBranchName();
                MatchCollection matches = regex.Matches(currentBranchName);
                string replaceText = "";

                if (matches.Count > 0 && matches[0].Groups.Count > groupIndex)
                {
                    replaceText = matches[0].Groups[groupIndex].Value;
                }

                message = message.Replace(regexMatch.Groups[0].Value, replaceText);
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"ReplaceMessage with regex replace exception: {ex}");
        }

        return message;
    }
}
