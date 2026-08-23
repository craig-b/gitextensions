namespace GitExtensions.Extensibility;

/// <summary>
///  Owner-scoped error reporting for engine components that run on background threads but must
///  surface failures to the user. Unlike <see cref="UserNotification"/>, which is a global
///  fire-and-forget funnel, an implementation is bound to a specific owner window and awaits
///  the user's acknowledgement.
/// </summary>
/// <remarks>
///  The WinForms implementation switches to the UI thread and shows an owned message box;
///  see <c>GitUI.ControlUserInteraction</c>.
/// </remarks>
public interface IUserInteraction
{
    /// <summary>
    ///  Presents an error message to the user and completes once it has been dismissed,
    ///  switching to the UI thread as required.
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <param name="caption">The message caption.</param>
    /// <param name="cancellationToken">A token cancelling the wait for the UI thread.</param>
    Task ShowErrorAsync(string text, string caption, CancellationToken cancellationToken = default);
}
