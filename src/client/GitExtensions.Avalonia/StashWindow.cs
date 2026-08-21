using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using GitCommands.LeftPanel;

namespace GitExtensions.Avalonia;

/// <summary>
///  Stash management over the portable slice API: save (all or staged only), apply, pop, and
///  drop, with the left panel's StashTreeBuilder rows and a "stash show --name-status" file pane.
/// </summary>
internal sealed class StashWindow : Window
{
    private readonly SliceSession _session;
    private readonly ListBox _list = new();
    private readonly ListBox _files = new();
    private readonly TextBox _message = new() { Watermark = Loc.T("Stash message (optional)") };
    private readonly CheckBox _keepIndex = new() { Content = Loc.T("Keep index") };
    private readonly CheckBox _includeUntracked = new() { Content = Loc.T("Include untracked") };
    private readonly Button _apply = new() { Content = Loc.T("Apply"), MinWidth = 80 };
    private readonly Button _pop = new() { Content = Loc.T("Pop"), MinWidth = 80 };
    private readonly Button _drop = new() { Content = Loc.T("Drop..."), MinWidth = 80 };
    private List<StashTreeNode> _stashes = [];

    /// <summary>Whether any mutation succeeded - the caller refreshes the sidebar off this.</summary>
    public bool StashesChanged { get; private set; }

    public StashWindow(SliceSession session)
    {
        _session = session;

        Title = Loc.T("Stashes");
        Width = 760;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _list.SelectionChanged += async (_, _) => await RefreshFilesAsync();

        Button stashAll = new() { Content = Loc.T("Stash all changes") };
        stashAll.Click += async (_, _) => await SaveStashAsync(stagedOnly: false);

        Button stashStaged = new() { Content = Loc.T("Stash staged") };
        stashStaged.Click += async (_, _) => await SaveStashAsync(stagedOnly: true);

        _apply.Click += async (_, _) => await ApplySelectedAsync();
        _pop.Click += async (_, _) => await PopSelectedAsync();
        _drop.Click += async (_, _) => await DropSelectedAsync();

        Button close = new() { Content = Loc.T("Close"), MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new global::Avalonia.Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("340,16,*"),
            RowDefinitions = new RowDefinitions("*,12,Auto"),
            Children =
            {
                At(new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,8,*"),
                    Children =
                    {
                        At(new StackPanel
                        {
                            Spacing = 8,
                            Children =
                            {
                                _message,
                                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { _keepIndex, _includeUntracked } },
                                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { stashAll, stashStaged } },
                            },
                        }, row: 0, column: 0),
                        At(_list, row: 2, column: 0),
                    },
                }, row: 0, column: 0),
                At(new Grid
                {
                    RowDefinitions = new RowDefinitions("Auto,8,*"),
                    Children =
                    {
                        At(new TextBlock { Text = Loc.T("Changed files"), FontWeight = global::Avalonia.Media.FontWeight.Bold }, row: 0, column: 0),
                        At(_files, row: 2, column: 0),
                    },
                }, row: 0, column: 2),
                At(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { _apply, _pop, _drop, close },
                }, row: 2, column: 0, columnSpan: 3),
            },
        };

        ReloadStashes();
    }

    private static Control At(Control control, int row, int column, int columnSpan = 1)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, columnSpan);
        return control;
    }

    private StashTreeNode? Selected
        => _list.SelectedIndex >= 0 && _list.SelectedIndex < _stashes.Count ? _stashes[_list.SelectedIndex] : null;

    private void ReloadStashes()
    {
        _stashes = [.. _session.GetStashPanel()];
        _list.ItemsSource = _stashes.Select(stash =>
        {
            ListBoxItem item = new() { Content = stash.DisplayName };
            ToolTip.SetTip(item, stash.ReflogSelector);
            return item;
        }).ToList();
        _list.SelectedIndex = -1;
        _files.ItemsSource = null;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool hasSelection = Selected is not null;
        _apply.IsEnabled = hasSelection;
        _pop.IsEnabled = hasSelection;
        _drop.IsEnabled = hasSelection;
    }

    private async Task RefreshFilesAsync()
    {
        UpdateButtons();

        if (Selected is not StashTreeNode selected)
        {
            _files.ItemsSource = null;
            return;
        }

        (bool success, string output) = await _session.StashShowAsync(selected.ReflogSelector);
        _files.ItemsSource = success
            ? output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(FormatFileLine).ToList()
            : [Loc.T("(failed to load stash contents)")];
    }

    /// <summary>A "stash show --name-status" line ("M\tpath", renames carry two paths).</summary>
    private static string FormatFileLine(string line)
    {
        string[] parts = line.TrimEnd('\r').Split('\t');
        return parts.Length >= 2 ? $"{parts[0]}  {string.Join(" -> ", parts.Skip(1))}" : line;
    }

    private async Task SaveStashAsync(bool stagedOnly)
    {
        (bool success, string output) = stagedOnly
            ? await _session.StashStagedAsync()
            : await _session.StashSaveAsync(_message.Text, _keepIndex.IsChecked is true, _includeUntracked.IsChecked is true);

        if (!success)
        {
            await ConfirmDialog.ErrorAsync(this, Loc.T("Stash"), output);
            return;
        }

        StashesChanged = true;
        _message.Text = "";
        ReloadStashes();
    }

    private async Task ApplySelectedAsync()
    {
        if (Selected is not StashTreeNode selected)
        {
            return;
        }

        (bool success, string output) = await _session.StashApplyAsync(selected.ReflogSelector);
        if (!success)
        {
            await ConfirmDialog.ErrorAsync(this, Loc.T("Apply stash"), output);
            return;
        }

        StashesChanged = true;
        ReloadStashes();
    }

    private async Task PopSelectedAsync()
    {
        if (Selected is not StashTreeNode selected)
        {
            return;
        }

        // Pop of the SELECTED stash: SliceSession.StashPopAsync() always pops stash@{0}, which
        // is the wrong stash whenever another row is selected. So pop is implemented as apply
        // followed by drop - and the drop only runs when the apply succeeded, matching
        // `git stash pop`'s keep-the-stash-on-conflict behavior.
        (bool applied, string applyOutput) = await _session.StashApplyAsync(selected.ReflogSelector);
        if (!applied)
        {
            await ConfirmDialog.ErrorAsync(this, Loc.T("Pop stash"), applyOutput);
            return;
        }

        StashesChanged = true;

        (bool dropped, string dropOutput) = await _session.StashDropAsync(selected.ReflogSelector);
        if (!dropped)
        {
            await ConfirmDialog.ErrorAsync(this, Loc.T("Pop stash"), dropOutput);
        }

        ReloadStashes();
    }

    private async Task DropSelectedAsync()
    {
        if (Selected is not StashTreeNode selected
            || !await ConfirmDialog.ConfirmAsync(this, Loc.T("Drop stash"), $"{Loc.T("Are you sure you want to drop")} {selected.ReflogSelector}?"))
        {
            return;
        }

        (bool success, string output) = await _session.StashDropAsync(selected.ReflogSelector);
        if (!success)
        {
            await ConfirmDialog.ErrorAsync(this, Loc.T("Drop stash"), output);
            return;
        }

        StashesChanged = true;
        ReloadStashes();
    }
}
