namespace GitExtensions.Extensibility.Git.UICommands;

/// <summary>
///  Executes <see cref="IUICommand"/> intents. The single entry point that replaces the
///  Start*Dialog method family: adding a new operation means adding a record, not a method.
/// </summary>
public interface IUICommandBus
{
    /// <summary>
    ///  Executes the given intent.
    /// </summary>
    /// <returns>
    ///  <see langword="true"/> if the operation ran and was not cancelled by the user;
    ///  fire-and-forget operations always return <see langword="true"/>.
    /// </returns>
    bool Execute(IUICommand command);
}
