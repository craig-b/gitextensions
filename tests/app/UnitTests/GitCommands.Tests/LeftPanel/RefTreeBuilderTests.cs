using GitCommands.LeftPanel;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
using NSubstitute;

namespace GitCommandsTests.LeftPanel;

public class RefTreeBuilderTests
{
    private static IGitRef Ref(string name)
    {
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.LocalName.Returns(name);
        gitRef.ObjectId.Returns(ObjectId.Parse("aaaa000000000000000000000000000000000000"));
        return gitRef;
    }

    private static string Render(IEnumerable<RefTreeNode> nodes)
        => string.Join(",", nodes.Select(n => n.Children.Count == 0 ? n.Name : $"{n.Name}({Render(n.Children)})"));

    [Test]
    public void Builds_the_documented_hierarchy()
    {
        // The example from LocalBranchTree.FillTree's doc comment.
        string[] names =
        [
            "a-branch",
            "develop/crazy-branch",
            "develop/features/feat-next",
            "develop/features/feat-next2",
            "develop/issues/iss444",
            "develop/wild-branch",
            "issues/iss111",
            "master",
        ];

        IReadOnlyList<RefTreeNode> roots = RefTreeBuilder.Build(names.Select(Ref), r => r.LocalName, prioritySetting: "");

        Render(roots).Should().Be("a-branch,develop(crazy-branch,features(feat-next,feat-next2),issues(iss444),wild-branch),issues(iss111),master");
    }

    [Test]
    public void Priority_setting_reorders_and_folders_follow_first_need()
    {
        IReadOnlyList<RefTreeNode> roots = RefTreeBuilder.Build(
            new[] { "feature/x", "master", "develop/y" }.Select(Ref),
            r => r.LocalName,
            prioritySetting: "master;develop/.*");

        Render(roots).Should().Be("master,develop(y),feature(x)");
    }

    [Test]
    public void Leaves_carry_the_object_id_and_current_marker()
    {
        IReadOnlyList<RefTreeNode> roots = RefTreeBuilder.Build(
            new[] { "main", "dev" }.Select(Ref), r => r.LocalName, "", currentRefName: "main");

        roots.Single(n => n.Name == "main").IsCurrent.Should().BeTrue();
        roots.Single(n => n.Name == "main").ObjectId.Should().NotBeNull();
        roots.Single(n => n.Name == "dev").IsCurrent.Should().BeFalse();
    }

    [Test]
    public void A_folder_name_and_a_leaf_name_can_coexist()
    {
        // A ref "a" and a ref "a/b": the folder and the leaf are separate nodes.
        IReadOnlyList<RefTreeNode> roots = RefTreeBuilder.Build(
            new[] { "a", "a/b" }.Select(Ref), r => r.LocalName, "");

        Render(roots).Should().Be("a,a(b)");
    }
}
