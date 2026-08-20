using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace GitExtensions.Avalonia;

/// <summary>
///  The command palette: type to filter, Enter or double-click to run.
///  Entries are whatever the caller projects - commands for the current selection plus
///  jump-to-ref navigation.
/// </summary>
internal static class CommandPalette
{
    public static async Task ShowAsync(Window owner, IReadOnlyList<(string Label, Func<Task> Execute)> entries)
    {
        Func<Task>? chosen = null;

        Window dialog = new()
        {
            Title = "Command palette",
            Width = 560,
            Height = 420,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false,
        };

        TextBox searchBox = new() { Watermark = "Type a command or ref name..." };
        ListBox listBox = new() { Margin = new global::Avalonia.Thickness(0, 8, 0, 0) };

        List<(string Label, Func<Task> Execute)> visible = [.. entries];
        void Refresh()
        {
            string search = searchBox.Text ?? "";
            visible = [.. entries.Where(entry => entry.Label.Contains(search, StringComparison.OrdinalIgnoreCase))];
            listBox.ItemsSource = visible.Select(entry => entry.Label).ToList();
            listBox.SelectedIndex = visible.Count > 0 ? 0 : -1;
        }

        void Choose()
        {
            if (listBox.SelectedIndex >= 0 && listBox.SelectedIndex < visible.Count)
            {
                chosen = visible[listBox.SelectedIndex].Execute;
                dialog.Close();
            }
        }

        searchBox.TextChanged += (_, _) => Refresh();
        searchBox.KeyDown += (_, keyArgs) =>
        {
            switch (keyArgs.Key)
            {
                case Key.Enter:
                    keyArgs.Handled = true;
                    Choose();
                    break;
                case Key.Escape:
                    keyArgs.Handled = true;
                    dialog.Close();
                    break;
                case Key.Down:
                    keyArgs.Handled = true;
                    listBox.SelectedIndex = Math.Min(listBox.SelectedIndex + 1, visible.Count - 1);
                    break;
                case Key.Up:
                    keyArgs.Handled = true;
                    listBox.SelectedIndex = Math.Max(listBox.SelectedIndex - 1, 0);
                    break;
            }
        };
        listBox.DoubleTapped += (_, _) => Choose();

        dialog.Content = new DockPanel
        {
            Margin = new global::Avalonia.Thickness(12),
            Children = { searchBox, listBox },
        };
        DockPanel.SetDock(searchBox, Dock.Top);

        Refresh();
        dialog.Opened += (_, _) => searchBox.Focus();
        await dialog.ShowDialog(owner);

        if (chosen is not null)
        {
            await chosen();
        }
    }
}
