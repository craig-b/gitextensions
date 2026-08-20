using GitExtensions.Extensibility.Settings;

namespace GitCommandsTests.Settings.Pages;

/// <summary>Dictionary-backed settings source; setting null removes the key (git-config unset).</summary>
internal sealed class MemorySettingsSource : SettingsSource
{
    private readonly Dictionary<string, string?> _values = [];

    public override string? GetValue(string name) => _values.TryGetValue(name, out string? value) ? value : null;

    public override void SetValue(string name, string? value)
    {
        if (value is null)
        {
            _values.Remove(name);
        }
        else
        {
            _values[name] = value;
        }
    }
}
