using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Settings;

namespace GitExtensions.Extensibility.Plugins;

public interface IGitPlugin
{
    Guid Id { get; }

    string? Name { get; }

    string? Description { get; }

    /// <summary>
    ///  The plugin's icon (an <c>Image</c> in the WinForms host).
    /// </summary>
    object? Icon { get; }

    IGitPluginSettingsContainer? SettingsContainer { get; set; }

    bool HasSettings { get; }

    IEnumerable<ISetting> GetSettings();

    void Register(IGitUICommands gitUiCommands);

    void Unregister(IGitUICommands gitUiCommands);

    /// <summary>
    /// Runs the plugin and returns whether the RevisionGrid should be refreshed.
    /// </summary>
    bool Execute(GitUIEventArgs args);
}
