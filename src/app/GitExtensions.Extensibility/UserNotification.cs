using System.Diagnostics;

namespace GitExtensions.Extensibility;

/// <summary>
///  Platform-neutral funnel for the few places where the engine must surface an error to the user
///  outside any UI flow.
/// </summary>
/// <remarks>
///  The WinForms hosts route this to <c>MessageBoxes</c> at startup (see GitExtensions.Program);
///  the default only traces, which suits tests and non-UI hosts.
/// </remarks>
public static class UserNotification
{
    /// <summary>
    ///  Displays an error message to the user. Arguments are the message text and an optional
    ///  caption.
    /// </summary>
    public static Action<string, string?> ShowError { get; set; }
        = static (text, caption) => Trace.TraceError(caption is null ? text : $"{caption}: {text}");
}
