using GitCommands.UserRepositoryHistory;

namespace GitCommands.Dashboard;

public enum CategoryNameValidation
{
    Ok,
    Empty,
    Duplicate,
}

/// <summary>
///  Category name rules. Duplicates are judged with the same culture-aware comparer the dashboard
///  groups with (<see cref="DashboardList.CategoryComparer"/>) — historically validation compared
///  ordinally while grouping compared by culture, so two "duplicates" could form one group.
/// </summary>
public static class CategoryNameValidator
{
    public static CategoryNameValidation Validate(string? name, IEnumerable<string> existingCategories)
    {
        if (string.IsNullOrEmpty(name))
        {
            return CategoryNameValidation.Empty;
        }

        return existingCategories.Contains(name, DashboardList.CategoryComparer) ? CategoryNameValidation.Duplicate : CategoryNameValidation.Ok;
    }
}

/// <summary>
///  The dashboard's category mutations over the (injected) repository manager. Deleting a
///  category un-favourites its members (a favourite IS a repository with a category); it never
///  deletes repositories.
/// </summary>
public static class CategoryCommands
{
    public static async Task AssignAsync(ILocalRepositoryManager manager, Repository repository, string? category)
        => await manager.AssignCategoryAsync(repository, category);

    public static async Task RenameAsync(ILocalRepositoryManager manager, IEnumerable<Repository> repositories, string? originalName, string? newName)
    {
        foreach (Repository repository in repositories.Where(repo => repo.Category == originalName))
        {
            await manager.AssignCategoryAsync(repository, newName);
        }
    }

    public static Task DeleteAsync(ILocalRepositoryManager manager, IEnumerable<Repository> repositories, string categoryName)
        => RenameAsync(manager, repositories, categoryName, newName: null);

    public static async Task ClearRecentAsync(ILocalRepositoryManager manager, IEnumerable<Repository> repositories)
    {
        foreach (Repository repository in repositories)
        {
            await manager.RemoveRecentAsync(repository.Path);
        }
    }
}
