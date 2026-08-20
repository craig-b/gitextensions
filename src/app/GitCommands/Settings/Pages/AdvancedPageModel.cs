namespace GitCommands.Settings.Pages;

/// <summary>Presentation model for the advanced settings page.</summary>
public sealed class AdvancedPageModel : SettingsPageModel
{
    /// <summary>The stored symbol per choice index; the last choice ("(none)") stores empty.</summary>
    private static readonly string[] _normalisationSymbols = ["_", "-", string.Empty];

    public AdvancedPageModel()
        : base("Advanced")
    {
        Groups =
        [
            new SettingsGroup("Checkout",
                AlwaysShowCheckoutDialog = new BoolSettingsEntry("Always show checkout dialog",
                    () => AppSettings.AlwaysShowCheckoutBranchDlg, value => AppSettings.AlwaysShowCheckoutBranchDlg = value),
                UseLastChosenLocalChangesAction = new BoolSettingsEntry("Use last chosen \"local changes\" action as default action.\nThis action will be performed without warning while checking out branch.",
                    () => AppSettings.UseDefaultCheckoutBranchAction, value => AppSettings.UseDefaultCheckoutBranchAction = value)),

            new SettingsGroup("General",
                DontShowHelpImages = new BoolSettingsEntry("Don't show help images",
                    () => AppSettings.DontShowHelpImages, value => AppSettings.DontShowHelpImages = value),
                AlwaysShowAdvancedOptions = new BoolSettingsEntry("Always show advanced options",
                    () => AppSettings.AlwaysShowAdvOpt, value => AppSettings.AlwaysShowAdvOpt = value),
                UseConsoleEmulator = new BoolSettingsEntry("Use Console Emulator for console output in command dialogs",
                    () => AppSettings.UseConsoleEmulatorForCommands.Value, value => AppSettings.UseConsoleEmulatorForCommands.Value = value),
                AutoNormaliseBranchName = new BoolSettingsEntry("Auto normalise branch name",
                    () => AppSettings.AutoNormaliseBranchName, value => AppSettings.AutoNormaliseBranchName = value),
                AutoNormaliseSymbol = new ChoiceSettingsEntry("Symbol to use:", ["_", "-", "(none)"],
                    () => Math.Max(0, Array.IndexOf(_normalisationSymbols, AppSettings.AutoNormaliseSymbol)),
                    index => AppSettings.AutoNormaliseSymbol = _normalisationSymbols[index])),

            new SettingsGroup("Commit",
                CommitAndPushForcedWhenAmend = new BoolSettingsEntry("Push forced with lease when Commit & Push action is performed with Amend option checked",
                    () => AppSettings.CommitAndPushForcedWhenAmend, value => AppSettings.CommitAndPushForcedWhenAmend = value)),

            new SettingsGroup("Updates",
                CheckForUpdates = new BoolSettingsEntry("Check for updates weekly",
                    () => AppSettings.CheckForUpdates, value => AppSettings.CheckForUpdates = value),
                CheckForReleaseCandidates = new BoolSettingsEntry("Check for release candidate versions",
                    () => AppSettings.CheckForReleaseCandidates, value => AppSettings.CheckForReleaseCandidates = value)),
        ];
    }

    public BoolSettingsEntry AlwaysShowCheckoutDialog { get; }
    public BoolSettingsEntry UseLastChosenLocalChangesAction { get; }

    public BoolSettingsEntry DontShowHelpImages { get; }
    public BoolSettingsEntry AlwaysShowAdvancedOptions { get; }
    public BoolSettingsEntry UseConsoleEmulator { get; }
    public BoolSettingsEntry AutoNormaliseBranchName { get; }
    public ChoiceSettingsEntry AutoNormaliseSymbol { get; }

    public BoolSettingsEntry CommitAndPushForcedWhenAmend { get; }

    public BoolSettingsEntry CheckForUpdates { get; }
    public BoolSettingsEntry CheckForReleaseCandidates { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
