using GitUI.UserControls.Settings;

namespace GitUI.CommandsDialogs.SettingsDialog;

/// <summary>
///  Binds portable settings entries to the two checkbox flavors the pages use
///  (<see cref="CheckBox"/> and the tooltip-bearing <see cref="SettingsCheckBox"/>).
/// </summary>
internal static class SettingsPageBindings
{
    public static bool GetChecked(Control control)
        => control switch
        {
            CheckBox checkBox => checkBox.Checked,
            SettingsCheckBox settingsCheckBox => settingsCheckBox.Checked,
            _ => throw new NotSupportedException($"No Checked accessor for {control.GetType()}"),
        };

    public static void SetChecked(Control control, bool value)
    {
        switch (control)
        {
            case CheckBox checkBox:
                checkBox.Checked = value;
                break;
            case SettingsCheckBox settingsCheckBox:
                settingsCheckBox.Checked = value;
                break;
            default:
                throw new NotSupportedException($"No Checked accessor for {control.GetType()}");
        }
    }
}
