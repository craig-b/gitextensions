using GitCommands;
using GitExtUtils.GitUI.Theming;
using GitUI.Theming;
using ResourceManager;

namespace GitUI.UserControls;

internal partial class OutputHistoryControl : GitExtensionsControl
{
    internal OutputHistoryControl()
    {
        InitializeComponent();
        TextBox.Font = AppSettings.FixedWidthFont;
        TextBox.ForeColor = Application.IsDarkModeEnabled ? AppColor.AnsiTerminalWhiteForeNormal.GetThemeColor() : SystemColors.WindowText;
        InitializeComplete();
    }
}
