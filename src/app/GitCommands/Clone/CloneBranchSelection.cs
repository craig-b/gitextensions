namespace GitCommands.Clone;

/// <summary>
///  The clone dialog's branch dropdown: two view-captioned sentinels ahead of the real branches.
///  "Default (remote HEAD)" maps to an empty branch argument (checkout the remote HEAD), "None"
///  maps to <see langword="null"/> (<c>--no-checkout</c>), anything else is a literal branch.
/// </summary>
public static class CloneBranchSelection
{
    public static string? ToBranchArgument(string? text, string defaultRemoteHeadCaption, string noneCaption)
    {
        if (text == defaultRemoteHeadCaption)
        {
            return "";
        }

        return text == noneCaption ? null : text;
    }

    /// <summary>
    ///  The dropdown contents after a remote probe: sentinels first, then the branch names;
    ///  <c>Reselect</c> keeps the user's typed text only when it still exists in the new list.
    /// </summary>
    public static (IReadOnlyList<string> Items, string? Reselect) MergeBranchList(
        IReadOnlyList<string> defaultItems,
        IEnumerable<string> branchLocalNames,
        string? currentText)
    {
        List<string> names = [.. defaultItems, .. branchLocalNames];
        return (names, names.Any(name => name == currentText) ? currentText : null);
    }
}
