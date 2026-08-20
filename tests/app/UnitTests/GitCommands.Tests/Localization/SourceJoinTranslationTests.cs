using GitCommands.Localization;

namespace GitCommandsTests.Localization;

public sealed class SourceJoinTranslationTests
{
    private static string WriteXlf(string directory, string name, string body)
    {
        string path = Path.Combine(directory, $"{name}.xlf");
        File.WriteAllText(path, $"""
            <?xml version="1.0" ?><xliff version="1.0">
              <file original="A"><body>{body}</body></file>
            </xliff>
            """);
        return path;
    }

    private static string Unit(string source, string target)
        => $"<trans-unit id=\"x\"><source>{source}</source><target>{target}</target></trans-unit>";

    [Test]
    public void Catalog_joins_sources_and_most_frequent_target_wins()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string path = WriteXlf(directory, "Testish",
                Unit("Fetch", "Holen") + Unit("Fetch", "Abrufen") + Unit("Fetch", "Holen") + Unit("Push", "Schieben") + Unit("Empty", ""));

            IReadOnlyDictionary<string, string> catalog = SourceJoinTranslation.LoadCatalog(path);

            catalog["Fetch"].Should().Be("Holen");
            catalog["Push"].Should().Be("Schieben");
            catalog.Should().NotContainKey("Empty");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Translator_falls_back_to_English_and_identity_for_no_language()
    {
        SourceJoinTranslator translator = new(new Dictionary<string, string> { ["Fetch"] = "Holen" });

        translator.T("Fetch").Should().Be("Holen");
        translator.T("Unknown caption").Should().Be("Unknown caption");
        SourceJoinTranslator.Identity.T("Fetch").Should().Be("Fetch");
        SourceJoinTranslator.Load("/nonexistent", null).Should().BeSameAs(SourceJoinTranslator.Identity);
        SourceJoinTranslator.Load("/nonexistent", "English").Should().BeSameAs(SourceJoinTranslator.Identity);
        SourceJoinTranslator.Load("/nonexistent", "German").Count.Should().Be(0);
    }

    [Test]
    public void Languages_exclude_plugins_and_English()
    {
        string directory = Directory.CreateTempSubdirectory().FullName;
        try
        {
            WriteXlf(directory, "German", Unit("a", "b"));
            WriteXlf(directory, "German.Plugins", Unit("a", "b"));
            WriteXlf(directory, "English", Unit("a", "b"));
            WriteXlf(directory, "Czech", Unit("a", "b"));

            SourceJoinTranslation.FindLanguages(directory).Should().Equal("Czech", "German");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
