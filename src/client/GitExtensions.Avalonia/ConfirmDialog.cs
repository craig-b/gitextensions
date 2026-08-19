using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace GitExtensions.Avalonia;

/// <summary>
///  A minimal modal choice dialog - the slice's stand-in for MessageBoxes/TaskDialog while the
///  commit screen walks the portable CommitDialogGates.
/// </summary>
internal static class ConfirmDialog
{
    /// <returns>The index of the chosen button, or -1 when the window was closed.</returns>
    public static async Task<int> ShowAsync(Window owner, string caption, string text, params string[] buttons)
    {
        int result = -1;

        Window dialog = new()
        {
            Title = caption,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        StackPanel buttonRow = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
        };

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            Button button = new() { Content = buttons[i], MinWidth = 80 };
            button.Click += (_, _) =>
            {
                result = index;
                dialog.Close();
            };
            buttonRow.Children.Add(button);
        }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 16,
            MaxWidth = 500,
            Children =
            {
                new TextBlock { Text = text, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap },
                buttonRow,
            },
        };

        await dialog.ShowDialog(owner);
        return result;
    }

    public static async Task<bool> ConfirmAsync(Window owner, string caption, string text)
        => await ShowAsync(owner, caption, text, "Yes", "No") == 0;

    public static Task ErrorAsync(Window owner, string caption, string text)
        => ShowAsync(owner, caption, text, "OK");

    /// <returns>The entered text, or null when cancelled.</returns>
    public static async Task<string?> InputAsync(Window owner, string caption, string prompt, string watermark = "")
    {
        string? result = null;

        Window dialog = new()
        {
            Title = caption,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        TextBox input = new() { Watermark = watermark, MinWidth = 320 };

        Button okButton = new() { Content = "OK", MinWidth = 80, IsDefault = true };
        okButton.Click += (_, _) =>
        {
            result = input.Text;
            dialog.Close();
        };

        Button cancelButton = new() { Content = "Cancel", MinWidth = 80, IsCancel = true };
        cancelButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = prompt },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { okButton, cancelButton },
                },
            },
        };

        dialog.Opened += (_, _) => input.Focus();

        await dialog.ShowDialog(owner);
        return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
    }
}
