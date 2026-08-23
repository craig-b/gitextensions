using GitCommands;
using GitCommands.Git;
using GitCommands.LeftPanel;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;
using NSubstitute;
// the sibling GitCommandsTests.Remote test namespace shadows the type name here
using GitRemote = GitExtensions.Extensibility.Git.Remote;

namespace GitCommandsTests.LeftPanel;

public class RemoteTreeBuilderTests
{
    private static IGitRef RemoteRef(string name)
    {
        IGitRef gitRef = Substitute.For<IGitRef>();
        gitRef.Name.Returns(name);
        gitRef.LocalName.Returns(name);
        gitRef.CompleteName.Returns($"refs/remotes/{name}");
        gitRef.ObjectId.Returns(ObjectId.Parse("aaaa000000000000000000000000000000000000"));
        return gitRef;
    }

    private static GitRemote NamedRemote(string name) => new(name, fetchUrl: $"https://example.test/{name}.git", firstPushUrl: $"https://example.test/{name}.git");

    private static string Render(IEnumerable<RefTreeNode> nodes)
        => string.Join(",", nodes.Select(node =>
        {
            string label = node.Kind switch
            {
                RefTreeNodeKind.RemoteRepo => node.Enabled ? $"R:{node.Name}" : $"r:{node.Name}",
                RefTreeNodeKind.InactiveGroup => "INACTIVE",
                RefTreeNodeKind.RemoteBranch => node.Name,
                _ => $"{node.Name}/",
            };
            return node.Children.Count == 0 ? label : $"{label}({Render(node.Children)})";
        }));

    [Test]
    public void Builds_remote_roots_with_folded_branches()
    {
        IReadOnlyList<RefTreeNode> roots = RemoteTreeBuilder.Build(
            [RemoteRef("origin/main"), RemoteRef("origin/feature/x"), RemoteRef("upstream/main")],
            [NamedRemote("origin"), NamedRemote("upstream")],
            disabledRemotes: [],
            branchPrioritySetting: "",
            remotePrioritySetting: "");

        Render(roots).Should().Be("R:origin(main,feature/(x)),R:upstream(main)");
    }

    [Test]
    public void Branches_of_unconfigured_remotes_are_dropped()
    {
        IReadOnlyList<RefTreeNode> roots = RemoteTreeBuilder.Build(
            [RemoteRef("origin/main"), RemoteRef("gone/main")],
            [NamedRemote("origin")],
            disabledRemotes: [],
            branchPrioritySetting: "",
            remotePrioritySetting: "");

        Render(roots).Should().Be("R:origin(main)");
    }

    [Test]
    public void Remotes_without_branches_still_appear()
    {
        IReadOnlyList<RefTreeNode> roots = RemoteTreeBuilder.Build(
            [RemoteRef("origin/main")],
            [NamedRemote("origin"), NamedRemote("empty")],
            disabledRemotes: [],
            branchPrioritySetting: "",
            remotePrioritySetting: "");

        Render(roots).Should().Be("R:empty,R:origin(main)", because: "remotes order alphabetically without priorities");
    }

    [Test]
    public void Disabled_remotes_gather_under_a_trailing_group()
    {
        IReadOnlyList<RefTreeNode> roots = RemoteTreeBuilder.Build(
            [RemoteRef("origin/main")],
            [NamedRemote("origin")],
            disabledRemotes: [NamedRemote("old-fork"), NamedRemote("archive")],
            branchPrioritySetting: "",
            remotePrioritySetting: "");

        Render(roots).Should().Be("R:origin(main),INACTIVE(r:archive,r:old-fork)");
    }

    [Test]
    public void Remote_priority_setting_orders_roots()
    {
        IReadOnlyList<RefTreeNode> roots = RemoteTreeBuilder.Build(
            [RemoteRef("origin/main"), RemoteRef("upstream/main"), RemoteRef("fork/main")],
            [NamedRemote("origin"), NamedRemote("upstream"), NamedRemote("fork")],
            disabledRemotes: [],
            branchPrioritySetting: "",
            remotePrioritySetting: "upstream");

        Render(roots).Should().Be("R:upstream(main),R:fork(main),R:origin(main)");
    }

    [Test]
    public void Ahead_behind_decorates_the_tracked_branch()
    {
        Dictionary<string, AheadBehindData> aheadBehind = new()
        {
            ["refs/remotes/origin/main"] = new AheadBehindData("main", "refs/remotes/origin/main", "1", "2"),
        };

        IReadOnlyList<RefTreeNode> roots = RemoteTreeBuilder.Build(
            [RemoteRef("origin/main"), RemoteRef("origin/other")],
            [NamedRemote("origin")],
            disabledRemotes: [],
            branchPrioritySetting: "",
            remotePrioritySetting: "",
            aheadBehind);

        RefTreeNode main = roots.Single().Children.Single(node => node.Name == "main");
        main.AheadBehindDisplay.Should().NotBeNullOrEmpty();
        main.RelatedBranch.Should().Be($"{GitRefName.RefsHeadsPrefix}main");
        roots.Single().Children.Single(node => node.Name == "other").AheadBehindDisplay.Should().BeNull();
    }

    [Test]
    public void Zero_object_id_throws()
    {
        IGitRef broken = Substitute.For<IGitRef>();
        broken.Name.Returns("origin/broken");
        broken.LocalName.Returns("origin/broken");
        broken.ObjectId.Returns(ObjectId.Parse(new string('0', 40)));

        Action build = () => RemoteTreeBuilder.Build(
            [broken],
            [NamedRemote("origin")],
            disabledRemotes: [],
            branchPrioritySetting: "",
            remotePrioritySetting: "");

        build.Should().Throw<InvalidOperationException>();
    }
}
