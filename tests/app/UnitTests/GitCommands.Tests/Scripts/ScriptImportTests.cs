using GitCommands.Scripts;

namespace GitCommandsTests.Scripts;

public sealed class ScriptImportTests
{
    private const string Xml = """
        <ArrayOfScriptInfo>
          <ScriptInfo>
            <Enabled>true</Enabled>
            <Name>Fetch after commit</Name>
            <Command>git</Command>
            <Arguments>fetch {sBranch}</Arguments>
            <AskConfirmation>true</AskConfirmation>
            <RunInBackground>false</RunInBackground>
            <IsPowerShell>false</IsPowerShell>
          </ScriptInfo>
          <ScriptInfo>
            <Enabled>false</Enabled>
            <Name>Notify</Name>
            <Command>Write-Host</Command>
            <Arguments>done {cBranch}</Arguments>
            <IsPowerShell>true</IsPowerShell>
          </ScriptInfo>
          <ScriptInfo>
            <Name>Fetch after commit</Name>
            <Command>git</Command>
            <Arguments>pull</Arguments>
          </ScriptInfo>
        </ArrayOfScriptInfo>
        """;

    [Test]
    public void Direct_and_powershell_scripts_map_to_interpreters()
    {
        IReadOnlyList<ScriptDefinition> imported = ScriptImport.FromWinFormsXml(Xml);

        imported.Should().HaveCount(3);

        imported[0].Should().Be(new ScriptDefinition(
            "fetch-after-commit", "Fetch after commit", "git", "fetch {sBranch}",
            Destructive: true, RunInBackground: false, Hotkey: null, Enabled: true, Surfaces: ScriptSurfaces.CommitMenu));

        imported[1].Interpreter.Should().Be("pwsh");
        imported[1].Command.Should().Be("Write-Host done {cBranch}");
        imported[1].Enabled.Should().BeFalse();
    }

    [Test]
    public void Colliding_names_get_unique_slugs()
    {
        IReadOnlyList<ScriptDefinition> imported = ScriptImport.FromWinFormsXml(Xml);

        imported[0].Slug.Should().Be("fetch-after-commit");
        imported[2].Slug.Should().Be("fetch-after-commit-2");
    }

    [Test]
    public void Empty_and_invalid_input_yields_nothing()
    {
        ScriptImport.FromWinFormsXml(null).Should().BeEmpty();
        ScriptImport.FromWinFormsXml("").Should().BeEmpty();
        ScriptImport.FromWinFormsXml("not xml <").Should().BeEmpty();
        ScriptImport.FromWinFormsXml("<ArrayOfScriptInfo><ScriptInfo><Command>git</Command></ScriptInfo></ArrayOfScriptInfo>").Should().BeEmpty();
    }

    [Test]
    public void Merge_keeps_existing_on_slug_collision()
    {
        ScriptDefinition existing = new("fetch-after-commit", "Mine", "shell", "echo mine");
        IReadOnlyList<ScriptDefinition> imported = ScriptImport.FromWinFormsXml(Xml);

        IReadOnlyList<ScriptDefinition> merged = ScriptImport.Merge([existing], imported);

        merged.Should().Contain(existing);
        merged.Count(definition => definition.Slug == "fetch-after-commit").Should().Be(1);
        merged.Should().Contain(definition => definition.Slug == "notify");
    }
}
