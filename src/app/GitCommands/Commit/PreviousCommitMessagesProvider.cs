using System.Text.RegularExpressions;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;

namespace GitCommands.Commit;

/// <param name="Message">The full commit message (the value a click applies).</param>
/// <param name="Label">The menu label: first line, shortened to 72 characters.</param>
public sealed record PreviousCommitMessage(string Message, string Label);

/// <summary>
///  Builds the commit dialog's "previous commit messages" menu entries (extracted from
///  FormCommit's drop-down-opening handler): recent messages from git log,
///  optionally filtered to the current author, with the last locally-entered message inserted
///  at the front when git does not already know it.
/// </summary>
public static class PreviousCommitMessagesProvider
{
    /// <summary>
    ///  The author filter used by "show only my messages": an exact "name &lt;email&gt;" match.
    /// </summary>
    public static string BuildAuthorPattern(string userName, string userEmail)
        => $"^{Regex.Escape(userName)} <{Regex.Escape(userEmail)}>$";

    public static IReadOnlyList<PreviousCommitMessage> GetMessages(IGitModule module, string? lastCommitMessage, int maxCount, string authorPattern)
    {
        List<string> prevMessages = [.. module.GetPreviousCommitMessages(maxCount, "HEAD", authorPattern)
            .WhereNotNull()
            .Select(message => message.TrimEnd('\n'))
            .Where(message => !string.IsNullOrWhiteSpace(message))];

        if (!string.IsNullOrWhiteSpace(lastCommitMessage) && !prevMessages.Contains(lastCommitMessage))
        {
            // If the list is already full
            if (prevMessages.Count == maxCount)
            {
                // Remove the last item
                prevMessages.RemoveAt(maxCount - 1);
            }

            // Insert the last commit message as the first entry
            prevMessages.Insert(0, lastCommitMessage);
        }

        return [.. prevMessages.Select(message => new PreviousCommitMessage(message, GetLabel(message)))];
    }

    private static string GetLabel(string commitMessage)
    {
        const int maxLabelLength = 72;

        string label = commitMessage;
        int newlineIndex = label.IndexOf('\n');

        if (newlineIndex != -1)
        {
            label = label[..newlineIndex];
        }

        if (label.Length > maxLabelLength)
        {
            label = label.ShortenTo(maxLabelLength);
        }

        return label;
    }
}
