using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using GitUI.UserControls.RevisionGrid;

namespace GitExtensions.Avalonia;

/// <summary>
///  The browse log's filter bar over the portable <see cref="FilterInfo"/> - the client's
///  FilterToolBar: a revision-filter box with its kind picker (message/author/committer/
///  diff-contains), the space-separated branch filter, and the current-only / first-parent /
///  reflog toggles. Every change rebuilds the exact WinForms git-log arguments.
/// </summary>
internal sealed class FilterBar : WrapPanel
{
    private readonly FilterInfo _filter;
    private readonly TextBox _text;
    private readonly ComboBox _kind;
    private readonly TextBox _branches;
    private readonly ToggleButton _currentOnly;
    private readonly ToggleButton _firstParent;
    private readonly ToggleButton _reflog;
    private readonly TextBlock _pathLabel;
    private bool _syncing;

    /// <summary>Raised after the filter changed; the host restarts the log stream.</summary>
    public event EventHandler? FiltersChanged;

    public FilterBar(FilterInfo filter)
    {
        _filter = filter;
        Orientation = Orientation.Horizontal;
        Margin = new global::Avalonia.Thickness(0, 2, 0, 2);

        _text = new TextBox
        {
            Watermark = Loc.T("Filter revisions"),
            FontSize = 11,
            MinWidth = 150,
            Padding = new global::Avalonia.Thickness(6, 2),
            Margin = new global::Avalonia.Thickness(0, 0, 4, 2),
        };
        _text.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Enter)
            {
                keyArgs.Handled = true;
                ApplyText();
            }
        };

        _kind = new ComboBox
        {
            ItemsSource = new[] { Loc.T("Message"), Loc.T("Author"), Loc.T("Committer"), Loc.T("Diff contains") },
            SelectedIndex = 0,
            FontSize = 11,
            Margin = new global::Avalonia.Thickness(0, 0, 8, 2),
        };
        _kind.SelectionChanged += (_, _) =>
        {
            if (!_syncing && !string.IsNullOrEmpty(_text.Text))
            {
                ApplyText();
            }
        };

        _branches = new TextBox
        {
            Watermark = Loc.T("Branches (space-separated)"),
            FontSize = 11,
            MinWidth = 150,
            Padding = new global::Avalonia.Thickness(6, 2),
            Margin = new global::Avalonia.Thickness(0, 0, 8, 2),
        };
        _branches.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Enter)
            {
                keyArgs.Handled = true;
                ApplyBranchFilter(_branches.Text ?? "");
            }
        };

        _currentOnly = Toggle("Current only", "Show the current branch only", () =>
        {
            _filter.ShowCurrentBranchOnly = !_filter.ShowCurrentBranchOnly;
            RaiseChanged();
        });
        _firstParent = Toggle("First parent", "Show only the first parent of merge commits", () =>
        {
            _filter.ShowOnlyFirstParent = !_filter.ShowOnlyFirstParent;
            RaiseChanged();
        });
        _reflog = Toggle("Reflog", "Show reflog references", () =>
        {
            _filter.ShowReflogReferences = !_filter.ShowReflogReferences;
            RaiseChanged();
        });

        _pathLabel = new TextBlock { FontSize = 11, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new global::Avalonia.Thickness(0, 0, 8, 2) };

        Button clear = new()
        {
            Content = Loc.T("Clear"),
            FontSize = 11,
            Padding = new global::Avalonia.Thickness(6, 2),
            Margin = new global::Avalonia.Thickness(0, 0, 4, 2),
        };
        ToolTip.SetTip(clear, Loc.T("Reset all revision filters"));
        clear.Click += (_, _) => ClearAll();

        Children.Add(_text);
        Children.Add(_kind);
        Children.Add(_branches);
        Children.Add(_currentOnly);
        Children.Add(_firstParent);
        Children.Add(_reflog);
        Children.Add(_pathLabel);
        Children.Add(clear);

        SyncState();
    }

    public void FocusText() => _text.Focus();

    /// <summary>The left panel's "Filter for selected": space-separated ref names into the branch filter.</summary>
    public void ApplyBranchFilter(string spaceSeparatedRefs)
    {
        _filter.SetBranchFilter(spaceSeparatedRefs);
        RaiseChanged();
    }

    /// <summary>The file surface's "Filter file in grid".</summary>
    public void ApplyPathFilter(string pathFilter)
    {
        _filter.ByPathFilter = !string.IsNullOrWhiteSpace(pathFilter);
        _filter.PathFilter = pathFilter;
        RaiseChanged();
    }

    public void ShowAllBranches()
    {
        _filter.ByBranchFilter = false;
        _filter.ShowCurrentBranchOnly = false;
        RaiseChanged();
    }

    public void ShowCurrentBranchOnly()
    {
        _filter.ShowCurrentBranchOnly = true;
        RaiseChanged();
    }

    public void ToggleReflog()
    {
        _filter.ShowReflogReferences = !_filter.ShowReflogReferences;
        RaiseChanged();
    }

    public void ClearAll()
    {
        _filter.ResetAllFilters();
        _text.Text = "";
        _branches.Text = "";
        RaiseChanged();
    }

    private void ApplyText()
    {
        int kind = _kind.SelectedIndex;
        bool changed = _filter.Apply(new RevisionFilter(
            _text.Text ?? "",
            byCommit: kind == 0,
            byCommitter: kind == 2,
            byAuthor: kind == 1,
            byDiffContent: kind == 3));
        if (changed)
        {
            RaiseChanged();
        }
    }

    private void RaiseChanged()
    {
        SyncState();
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SyncState()
    {
        _syncing = true;
        _currentOnly.IsChecked = _filter.IsShowCurrentBranchOnlyChecked;
        _firstParent.IsChecked = _filter.ShowOnlyFirstParent;
        _reflog.IsChecked = _filter.ShowReflogReferences;
        _branches.Text = _filter.BranchFilter;
        _pathLabel.Text = string.IsNullOrWhiteSpace(_filter.PathFilter) ? "" : $"{ResourceManager.TranslatedStrings.PathFilter}: {_filter.PathFilter}";
        _pathLabel.IsVisible = _pathLabel.Text.Length > 0;
        _syncing = false;
    }

    private ToggleButton Toggle(string caption, string tooltip, Action onToggle)
    {
        ToggleButton button = new()
        {
            Content = Loc.T(caption),
            FontSize = 11,
            Padding = new global::Avalonia.Thickness(6, 2),
            Margin = new global::Avalonia.Thickness(0, 0, 4, 2),
        };
        ToolTip.SetTip(button, Loc.T(tooltip));
        button.IsCheckedChanged += (_, _) =>
        {
            if (!_syncing)
            {
                onToggle();
            }
        };
        return button;
    }
}
