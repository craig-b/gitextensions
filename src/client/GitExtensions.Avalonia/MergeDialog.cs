using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands.Merge;

namespace GitExtensions.Avalonia;

/// <summary>
///  The merge options dialog over the portable MergeBranchOptions model.
///  Option interplay (squash vs --no-ff, strategy text, log count) is
///  <see cref="MergeOptionAvailability"/> - the same rules FormMergeBranch binds.
/// </summary>
internal static class MergeDialog
{
    public static async Task<MergeBranchOptions?> ShowAsync(Window owner, string branch, string currentBranch)
    {
        MergeBranchOptions? result = null;

        Window dialog = new()
        {
            Title = "Merge branch",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        RadioButton fastForward = new() { Content = "Keep a single branch line if possible (fast-forward)", IsChecked = true, GroupName = "ff" };
        RadioButton noFastForward = new() { Content = "Always create a new merge commit (--no-ff)", GroupName = "ff" };
        CheckBox squash = new() { Content = "Squash commits" };
        CheckBox noCommit = new() { Content = "Do not commit" };
        CheckBox unrelated = new() { Content = "Allow unrelated histories" };
        CheckBox useStrategy = new() { Content = "Merge strategy:" };
        ComboBox strategy = new() { ItemsSource = MergeStrategies.Known, SelectedIndex = 0, MinWidth = 140, IsVisible = false };
        CheckBox addLog = new() { Content = "Add log messages:" };
        NumericUpDown logCount = new() { Minimum = 1, Maximum = 100, Value = 20, Increment = 1, MinWidth = 110, IsEnabled = false, FormatString = "0" };

        void UpdateAvailability()
        {
            MergeOptionAvailability availability = MergeOptionAvailability.Evaluate(
                noFastForward: noFastForward.IsChecked is true,
                nonDefaultStrategy: useStrategy.IsChecked is true,
                addLogMessages: addLog.IsChecked is true,
                addMergeMessage: false);

            squash.IsEnabled = availability.SquashAllowed;
            if (!availability.SquashAllowed)
            {
                squash.IsChecked = false;
            }

            strategy.IsVisible = availability.StrategyTextVisible;
            logCount.IsEnabled = availability.LogCountEnabled;
        }

        fastForward.IsCheckedChanged += (_, _) => UpdateAvailability();
        noFastForward.IsCheckedChanged += (_, _) => UpdateAvailability();
        useStrategy.IsCheckedChanged += (_, _) => UpdateAvailability();
        addLog.IsCheckedChanged += (_, _) => UpdateAvailability();

        Button merge = new() { Content = "Merge", MinWidth = 90, IsDefault = true };
        Button cancel = new() { Content = "Cancel", MinWidth = 90, IsCancel = true };
        merge.Click += (_, _) =>
        {
            result = new MergeBranchOptions(
                branch,
                AllowFastForward: fastForward.IsChecked is true,
                Squash: squash.IsChecked is true,
                NoCommit: noCommit.IsChecked is true,
                Strategy: useStrategy.IsChecked is true ? strategy.SelectedItem as string ?? "" : "",
                AllowUnrelatedHistories: unrelated.IsChecked is true,
                LogMessageCount: addLog.IsChecked is true ? (int)(logCount.Value ?? 20) : null);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 10,
            MinWidth = 420,
            Children =
            {
                new TextBlock { Text = $"Merge {branch} into {currentBranch}", FontWeight = global::Avalonia.Media.FontWeight.Bold },
                fastForward,
                noFastForward,
                squash,
                noCommit,
                unrelated,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { useStrategy, strategy } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addLog, logCount } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { merge, cancel },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
