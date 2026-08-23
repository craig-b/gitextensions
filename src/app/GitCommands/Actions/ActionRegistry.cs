namespace GitCommands.Actions;

public enum ActionTier
{
    /// <summary>Shown in Simple mode.</summary>
    Core,

    /// <summary>Added by Normal mode.</summary>
    Common,

    /// <summary>Nested/rendered as advanced by Normal mode; palette-only in Simple mode.</summary>
    Advanced,
}

public enum MenuProfileMode
{
    Simple,
    Normal,
    Custom,
}

public enum InapplicableItemPolicy
{
    /// <summary>Keep inapplicable items visible but disabled - positional stability.</summary>
    Gray,

    /// <summary>Hide inapplicable items - shorter menus.</summary>
    Hide,
}

/// <summary>
///  One user-invokable action: context menus, toolbars, and the command palette all
///  project from these, so they can never drift apart.
/// </summary>
public sealed record ActionDescriptor(
    string Id,
    string Caption,
    string Group,
    ActionTier Tier = ActionTier.Common,
    bool Destructive = false,
    string? Hotkey = null);

/// <summary>The user's menu configuration; Custom carries an explicit id order (which IS pinning).</summary>
public sealed record MenuProfile(
    MenuProfileMode Mode = MenuProfileMode.Normal,
    InapplicableItemPolicy InapplicablePolicy = InapplicableItemPolicy.Gray,
    IReadOnlyList<string>? CustomOrder = null)
{
    public static MenuProfile Normal { get; } = new();

    public static MenuProfile Simple { get; } = new(MenuProfileMode.Simple);
}

/// <summary>A projected row: the action with its applicability resolved.</summary>
public sealed record ProjectedMenuItem(ActionDescriptor Action, bool Enabled);

/// <summary>
///  Projects a declared action list into displayable groups: tier-filtered by the profile,
///  applicability resolved per item, inapplicable items grayed or hidden per policy, group
///  boundaries preserved (views render them as separators or submenus). Empty groups vanish.
/// </summary>
public static class MenuProjector
{
    public static IReadOnlyList<IReadOnlyList<ProjectedMenuItem>> Project(
        IReadOnlyList<ActionDescriptor> actions,
        MenuProfile profile,
        Func<ActionDescriptor, bool> isApplicable)
    {
        IEnumerable<ActionDescriptor> selected = profile.Mode switch
        {
            MenuProfileMode.Simple => actions.Where(action => action.Tier is ActionTier.Core),
            MenuProfileMode.Custom when profile.CustomOrder is not null =>
                profile.CustomOrder
                    .Select(id => actions.FirstOrDefault(action => action.Id == id))
                    .OfType<ActionDescriptor>(),
            _ => actions,
        };

        List<IReadOnlyList<ProjectedMenuItem>> groups = [];
        List<ProjectedMenuItem> currentGroup = [];
        string? currentGroupName = null;

        foreach (ActionDescriptor action in selected)
        {
            if (action.Group != currentGroupName && currentGroup.Count > 0)
            {
                groups.Add(currentGroup);
                currentGroup = [];
            }

            currentGroupName = action.Group;

            bool applicable = isApplicable(action);
            if (!applicable && profile.InapplicablePolicy is InapplicableItemPolicy.Hide)
            {
                continue;
            }

            currentGroup.Add(new ProjectedMenuItem(action, applicable));
        }

        if (currentGroup.Count > 0)
        {
            groups.Add(currentGroup);
        }

        return groups;
    }
}
