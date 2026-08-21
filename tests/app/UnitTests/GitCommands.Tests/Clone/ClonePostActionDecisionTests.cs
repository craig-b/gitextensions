using GitCommands.Clone;

namespace GitCommandsTests.Clone;

public sealed class ClonePostActionDecisionTests
{
    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(false, false)]
    public void Protocol_handler_launch_opens_a_new_instance_regardless_of_hosting(bool isHostedDialog, bool hasAcquiredSubscribers)
    {
        ClonePostActionDecision.Decide(openedFromProtocolHandler: true, isHostedDialog, hasAcquiredSubscribers)
            .Should().Be(ClonePostAction.OpenInNewInstance);
    }

    [Test]
    public void Hosted_dialog_with_subscribers_announces_the_acquisition()
    {
        ClonePostActionDecision.Decide(openedFromProtocolHandler: false, isHostedDialog: true, hasAcquiredSubscribers: true)
            .Should().Be(ClonePostAction.AnnounceAcquired);
    }

    [Test]
    public void Hosted_dialog_without_subscribers_does_nothing()
    {
        ClonePostActionDecision.Decide(openedFromProtocolHandler: false, isHostedDialog: true, hasAcquiredSubscribers: false)
            .Should().Be(ClonePostAction.None);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Unhosted_dialog_does_nothing(bool hasAcquiredSubscribers)
    {
        ClonePostActionDecision.Decide(openedFromProtocolHandler: false, isHostedDialog: false, hasAcquiredSubscribers)
            .Should().Be(ClonePostAction.None);
    }
}
