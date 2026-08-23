namespace GitExtensions.Extensibility.Git.UICommands;

/// <summary>
///  Marker for a UI command intent: an immutable record describing a user-level operation
///  (typically "open dialog X with these arguments") as pure data.
/// </summary>
/// <remarks>
///  Intents deliberately carry no owner window; the executing host resolves the owner ambiently.
///  This mirrors <see cref="IGitCommand"/>, which does the same for git commands.
/// </remarks>
public interface IUICommand
{
}
