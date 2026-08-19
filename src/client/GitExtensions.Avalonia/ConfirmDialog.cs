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
}
