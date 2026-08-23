using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands.Pull;

namespace GitExtensions.Avalonia;

/// <summary>
///  The pull/fetch options dialog over the portable PullOptions model:
///  action/source interplay comes from <see cref="PullOptionAvailability"/> - the same
///  rules FormPull binds.
/// </summary>
internal static class PullDialog
{
    private const string AllRemotes = "[ All remotes ]";

    public static async Task<PullOptions?> ShowAsync(
        Window owner,
        IReadOnlyList<string> remotes,
        string? defaultRemote,
        PullActionKind defaultAction,
        bool defaultAllRemotes,
        bool defaultPrune)
    {
        PullOptions? result = null;

        Window dialog = new()
        {
            Title = "Pull",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        List<string> sources = [.. remotes, AllRemotes];
        ComboBox source = new()
        {
            ItemsSource = sources,
            SelectedIndex = defaultAllRemotes ? sources.Count - 1 : Math.Max(0, sources.IndexOf(defaultRemote ?? "")),
            MinWidth = 220,
        };
        TextBox remoteBranch = new() { Watermark = "Remote branch (default: tracking)", MinWidth = 220 };

        RadioButton merge = new() { Content = "Merge remote changes into the current branch", GroupName = "action" };
        RadioButton rebase = new() { Content = "Rebase the current branch onto the remote changes", GroupName = "action" };
        RadioButton fetch = new() { Content = "Fetch only (no merge or rebase)", GroupName = "action" };
        (defaultAction switch { PullActionKind.Rebase => rebase, PullActionKind.Fetch => fetch, _ => merge }).IsChecked = true;

        CheckBox prune = new() { Content = "Prune deleted remote branches", IsChecked = defaultPrune };
        CheckBox pruneTags = new() { Content = "Prune deleted remote tags" };

        void UpdateAvailability()
        {
            bool isPullAll = source.SelectedIndex == sources.Count - 1;
            PullActionKind action = rebase.IsChecked is true ? PullActionKind.Rebase
                : fetch.IsChecked is true ? PullActionKind.Fetch
                : PullActionKind.Merge;
            PullOptionAvailability availability = PullOptionAvailability.Evaluate(action, isPullAll);

            merge.IsEnabled = availability.MergeAllowed;
            rebase.IsEnabled = availability.RebaseAllowed;
            if (availability.ForceFetchAction)
            {
                fetch.IsChecked = true;
            }

            prune.IsEnabled = availability.PruneAllowed;
            pruneTags.IsEnabled = availability.PruneTagsAllowed;
            remoteBranch.IsEnabled = !isPullAll;
        }

        source.SelectionChanged += (_, _) => UpdateAvailability();
        merge.IsCheckedChanged += (_, _) => UpdateAvailability();
        rebase.IsCheckedChanged += (_, _) => UpdateAvailability();
        fetch.IsCheckedChanged += (_, _) => UpdateAvailability();
        UpdateAvailability();

        Button pull = new() { Content = "Pull", MinWidth = 90, IsDefault = true };
        Button cancel = new() { Content = "Cancel", MinWidth = 90, IsCancel = true };
        pull.Click += (_, _) =>
        {
            bool isPullAll = source.SelectedIndex == sources.Count - 1;
            PullActionKind action = rebase.IsChecked is true ? PullActionKind.Rebase
                : fetch.IsChecked is true ? PullActionKind.Fetch
                : PullActionKind.Merge;
            PullOptionAvailability availability = PullOptionAvailability.Evaluate(action, isPullAll);

            result = new PullOptions(
                action,
                isPullAll ? PullSourceKind.AllRemotes : PullSourceKind.Remote,
                Source: isPullAll ? "" : sources[source.SelectedIndex],
                RemoteBranch: string.IsNullOrWhiteSpace(remoteBranch.Text) || isPullAll ? null : remoteBranch.Text.Trim(),
                LocalBranch: null,
                Prune: availability.PruneAllowed && prune.IsChecked is true,
                PruneTags: availability.PruneTagsAllowed && pruneTags.IsChecked is true);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 10,
            MinWidth = 440,
            Children =
            {
                new TextBlock { Text = "Pull changes", FontWeight = global::Avalonia.Media.FontWeight.Bold },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "From:", VerticalAlignment = VerticalAlignment.Center }, source } },
                remoteBranch,
                merge,
                rebase,
                fetch,
                prune,
                pruneTags,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { pull, cancel },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
