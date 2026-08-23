using System.Text;
using GitCommands.Patches;

namespace GitCommandsTests.Patches;

public sealed class LinePatchPlannerTests
{
    private const string Diff =
        "diff --git a/f.txt b/f.txt\n" +
        "index 0000001..0000002 100644\n" +
        "--- a/f.txt\n" +
        "+++ b/f.txt\n" +
        "@@ -1,3 +1,3 @@\n" +
        " context\n" +
        "-old line\n" +
        "+new line\n" +
        " tail\n";

    private static (int Start, int Length) SelectHunkBody()
    {
        int start = Diff.IndexOf("-old line");
        return (start, Diff.IndexOf(" tail") - start);
    }

    [Test]
    public void Verbs_map_to_the_winforms_apply_flag_sets()
    {
        LinePatchPlanner.ApplyArguments(LinePatchVerb.Stage).ToString()
            .Should().Be("apply --cached --index --whitespace=nowarn");
        LinePatchPlanner.ApplyArguments(LinePatchVerb.Unstage).ToString()
            .Should().Be("apply --cached --index --whitespace=nowarn --reverse");
        LinePatchPlanner.ApplyArguments(LinePatchVerb.ResetIndex).ToString()
            .Should().Be("apply --whitespace=nowarn --reverse --index");
        LinePatchPlanner.ApplyArguments(LinePatchVerb.ResetWorkTree).ToString()
            .Should().Be("apply --whitespace=nowarn");
        LinePatchPlanner.ApplyArguments(LinePatchVerb.Apply).ToString()
            .Should().Be("apply --3way --index --whitespace=nowarn");
        LinePatchPlanner.ApplyArguments(LinePatchVerb.Revert).ToString()
            .Should().Be("apply --3way --index --whitespace=nowarn");
    }

    [Test]
    public void Stage_plan_builds_a_patch_for_the_selected_lines()
    {
        (int start, int length) = SelectHunkBody();

        LinePatchPlan? plan = LinePatchPlanner.Plan(LinePatchVerb.Stage, Diff, start, length, Encoding.UTF8);

        plan.Should().NotBeNull();
        string patch = Encoding.UTF8.GetString(plan!.Patch);
        patch.Should().Contain("--- a/f.txt").And.Contain("+++ b/f.txt");
        patch.Should().Contain("-old line").And.Contain("+new line");
    }

    [Test]
    public void Reset_worktree_plan_uses_the_reset_patch_builder()
    {
        (int start, int length) = SelectHunkBody();

        LinePatchPlan? plan = LinePatchPlanner.Plan(LinePatchVerb.ResetWorkTree, Diff, start, length, Encoding.UTF8);

        plan.Should().NotBeNull();
        // The reset patch is applied forward to restore the OLD content: +/- roles swap.
        string patch = Encoding.UTF8.GetString(plan!.Patch);
        patch.Should().Contain("+old line").And.Contain("-new line");
    }

    [Test]
    public void Header_only_selection_yields_no_plan()
    {
        LinePatchPlanner.Plan(LinePatchVerb.Stage, Diff, 0, 5, Encoding.UTF8).Should().BeNull();
    }
}
