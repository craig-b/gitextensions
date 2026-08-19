namespace GitCommands.Commit;

/// <summary>
///  The Conventional Commits vocabulary and the header-rewrite logic behind the commit dialog's
///  "Conventional Commits" menu and hotkeys (extracted from FormCommit).
///  Pure text math: the view supplies the current message state and applies the result.
/// </summary>
public static class ConventionalCommitMessage
{
    public const string FeatureKeyword = "feat";

    public static readonly string[] HeaderCommitTypes = ["build", "chore", "ci", "docs", FeatureKeyword, "fix", "perf", "refactor", "style", "test"];

    public static readonly string[] FooterKeywords = ["BREAKING CHANGE", "Co-authored-by", "Reviewed-by"];

    /// <summary>
    ///  Rewrites the message's first line to carry <paramref name="keyword"/> - replacing an
    ///  existing conventional-commit type or prefixing one - and computes where the caret goes.
    /// </summary>
    /// <param name="keyword">The conventional-commit type to apply (e.g. "feat").</param>
    /// <param name="messageText">The whole current message.</param>
    /// <param name="firstLine">The message's first line as the view's document reports it.</param>
    /// <param name="currentPosition">The current caret position within the message.</param>
    /// <param name="insertScopeParentheses">Whether to insert "()" for a scope and place the caret inside.</param>
    /// <returns>The new first line and the new caret position.</returns>
    public static (string Message, int SelectionStart) PrefixOrReplaceKeyword(string keyword, string messageText, string firstLine, int currentPosition, bool insertScopeParentheses)
    {
        string scope = insertScopeParentheses ? "()" : "";
        int scopePosition = keyword.Length + 1;
        int titlePosition = keyword.Length + (scope.Length / 2) + 2;

        string currentTitle = string.IsNullOrWhiteSpace(messageText) ? string.Empty : firstLine;

        // Replacing current keyword
        foreach (string key in HeaderCommitTypes)
        {
            if (!currentTitle.StartsWith(key))
            {
                continue;
            }

            if (currentTitle.Length == key.Length)
            {
                return ($"{keyword}{scope}: ", insertScopeParentheses ? scopePosition : titlePosition);
            }

            char nextChar = currentTitle[key.Length];
            if (!insertScopeParentheses)
            {
                if (nextChar == ':' || nextChar == '(' || nextChar == '!')
                {
                    return ReplaceKeyword(_ => titlePosition);
                }
            }
            else
            {
                if (nextChar == ':' || nextChar == '!')
                {
                    return ($"{keyword}(){currentTitle[key.Length..]}", scopePosition);
                }

                if (nextChar == '(')
                {
                    return ReplaceKeyword(newTitle => 2 + Math.Max(newTitle.IndexOf(':'), newTitle.IndexOf('(')));
                }
            }

            (string Message, int SelectionStart) ReplaceKeyword(Func<string, int> maxPosition)
            {
                string newTitle = $"{keyword}{currentTitle[key.Length..]}";
                int newMessageLength = messageText.Length + newTitle.Length - currentTitle.Length;
                return (newTitle, Math.Min(newMessageLength, Math.Max(maxPosition(newTitle), currentPosition + keyword.Length - key.Length)));
            }
        }

        // Append current keyword
        return ($"{keyword}{scope}: {currentTitle}", insertScopeParentheses ? scopePosition : titlePosition + currentPosition);
    }
}
