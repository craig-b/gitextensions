namespace GitExtensions.Extensibility.Git.UICommands;

/// <summary>
///  Executes <see cref="IUICommand"/> intents. The single entry point that replaces the
///  Start*Dialog method family: adding a new operation means adding a record, not a method.
/// </summary>
public interface IUICommandBus
{
    /// <summary>
    ///  Executes the given intent. The owner window for any dialogs is resolved ambiently
    ///  by the host.
    /// </summary>
    /// <returns>
    ///  <see langword="true"/> if the operation ran and was not cancelled by the user;
    ///  fire-and-forget operations always return <see langword="true"/>.
    /// </returns>
    bool Execute(IUICommand command);

    /// <summary>
    ///  Executes the given intent with an explicit owner window for any dialogs.
    /// </summary>
    /// <param name="command">The intent to execute.</param>
    /// <param name="ownerWindow">
    ///  The host-specific owner window (an <c>IWin32Window</c> in the WinForms host); the host
    ///  casts. <see langword="null"/> means "no owner window" — it is NOT resolved ambiently.
    /// </param>
    /// <returns>
    ///  <see langword="true"/> if the operation ran and was not cancelled by the user;
    ///  fire-and-forget operations always return <see langword="true"/>.
    /// </returns>
    bool Execute(IUICommand command, object? ownerWindow);
}
