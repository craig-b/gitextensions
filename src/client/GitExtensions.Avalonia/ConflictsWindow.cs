using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using GitCommands.Conflicts;
using GitExtensions.Extensibility.Git;

namespace GitExtensions.Avalonia;

/// <summary>
///  The conflict-resolution window (FormResolveConflicts' shape) over the conflicts
///  models: conflicted files with their kind, rebase-aware ours/theirs labels, per-kind
///  outcome buttons, mark-resolved, and the all-resolved completion flow.
/// </summary>
public sealed class ConflictsWindow : Window
{
    private readonly SliceSession _session;
    private readonly ListBox _list = new() { MinWidth = 340, MinHeight = 300 };
    private readonly TextBlock _description = new() { FontSize = 12, Foreground = global::Avalonia.Media.Brushes.Gray, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap, MaxWidth = 380 };
    private readonly Button _takeLocal = new() { MinWidth = 220 };
    private readonly Button _takeRemote = new() { MinWidth = 220 };
    private readonly Button _takeBase = new() { Content = "Take base", MinWidth = 220 };
    private readonly Button _deleteFile = new() { Content = "Delete file", MinWidth = 220 };
    private readonly Button _markResolved = new() { Content = "Mark resolved (keep working tree)", MinWidth = 220 };
    private List<ConflictData> _conflicts = [];
    private bool _thereWereConflicts;

    public bool ShouldOfferCommit { get; private set; }

    public ConflictsWindow(SliceSession session)
    {
        _session = session;
        Title = $"Resolve conflicts - {session.WorkingDir}";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        ConflictSideLabels labels = ConflictSideLabels.Resolve(_session.InRebase, "ours", "theirs");
        _takeLocal.Content = $"Take local ({labels.LocalLabel})";
        _takeRemote.Content = $"Take remote ({labels.RemoteLabel})";

        _list.SelectionChanged += (_, _) => UpdateButtons();
        _takeLocal.Click += async (_, _) => await ResolveSelectedAsync(ConflictOutcome.TakeLocal);
        _takeRemote.Click += async (_, _) => await ResolveSelectedAsync(ConflictOutcome.TakeRemote);
        _takeBase.Click += async (_, _) => await ResolveSelectedAsync(ConflictOutcome.TakeBase);
        _deleteFile.Click += async (_, _) => await ResolveSelectedAsync(ConflictOutcome.DeleteFile);
        _markResolved.Click += async (_, _) =>
        {
            if (SelectedConflict() is ConflictData conflict)
            {
                await _session.StageConflictedFileAsync(conflict.Filename);
                await ReloadAsync();
            }
        };

        Button rescan = new() { Content = "Rescan" };
        rescan.Click += async (_, _) => await ReloadAsync();
        Button close = new() { Content = "Close", MinWidth = 90, IsCancel = true };
        close.Click += (_, _) => Close();

        Content = new Grid
        {
            Margin = new global::Avalonia.Thickness(16),
            ColumnDefinitions = new ColumnDefinitions("Auto,16,Auto"),
            Children =
            {
                WithColumn(new ScrollViewer { Content = _list }, 0),
                WithColumn(new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        _description,
                        _takeLocal,
                        _takeRemote,
                        _takeBase,
                        _deleteFile,
                        _markResolved,
                        new Separator(),
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            Spacing = 8,
                            Children = { rescan, close },
                        },
                    },
                }, 2),
            },
        };

        Loaded += async (_, _) => await ReloadAsync();
    }

    private static Control WithColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private ConflictData? SelectedConflict()
        => _list.SelectedIndex >= 0 && _list.SelectedIndex < _conflicts.Count ? _conflicts[_list.SelectedIndex] : null;

    private async Task ReloadAsync()
    {
        _conflicts = await _session.GetConflictsAsync();
        _thereWereConflicts |= _conflicts.Count > 0;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _list.ItemsSource = _conflicts
                .Select(conflict => $"{conflict.Filename}  [{Describe(ConflictClassifier.Classify(conflict))}]")
                .ToList();
            if (_conflicts.Count > 0)
            {
                _list.SelectedIndex = 0;
            }

            UpdateButtons();
        });

        ConflictCompletionDecision completion = ConflictCompletionDecision.Evaluate(
            stillConflicted: _session.InConflictedMerge,
            _thereWereConflicts,
            _session.InPatch,
            _session.InRebase,
            offerCommit: true);

        if (completion.ShouldClose)
        {
            ShouldOfferCommit = completion.ShouldOfferCommit;
            Close();
        }
    }

    private void UpdateButtons()
    {
        ConflictData? conflict = SelectedConflict();
        IReadOnlyList<ConflictOutcome> outcomes = conflict is ConflictData selected
            ? ConflictResolutionChoices.For(ConflictClassifier.Classify(selected))
            : [];

        _takeLocal.IsEnabled = outcomes.Contains(ConflictOutcome.TakeLocal);
        _takeRemote.IsEnabled = outcomes.Contains(ConflictOutcome.TakeRemote);
        _takeBase.IsEnabled = outcomes.Contains(ConflictOutcome.TakeBase);
        _deleteFile.IsEnabled = outcomes.Contains(ConflictOutcome.DeleteFile);
        _markResolved.IsEnabled = conflict is not null;
        _description.Text = conflict is ConflictData current ? Describe(ConflictClassifier.Classify(current)) : "";
    }

    private async Task ResolveSelectedAsync(ConflictOutcome outcome)
    {
        if (SelectedConflict() is not ConflictData conflict)
        {
            return;
        }

        switch (outcome)
        {
            case ConflictOutcome.TakeLocal:
                await _session.ResolveConflictSideAsync(conflict.Filename, ConflictSide.Local);
                break;
            case ConflictOutcome.TakeRemote:
                await _session.ResolveConflictSideAsync(conflict.Filename, ConflictSide.Remote);
                break;
            case ConflictOutcome.TakeBase:
                await _session.ResolveConflictSideAsync(conflict.Filename, ConflictSide.Base);
                break;
            case ConflictOutcome.DeleteFile:
                if (await ConfirmDialog.ConfirmAsync(this, "Delete file", $"Delete {conflict.Filename}?"))
                {
                    await _session.RemoveConflictedFileAsync(conflict.Filename);
                }

                break;
        }

        await ReloadAsync();
    }

    private static string Describe(ConflictKind kind)
        => kind switch
        {
            ConflictKind.ChangedBothSides => "changed on both sides",
            ConflictKind.AddedBothSides => "added on both sides",
            ConflictKind.DeletedLocallyModifiedRemotely => "deleted locally, modified remotely",
            ConflictKind.ModifiedLocallyDeletedRemotely => "modified locally, deleted remotely",
            _ => "conflict",
        };

    /// <summary>Verification harness: resolve each conflict with its first offered outcome.</summary>
    internal async Task<string> RunHarnessAsync(ConflictOutcome preferred)
    {
        await Task.Delay(800);
        int initial = _conflicts.Count;
        List<string> kinds = [.. _conflicts.Select(conflict => Describe(ConflictClassifier.Classify(conflict)))];
        for (int guard = 0; _conflicts.Count > 0 && guard < initial + 5; guard++)
        {
            _list.SelectedIndex = 0;
            ConflictData conflict = _conflicts[0];
            IReadOnlyList<ConflictOutcome> outcomes = ConflictResolutionChoices.For(ConflictClassifier.Classify(conflict));
            ConflictOutcome outcome = outcomes.Contains(preferred) ? preferred : outcomes[0];
            if (outcome is ConflictOutcome.DeleteFile)
            {
                await _session.RemoveConflictedFileAsync(conflict.Filename);
                await ReloadAsync();
            }
            else
            {
                await ResolveSelectedAsync(outcome);
            }
        }

        return $"{initial} conflicts [{string.Join("; ", kinds)}] -> {(_conflicts.Count == 0 ? "resolved" : $"{_conflicts.Count} LEFT")}, offerCommit={ShouldOfferCommit}";
    }
}
