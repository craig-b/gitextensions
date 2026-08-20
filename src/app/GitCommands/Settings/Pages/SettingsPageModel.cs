namespace GitCommands.Settings.Pages;

/// <summary>
///  A captioned run of entries within a page (the labeled sections of the WinForms layout).
/// </summary>
public sealed class SettingsGroup
{
    public SettingsGroup(string caption, params SettingsEntry[] entries)
    {
        Caption = caption;
        Entries = entries;
    }

    public string Caption { get; }

    public IReadOnlyList<SettingsEntry> Entries { get; }
}

/// <summary>
///  Portable presentation model for one settings page: the page's entries in display order,
///  detached from any UI toolkit. Views bind entry values; hosts call <see cref="Load"/>
///  before showing and <see cref="Save"/> on apply.
/// </summary>
public abstract class SettingsPageModel
{
    protected SettingsPageModel(string title)
    {
        Title = title;
    }

    public string Title { get; }

    public abstract IReadOnlyList<SettingsGroup> Groups { get; }

    public IEnumerable<SettingsEntry> Entries => Groups.SelectMany(group => group.Entries);

    public void Load()
    {
        foreach (SettingsEntry entry in Entries)
        {
            entry.Load();
        }
    }

    public virtual void Save()
    {
        foreach (SettingsEntry entry in Entries)
        {
            entry.Save();
        }
    }
}
