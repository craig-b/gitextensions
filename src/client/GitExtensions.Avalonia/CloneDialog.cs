using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using GitCommands;
using GitCommands.Clone;

namespace GitExtensions.Avalonia;

/// <summary>What the user asked to clone; the target directory has been created.</summary>
internal sealed record CloneRequest(string From, string TargetDirectory, bool Bare, bool InitSubmodules, string? Branch, int? Depth, bool? IsSingleBranch);

/// <summary>
///  The clone dialog over the portable clone models: FormClone's seeding cascade, destination
///  preview, remote branch probe with failure classification, the shallow-clone rule, and the
///  branch tri-state.
/// </summary>
internal static class CloneDialog
{
    private static readonly string BranchDefaultHead = "(default: remote HEAD)";
    private static readonly string BranchNone = "(none: don't checkout after clone)";

    public static async Task<CloneRequest?> ShowAsync(Window owner, SliceSession session)
    {
        CloneRequest? result = null;

        Window dialog = new()
        {
            Title = Loc.T("Clone repository"),
            Width = 620,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        TextBox from = new() { Watermark = Loc.T("Repository to clone (URL or path)"), MinWidth = 420 };
        Button fromBrowse = new() { Content = Loc.T("Browse...") };
        TextBox destination = new() { Watermark = Loc.T("Destination folder"), MinWidth = 420 };
        Button toBrowse = new() { Content = Loc.T("Browse...") };
        TextBox subdirectory = new() { Watermark = Loc.T("Subdirectory to create"), MinWidth = 420 };
        ComboBox branches = new() { MinWidth = 260 };
        Button loadBranches = new() { Content = Loc.T("Load branches") };
        CheckBox fullHistory = new() { Content = Loc.T("Download full history"), IsChecked = true };
        CheckBox initSubmodules = new() { Content = Loc.T("Initialize all submodules"), IsChecked = true };
        CheckBox central = new() { Content = Loc.T("Bare repository (no working directory)"), IsChecked = false };
        TextBlock info = new() { TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        TextBlock error = new() { Foreground = global::Avalonia.Media.Brushes.OrangeRed, IsVisible = false, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap };
        Button clone = new() { Content = Loc.T("Clone"), MinWidth = 100, IsDefault = true };
        Button cancel = new() { Content = Loc.T("Cancel"), MinWidth = 90, IsCancel = true };

        List<string> defaultBranchItems = [BranchDefaultHead, BranchNone];
        branches.ItemsSource = defaultBranchItems;
        branches.SelectedIndex = 0;

        // FormClone's seeding cascade, clipboard included.
        string? clipboardText = null;
        try
        {
            clipboardText = TopLevel.GetTopLevel(owner)?.Clipboard is { } clipboard ? await clipboard.GetTextAsync() : null;
        }
        catch
        {
            // We tried.
        }

        CloneSeed seed = CloneSourceSeed.Resolve(
            urlArgument: null,
            clipboardText,
            AppSettings.DefaultCloneDestinationPath,
            session.IsValidRepository,
            session.WorkingDir,
            session.GetSuggestedCloneSource,
            Directory.Exists);
        from.Text = seed.Source ?? "";
        destination.Text = seed.Destination ?? "";

        void UpdateDerived()
        {
            string repositoryName = PathUtil.GetRepositoryName(from.Text);
            if (repositoryName != "")
            {
                subdirectory.Text = repositoryName;
            }

            CloneDestinationPreview preview = CloneModel.EvaluateDestination(
                destination.Text,
                subdirectory.Text,
                Loc.T("destination"),
                Loc.T("subdirectory"),
                path => Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any());

            info.Text = string.Format(Loc.T("Your new repository will be cloned to: {0}"), preview.Path) + preview.State switch
            {
                CloneDestinationState.ExistsNotEmpty => " " + Loc.T("(directory already exists and is not empty)"),
                CloneDestinationState.New => " " + Loc.T("(new directory)"),
                _ => "",
            };
            clone.IsEnabled = preview.State is not CloneDestinationState.Incomplete;
        }

        from.TextChanged += (_, _) =>
        {
            error.IsVisible = false;
            branches.ItemsSource = defaultBranchItems;
            branches.SelectedIndex = 0;
            UpdateDerived();
        };
        destination.TextChanged += (_, _) => UpdateDerived();
        subdirectory.TextChanged += (_, _) => UpdateDerived();
        UpdateDerived();

        fromBrowse.Click += async (_, _) => from.Text = await PickFolderAsync(dialog) ?? from.Text;
        toBrowse.Click += async (_, _) => destination.Text = await PickFolderAsync(dialog) ?? destination.Text;

        loadBranches.Click += async (_, _) =>
        {
            error.IsVisible = false;
            loadBranches.IsEnabled = false;
            try
            {
                (RemoteProbeStatus status, IReadOnlyList<string> names, string? errorOutput) = await session.ProbeRemoteBranchesAsync(from.Text);
                if (status is RemoteProbeStatus.Success)
                {
                    (IReadOnlyList<string> items, string? reselect) = CloneBranchSelection.MergeBranchList(
                        defaultBranchItems, names, branches.SelectedItem as string);
                    branches.ItemsSource = items;
                    branches.SelectedIndex = reselect is null ? 0 : items.ToList().IndexOf(reselect);
                }
                else
                {
                    // No PuTTY remediation on this host - auth/host-key failures surface as errors.
                    error.Text = errorOutput ?? Loc.T("Loading branches failed.");
                    error.IsVisible = true;
                }
            }
            finally
            {
                loadBranches.IsEnabled = true;
            }
        };

        clone.Click += (_, _) =>
        {
            if (CloneModel.Validate(destination.Text) is not CloneValidation.Ok)
            {
                error.Text = Loc.T("Destination folder must be an absolute path.");
                error.IsVisible = true;
                return;
            }

            try
            {
                string targetDirectory = CloneModel.ResolveTargetDirectory(destination.Text!, subdirectory.Text ?? "");
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                (int? depth, bool? isSingleBranch) = CloneModel.ShallowOptions(fullHistory.IsChecked is true);
                string? branch = CloneBranchSelection.ToBranchArgument(branches.SelectedItem as string, BranchDefaultHead, BranchNone);

                result = new(from.Text ?? "", targetDirectory, central.IsChecked is true, initSubmodules.IsChecked is true, branch, depth, isSingleBranch);
                dialog.Close();
            }
            catch (Exception ex)
            {
                error.Text = ex.Message;
                error.IsVisible = true;
            }
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = Loc.T("Repository to clone:") },
                Row(from, fromBrowse),
                new TextBlock { Text = Loc.T("Destination:") },
                Row(destination, toBrowse),
                new TextBlock { Text = Loc.T("Subdirectory to create:") },
                subdirectory,
                new TextBlock { Text = Loc.T("Branch:") },
                Row(branches, loadBranches),
                fullHistory,
                initSubmodules,
                central,
                info,
                error,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { clone, cancel },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;

        static StackPanel Row(Control first, Control second)
            => new() { Orientation = Orientation.Horizontal, Spacing = 6, Children = { first, second } };
    }

    private static async Task<string?> PickFolderAsync(Window dialog)
    {
        if (TopLevel.GetTopLevel(dialog)?.StorageProvider is not { } storage)
        {
            return null;
        }

        IReadOnlyList<IStorageFolder> picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions());
        return picked.Count == 1 ? picked[0].TryGetLocalPath() : null;
    }
}
