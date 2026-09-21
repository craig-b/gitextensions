using CommonTestUtils;
using GitUI;
using GitUI.CommandsDialogs.BrowseDialog;

namespace GitExtensions.UITests.CommandsDialogs;

[Apartment(ApartmentState.STA)]
public class FormUpdatesTests
{
    // Created once for the fixture
    private ReferenceRepository _referenceRepository = null!;

    // Created once for each test
    private GitUICommands _commands = null!;

    [SetUp]
    public void SetUp()
    {
        _referenceRepository = new ReferenceRepository();
        _commands = new GitUICommands(GlobalServiceContainer.CreateDefaultMockServiceContainer(), _referenceRepository.Module);
    }

    [TearDown]
    public void TearDown()
    {
        _referenceRepository.Dispose();
    }

    [Test]
    public async Task Should_offer_the_update_only_where_it_can_be_carried_out()
    {
        await RunFormTestAsync(async form =>
        {
            FormUpdates.TestAccessor accessor = form.GetTestAccessor();
            accessor.State = UpdateState.UpdateAvailable;
            accessor.LatestTag = "wine-v12";
            accessor.UpdateProgram = @"Z:\opt\gitext-wine\update";
            await accessor.RenderAsync();

            accessor.UpdateNowButton.Visible.Should().BeTrue();
            accessor.DirectDownloadLink.Visible.Should().BeTrue();
            accessor.LabelText.Should().Contain("wine-v12");
        });
    }

    [Test]
    public async Task Should_offer_only_the_download_where_there_is_no_helper()
    {
        await RunFormTestAsync(async form =>
        {
            FormUpdates.TestAccessor accessor = form.GetTestAccessor();
            accessor.State = UpdateState.UpdateAvailable;
            accessor.LatestTag = "wine-v12";
            accessor.UpdateProgram = string.Empty;
            await accessor.RenderAsync();

            accessor.UpdateNowButton.Visible.Should().BeFalse();
            accessor.DirectDownloadLink.Visible.Should().BeTrue();
        });
    }

    [Test]
    public async Task Should_say_so_when_the_check_could_not_run()
    {
        await RunFormTestAsync(async form =>
        {
            FormUpdates.TestAccessor accessor = form.GetTestAccessor();
            accessor.State = UpdateState.CheckFailed;
            await accessor.RenderAsync();

            // The check this replaced left the dialog searching forever when it could not reach
            // GitHub, so the terminal state is the point of this test.
            accessor.UpdateNowButton.Visible.Should().BeFalse();
            accessor.LabelText.Should().NotBeEmpty();
        });
    }

    [Test]
    public async Task Should_not_offer_an_update_to_a_build_that_is_not_a_release()
    {
        await RunFormTestAsync(async form =>
        {
            FormUpdates.TestAccessor accessor = form.GetTestAccessor();
            accessor.State = UpdateState.NotFromRelease;
            accessor.LatestTag = "wine-v12";
            accessor.UpdateProgram = @"Z:\opt\gitext-wine\update";
            await accessor.RenderAsync();

            // A development overlay compares as older than every release; offering it an update
            // that cannot apply would mean a dialog on every weekly check.
            accessor.UpdateNowButton.Visible.Should().BeFalse();
            accessor.LabelText.Should().Contain("wine-v12");
        });
    }

    private async Task RunFormTestAsync(Func<FormUpdates, Task> testDriverAsync)
    {
        await Task.CompletedTask;
        UITest.RunForm(
            () =>
            {
                using FormUpdates form = new(new Version(4, 2, 0));
                form.ShowDialog(owner: null);
            },
            testDriverAsync);
    }
}
