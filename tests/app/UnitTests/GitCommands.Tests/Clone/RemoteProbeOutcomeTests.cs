using GitCommands.Clone;

namespace GitCommandsTests.Clone;

public sealed class RemoteProbeOutcomeTests
{
    [TestCase(null)]
    [TestCase("")]
    public void No_error_output_is_success(string? errorOutput)
    {
        RemoteProbeOutcome.Classify(errorOutput).Should().Be(RemoteProbeStatus.Success);
    }

    [Test]
    public void Fatal_error_mentioning_authentication_is_an_authentication_failure()
    {
        RemoteProbeOutcome.Classify("FATAL ERROR: Disconnected: no supported authentication methods available")
            .Should().Be(RemoteProbeStatus.AuthenticationFailed);
    }

    [TestCase("FATAL ERROR: Server unexpectedly closed network connection")]
    [TestCase("remote: authentication required")]
    public void Either_authentication_marker_alone_is_a_plain_error(string errorOutput)
    {
        RemoteProbeOutcome.Classify(errorOutput).Should().Be(RemoteProbeStatus.Error);
    }

    [Test]
    public void Uncached_host_key_is_detected_case_insensitively()
    {
        RemoteProbeOutcome.Classify("The Server's Host Key Is Not Cached In The Registry.")
            .Should().Be(RemoteProbeStatus.HostKeyNotCached);
    }

    [Test]
    public void Any_other_output_is_a_plain_error()
    {
        RemoteProbeOutcome.Classify("fatal: repository 'https://host/missing.git' not found")
            .Should().Be(RemoteProbeStatus.Error);
    }
}
