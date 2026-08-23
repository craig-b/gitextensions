using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands.Git;

namespace GitExtensions.Avalonia;

/// <summary>The push dialog's explicit choices, ready for the portable Commands.Push builder.</summary>
internal sealed record PushChoice(string Remote, string LocalBranch, string RemoteBranch, ForcePushOptions Force, bool Track);

/// <summary>
///  The push options dialog: prefilled from the session's PushPreflight-derived
///  defaults; force options mirror FormPush's three-way choice.
/// </summary>
internal static class PushDialog
{
    private static readonly (string Caption, ForcePushOptions Option)[] ForceChoices =
    [
        ("Do not force", ForcePushOptions.DoNotForce),
        ("Force with lease (safe)", ForcePushOptions.ForceWithLease),
        ("Force (may discard remote work)", ForcePushOptions.Force),
    ];

    public static async Task<PushChoice?> ShowAsync(
        Window owner,
        IReadOnlyList<string> remotes,
        string defaultRemote,
        string localBranch,
        string? defaultRemoteBranch,
        bool defaultTrack)
    {
        PushChoice? result = null;

        Window dialog = new()
        {
            Title = "Push",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        ComboBox remote = new()
        {
            ItemsSource = remotes,
            SelectedIndex = Math.Max(0, remotes.ToList().IndexOf(defaultRemote)),
            MinWidth = 220,
        };
        TextBox local = new() { Text = localBranch, MinWidth = 220 };
        TextBox remoteBranch = new() { Text = defaultRemoteBranch ?? localBranch, MinWidth = 220 };
        ComboBox force = new() { ItemsSource = ForceChoices.Select(choice => choice.Caption).ToList(), SelectedIndex = 0, MinWidth = 220 };
        CheckBox track = new() { Content = "Set up tracking (-u)", IsChecked = defaultTrack };

        Button push = new() { Content = "Push", MinWidth = 90, IsDefault = true };
        Button cancel = new() { Content = "Cancel", MinWidth = 90, IsCancel = true };
        push.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(local.Text) || remote.SelectedItem is not string selectedRemote)
            {
                return;
            }

            result = new PushChoice(
                selectedRemote,
                local.Text.Trim(),
                remoteBranch.Text?.Trim() ?? "",
                ForceChoices[force.SelectedIndex].Option,
                track.IsChecked is true);
            dialog.Close();
        };
        cancel.Click += (_, _) => dialog.Close();

        static StackPanel Labeled(string caption, Control control) => new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new TextBlock { Text = caption, VerticalAlignment = VerticalAlignment.Center, MinWidth = 100 }, control },
        };

        dialog.Content = new StackPanel
        {
            Margin = new global::Avalonia.Thickness(16),
            Spacing = 10,
            MinWidth = 440,
            Children =
            {
                new TextBlock { Text = "Push branch", FontWeight = global::Avalonia.Media.FontWeight.Bold },
                Labeled("Remote:", remote),
                Labeled("Local branch:", local),
                Labeled("To branch:", remoteBranch),
                Labeled("Force:", force),
                track,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { push, cancel },
                },
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }
}
