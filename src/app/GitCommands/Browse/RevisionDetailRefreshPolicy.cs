namespace GitCommands.Browse;

/// <summary>The detail panes the browse window fills for a selected revision.</summary>
[Flags]
public enum RevisionDetailTarget
{
    None = 0,
    DiffList = 1 << 0,
    FileTree = 1 << 1,
    CommitInfo = 1 << 2,
    GpgInfo = 1 << 3,
}

/// <summary>
///  The browse window's lazy-refresh rule: a detail pane fills only
///  while it is the active pane and only once per revision selection — except commit info,
///  which also fills when it is positioned outside the tab control entirely.
/// </summary>
public static class RevisionDetailRefreshPolicy
{
    public static bool ShouldFill(
        RevisionDetailTarget target,
        RevisionDetailTarget alreadyFilled,
        RevisionDetailTarget activePane,
        bool commitInfoInTabControl = true)
    {
        if (alreadyFilled.HasFlag(target))
        {
            return false;
        }

        return target is RevisionDetailTarget.CommitInfo
            ? !commitInfoInTabControl || activePane is RevisionDetailTarget.CommitInfo
            : activePane == target;
    }
}
