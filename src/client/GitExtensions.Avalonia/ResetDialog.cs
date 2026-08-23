using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands.Reset;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Avalonia;

/// <summary>
///  The reset-current-branch mode picker over <see cref="ResetCurrentBranchPolicy"/>:
///  default mode follows the working-directory state, hard reset is the caller's confirmation.
/// </summary>
internal static class ResetDialog
{
    private static readonly (ResetMode Mode, string Caption, string Detail)[] Modes =
    [
        (ResetMode.Soft, "Soft", "keep all changes staged"),
        (ResetMode.Mixed, "Mixed", "keep changes in the working directory, unstaged"),
        (ResetMode.Keep, "Keep", "keep local changes, abort if a file differs"),
        (ResetMode.Merge, "Merge", "like keep, but honors unmerged entries"),
        (ResetMode.Hard, "Hard", "discard ALL local changes"),
    ];

    public static async Task<ResetMode?> ShowAsync(Window owner, string targetDescription, bool isDirtyWorkingDir)
    {
        ResetMode? result = null;
        ResetMode defaultMode = ResetCurrentBranchPolicy.ResolveDefaultMode(isDirtyWorkingDir);

        Window dialog = new()
        {
            Title = "Reset current branch",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        StackPanel radios = new() { Spacing = 6 };
        RadioButton[] buttons = new RadioButton[Modes.Length];
        for (int i = 0; i < Modes.Length; i++)
        {
            buttons[i] = new RadioButton
            {
                Content = $"{Modes[i].Caption} — {Modes[i].Detail}",
                GroupName = "mode",
                IsChecked = Modes[i].Mode == defaultMode,
            };
            radios.Children.Add(buttons[i]);
        }

        Button ok = new() { Content = "Reset", MinWidth = 90, IsDefault = true };
        Button cancel = new() { Content = "Cancel", MinWidth = 90, IsCancel = true };
        ok.Click += (_, _) =>
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i].IsChecked is true)
                {
                    result = Modes[i].Mode;
                }
            }

            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 12,
            MinWidth = 420,
            Children =
            {
                new TextBlock { Text = $"Reset current branch to {targetDescription}", FontWeight = global::Avalonia.Media.FontWeight.Bold },
                radios,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
