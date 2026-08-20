using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands.Git;
using GitCommands.Git.Tag;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Avalonia;

/// <summary>
///  The create-tag dialog over the portable GitCreateTagArgs + TagDialogModel:
///  operation choices and their gpg-key/message availability come from the model.
/// </summary>
internal static class CreateTagDialog
{
    private static readonly string[] OperationCaptions =
    [
        "Lightweight tag",
        "Annotated tag",
        "Sign with default GPG",
        "Sign with specific GPG",
    ];

    public static async Task<(GitCreateTagArgs Args, bool Push)?> ShowAsync(Window owner, ObjectId targetId, string pushRemote)
    {
        (GitCreateTagArgs, bool)? result = null;

        Window dialog = new()
        {
            Title = "Create tag",
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        TextBox name = new() { Watermark = "Tag name", MinWidth = 300 };
        ComboBox operation = new() { ItemsSource = OperationCaptions, SelectedIndex = 0, MinWidth = 220 };
        TextBox message = new() { Watermark = "Tag message", AcceptsReturn = true, MinHeight = 70, IsEnabled = false };
        TextBox gpgKey = new() { Watermark = "GPG key id", IsEnabled = false };
        CheckBox force = new() { Content = "Force (replace existing tag)" };
        CheckBox push = new() { Content = $"Push tag to '{pushRemote}'" };

        operation.SelectionChanged += (_, _) =>
        {
            TagOptionAvailability availability = TagOptionAvailability.Evaluate(TagDialogModel.OperationChoices[operation.SelectedIndex]);
            message.IsEnabled = availability.MessageEnabled;
            gpgKey.IsEnabled = availability.GpgKeyEnabled;
        };

        Button create = new() { Content = "Create tag", MinWidth = 100, IsDefault = true };
        Button cancel = new() { Content = "Cancel", MinWidth = 90, IsCancel = true };
        create.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                return;
            }

            result = (
                new GitCreateTagArgs(
                    name.Text.Trim(),
                    targetId,
                    TagDialogModel.OperationChoices[operation.SelectedIndex],
                    message.Text ?? "",
                    gpgKey.Text ?? "",
                    force.IsChecked is true),
                push.IsChecked is true);
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
                new TextBlock { Text = $"Create tag at {targetId.ToShortString()}", FontWeight = global::Avalonia.Media.FontWeight.Bold },
                name,
                operation,
                message,
                gpgKey,
                force,
                push,
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
