using GitCommands.Actions;

namespace GitCommands.Scripts;

/// <summary>Which projected surfaces a script appears on.</summary>
[Flags]
public enum ScriptSurfaces
{
    None = 0,
    CommitMenu = 1,
    RefMenu = 2,
}

/// <summary>
///  A user-defined script: a registry action with a command
///  template. Placement, hotkeys, ordering, and confirmation all ride the action-registry machinery -
///  the old ScriptInfo's menu/hotkey/confirmation flags are deliberately gone.
/// </summary>
public sealed record ScriptDefinition(
    string Slug,
    string Caption,
    string Interpreter,
    string Command,
    bool Destructive = false,
    bool RunInBackground = false,
    string? Hotkey = null,
    bool Enabled = true,
    ScriptSurfaces Surfaces = ScriptSurfaces.CommitMenu)
{
    public string ActionId => $"script.{Slug}";
}

/// <summary>INI-native storage: Scripts.List holds the slugs, Scripts.&lt;slug&gt;.* the fields.</summary>
public static class ScriptStorage
{
    public static IReadOnlyList<ScriptDefinition> Load(Func<string, string?> getString)
    {
        string[] slugs = (getString("Scripts.List") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        List<ScriptDefinition> definitions = [];
        foreach (string slug in slugs)
        {
            definitions.Add(new ScriptDefinition(
                slug,
                Caption: getString($"Scripts.{slug}.Caption") ?? slug,
                Interpreter: getString($"Scripts.{slug}.Interpreter") ?? "shell",
                Command: getString($"Scripts.{slug}.Command") ?? "",
                Destructive: getString($"Scripts.{slug}.Destructive") == "true",
                RunInBackground: getString($"Scripts.{slug}.RunInBackground") == "true",
                Hotkey: NullIfEmpty(getString($"Scripts.{slug}.Hotkey")),
                Enabled: getString($"Scripts.{slug}.Enabled") != "false",
                Surfaces: ParseSurfaces(getString($"Scripts.{slug}.Surfaces"))));
        }

        return definitions;

        static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static void Save(IReadOnlyList<ScriptDefinition> definitions, Action<string, string> setString)
    {
        setString("Scripts.List", string.Join(",", definitions.Select(definition => definition.Slug)));
        foreach (ScriptDefinition definition in definitions)
        {
            setString($"Scripts.{definition.Slug}.Caption", definition.Caption);
            setString($"Scripts.{definition.Slug}.Interpreter", definition.Interpreter);
            setString($"Scripts.{definition.Slug}.Command", definition.Command);
            setString($"Scripts.{definition.Slug}.Destructive", definition.Destructive ? "true" : "false");
            setString($"Scripts.{definition.Slug}.RunInBackground", definition.RunInBackground ? "true" : "false");
            setString($"Scripts.{definition.Slug}.Hotkey", definition.Hotkey ?? "");
            setString($"Scripts.{definition.Slug}.Enabled", definition.Enabled ? "true" : "false");
            setString($"Scripts.{definition.Slug}.Surfaces", FormatSurfaces(definition.Surfaces));
        }
    }

    public static ScriptSurfaces ParseSurfaces(string? value)
        => value switch
        {
            "ref" => ScriptSurfaces.RefMenu,
            "commit,ref" or "ref,commit" or "both" => ScriptSurfaces.CommitMenu | ScriptSurfaces.RefMenu,
            _ => ScriptSurfaces.CommitMenu,
        };

    public static string FormatSurfaces(ScriptSurfaces surfaces)
        => surfaces switch
        {
            ScriptSurfaces.RefMenu => "ref",
            ScriptSurfaces.CommitMenu | ScriptSurfaces.RefMenu => "commit,ref",
            _ => "commit",
        };

    /// <summary>A caption becomes a slug: lowercased words joined by dashes.</summary>
    public static string SlugFromCaption(string caption)
        => string.Join("-", caption.ToLowerInvariant().Split([' ', '.', ','], StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The substitution inputs, all data (the host builds it from its selection state).</summary>
public sealed record ScriptTokenContext(
    string? SelectedHash = null,
    string? SelectedHashes = null,
    string? SelectedSubject = null,
    string? SelectedMessage = null,
    string? SelectedAuthor = null,
    string? SelectedBranch = null,
    string? SelectedTag = null,
    string? SelectedRemoteBranch = null,
    string? SelectedRemote = null,
    string? SelectedRemoteUrl = null,
    string? CurrentBranch = null,
    string? CurrentHash = null,
    string? CurrentRemote = null,
    string? CurrentRemoteUrl = null,
    string? RepoName = null,
    string? RepoDir = null,
    string? RefName = null);

/// <summary>
///  Expands {selected.*}/{current.*}/{repo.*}/{ref.name}/{prompt:...} tokens; the legacy
///  WinForms names ({sHash}, {cBranch}, ...) are aliases so old scripts paste in unchanged.
/// </summary>
public static class ScriptTokenSubstitution
{
    /// <summary>legacy name (without braces) → canonical dotted name.</summary>
    public static IReadOnlyDictionary<string, string> LegacyAliases { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sHash"] = "selected.hash",
        ["sHashes"] = "selected.hashes",
        ["sSubject"] = "selected.subject",
        ["sMessage"] = "selected.message",
        ["sAuthor"] = "selected.author",
        ["sBranch"] = "selected.branch",
        ["sLocalBranch"] = "selected.branch",
        ["sTag"] = "selected.tag",
        ["sRemoteBranch"] = "selected.remoteBranch",
        ["sRemote"] = "selected.remote",
        ["sRemoteUrl"] = "selected.remoteUrl",
        ["cBranch"] = "current.branch",
        ["cLocalBranch"] = "current.branch",
        ["cHash"] = "current.hash",
        ["cDefaultRemote"] = "current.remote",
        ["cDefaultRemoteUrl"] = "current.remoteUrl",
        ["RepoName"] = "repo.name",
        ["WorkingDir"] = "repo.dir",
        ["UserInput"] = "prompt:Enter a value",
    };

    public static string Expand(string command, ScriptTokenContext context, Func<string, string?> prompt)
    {
        // Rewrite legacy tokens to their canonical spelling first, then substitute once.
        foreach ((string legacy, string canonical) in LegacyAliases)
        {
            command = command.Replace($"{{{legacy}}}", $"{{{canonical}}}");
        }

        command = Replace(command, "selected.hash", context.SelectedHash);
        command = Replace(command, "selected.hashes", context.SelectedHashes);
        command = Replace(command, "selected.subject", context.SelectedSubject);
        command = Replace(command, "selected.message", context.SelectedMessage);
        command = Replace(command, "selected.author", context.SelectedAuthor);
        command = Replace(command, "selected.branch", context.SelectedBranch);
        command = Replace(command, "selected.tag", context.SelectedTag);
        command = Replace(command, "selected.remoteBranch", context.SelectedRemoteBranch);
        command = Replace(command, "selected.remote", context.SelectedRemote);
        command = Replace(command, "selected.remoteUrl", context.SelectedRemoteUrl);
        command = Replace(command, "current.branch", context.CurrentBranch);
        command = Replace(command, "current.hash", context.CurrentHash);
        command = Replace(command, "current.remote", context.CurrentRemote);
        command = Replace(command, "current.remoteUrl", context.CurrentRemoteUrl);
        command = Replace(command, "repo.name", context.RepoName);
        command = Replace(command, "repo.dir", context.RepoDir);
        command = Replace(command, "ref.name", context.RefName);

        // {prompt:Question} - each occurrence asks once; a cancelled prompt empties the token.
        int promptStart;
        while ((promptStart = command.IndexOf("{prompt:", StringComparison.Ordinal)) >= 0)
        {
            int promptEnd = command.IndexOf('}', promptStart);
            if (promptEnd < 0)
            {
                break;
            }

            string question = command[(promptStart + "{prompt:".Length)..promptEnd];
            string answer = prompt(question) ?? "";
            command = command[..promptStart] + answer + command[(promptEnd + 1)..];
        }

        return command;

        static string Replace(string command, string token, string? value)
            => command.Replace($"{{{token}}}", value ?? "");
    }

    /// <summary>The prompt questions a command will ask, in order (hosts pre-collect answers asynchronously).</summary>
    public static IReadOnlyList<string> ExtractPrompts(string command)
    {
        foreach ((string legacy, string canonical) in LegacyAliases)
        {
            command = command.Replace($"{{{legacy}}}", $"{{{canonical}}}");
        }

        List<string> questions = [];
        int searchStart = 0;
        int promptStart;
        while ((promptStart = command.IndexOf("{prompt:", searchStart, StringComparison.Ordinal)) >= 0)
        {
            int promptEnd = command.IndexOf('}', promptStart);
            if (promptEnd < 0)
            {
                break;
            }

            questions.Add(command[(promptStart + "{prompt:".Length)..promptEnd]);
            searchStart = promptEnd + 1;
        }

        return questions;
    }

    /// <summary>Whether the command references any token the given context leaves empty (for enablement).</summary>
    public static bool RequiresSelectedRevision(string command)
    {
        string canonical = command;
        foreach ((string legacy, string alias) in LegacyAliases)
        {
            canonical = canonical.Replace($"{{{legacy}}}", $"{{{alias}}}");
        }

        return canonical.Contains("{selected.", StringComparison.Ordinal);
    }
}

/// <summary>The registry projection: enabled scripts become actions in the "scripts" group.</summary>
public static class ScriptActions
{
    public static IReadOnlyList<ActionDescriptor> ToDescriptors(IReadOnlyList<ScriptDefinition> definitions, ScriptSurfaces surface)
        => [.. definitions
            .Where(definition => definition.Enabled && definition.Surfaces.HasFlag(surface))
            .Select(definition => new ActionDescriptor(
                definition.ActionId,
                definition.Caption,
                "scripts",
                ActionTier.Common,
                Destructive: definition.Destructive,
                Hotkey: definition.Hotkey))];
}

/// <summary>How an interpreter choice maps to a process invocation.</summary>
public static class ScriptInterpreter
{
    /// <summary>
    ///  "shell" (or blank) is sh -c / cmd /c per platform; bash/sh/zsh use -c, pwsh and
    ///  powershell use -Command, cmd uses /c; anything else runs the interpreter with the
    ///  expanded command as its argument line.
    /// </summary>
    public static (string FileName, string Arguments) Resolve(string interpreter, string expandedCommand, bool isWindows)
    {
        string quoted = "\"" + expandedCommand.Replace("\"", "\\\"") + "\"";
        return (string.IsNullOrWhiteSpace(interpreter) ? "shell" : interpreter.Trim().ToLowerInvariant()) switch
        {
            "shell" => isWindows ? ("cmd", $"/c {quoted}") : ("/bin/sh", $"-c {quoted}"),
            "cmd" => ("cmd", $"/c {quoted}"),
            "bash" => ("bash", $"-c {quoted}"),
            "sh" => ("sh", $"-c {quoted}"),
            "zsh" => ("zsh", $"-c {quoted}"),
            "pwsh" => ("pwsh", $"-Command {quoted}"),
            "powershell" => ("powershell", $"-Command {quoted}"),
            _ => (interpreter, expandedCommand),
        };
    }
}

/// <summary>The zero-config palette entries: the user's git aliases.</summary>
public static class GitAliasParser
{
    /// <summary>Parses "git config --get-regexp ^alias\." output into (name, expansion) pairs.</summary>
    public static IReadOnlyList<(string Name, string Expansion)> Parse(string configOutput)
    {
        List<(string, string)> aliases = [];
        foreach (string line in configOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith("alias.", StringComparison.Ordinal))
            {
                continue;
            }

            int space = line.IndexOf(' ');
            if (space <= "alias.".Length)
            {
                continue;
            }

            aliases.Add((line["alias.".Length..space], line[(space + 1)..]));
        }

        return aliases;
    }
}
