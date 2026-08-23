using GitCommands.Dashboard;
using GitCommands.UserRepositoryHistory;
using NSubstitute;

namespace GitCommandsTests.Dashboard;
public class CategoryCommandsTests
{
    private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\" : "/";

    private static Repository Repo(string name, string? category = null)
        => new(Path.Combine(_root, "repos", name)) { Category = category };

    #region CategoryNameValidator
    [TestCase(null)]
    [TestCase("")]
    public void Validate_should_return_Empty_for_null_or_empty_name(string? name)
    {
        CategoryNameValidator.Validate(name, ["Tools"]).Should().Be(CategoryNameValidation.Empty);
    }

    [Test]
    public void Validate_should_return_Duplicate_for_an_existing_name()
    {
        CategoryNameValidator.Validate("Tools", ["Work", "Tools"]).Should().Be(CategoryNameValidation.Duplicate);
    }

    [Test]
    public void Validate_should_judge_duplicates_with_the_culture_aware_comparer_not_ordinally()
    {
        // U+00AD (soft hyphen) is an ignorable character for linguistic comparison (both ICU and
        // NLS), so a culture-aware comparer sees "To\u00adols" as "Tools" while an ordinal one
        // would not - proving the validator uses DashboardList.CategoryComparer.
        const string nameWithSoftHyphen = "To\u00adols";

        string.Equals(nameWithSoftHyphen, "Tools", StringComparison.Ordinal).Should().BeFalse();
        CategoryNameValidator.Validate(nameWithSoftHyphen, ["Tools"]).Should().Be(CategoryNameValidation.Duplicate);
    }

    [Test]
    public void Validate_should_return_Ok_for_a_new_name()
    {
        CategoryNameValidator.Validate("Fresh", ["Work", "Tools"]).Should().Be(CategoryNameValidation.Ok);
    }
    #endregion

    #region CategoryCommands
    [Test]
    public async Task AssignAsync_should_delegate_to_the_manager()
    {
        ILocalRepositoryManager manager = Substitute.For<ILocalRepositoryManager>();
        Repository repository = Repo("alpha");

        await CategoryCommands.AssignAsync(manager, repository, "Tools");

        await manager.Received(1).AssignCategoryAsync(repository, "Tools");
        manager.ReceivedCalls().Should().ContainSingle();
    }

    [Test]
    public async Task RenameAsync_should_assign_the_new_name_to_exactly_the_repos_in_the_original_category()
    {
        ILocalRepositoryManager manager = Substitute.For<ILocalRepositoryManager>();
        Repository inCategory1 = Repo("alpha", category: "Old");
        Repository otherCategory = Repo("beta", category: "Other");
        Repository inCategory2 = Repo("gamma", category: "Old");
        Repository noCategory = Repo("delta");

        await CategoryCommands.RenameAsync(manager, [inCategory1, otherCategory, inCategory2, noCategory], "Old", "New");

        await manager.Received(1).AssignCategoryAsync(inCategory1, "New");
        await manager.Received(1).AssignCategoryAsync(inCategory2, "New");
        manager.ReceivedCalls().Should().HaveCount(2);
    }

    [Test]
    public async Task DeleteAsync_should_rename_the_category_members_to_null()
    {
        ILocalRepositoryManager manager = Substitute.For<ILocalRepositoryManager>();
        Repository inCategory = Repo("alpha", category: "Old");
        Repository otherCategory = Repo("beta", category: "Other");

        await CategoryCommands.DeleteAsync(manager, [inCategory, otherCategory], "Old");

        await manager.Received(1).AssignCategoryAsync(inCategory, null);
        manager.ReceivedCalls().Should().ContainSingle();
    }

    [Test]
    public async Task ClearRecentAsync_should_remove_each_repo_path_from_the_recent_list()
    {
        ILocalRepositoryManager manager = Substitute.For<ILocalRepositoryManager>();
        Repository first = Repo("alpha");
        Repository second = Repo("beta");

        await CategoryCommands.ClearRecentAsync(manager, [first, second]);

        await manager.Received(1).RemoveRecentAsync(first.Path);
        await manager.Received(1).RemoveRecentAsync(second.Path);
        manager.ReceivedCalls().Should().HaveCount(2);
    }
    #endregion
}
