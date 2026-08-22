using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GitCommands.Compare;
using GitExtensions.Avalonia.Rendering;
using GitExtensions.Extensibility.Git;
using GitUIPluginInterfaces;

namespace GitExtensions.Avalonia;

/// <summary>
///  The compare window (FormDiff's shape): BASE and Compare commits, swap, an optional
///  compare-to-merge-base, the changed-file list between the effective pair, and per-file
///  diffs - decisions from <see cref="CompareRevisions"/>.
/// </summary>
public sealed class CompareWindow : Window
{
    private readonly SliceSession _session;
    private ObjectId _firstId;
    private ObjectId _secondId;
    private string _firstLabel;
    private string _secondLabel;
    private readonly ObjectId? _mergeBase;
    private readonly TextBlock _header = new() { FontWeight = global::Avalonia.Media.FontWeight.Bold };
    private readonly CheckBox _compareToMergeBase = new() { Content = "Compare to merge base" };
    private readonly ListBox _files = new() { MinWidth = 260 };
    private readonly SelectableTextBlock _diffGutter = new() { FontFamily = "monospace", FontSize = 12.5, Foreground = global::Avalonia.Media.Brushes.Gray, Margin = new global::Avalonia.Thickness(0, 0, 10, 0) };
    private readonly SelectableTextBlock _diffText = new() { FontFamily = "monospace", FontSize = 12.5 };
    private IReadOnlyList<GitItemStatus> _fileList = [];
    private CancellationTokenSource? _cts;

    public CompareWindow(SliceSession session, ObjectId firstId, string firstLabel, ObjectId secondId, string secondLabel)
    {
        _session = session;
        _firstId = firstId;
        _secondId = secondId;
        _firstLabel = firstLabel;
        _secondLabel = secondLabel;

        Title = "Compare";
        Width = 1250;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _mergeBase = _session.ResolveMergeBase(firstId, secondId);
        _compareToMergeBase.Content = $"Compare to merge base ({_mergeBase?.ToShortString() ?? "n/a"})";
        _compareToMergeBase.IsEnabled = _mergeBase is not null;
        _compareToMergeBase.IsCheckedChanged += (_, _) => _ = PopulateAsync();
        DiffPaneMenu.Attach(_diffText, () => _diffPaneText);

        Button swap = new() { Content = "Swap", FontSize = 12 };
        swap.Click += (_, _) =>
        {
            (_firstId, _secondId) = (_secondId, _firstId);
            (_firstLabel, _secondLabel) = (_secondLabel, _firstLabel);
            _ = PopulateAsync();
        };

        _files.SelectionChanged += (_, _) => _ = ShowSelectedFileAsync();

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*"),
            Margin = new global::Avalonia.Thickness(10),
            Children =
            {
                WithRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 16,
                    Margin = new global::Avalonia.Thickness(0, 0, 0, 8),
                    Children = { _header, swap, _compareToMergeBase },
                }, 0),
                WithRow(new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,10,*"),
                    Children =
                    {
                        WithColumn(new ScrollViewer { Content = _files }, 0),
                        WithColumn(new ScrollViewer
                        {
                            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _diffGutter, _diffText } },
                        }, 2),
                    },
                }, 1),
            },
        };

        Loaded += (_, _) => _ = PopulateAsync();
    }

    private static Control WithRow(Control control, int row)
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static Control WithColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private ObjectId EffectiveBase => CompareRevisions.ResolveBase(_firstId, _mergeBase, _compareToMergeBase.IsChecked is true);

    private async Task PopulateAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        CancellationToken cancellationToken = _cts.Token;

        ObjectId baseId = EffectiveBase;
        _header.Text = $"BASE: {(_compareToMergeBase.IsChecked is true ? $"merge base {baseId.ToShortString()}" : _firstLabel)}  →  Compare: {_secondLabel}";

        try
        {
            _fileList = await Task.Run(() => _session.GetDiffFilesBetween(baseId, _secondId, cancellationToken), cancellationToken);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _files.ItemsSource = _fileList.Select(file => file.Name).ToList();
                if (_fileList.Count > 0)
                {
                    _files.SelectedIndex = 0;
                }
                else
                {
                    _diffText.Text = "No differences.";
                    _diffGutter.Text = "";
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _diffText.Text = ex.Message;
        }
    }

    private async Task ShowSelectedFileAsync()
    {
        if (_files.SelectedIndex < 0 || _files.SelectedIndex >= _fileList.Count)
        {
            return;
        }

        GitItemStatus file = _fileList[_files.SelectedIndex];
        ObjectId baseId = EffectiveBase;

        try
        {
            var (diffText, spans, lineNumbers) = await Task.Run(() => _session.GetRevisionFileDiff(baseId, _secondId, file));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _diffPaneText = diffText;
                _diffText.Inlines!.Clear();
                _diffText.Inlines.AddRange(InlineRendering.ToInlines(diffText, spans));
                _diffGutter.Text = LineNumberGutter.Build(diffText, lineNumbers);
            });
        }
        catch (Exception ex)
        {
            _diffPaneText = null;
            _diffText.Text = ex.Message;
        }
    }

    /// <summary>The diff pane's current unified diff text (inlines don't retain it).</summary>
    private string? _diffPaneText;

    /// <summary>Verification harness: report the file count and first diff size.</summary>
    internal async Task<(int Files, int DiffInlines)> ProbeAsync()
    {
        await Task.Delay(1500);
        return (_fileList.Count, _diffText.Inlines?.Count ?? 0);
    }
}
