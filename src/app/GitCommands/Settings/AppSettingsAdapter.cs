using GitExtensions.Extensibility.Settings;

namespace GitCommands.Settings;

/// <summary>
///  Default <see cref="ISettings"/> implementation, reading the same backing store as the
///  <see cref="AppSettings"/> statics so both surfaces always agree.
/// </summary>
public sealed class AppSettingsAdapter : ISettings
{
    public bool DontConfirmSwitchWorktree => AppSettings.DontConfirmSwitchWorktree;

    public bool? UpdateSubmodulesOnCheckout => AppSettings.UpdateSubmodulesOnCheckout;

    public bool? DontConfirmUpdateSubmodulesOnCheckout => AppSettings.DontConfirmUpdateSubmodulesOnCheckout;

    public bool UseBrowseForFileHistory => AppSettings.UseBrowseForFileHistory.Value;
}
