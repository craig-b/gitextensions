namespace GitCommands.LeftPanel;

public enum LeftPanelSection
{
    Branches,
    Remotes,
    Tags,
    Stashes,
    Worktrees,
    Submodules,
}

/// <summary>
///  The sidebar's section order setting: a tolerant comma-separated name list
///  (the MenuCustomOrder pattern). Unknown names are ignored, missing sections append in
///  default order, so a partial or misspelled setting can never lose a section.
/// </summary>
public static class LeftPanelSectionOrder
{
    public static IReadOnlyList<LeftPanelSection> DefaultOrder { get; } =
    [
        LeftPanelSection.Branches,
        LeftPanelSection.Remotes,
        LeftPanelSection.Tags,
        LeftPanelSection.Stashes,
        LeftPanelSection.Worktrees,
        LeftPanelSection.Submodules,
    ];

    public static string ToText(IEnumerable<LeftPanelSection> order)
        => string.Join(", ", order);

    public static IReadOnlyList<LeftPanelSection> Parse(string? text)
    {
        List<LeftPanelSection> result = [];
        foreach (string token in (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse(token, ignoreCase: true, out LeftPanelSection section) && !result.Contains(section))
            {
                result.Add(section);
            }
        }

        result.AddRange(DefaultOrder.Where(section => !result.Contains(section)));
        return result;
    }
}
