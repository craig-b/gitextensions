using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using GitCommands;
using GitCommands.Open;
using GitCommands.UserRepositoryHistory;

namespace GitExtensions.Avalonia;

/// <summary>
///  The open-repository dialog over OpenRepositoryModel: the candidate directories
///  FormOpenDirectory offers (default clone destination, parent of the current repository,
///  recent history), a free path box with go-up/browse, and the same open gate.
/// </summary>
internal static class OpenRepositoryDialog
{
    /// <returns>The validated working directory to open (trailing separator), or <see langword="null"/> when cancelled.</returns>
    public static async Task<string?> ShowAsync(Window owner, string? currentWorkingDir)
    {
        IList<Repository> history = await RepositoryHistoryManager.Locals.LoadRecentHistoryAsync();
        IReadOnlyList<string> candidates = OpenRepositoryModel.CandidateDirectories(
            AppSettings.DefaultCloneDestinationPath,
            currentWorkingDir,
            history.Select(r => r.Path),
            AppSettings.RecentWorkingDir,
            EnvironmentConfiguration.GetHomeDir());

        string? result = null;

        Window dialog = new()
        {
            Title = Loc.T("Open repository"),
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        TextBox path = new() { Watermark = Loc.T("Repository directory"), MinWidth = 380 };
        Button up = new() { Content = "↑", IsEnabled = false };
        ToolTip.SetTip(up, Loc.T("Go to parent directory"));
        Button browse = new() { Content = Loc.T("Browse...") };
        ListBox recent = new() { ItemsSource = candidates, MaxHeight = 220 };
        TextBlock error = new() { Foreground = global::Avalonia.Media.Brushes.OrangeRed, IsVisible = false, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        Button open = new() { Content = Loc.T("Open"), MinWidth = 100, IsDefault = true };
        Button cancel = new() { Content = Loc.T("Cancel"), MinWidth = 90, IsCancel = true };

        void TryOpen()
        {
            if (OpenRepositoryModel.TryGetOpenablePath(path.Text, Directory.Exists, GitModule.IsValidGitWorkingDir) is string openablePath)
            {
                result = openablePath;
                dialog.Close();
                return;
            }

            error.Text = Loc.T("The selected directory is not a valid git repository.");
            error.IsVisible = true;
        }

        path.TextChanged += (_, _) =>
        {
            error.IsVisible = false;
            up.IsEnabled = OpenRepositoryModel.CanGoUp(path.Text, Directory.Exists);
        };
        up.Click += (_, _) =>
        {
            if (OpenRepositoryModel.ParentOf(path.Text) is string parent)
            {
                path.Text = parent;
            }
        };
        browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(dialog)?.StorageProvider is not { } storage)
            {
                return;
            }

            IReadOnlyList<IStorageFolder> picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.T("Open repository") });
            if (picked.Count == 1 && picked[0].TryGetLocalPath() is string pickedPath)
            {
                path.Text = pickedPath;
                TryOpen();
            }
        };
        recent.SelectionChanged += (_, _) =>
        {
            if (recent.SelectedItem is string selected)
            {
                path.Text = selected;
            }
        };
        recent.DoubleTapped += (_, _) => TryOpen();
        open.Click += (_, _) => TryOpen();
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = Loc.T("Directory:") },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { path, up, browse },
                },
                new TextBlock { Text = Loc.T("Recent and suggested:") },
                recent,
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { open, cancel },
                },
            },
        };

        dialog.Opened += (_, _) => path.Focus();
        await dialog.ShowDialog(owner);
        return result;
    }
}
