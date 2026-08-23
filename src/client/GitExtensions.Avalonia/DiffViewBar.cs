using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using GitCommands;
using GitCommands.Settings;

namespace GitExtensions.Avalonia;

/// <summary>
///  The per-pane diff view bar: the pointer rule evicted the view toggles from the context
///  menu, and this is where they live. Whitespace modes, context-line count, entire file,
///  and treat-as-text all route into the actual git arguments (SliceSession); appearance,
///  syntax highlighting, and nonprinting characters wait on renderer support.
/// </summary>
internal sealed class DiffViewBar : WrapPanel
{
    private readonly SliceSession _session;
    private readonly ToggleButton _wsEol;
    private readonly ToggleButton _wsChange;
    private readonly ToggleButton _wsAll;
    private readonly Button _lessContext;
    private readonly Button _moreContext;
    private readonly TextBlock _contextLabel;
    private readonly ToggleButton _entireFile;
    private readonly ToggleButton _asText;
    private bool _syncing;

    /// <summary>Raised after any option changes; the host re-renders its diff.</summary>
    public event EventHandler? OptionsChanged;

    public DiffViewBar(SliceSession session)
    {
        _session = session;
        Orientation = Orientation.Horizontal;
        Margin = new global::Avalonia.Thickness(0, 2, 0, 2);

        _wsEol = Toggle("EOL", "Ignore whitespace changes at end of line", () => CycleWhitespace(IgnoreWhitespaceKind.Eol));
        _wsChange = Toggle("WS", "Ignore changes in amount of whitespace", () => CycleWhitespace(IgnoreWhitespaceKind.Change));
        _wsAll = Toggle("All WS", "Ignore all whitespace changes", () => CycleWhitespace(IgnoreWhitespaceKind.AllSpace));

        _lessContext = SmallButton("−", "Decrease the number of context lines", () =>
        {
            _session.DiffContextLines = Math.Max(0, _session.DiffContextLines - 1);
            AppSettings.NumberOfContextLines = _session.DiffContextLines;
            RaiseChanged();
        });
        _contextLabel = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8, Margin = new global::Avalonia.Thickness(0, 0, 4, 2) };
        _moreContext = SmallButton("+", "Increase the number of context lines", () =>
        {
            _session.DiffContextLines++;
            AppSettings.NumberOfContextLines = _session.DiffContextLines;
            RaiseChanged();
        });

        _entireFile = Toggle("Entire file", "Show the entire file", () =>
        {
            AppSettings.ShowEntireFile.Value = !AppSettings.ShowEntireFile.Value;
            RaiseChanged();
        });
        _asText = Toggle("As text", "Treat all files as text (this session)", () =>
        {
            _session.TreatAllFilesAsText = !_session.TreatAllFilesAsText;
            RaiseChanged();
        });

        Children.Add(_wsEol);
        Children.Add(_wsChange);
        Children.Add(_wsAll);
        Children.Add(new Separator { Width = 8 });
        Children.Add(_lessContext);
        Children.Add(_contextLabel);
        Children.Add(_moreContext);
        Children.Add(_entireFile);
        Children.Add(new Separator { Width = 8 });
        Children.Add(_asText);

        SyncState();
    }

    private SelectableTextBlock? _findPane;
    private ScrollViewer? _findScroller;
    private Func<string?>? _findGetText;
    private TextBox? _findBox;
    private TextBlock? _findMatches;
    private readonly List<int> _matchOffsets = [];
    private int _matchIndex = -1;
    private string _lastQuery = "";

    /// <summary>
    ///  Adds the find half of the bar (Ctrl+F is pane-global, so it lives here, not in the
    ///  context menu): a query box with next/previous, a match counter, and hunk navigation.
    /// </summary>
    public void AttachFind(SelectableTextBlock pane, ScrollViewer scroller, Func<string?> getText)
    {
        _findPane = pane;
        _findScroller = scroller;
        _findGetText = getText;

        _findBox = new TextBox
        {
            Watermark = Loc.T("Find"),
            FontSize = 11,
            MinWidth = 130,
            Padding = new global::Avalonia.Thickness(6, 2),
            Margin = new global::Avalonia.Thickness(0, 0, 4, 2),
        };
        _findBox.KeyDown += (_, keyArgs) =>
        {
            if (keyArgs.Key == Key.Enter)
            {
                keyArgs.Handled = true;
                Navigate(keyArgs.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : +1);
            }
            else if (keyArgs.Key == Key.Escape)
            {
                keyArgs.Handled = true;
                pane.Focus();
            }
        };
        _findBox.TextChanged += (_, _) => Navigate(+1, requery: true);

        _findMatches = new TextBlock { FontSize = 11, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, MinWidth = 34 };

        Children.Add(new Separator { Width = 8 });
        Children.Add(_findBox);
        Children.Add(SmallButton("↑", "Previous match (Shift+Enter)", () => Navigate(-1)));
        Children.Add(SmallButton("↓", "Next match (Enter)", () => Navigate(+1)));
        Children.Add(_findMatches);
        Children.Add(SmallButton("⌃", "Previous change (hunk)", () => NavigateHunk(-1)));
        Children.Add(SmallButton("⌄", "Next change (hunk)", () => NavigateHunk(+1)));
    }

    /// <summary>Focuses the find box (the hosts' Ctrl+F).</summary>
    public void FocusFind()
    {
        _findBox?.Focus();
        _findBox?.SelectAll();
    }

    private void Navigate(int direction, bool requery = false)
    {
        if (_findGetText?.Invoke() is not string text || _findBox?.Text is not string query || query.Length == 0)
        {
            SetMatches(0, -1);
            return;
        }

        if (requery || query != _lastQuery)
        {
            _lastQuery = query;
            _matchOffsets.Clear();
            _matchIndex = -1;
            for (int at = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
                 at >= 0;
                 at = at + query.Length < text.Length ? text.IndexOf(query, at + query.Length, StringComparison.OrdinalIgnoreCase) : -1)
            {
                _matchOffsets.Add(at);
            }
        }

        if (_matchOffsets.Count == 0)
        {
            SetMatches(0, -1);
            return;
        }

        _matchIndex = _matchIndex < 0
            ? (direction >= 0 ? 0 : _matchOffsets.Count - 1)
            : ((_matchIndex + direction) % _matchOffsets.Count + _matchOffsets.Count) % _matchOffsets.Count;
        SetMatches(_matchOffsets.Count, _matchIndex);
        ShowMatch(text, _matchOffsets[_matchIndex], query.Length);
    }

    /// <summary>Jumps between hunk headers - the pane-global next/previous change.</summary>
    private void NavigateHunk(int direction)
    {
        if (_findGetText?.Invoke() is not string text || _findPane is null || _findScroller is null)
        {
            return;
        }

        List<int> hunks = [];
        for (int at = text.IndexOf("\n@@", StringComparison.Ordinal); at >= 0; at = text.IndexOf("\n@@", at + 1, StringComparison.Ordinal))
        {
            hunks.Add(at + 1);
        }

        if (hunks.Count == 0)
        {
            return;
        }

        int current = Math.Min(_findPane.SelectionStart, _findPane.SelectionEnd);
        int target = direction > 0
            ? hunks.Find(offset => offset > current)
            : hunks.FindLast(offset => offset < current);
        if (target == 0 && (direction > 0 ? hunks[^1] <= current : hunks[0] >= current))
        {
            target = direction > 0 ? hunks[0] : hunks[^1];
        }

        ShowMatch(text, target, text.IndexOf('\n', target) is var eol && eol > target ? eol - target : 2);
    }

    /// <summary>Selects the range in the pane and scrolls its line into view.</summary>
    private void ShowMatch(string text, int offset, int length)
    {
        if (_findPane is null || _findScroller is null)
        {
            return;
        }

        _findPane.SelectionStart = offset;
        _findPane.SelectionEnd = offset + length;

        int totalLines = 1;
        int matchLine = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                totalLines++;
                if (i < offset)
                {
                    matchLine++;
                }
            }
        }

        double lineHeight = _findPane.TextLayout.Height / Math.Max(1, totalLines);
        _findScroller.Offset = new global::Avalonia.Vector(
            _findScroller.Offset.X,
            Math.Max(0, (matchLine - 3) * lineHeight));
    }

    private void SetMatches(int count, int index)
    {
        if (_findMatches is not null)
        {
            _findMatches.Text = count == 0 ? "" : $"{index + 1}/{count}";
        }
    }

    /// <summary>Each whitespace button toggles its mode against None (the WinForms semantics).</summary>
    private void CycleWhitespace(IgnoreWhitespaceKind kind)
    {
        AppSettings.IgnoreWhitespaceKind.Value = AppSettings.IgnoreWhitespaceKind.Value == kind
            ? IgnoreWhitespaceKind.None
            : kind;
        RaiseChanged();
    }

    private void RaiseChanged()
    {
        SyncState();
        OptionsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The WinForms check-state ladder: stronger modes light the weaker buttons too.</summary>
    private void SyncState()
    {
        _syncing = true;
        IgnoreWhitespaceKind kind = AppSettings.IgnoreWhitespaceKind.Value;
        _wsEol.IsChecked = kind is not IgnoreWhitespaceKind.None;
        _wsChange.IsChecked = kind is IgnoreWhitespaceKind.Change or IgnoreWhitespaceKind.AllSpace;
        _wsAll.IsChecked = kind is IgnoreWhitespaceKind.AllSpace;

        bool entire = AppSettings.ShowEntireFile.Value;
        _entireFile.IsChecked = entire;
        _lessContext.IsEnabled = !entire;
        _moreContext.IsEnabled = !entire;
        _contextLabel.Text = $"±{_session.DiffContextLines}";
        _asText.IsChecked = _session.TreatAllFilesAsText;
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

    private static Button SmallButton(string caption, string tooltip, Action onClick)
    {
        Button button = new()
        {
            Content = caption,
            FontSize = 11,
            Padding = new global::Avalonia.Thickness(6, 2),
            Margin = new global::Avalonia.Thickness(0, 0, 4, 2),
        };
        ToolTip.SetTip(button, Loc.T(tooltip));
        button.Click += (_, _) => onClick();
        return button;
    }
}
