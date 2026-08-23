using GitExtensions.Extensibility.Git.UICommands;

namespace GitUI.UICommandHandlers;

/// <summary>
///  Executes one <see cref="IUICommand"/> intent in the WinForms host.
/// </summary>
/// <remarks>
///  Handlers are host implementation detail, deliberately not part of the portable contract:
///  a different front end ships its own handlers for the same intents. The owner window is
///  execution context resolved by the host (explicitly at legacy call sites, ambiently on the
///  <see cref="IUICommandBus"/> path), never part of the intent itself.
/// </remarks>
internal interface IUICommandHandler<in TCommand>
    where TCommand : IUICommand
{
    /// <returns>
    ///  <see langword="true"/> if the operation ran and was not cancelled by the user;
    ///  fire-and-forget operations always return <see langword="true"/>.
    /// </returns>
    bool Execute(TCommand command, IWin32Window? owner);
}
