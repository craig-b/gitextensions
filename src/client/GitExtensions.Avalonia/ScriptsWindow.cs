using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands;
using GitCommands.Scripts;

namespace GitExtensions.Avalonia;

/// <summary>
///  The scripts editor: list left, definition right. Saved scripts project into the
///  menus/palette/hotkeys through the action registry.
/// </summary>
public sealed class ScriptsWindow : Window
{
    private readonly ListBox _list = new() { MinWidth = 200, MinHeight = 320 };
    private readonly TextBox _caption = new() { Watermark = "Caption" };
    private readonly TextBox _interpreter = new() { Watermark = "Interpreter (shell, bash, pwsh, or an executable)" };
    private readonly TextBox _command = new() { Watermark = "Command ({selected.hash}, {current.branch}, {prompt:...} ...)", AcceptsReturn = true, MinHeight = 80 };
    private readonly TextBox _hotkey = new() { Watermark = "Hotkey (e.g. Ctrl+Shift+K, optional)" };
    private readonly CheckBox _destructive = new() { Content = "Destructive (ask before running)" };
    private readonly CheckBox _background = new() { Content = "Run in background" };
    private readonly CheckBox _enabled = new() { Content = "Enabled", IsChecked = true };
    private readonly CheckBox _onCommitMenu = new() { Content = "Show in commit menu", IsChecked = true };
    private readonly CheckBox _onRefMenu = new() { Content = "Show in branch/tag menu" };
    private List<ScriptDefinition> _scripts = [];

    public ScriptsWindow()
    {
        Title = "Scripts";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _list.SelectionChanged += (_, _) => BindSelection();

        Button newButton = new() { Content = "New" };
        newButton.Click += (_, _) =>
        {
            _list.SelectedIndex = -1;
            BindSelection();
            _caption.Focus();
        };

        Button deleteButton = new() { Content = "Delete" };
        deleteButton.Click += (_, _) =>
        {
            if (SelectedScript() is ScriptDefinition selected)
            {
                _scripts.Remove(selected);
                Persist();
                RefreshList(preserve: null);
            }
        };

        Button import = new() { Content = "Import from Git Extensions" };
        import.Click += (_, _) =>
        {
            IReadOnlyList<ScriptDefinition> imported = ScriptImport.FromWinFormsXml(AppSettings.GetString("ownScripts", null));
            if (imported.Count == 0)
            {
                return;
            }

            _scripts = [.. ScriptImport.Merge(_scripts, imported)];
            Persist();
            RefreshList(preserve: imported[0].Slug);
        };

        Button save = new() { Content = "Save script", MinWidth = 100, IsDefault = true };
        save.Click += (_, _) => SaveCurrent();
        Button close = new() { Content = "Close", MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new global::Avalonia.Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("Auto,16,420"),
            Children =
            {
                WithColumn(new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        _list,
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { newButton, deleteButton } },
                        import,
                    },
                }, 0),
                WithColumn(new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Script", FontWeight = global::Avalonia.Media.FontWeight.Bold },
                        _caption,
                        _interpreter,
                        _command,
                        _hotkey,
                        _destructive,
                        _background,
                        _enabled,
                        _onCommitMenu,
                        _onRefMenu,
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Spacing = 8,
                            Children = { save, close },
                        },
                    },
                }, 2),
            },
        };

        _scripts = [.. ScriptStorage.Load(key => AppSettings.GetString(key, null))];
        RefreshList(preserve: null);
    }

    private static Control WithColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private ScriptDefinition? SelectedScript()
        => _list.SelectedIndex >= 0 && _list.SelectedIndex < _scripts.Count ? _scripts[_list.SelectedIndex] : null;

    private void RefreshList(string? preserve)
    {
        _list.ItemsSource = _scripts.Select(script => script.Enabled ? script.Caption : $"{script.Caption} (disabled)").ToList();
        _list.SelectedIndex = preserve is null ? -1 : _scripts.FindIndex(script => script.Slug == preserve);
        BindSelection();
    }

    private void BindSelection()
    {
        ScriptDefinition? script = SelectedScript();
        _caption.Text = script?.Caption ?? "";
        _interpreter.Text = script?.Interpreter ?? "shell";
        _command.Text = script?.Command ?? "";
        _hotkey.Text = script?.Hotkey ?? "";
        _destructive.IsChecked = script?.Destructive is true;
        _background.IsChecked = script?.RunInBackground is true;
        _enabled.IsChecked = script?.Enabled ?? true;
        _onCommitMenu.IsChecked = script is null || script.Surfaces.HasFlag(ScriptSurfaces.CommitMenu);
        _onRefMenu.IsChecked = script?.Surfaces.HasFlag(ScriptSurfaces.RefMenu) is true;
    }

    private void SaveCurrent()
    {
        if (string.IsNullOrWhiteSpace(_caption.Text) || string.IsNullOrWhiteSpace(_command.Text))
        {
            return;
        }

        ScriptSurfaces surfaces =
            (_onCommitMenu.IsChecked is true ? ScriptSurfaces.CommitMenu : ScriptSurfaces.None)
            | (_onRefMenu.IsChecked is true ? ScriptSurfaces.RefMenu : ScriptSurfaces.None);

        string slug = SelectedScript()?.Slug ?? ScriptStorage.SlugFromCaption(_caption.Text);
        ScriptDefinition updated = new(
            slug,
            _caption.Text.Trim(),
            string.IsNullOrWhiteSpace(_interpreter.Text) ? "shell" : _interpreter.Text.Trim(),
            _command.Text,
            _destructive.IsChecked is true,
            _background.IsChecked is true,
            string.IsNullOrWhiteSpace(_hotkey.Text) ? null : _hotkey.Text.Trim(),
            _enabled.IsChecked is true,
            surfaces is ScriptSurfaces.None ? ScriptSurfaces.CommitMenu : surfaces);

        int index = _scripts.FindIndex(script => script.Slug == slug);
        if (index >= 0)
        {
            _scripts[index] = updated;
        }
        else
        {
            _scripts.Add(updated);
        }

        Persist();
        RefreshList(preserve: slug);
    }

    private void Persist()
        => ScriptStorage.Save(_scripts, AppSettings.SetString);
}
