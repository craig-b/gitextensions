using GitCommands.Scripts;

namespace GitCommandsTests.Scripts;

public sealed class ScriptsModelTests
{
    [Test]
    public void Storage_round_trips_through_flat_keys()
    {
        Dictionary<string, string> store = [];
        ScriptDefinition script = new(
            "clean-up", "Clean up", "bash", "echo {current.branch}",
            Destructive: true, RunInBackground: true, Hotkey: "Ctrl+K", Enabled: false,
            Surfaces: ScriptSurfaces.CommitMenu | ScriptSurfaces.RefMenu);

        ScriptStorage.Save([script], (key, value) => store[key] = value);
        IReadOnlyList<ScriptDefinition> loaded = ScriptStorage.Load(key => store.GetValueOrDefault(key));

        loaded.Should().Equal(script);
        store["Scripts.List"].Should().Be("clean-up");
        store["Scripts.clean-up.Surfaces"].Should().Be("commit,ref");
    }

    [Test]
    public void Slugs_and_surfaces_parse()
    {
        ScriptStorage.SlugFromCaption("Clean Up. Branches").Should().Be("clean-up-branches");
        ScriptStorage.ParseSurfaces("ref").Should().Be(ScriptSurfaces.RefMenu);
        ScriptStorage.ParseSurfaces("commit,ref").Should().Be(ScriptSurfaces.CommitMenu | ScriptSurfaces.RefMenu);
        ScriptStorage.ParseSurfaces(null).Should().Be(ScriptSurfaces.CommitMenu);
    }

    [Test]
    public void Tokens_expand_with_dotted_names_and_legacy_aliases()
    {
        ScriptTokenContext context = new(
            SelectedHash: "abc123",
            CurrentBranch: "main",
            RepoName: "repo",
            RefName: "feature/x");

        ScriptTokenSubstitution.Expand(
                "new={selected.hash} legacy={sHash} branch={cBranch} ref={ref.name} missing={selected.tag}",
                context,
                _ => null)
            .Should().Be("new=abc123 legacy=abc123 branch=main ref=feature/x missing=");
    }

    [Test]
    public void Prompts_extract_and_substitute()
    {
        ScriptTokenSubstitution.ExtractPrompts("a {prompt:Version?} b {UserInput} c {prompt:Version?}")
            .Should().Equal("Version?", "Enter a value", "Version?");

        ScriptTokenSubstitution.Expand("tag {prompt:Version?}", new ScriptTokenContext(), question => question == "Version?" ? "1.0" : null)
            .Should().Be("tag 1.0");
    }

    [Test]
    public void Selected_revision_requirement_detection()
    {
        ScriptTokenSubstitution.RequiresSelectedRevision("echo {selected.hash}").Should().BeTrue();
        ScriptTokenSubstitution.RequiresSelectedRevision("echo {sSubject}").Should().BeTrue();
        ScriptTokenSubstitution.RequiresSelectedRevision("echo {current.branch}").Should().BeFalse();
    }

    [Test]
    public void Descriptors_project_only_enabled_scripts_for_the_surface()
    {
        ScriptDefinition commitOnly = new("a", "A", "shell", "x");
        ScriptDefinition both = new("b", "B", "shell", "x", Destructive: true, Hotkey: "F9", Surfaces: ScriptSurfaces.CommitMenu | ScriptSurfaces.RefMenu);
        ScriptDefinition disabled = new("c", "C", "shell", "x", Enabled: false);

        var commitActions = ScriptActions.ToDescriptors([commitOnly, both, disabled], ScriptSurfaces.CommitMenu);
        commitActions.Select(action => action.Id).Should().Equal("script.a", "script.b");
        commitActions[1].Destructive.Should().BeTrue();
        commitActions[1].Hotkey.Should().Be("F9");
        commitActions[0].Group.Should().Be("scripts");

        ScriptActions.ToDescriptors([commitOnly, both, disabled], ScriptSurfaces.RefMenu)
            .Select(action => action.Id).Should().Equal("script.b");
    }

    [Test]
    public void Safe_expansion_keeps_repo_content_out_of_the_shell_command()
    {
        string malicious = "\"; rm -rf ~; $(touch pwned) `id` #";
        ScriptTokenContext context = new(SelectedSubject: malicious, CurrentBranch: "main");

        ExpandedScript posix = ScriptTokenSubstitution.ExpandSafe(
            "notify {selected.subject} on {current.branch}", context, new Dictionary<string, string>(), ScriptInterpreterKind.PosixShell);

        posix.Command.Should().Be("notify \"$GE_SCRIPT_SELECTED_SUBJECT\" on \"$GE_SCRIPT_CURRENT_BRANCH\"");
        posix.Command.Should().NotContain("rm -rf");
        posix.Environment["GE_SCRIPT_SELECTED_SUBJECT"].Should().Be(malicious);
        posix.Environment["GE_SCRIPT_CURRENT_BRANCH"].Should().Be("main");
    }

    [Test]
    public void Safe_expansion_uses_each_interpreters_reference_syntax()
    {
        ScriptTokenContext context = new(CurrentBranch: "main");

        ScriptTokenSubstitution.ExpandSafe("x {current.branch}", context, new Dictionary<string, string>(), ScriptInterpreterKind.Cmd)
            .Command.Should().Be("x %GE_SCRIPT_CURRENT_BRANCH%");
        ScriptTokenSubstitution.ExpandSafe("x {current.branch}", context, new Dictionary<string, string>(), ScriptInterpreterKind.PowerShell)
            .Command.Should().Be("x $env:GE_SCRIPT_CURRENT_BRANCH");
        ScriptTokenSubstitution.ExpandSafe("x {cBranch}", context, new Dictionary<string, string>(), ScriptInterpreterKind.PosixShell)
            .Command.Should().Be("x \"$GE_SCRIPT_CURRENT_BRANCH\"");
    }

    [Test]
    public void Safe_expansion_quotes_argv_for_direct_interpreters()
    {
        ScriptTokenContext context = new(SelectedSubject: "say \"hi\" \\ there");

        ExpandedScript direct = ScriptTokenSubstitution.ExpandSafe(
            "{selected.subject}", context, new Dictionary<string, string>(), ScriptInterpreterKind.Direct);

        direct.Command.Should().Be("\"say \\\"hi\\\" \\\\ there\"");
        direct.Environment.Should().BeEmpty();
    }

    [Test]
    public void Safe_expansion_routes_prompts_through_the_environment()
    {
        ExpandedScript expanded = ScriptTokenSubstitution.ExpandSafe(
            "tag {prompt:Version?}", new ScriptTokenContext(),
            new Dictionary<string, string> { ["Version?"] = "1.0; rm x" }, ScriptInterpreterKind.PosixShell);

        expanded.Command.Should().Be("tag \"$GE_SCRIPT_PROMPT_0\"");
        expanded.Environment["GE_SCRIPT_PROMPT_0"].Should().Be("1.0; rm x");
    }

    [Test]
    public void Interpreter_classification()
    {
        ScriptInterpreter.Classify("bash").Should().Be(ScriptInterpreterKind.PosixShell);
        ScriptInterpreter.Classify("pwsh").Should().Be(ScriptInterpreterKind.PowerShell);
        ScriptInterpreter.Classify("cmd").Should().Be(ScriptInterpreterKind.Cmd);
        ScriptInterpreter.Classify("/usr/bin/notify-send").Should().Be(ScriptInterpreterKind.Direct);
    }

    [Test]
    public void Interpreter_resolution()
    {
        ScriptInterpreter.Resolve("shell", "echo hi", isWindows: false).Should().Be(("/bin/sh", "-c \"echo hi\""));
        ScriptInterpreter.Resolve("", "echo hi", isWindows: true).Should().Be(("cmd", "/c \"echo hi\""));
        ScriptInterpreter.Resolve("bash", "echo \"x\"", isWindows: false).Should().Be(("bash", "-c \"echo \\\"x\\\"\""));
        ScriptInterpreter.Resolve("pwsh", "Get-Item", isWindows: false).Should().Be(("pwsh", "-Command \"Get-Item\""));
        ScriptInterpreter.Resolve("/usr/bin/notify-send", "hello", isWindows: false).Should().Be(("/usr/bin/notify-send", "hello"));
    }

    [Test]
    public void Alias_parser_reads_config_output()
    {
        string output = "alias.st status -s\nalias.cleanup !git branch --merged | grep -v main\nnoise\n";

        GitAliasParser.Parse(output).Should().Equal(
            ("st", "status -s"),
            ("cleanup", "!git branch --merged | grep -v main"));
    }
}
