using System.Xml.Linq;

namespace GitCommands.Scripts;

/// <summary>
///  One-shot import of the WinForms ScriptInfo list (serialized in AppSettings.OwnScripts)
///  into ScriptDefinitions. Token names carry over unchanged via the legacy aliases; the
///  event trigger and the integer hotkey slot are dropped (events deferred; the slot has no
///  gesture to map to).
/// </summary>
public static class ScriptImport
{
    public static IReadOnlyList<ScriptDefinition> FromWinFormsXml(string? ownScriptsXml)
    {
        if (string.IsNullOrWhiteSpace(ownScriptsXml))
        {
            return [];
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(ownScriptsXml);
        }
        catch
        {
            return [];
        }

        List<ScriptDefinition> definitions = [];
        HashSet<string> usedSlugs = new(StringComparer.Ordinal);

        foreach (XElement element in document.Descendants("ScriptInfo"))
        {
            string name = (string?)element.Element("Name") ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string command = (string?)element.Element("Command") ?? "";
            string arguments = (string?)element.Element("Arguments") ?? "";
            bool isPowerShell = (bool?)element.Element("IsPowerShell") ?? false;

            // WinForms: Command = the program, Arguments = its argument line. A direct exe
            // becomes Interpreter=<program>/Command=<arguments>; a PowerShell script folds
            // both into one pwsh command.
            (string interpreter, string commandTemplate) = isPowerShell
                ? ("pwsh", string.IsNullOrEmpty(arguments) ? command : $"{command} {arguments}")
                : (string.IsNullOrWhiteSpace(command) ? "shell" : command, arguments);

            // The revision-grid flag maps to the commit menu; user-menu-bar-only scripts
            // still land on the commit menu (the client has no separate user menu bar).
            definitions.Add(new ScriptDefinition(
                Slug: UniqueSlug(name),
                Caption: name,
                Interpreter: interpreter,
                Command: commandTemplate,
                Destructive: (bool?)element.Element("AskConfirmation") ?? false,
                RunInBackground: (bool?)element.Element("RunInBackground") ?? false,
                Hotkey: null,
                Enabled: (bool?)element.Element("Enabled") ?? true,
                Surfaces: ScriptSurfaces.CommitMenu));
        }

        return definitions;

        string UniqueSlug(string name)
        {
            string baseSlug = ScriptStorage.SlugFromCaption(name);
            if (string.IsNullOrEmpty(baseSlug))
            {
                baseSlug = "script";
            }

            string slug = baseSlug;
            int suffix = 2;
            while (!usedSlugs.Add(slug))
            {
                slug = $"{baseSlug}-{suffix++}";
            }

            return slug;
        }
    }

    /// <summary>Merges imports into existing definitions, keeping existing on a slug collision.</summary>
    public static IReadOnlyList<ScriptDefinition> Merge(IReadOnlyList<ScriptDefinition> existing, IReadOnlyList<ScriptDefinition> imported)
    {
        HashSet<string> existingSlugs = new(existing.Select(definition => definition.Slug), StringComparer.Ordinal);
        return [.. existing, .. imported.Where(definition => existingSlugs.Add(definition.Slug))];
    }
}
