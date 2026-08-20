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
