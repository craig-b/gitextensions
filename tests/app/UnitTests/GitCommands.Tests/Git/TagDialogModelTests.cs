using GitCommands.Git.Tag;
using GitExtensions.Extensibility.Git;
using GitCommandsBuilder = GitCommands.Git.Commands;

namespace GitCommandsTests.Git;

public sealed class TagDialogModelTests
{
    private static readonly ObjectId Commit = ObjectId.Parse("aaaa111111111111111111111111111111111111");
    private static readonly ObjectId Checkout = ObjectId.Parse("bbbb222222222222222222222222222222222222");

    [Test]
    public void Operation_choices_follow_the_dialog_order()
    {
        TagDialogModel.OperationChoices.Should().Equal(
            TagOperation.Lightweight, TagOperation.Annotate, TagOperation.SignWithDefaultKey, TagOperation.SignWithSpecificKey);
    }

    [Test]
    public void Target_normalization_degrades_artificial_and_zero_to_the_checkout()
    {
        TagDialogModel.NormalizeTarget(Commit, () => Checkout).Should().Be(Commit);
        TagDialogModel.NormalizeTarget(ObjectId.WorkTreeId, () => Checkout).Should().Be(Checkout);
        TagDialogModel.NormalizeTarget(ObjectId.Parse("0000000000000000000000000000000000000000"), () => Checkout).Should().Be(Checkout);
    }

    [TestCase(null, "origin")]
    [TestCase("", "origin")]
    [TestCase("upstream", "upstream")]
    public void Push_remote_falls_back_to_origin(string? currentRemote, string expected)
    {
        TagDialogModel.ResolvePushRemote(currentRemote).Should().Be(expected);
    }

    [TestCase(TagOperation.Lightweight, false, false)]
    [TestCase(TagOperation.Annotate, false, true)]
    [TestCase(TagOperation.SignWithDefaultKey, false, true)]
    [TestCase(TagOperation.SignWithSpecificKey, true, true)]
    public void Availability_follows_the_operation(TagOperation operation, bool gpgKey, bool message)
    {
        TagOptionAvailability.Evaluate(operation).Should().Be(new TagOptionAvailability(gpgKey, message));
    }

    [Test]
    public void Remote_tag_delete_pushes_an_empty_source()
    {
        GitCommandsBuilder.DeleteRemoteTag("origin", "v1.0").ToString().Should().Be("push \"origin\" :refs/tags/v1.0");
    }
}
