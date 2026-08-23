using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using GitCommands;
using GitCommands.Init;

namespace GitExtensions.Avalonia;

/// <summary>The create-new-repository dialog over InitRepositoryModel.</summary>
internal static class InitDialog
{
    /// <returns>(directory, central) or null when cancelled; the directory has been validated.</returns>
    public static async Task<(string Directory, bool Central)?> ShowAsync(Window owner, SliceSession session)
    {
        (string, bool)? result = null;

        Window dialog = new()
        {
            Title = Loc.T("Create new repository"),
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        TextBox directory = new()
        {
            Watermark = Loc.T("Directory"),
            MinWidth = 380,
            Text = InitRepositoryModel.SeedDirectory(
                explicitDirectory: null,
                session.IsValidRepository,
                session.WorkingDir,
                AppSettings.DefaultCloneDestinationPath),
        };
        Button browse = new() { Content = Loc.T("Browse...") };
        CheckBox central = new() { Content = Loc.T("Central repository, no working directory (--bare --shared=all)"), IsChecked = false };
        TextBlock error = new() { Foreground = global::Avalonia.Media.Brushes.OrangeRed, IsVisible = false, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        Button create = new() { Content = Loc.T("Create"), MinWidth = 100, IsDefault = true };
        Button cancel = new() { Content = Loc.T("Cancel"), MinWidth = 90, IsCancel = true };

        directory.TextChanged += (_, _) => error.IsVisible = false;
        browse.Click += async (_, _) =>
        {
            if (TopLevel.GetTopLevel(dialog)?.StorageProvider is { } storage)
            {
                System.Collections.Generic.IReadOnlyList<IStorageFolder> picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions());
                if (picked.Count == 1 && picked[0].TryGetLocalPath() is string pickedPath)
                {
                    directory.Text = pickedPath;
                }
            }
        };

        create.Click += (_, _) =>
        {
            switch (InitRepositoryModel.Validate(directory.Text ?? "", System.IO.File.Exists))
            {
                case InitValidation.NotRootedDirectoryPath:
                    error.Text = Loc.T("Please choose an absolute directory path.");
                    error.IsVisible = true;
                    return;

                case InitValidation.PathIsFile:
                    error.Text = Loc.T("Cannot initialize a new repository on a file. Please choose a directory.");
                    error.IsVisible = true;
                    return;
            }

            result = (directory.Text!, central.IsChecked is true);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = Loc.T("Directory:") },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { directory, browse } },
                central,
                error,
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
