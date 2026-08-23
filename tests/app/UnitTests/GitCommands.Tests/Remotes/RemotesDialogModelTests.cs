using GitCommands.Remotes;

namespace GitCommandsTests.Remotes;

public sealed class RemotesDialogModelTests
{
    [Test]
    public void Editor_fields_for_a_new_remote_are_blank()
    {
        RemoteEditorFields fields = RemoteEditorFields.FromRemote(null);

        fields.IsNew.Should().BeTrue();
        fields.CanSave.Should().BeFalse();
        fields.SeparatePushUrl.Should().BeFalse();
    }

    [Test]
    public void Editor_fields_map_the_remote_and_derive_the_separate_push_url()
    {
        ConfigFileRemote remote = new()
        {
            Name = "origin",
            Url = "https://host/a.git",
            PushUrl = "ssh://host/a.git",
            Color = "  ",
            Disabled = true,
        };

        RemoteEditorFields fields = RemoteEditorFields.FromRemote(remote);

        fields.Should().Be(new RemoteEditorFields(
            "origin", "https://host/a.git", "ssh://host/a.git", SeparatePushUrl: true, "", Color: null, IsNew: false, IsDisabled: true));
        fields.CanSave.Should().BeTrue();
    }

    [TestCase("", true, false)]
    [TestCase("ssh://other/repo", true, true)]
    [TestCase("url", true, false, "url")]
    public void Save_request_collapses_redundant_push_urls(string pushUrl, bool separate, bool expectedSeparate, string url = "URL")
    {
        RemoteSaveRequest request = RemoteSaveRequest.Normalize(" origin ", $" {url} ", $" {pushUrl} ", separate);

        request.Name.Should().Be("origin");
        request.Url.Should().Be(url);
        request.SeparatePushUrl.Should().Be(expectedSeparate);
        request.PushUrl.Should().Be(expectedSeparate ? pushUrl : null);
    }

    [Test]
    public void Save_request_case_insensitive_push_url_collapse()
    {
        RemoteSaveRequest.Normalize("o", "https://host/repo", "HTTPS://HOST/REPO", separatePushUrl: true)
            .SeparatePushUrl.Should().BeFalse();
        RemoteSaveRequest.Normalize("o", "https://host/repo", "https://host/other", separatePushUrl: true)
            .SeparatePushUrl.Should().BeTrue();
    }

    [Test]
    public void Name_conflicts_are_checked_only_for_new_remotes()
    {
        RemoteNameValidator.Check("origin", isNew: true, _ => true, _ => false).Should().Be(RemoteNameConflict.EnabledExists);
        RemoteNameValidator.Check("origin", isNew: true, _ => false, _ => true).Should().Be(RemoteNameConflict.DisabledExists);
        RemoteNameValidator.Check("origin", isNew: true, _ => false, _ => false).Should().Be(RemoteNameConflict.None);
        RemoteNameValidator.Check("origin", isNew: false, _ => true, _ => true).Should().Be(RemoteNameConflict.None);
    }

    [Test]
    public void Save_reaction_message_stops_everything()
    {
        RemoteSaveReaction reaction = RemoteSaveReaction.Evaluate(
            new ConfigFileRemoteSaveResult("git said no", shouldUpdateRemote: true), "url", separatePushUrl: true);

        reaction.ShowMessage.Should().BeTrue();
        reaction.Message.Should().Be("git said no");
        reaction.UpdateUrlHistory.Should().BeFalse();
        reaction.OfferConfigureAndFetch.Should().BeFalse();
    }

    [Test]
    public void Save_reaction_success_updates_history_and_gates_the_prompt()
    {
        RemoteSaveReaction reaction = RemoteSaveReaction.Evaluate(
            new ConfigFileRemoteSaveResult(null, shouldUpdateRemote: true), "url", separatePushUrl: false);

        reaction.ShowMessage.Should().BeFalse();
        reaction.UpdateUrlHistory.Should().BeTrue();
        reaction.UpdatePushUrlHistory.Should().BeFalse();
        reaction.OfferConfigureAndFetch.Should().BeTrue();

        RemoteSaveReaction.Evaluate(new ConfigFileRemoteSaveResult(null, shouldUpdateRemote: true), "", separatePushUrl: false)
            .OfferConfigureAndFetch.Should().BeFalse();
        RemoteSaveReaction.Evaluate(new ConfigFileRemoteSaveResult(null, shouldUpdateRemote: false), "url", separatePushUrl: false)
            .OfferConfigureAndFetch.Should().BeFalse();
    }

    [Test]
    public void List_selection_prefers_the_preselect_then_first_enabled()
    {
        ConfigFileRemote disabled = new() { Name = "old", Disabled = true };
        ConfigFileRemote enabled = new() { Name = "origin" };

        RemoteListSelection.Resolve([disabled, enabled], preselectRemote: "old").SelectedIndex.Should().Be(0);
        RemoteListSelection.Resolve([disabled, enabled], preselectRemote: null).SelectedIndex.Should().Be(1);
        RemoteListSelection.Resolve([disabled], preselectRemote: null).SelectedIndex.Should().Be(0);
    }

    [Test]
    public void Empty_list_disables_actions_and_focuses_the_name_box()
    {
        RemoteListSelection.Resolve([], preselectRemote: null)
            .Should().Be(new RemoteListSelection(SelectedIndex: -1, DeleteEnabled: false, ToggleEnabled: false, FocusNameBox: true));
    }

    [Test]
    public void Url_candidates_splice_the_typed_name_over_names_and_owners()
    {
        ConfigFileRemote origin = new() { Name = "upstream", Url = "https://github.com/upstream/repo.git" };

        var (candidates, fillEmptyUrl) = RemoteUrlSuggestions.GenerateCandidates(
            [origin], remote => remote.Url, "craig", RemoteUrlSuggestions.GenericRemoteNames([]));

        fillEmptyUrl.Should().BeTrue();
        candidates.Should().Contain("https://github.com/craig/repo.git");
    }

    [Test]
    public void Generic_typed_names_yield_placeholders_that_must_not_autofill()
    {
        ConfigFileRemote origin = new() { Name = "upstream", Url = "https://github.com/upstream/repo.git" };

        var (candidates, fillEmptyUrl) = RemoteUrlSuggestions.GenerateCandidates(
            [origin], remote => remote.Url, "origin", RemoteUrlSuggestions.GenericRemoteNames([]));

        fillEmptyUrl.Should().BeFalse();
        candidates.Should().Contain("https://github.com/TO_REPLACE/repo.git");
    }

    [Test]
    public void Remote_name_infers_from_a_hosting_url()
    {
        RemoteUrlSuggestions.TryInferNameFromUrl("https://github.com/craig-b/gitextensions.git").Should().Be("craig-b");
        RemoteUrlSuggestions.TryInferNameFromUrl("not a url").Should().BeNull();
    }
}
