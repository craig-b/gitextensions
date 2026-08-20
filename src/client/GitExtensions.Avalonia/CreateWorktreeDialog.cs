using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands.Worktree;

namespace GitExtensions.Avalonia;

/// <summary>
///  The create-worktree dialog over WorktreeCreateModel: existing
///  branch or new branch, with the directory suggestion and validity rules FormCreateWorktree binds.
/// </summary>
internal static class CreateWorktreeDialog
{
    /// <returns>(directory, newBranchOption) or null when cancelled.</returns>
    public static async Task<(string Directory, string NewBranchOption)?> ShowAsync(
        Window owner,
        string basePath,
        IReadOnlyList<string> existingBranches,
        string currentBranch)
    {
        (string, string)? result = null;
        List<string> selectable = [.. existingBranches.Where(branch => branch != currentBranch)];

        Window dialog = new()
        {
            Title = "Create worktree",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        RadioButton existing = new() { Content = "Checkout existing branch", GroupName = "branch", IsChecked = selectable.Count > 0, IsEnabled = selectable.Count > 0 };
        RadioButton createNew = new() { Content = "Create new branch", GroupName = "branch", IsChecked = selectable.Count == 0 };
        ComboBox branches = new() { ItemsSource = selectable, SelectedIndex = selectable.Count > 0 ? 0 : -1, MinWidth = 260 };
        TextBox newBranch = new() { Watermark = "New branch name", MinWidth = 260 };
        TextBox directory = new() { Watermark = "Worktree directory", MinWidth = 380 };
        Button create = new() { Content = "Create worktree", MinWidth = 120, IsDefault = true };
        Button cancel = new() { Content = "Cancel", MinWidth = 90, IsCancel = true };

        void Update()
        {
            branches.IsEnabled = existing.IsChecked is true;
            newBranch.IsEnabled = createNew.IsChecked is true;
            string branchName = existing.IsChecked is true ? branches.SelectedItem as string ?? "" : newBranch.Text ?? "";
            directory.Text = WorktreeCreateModel.SuggestDirectory(basePath, branchName);
            create.IsEnabled = WorktreeCreateModel.IsBranchChoiceValid(
                    existing.IsChecked is true, branches.SelectedItem is not null, newBranch.Text ?? "", existingBranches)
                && WorktreeCreateModel.IsTargetFolderValid(directory.Text);
        }

        existing.IsCheckedChanged += (_, _) => Update();
        branches.SelectionChanged += (_, _) => Update();
        newBranch.TextChanged += (_, _) => Update();
        Update();

        create.Click += (_, _) =>
        {
            string? option = WorktreeCreateModel.NewBranchOption(
                createNew.IsChecked is true, newBranch.Text?.Trim() ?? "", branches.SelectedItem as string);
            if (option is not null && !string.IsNullOrWhiteSpace(directory.Text))
            {
                result = (directory.Text, option);
                dialog.Close();
            }
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                existing,
                branches,
                createNew,
                newBranch,
                new TextBlock { Text = "Directory:" },
                directory,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { create, cancel },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
