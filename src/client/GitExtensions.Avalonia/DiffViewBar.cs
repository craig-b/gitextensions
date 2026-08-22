using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
internal sealed class DiffViewBar : StackPanel
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
        Spacing = 4;
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
        _contextLabel = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 };
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
        };
        ToolTip.SetTip(button, Loc.T(tooltip));
        button.Click += (_, _) => onClick();
        return button;
    }
}
