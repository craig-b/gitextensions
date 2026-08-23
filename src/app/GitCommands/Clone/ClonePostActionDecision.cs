namespace GitCommands.Clone;

public enum ClonePostAction
{
    /// <summary>Close silently; nobody could act on the new repository.</summary>
    None,

    /// <summary>Protocol-handler launch: open the clone in a fresh Browse window.</summary>
    OpenInNewInstance,

    /// <summary>Hosted as a dialog with an acquisition subscriber: announce the new repository.</summary>
    AnnounceAcquired,
}

/// <summary>
///  What "open the cloned repository?" should do when the user says yes. Asking the question at
///  all only makes sense for the first two outcomes — the view confirms, then performs.
/// </summary>
public static class ClonePostActionDecision
{
    public static ClonePostAction Decide(bool openedFromProtocolHandler, bool isHostedDialog, bool hasAcquiredSubscribers)
    {
        if (openedFromProtocolHandler)
        {
            return ClonePostAction.OpenInNewInstance;
        }

        return isHostedDialog && hasAcquiredSubscribers ? ClonePostAction.AnnounceAcquired : ClonePostAction.None;
    }
}
