namespace GitCommands.Clone;

public enum RemoteProbeStatus
{
    Success,

    /// <summary>Authentication failed for want of a key; the remediation is to ask the user for one and retry.</summary>
    AuthenticationFailed,

    /// <summary>The server's host key is not cached; the remediation is to offer caching it and retry.</summary>
    HostKeyNotCached,

    /// <summary>Any other failure; surfaced as an error, no retry.</summary>
    Error,
}

/// <summary>
///  Classifies the error output of a remote refs probe (<c>IGitModule.GetRemoteServerRefs</c>)
///  into the retry-able failure kinds the clone dialog has always string-sniffed.
/// </summary>
public static class RemoteProbeOutcome
{
    public static RemoteProbeStatus Classify(string? errorOutput)
    {
        if (string.IsNullOrEmpty(errorOutput))
        {
            return RemoteProbeStatus.Success;
        }

        if (errorOutput.Contains("FATAL ERROR") && errorOutput.Contains("authentication"))
        {
            return RemoteProbeStatus.AuthenticationFailed;
        }

        if (errorOutput.Contains("the server's host key is not cached in the registry", StringComparison.InvariantCultureIgnoreCase))
        {
            return RemoteProbeStatus.HostKeyNotCached;
        }

        return RemoteProbeStatus.Error;
    }
}
