using GitCommands.Commit;
using GitExtensions.Extensibility.Git;
using NSubstitute;

namespace GitCommandsTests.Commit;

public class PreviousCommitMessagesProviderTests
{
    private IGitModule _module = null!;

    [SetUp]
    public void Setup()
    {
        _module = Substitute.For<IGitModule>();
    }

    private void GitReturns(params string?[] messages)
        => _module.GetPreviousCommitMessages(Arg.Any<int>(), "HEAD", Arg.Any<string>()).Returns(messages);

    [Test]
    public void Messages_are_trimmed_and_blank_ones_dropped()
    {
        GitReturns("subject one\n\nbody\n", null, "   \n", "subject two\n");

        IReadOnlyList<PreviousCommitMessage> messages = PreviousCommitMessagesProvider.GetMessages(_module, lastCommitMessage: null, maxCount: 5, authorPattern: "");

        messages.Select(m => m.Message).Should().Equal("subject one\n\nbody", "subject two");
    }

    [Test]
    public void Label_is_the_first_line_shortened_to_72()
    {
        string longSubject = new('x', 100);
        GitReturns($"{longSubject}\nbody");

        IReadOnlyList<PreviousCommitMessage> messages = PreviousCommitMessagesProvider.GetMessages(_module, lastCommitMessage: null, maxCount: 5, authorPattern: "");

        messages.Single().Label.Should().HaveLength(72);
        messages.Single().Label.Should().StartWith("xxx");
        messages.Single().Message.Should().Be($"{longSubject}\nbody");
    }

    [Test]
    public void Unsaved_last_message_is_inserted_first()
    {
        GitReturns("committed one", "committed two");

        IReadOnlyList<PreviousCommitMessage> messages = PreviousCommitMessagesProvider.GetMessages(_module, lastCommitMessage: "work in progress", maxCount: 5, authorPattern: "");

        messages.Select(m => m.Message).Should().Equal("work in progress", "committed one", "committed two");
    }

    [Test]
    public void Last_message_already_known_to_git_is_not_duplicated()
    {
        GitReturns("committed one", "committed two");

        IReadOnlyList<PreviousCommitMessage> messages = PreviousCommitMessagesProvider.GetMessages(_module, lastCommitMessage: "committed one", maxCount: 5, authorPattern: "");

        messages.Select(m => m.Message).Should().Equal("committed one", "committed two");
    }

    [Test]
    public void Inserting_the_last_message_into_a_full_list_drops_the_oldest()
    {
        GitReturns("one", "two", "three");

        IReadOnlyList<PreviousCommitMessage> messages = PreviousCommitMessagesProvider.GetMessages(_module, lastCommitMessage: "unsaved", maxCount: 3, authorPattern: "");

        messages.Select(m => m.Message).Should().Equal("unsaved", "one", "two");
    }

    [Test]
    public void Author_pattern_is_an_escaped_exact_match()
    {
        PreviousCommitMessagesProvider.BuildAuthorPattern("First (Middle) Last", "user+tag@host.example")
            .Should().Be(@"^First\ \(Middle\)\ Last <user\+tag@host\.example>$");
    }
}
