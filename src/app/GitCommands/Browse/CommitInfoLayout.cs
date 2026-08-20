namespace GitCommands.Browse;

/// <summary>Where the revision-info and grid panes land for a commit-info position.</summary>
public enum RevisionPane
{
    CommitInfoTab,
    SplitPanel1,
    SplitPanel2,
}

/// <summary>The commit-info position layout: FormBrowse's 60-line reparenting block as data.</summary>
public readonly record struct CommitInfoLayout(
    bool CommitInfoInTabControl,
    RevisionPane RevisionInfoPane,
    RevisionPane RevisionGridPane,
    bool FixFirstPanel,
    bool DetailPanelCollapsed);

public static class CommitInfoLayoutTable
{
    public static CommitInfoLayout For(CommitInfoPosition position)
        => position switch
        {
            CommitInfoPosition.BelowList => new CommitInfoLayout(
                CommitInfoInTabControl: true,
                RevisionInfoPane: RevisionPane.CommitInfoTab,
                RevisionGridPane: RevisionPane.SplitPanel1,
                FixFirstPanel: false,
                DetailPanelCollapsed: true),
            CommitInfoPosition.RightwardFromList => new CommitInfoLayout(
                CommitInfoInTabControl: false,
                RevisionInfoPane: RevisionPane.SplitPanel2,
                RevisionGridPane: RevisionPane.SplitPanel1,
                FixFirstPanel: false,
                DetailPanelCollapsed: false),
            CommitInfoPosition.LeftwardFromList => new CommitInfoLayout(
                CommitInfoInTabControl: false,
                RevisionInfoPane: RevisionPane.SplitPanel1,
                RevisionGridPane: RevisionPane.SplitPanel2,
                FixFirstPanel: true,
                DetailPanelCollapsed: false),
            _ => throw new NotSupportedException(),
        };

    /// <summary>The toggle button cycles through the positions in declaration order.</summary>
    public static CommitInfoPosition Next(CommitInfoPosition position)
        => (CommitInfoPosition)(((int)position + 1) % Enum.GetValues<CommitInfoPosition>().Length);

    /// <summary>Rightward pins the detail width from the right edge; leftward from the left.</summary>
    public static int SplitterDistance(CommitInfoPosition position, int containerWidth, int preferredWidth)
        => position is CommitInfoPosition.RightwardFromList
            ? Math.Max(0, containerWidth - preferredWidth)
            : preferredWidth;
}
