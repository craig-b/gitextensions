using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GitCommands.Refs;

namespace GitExtensions.Avalonia;

/// <summary>
///  The batch-ref operations dialog ("Operate on N refs..."): a deselectable checked list with
///  observability columns (merged-into-current, upstream, tip subject), a verb picker with
///  per-verb options, the force gate for unmerged deletes, and per-row outcomes after the run.
///  All decisions live in the portable <see cref="BatchRefOperations"/> engine.
/// </summary>
internal sealed class RefOperationsWindow : Window
{
    private sealed class Row
    {
        public required BatchRefRow Data { get; init; }

        public required CheckBox Check { get; init; }

        public required TextBlock Status { get; init; }
    }

    private readonly SliceSession _session;
    private readonly List<Row> _rows = [];
    private readonly ComboBox _verb = new() { MinWidth = 120 };
    private readonly CheckBox _forceDelete = new() { Content = Loc.T("Force delete unmerged (-D)") };
    private readonly CheckBox _deleteRemote = new() { Content = Loc.T("Also delete on the remote (tracking counterpart)") };
    private readonly ComboBox _pushRemote = new() { MinWidth = 140 };
    private readonly CheckBox _forceWithLease = new() { Content = Loc.T("Force with lease") };
    private readonly StackPanel _deleteOptions = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
    private readonly StackPanel _pushOptions = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
    private readonly Button _run = new() { MinWidth = 140 };
    private readonly TextBlock _summary = new() { FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>Whether any run mutated refs - the caller refreshes the sidebar and log off this.</summary>
    public bool RefsChanged { get; private set; }

    public RefOperationsWindow(SliceSession session, IReadOnlyList<BatchRefRow> rows, IReadOnlySet<string> initiallyChecked)
    {
        _session = session;

        Title = Loc.T("Ref operations");
        Width = 860;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        StackPanel list = new() { Spacing = 2 };
        foreach (BatchRefRow data in rows)
        {
            CheckBox check = new() { IsChecked = initiallyChecked.Contains(data.Name), VerticalAlignment = VerticalAlignment.Center };
            check.IsCheckedChanged += (_, _) => UpdateRunState();
            TextBlock status = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

            Grid rowGrid = new()
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,240,90,110,150,*"),
                Children =
                {
                    AtColumn(check, 0),
                    AtColumn(new TextBlock { Text = data.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, 1),
                    AtColumn(new TextBlock { Text = KindLabel(data.Kind), FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }, 2),
                    AtColumn(MergedLabel(data.MergedIntoCurrent), 3),
                    AtColumn(new TextBlock { Text = data.Upstream ?? "", FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, 4),
                    AtColumn(status, 5),
                },
            };
            TextBlock subject = new() { Text = data.Subject ?? "", FontSize = 11, Opacity = 0.55, Margin = new global::Avalonia.Thickness(28, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };

            list.Children.Add(rowGrid);
            list.Children.Add(subject);
            _rows.Add(new Row { Data = data, Check = check, Status = status });
        }

        _verb.ItemsSource = new[] { Loc.T("Delete"), Loc.T("Push") };
        _verb.SelectedIndex = 0;
        _verb.SelectionChanged += (_, _) => UpdateRunState();
        _forceDelete.IsCheckedChanged += (_, _) => UpdateRunState();

        _deleteOptions.Children.Add(_forceDelete);
        _deleteOptions.Children.Add(_deleteRemote);

        IReadOnlyList<string> remotes = session.GetRemoteNames();
        _pushRemote.ItemsSource = remotes;
        _pushRemote.SelectedIndex = remotes.Count > 0 ? 0 : -1;
        _pushOptions.Children.Add(new TextBlock { Text = Loc.T("Remote:"), VerticalAlignment = VerticalAlignment.Center });
        _pushOptions.Children.Add(_pushRemote);
        _pushOptions.Children.Add(_forceWithLease);

        _run.Click += async (_, _) => await RunAsync();

        Button selectAll = new() { Content = Loc.T("All"), FontSize = 12 };
        selectAll.Click += (_, _) => SetAllChecked(true);
        Button selectNone = new() { Content = Loc.T("None"), FontSize = 12 };
        selectNone.Click += (_, _) => SetAllChecked(false);

        Button close = new() { Content = Loc.T("Close"), MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new global::Avalonia.Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,8,*,12,Auto"),
            Children =
            {
                AtRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Children =
                    {
                        new TextBlock { Text = Loc.T("Operation:"), VerticalAlignment = VerticalAlignment.Center },
                        _verb,
                        _deleteOptions,
                        _pushOptions,
                        selectAll,
                        selectNone,
                    },
                }, 0),
                AtRow(new ScrollViewer { Content = list }, 2),
                AtRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { _summary, _run, close },
                }, 4),
            },
        };

        UpdateRunState();
    }

    private static Control AtColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static Control AtRow(Control control, int row)
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static TextBlock MergedLabel(bool? mergedIntoCurrent)
    {
        TextBlock label = new()
        {
            Text = mergedIntoCurrent switch { true => Loc.T("merged"), false => Loc.T("NOT merged"), null => "" },
            FontSize = 12,
            Opacity = mergedIntoCurrent is false ? 1 : 0.7,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (mergedIntoCurrent is false)
        {
            label.Foreground = Brushes.Orange;
        }

        return label;
    }

    private static string KindLabel(BatchRefKind kind)
        => kind switch
        {
            BatchRefKind.LocalBranch => Loc.T("branch"),
            BatchRefKind.RemoteBranch => Loc.T("remote branch"),
            _ => Loc.T("tag"),
        };

    private BatchRefVerb SelectedVerb => _verb.SelectedIndex == 1 ? BatchRefVerb.Push : BatchRefVerb.Delete;

    private IReadOnlyList<BatchRefRow> CheckedRows => [.. _rows.Where(row => row.Check.IsChecked is true).Select(row => row.Data)];

    private void SetAllChecked(bool value)
    {
        foreach (Row row in _rows)
        {
            row.Check.IsChecked = value;
        }
    }

    /// <summary>The arming logic: caption carries the count; unmerged deletes stay disabled until forced.</summary>
    private void UpdateRunState()
    {
        BatchRefVerb verb = SelectedVerb;
        _deleteOptions.IsVisible = verb is BatchRefVerb.Delete;
        _pushOptions.IsVisible = verb is BatchRefVerb.Push;

        (IReadOnlyList<BatchRefRow> applicable, IReadOnlyList<BatchRefRow> skipped) = BatchRefOperations.Partition(verb, CheckedRows);
        foreach (Row row in _rows)
        {
            bool applies = BatchRefOperations.Applies(verb, row.Data);
            row.Check.Opacity = applies ? 1 : 0.5;
        }

        string verbCaption = verb is BatchRefVerb.Delete ? Loc.T("Delete") : Loc.T("Push");
        _run.Content = $"{verbCaption} {applicable.Count} {(applicable.Count == 1 ? Loc.T("ref") : Loc.T("refs"))}";

        bool forceBlocked = verb is BatchRefVerb.Delete
            && BatchRefOperations.RequiresForceDelete(applicable)
            && _forceDelete.IsChecked is not true;
        bool remoteMissing = verb is BatchRefVerb.Push && _pushRemote.SelectedItem is null;
        _run.IsEnabled = applicable.Count > 0 && !forceBlocked && !remoteMissing;

        _summary.Text = forceBlocked
            ? Loc.T("Unmerged branches checked - enable force to arm")
            : skipped.Count > 0
                ? string.Format(Loc.T("{0} checked row(s) not applicable - will be skipped"), skipped.Count)
                : "";
    }

    private async Task RunAsync()
    {
        BatchRefVerb verb = SelectedVerb;
        (IReadOnlyList<BatchRefRow> applicable, IReadOnlyList<BatchRefRow> skipped) = BatchRefOperations.Partition(verb, CheckedRows);
        if (applicable.Count == 0)
        {
            return;
        }

        IReadOnlyList<BatchRefCommand> commands = verb is BatchRefVerb.Delete
            ? BatchRefOperations.BuildDeleteCommands(applicable, _forceDelete.IsChecked is true, _deleteRemote.IsChecked is true)
            : [BatchRefOperations.BuildPushCommand(applicable, (string)_pushRemote.SelectedItem!, _forceWithLease.IsChecked is true)];

        _run.IsEnabled = false;
        foreach (BatchRefRow skippedRow in skipped)
        {
            SetStatus(skippedRow.Name, BatchRefOutcome.Skipped, null);
        }

        try
        {
            foreach (BatchRefCommand command in commands)
            {
                (bool success, string output) = await _session.RunBatchRefCommandAsync(command.Arguments);
                foreach (BatchRefRowResult result in BatchRefOperations.ParseResults(command, success, output))
                {
                    SetStatus(result.Name, result.Outcome, result.Message);
                    RefsChanged |= result.Outcome is BatchRefOutcome.Succeeded;
                }
            }

            _summary.Text = Loc.T("Done - see per-row results");
        }
        finally
        {
            _run.IsEnabled = true;
        }
    }

    private void SetStatus(string name, BatchRefOutcome outcome, string? message)
    {
        foreach (Row row in _rows.Where(row => row.Data.Name == name))
        {
            (string label, IBrush? brush) = outcome switch
            {
                BatchRefOutcome.Succeeded => ("✓ " + (message ?? "").Trim(), Brushes.Green),
                BatchRefOutcome.Failed => ("✗ " + (message ?? Loc.T("failed")).Trim(), Brushes.OrangeRed),
                _ => (Loc.T("skipped"), null),
            };
            row.Status.Text = label;
            if (brush is not null)
            {
                row.Status.Foreground = brush;
            }
        }
    }
}
