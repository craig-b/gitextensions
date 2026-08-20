namespace GitCommands.Actions;

/// <summary>
///  Hotkey overrides for registry actions: stored per action id, the descriptor's
///  hotkey is the default, and the "none" sentinel disables one.
/// </summary>
public static class HotkeyResolution
{
    public const string DisabledSentinel = "none";

    public static string SettingKey(string actionId) => $"Hotkeys.{actionId}";

    /// <summary>Blank falls back to the default; "none" disables; anything else overrides.</summary>
    public static string? Effective(string? overrideValue, string? defaultHotkey)
    {
        if (string.IsNullOrWhiteSpace(overrideValue))
        {
            return defaultHotkey;
        }

        string trimmed = overrideValue.Trim();
        return trimmed.Equals(DisabledSentinel, StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    /// <summary>The effective gesture per action id, omitting actions without one.</summary>
    public static IReadOnlyDictionary<string, string> ResolveAll(
        IEnumerable<ActionDescriptor> actions,
        Func<string, string?> getOverride)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach (ActionDescriptor action in actions)
        {
            if (Effective(getOverride(action.Id), action.Hotkey) is string gesture)
            {
                map[action.Id] = gesture;
            }
        }

        return map;
    }

    /// <summary>A canonical "Ctrl+Shift+X" spelling so stored gestures compare reliably.</summary>
    public static string Normalize(bool control, bool shift, bool alt, string keyName)
    {
        string gesture = "";
        if (control)
        {
            gesture += "Ctrl+";
        }

        if (shift)
        {
            gesture += "Shift+";
        }

        if (alt)
        {
            gesture += "Alt+";
        }

        return gesture + keyName;
    }
}
