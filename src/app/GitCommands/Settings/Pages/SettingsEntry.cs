namespace GitCommands.Settings.Pages;

/// <summary>
///  One row of a settings page: a caption plus load/save against the backing store.
///  Entries speak view semantics — any inversion between what the user sees and how the
///  value is stored belongs to the page model that constructs the entry, so every view
///  (WinForms, Avalonia) binds the value verbatim.
/// </summary>
public abstract class SettingsEntry
{
    protected SettingsEntry(string caption)
    {
        Caption = caption;
    }

    public string Caption { get; }

    /// <summary>Pulls the stored value into <c>Value</c>.</summary>
    public abstract void Load();

    /// <summary>Pushes <c>Value</c> back to the store (in memory; the host decides when to flush).</summary>
    public abstract void Save();
}

public sealed class BoolSettingsEntry : SettingsEntry
{
    private readonly Func<bool> _load;
    private readonly Action<bool> _save;

    public BoolSettingsEntry(string caption, Func<bool> load, Action<bool> save)
        : base(caption)
    {
        _load = load;
        _save = save;
    }

    public bool Value { get; set; }

    public override void Load() => Value = _load();

    public override void Save() => _save(Value);
}

/// <summary>
///  A three-state toggle; <c>null</c> renders as an indeterminate check state.
/// </summary>
public sealed class TriStateSettingsEntry : SettingsEntry
{
    private readonly Func<bool?> _load;
    private readonly Action<bool?> _save;

    public TriStateSettingsEntry(string caption, Func<bool?> load, Action<bool?> save)
        : base(caption)
    {
        _load = load;
        _save = save;
    }

    public bool? Value { get; set; }

    public override void Load() => Value = _load();

    public override void Save() => _save(Value);
}

public sealed class NumberSettingsEntry : SettingsEntry
{
    private readonly Func<int> _load;
    private readonly Action<int> _save;

    public NumberSettingsEntry(string caption, int minimum, int maximum, int increment, Func<int> load, Action<int> save)
        : base(caption)
    {
        Minimum = minimum;
        Maximum = maximum;
        Increment = increment;
        _load = load;
        _save = save;
    }

    public int Minimum { get; }
    public int Maximum { get; }
    public int Increment { get; }

    public int Value { get; set; }

    public override void Load() => Value = _load();

    public override void Save() => _save(Value);
}

/// <summary>
///  A number gated by a toggle (e.g. "limit commits to N"); how the disabled state is
///  stored (typically a sentinel value) is the constructing page model's business.
/// </summary>
public sealed class OptionalNumberSettingsEntry : SettingsEntry
{
    private readonly Func<(bool Enabled, int Number)> _load;
    private readonly Action<bool, int> _save;

    public OptionalNumberSettingsEntry(string caption, int minimum, int maximum, int increment,
        Func<(bool Enabled, int Number)> load, Action<bool, int> save)
        : base(caption)
    {
        Minimum = minimum;
        Maximum = maximum;
        Increment = increment;
        _load = load;
        _save = save;
    }

    public int Minimum { get; }
    public int Maximum { get; }
    public int Increment { get; }

    public bool Enabled { get; set; }
    public int Number { get; set; }

    public override void Load() => (Enabled, Number) = _load();

    public override void Save() => _save(Enabled, Number);
}

public sealed class StringSettingsEntry : SettingsEntry
{
    private readonly Func<string> _load;
    private readonly Action<string> _save;

    public StringSettingsEntry(string caption, Func<string> load, Action<string> save)
        : base(caption)
    {
        _load = load;
        _save = save;
    }

    public string Value { get; set; } = string.Empty;

    public override void Load() => Value = _load();

    public override void Save() => _save(Value);
}

/// <summary>
///  A single choice from a fixed list. The entry is type-erased to an index into
///  <see cref="Choices"/>; the page model owns the mapping to the stored value, and
///  every view must present the choices in this order.
/// </summary>
public sealed class ChoiceSettingsEntry : SettingsEntry
{
    private readonly Func<int> _load;
    private readonly Action<int> _save;

    public ChoiceSettingsEntry(string caption, IReadOnlyList<string> choices, Func<int> load, Action<int> save)
        : base(caption)
    {
        Choices = choices;
        _load = load;
        _save = save;
    }

    public IReadOnlyList<string> Choices { get; }

    public int SelectedIndex { get; set; }

    public override void Load() => SelectedIndex = _load();

    public override void Save() => _save(SelectedIndex);
}
