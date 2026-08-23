using GitCommands.LeftPanel;

namespace GitCommandsTests.LeftPanel;

public class RefPriorityOrderTests
{
    [Test]
    public void Empty_setting_keeps_the_original_order()
    {
        RefPriorityOrder.OrderByPriority(["b", "a", "c"], name => name, "")
            .Should().Equal("b", "a", "c");
    }

    [Test]
    public void Matches_sort_first_in_regex_order()
    {
        RefPriorityOrder.OrderByPriority(["feature/x", "master", "develop", "release/1"], name => name, "master;develop")
            .Should().Equal("master", "develop", "feature/x", "release/1");
    }

    [Test]
    public void Regexes_are_anchored_to_the_whole_name()
    {
        // "master" must not match "master-old" - the setting is wrapped as ^(...)$.
        RefPriorityOrder.OrderByPriority(["master-old", "master"], name => name, "master")
            .Should().Equal("master", "master-old");
    }

    [Test]
    public void Wildcards_in_the_setting_work()
    {
        RefPriorityOrder.OrderByPriority(["feature/x", "hotfix/1", "main"], name => name, @"hotfix/.*")
            .Should().Equal("hotfix/1", "feature/x", "main");
    }

    [Test]
    public void Non_matches_keep_their_relative_order()
    {
        RefPriorityOrder.OrderByPriority(["z", "y", "main", "x"], name => name, "main")
            .Should().Equal("main", "z", "y", "x");
    }
}
