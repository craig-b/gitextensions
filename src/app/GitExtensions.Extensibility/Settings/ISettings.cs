namespace GitExtensions.Extensibility.Settings;

/// <summary>
///  Injectable application settings for UI command handlers.
/// </summary>
/// <remarks>
///  This is the M3.4 strangler seam over the static <c>AppSettings</c> surface: handlers take
///  <see cref="ISettings"/> by constructor so they can be tested without touching global state,
///  while existing static call sites stay untouched and decay as code is migrated. Members are
///  added here as handlers need them - this is deliberately not a mirror of the 300+ statics.
///  (Not to be confused with <see cref="ISetting"/>, a single entry in a settings page.)
/// </remarks>
public interface ISettings
{
    bool DontConfirmSwitchWorktree { get; }

    bool? UpdateSubmodulesOnCheckout { get; }

    bool? DontConfirmUpdateSubmodulesOnCheckout { get; }

    bool UseBrowseForFileHistory { get; }
}
