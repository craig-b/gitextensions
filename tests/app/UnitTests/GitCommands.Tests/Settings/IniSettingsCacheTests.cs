using GitCommands.Settings;

namespace GitCommandsTests.Settings;

[NonParallelizable]
internal sealed class IniSettingsCacheTests
{
    private string _filePath = "";

    [SetUp]
    public void SetUp()
    {
        _filePath = Path.Join(Path.GetTempPath(), $"ge-ini-test-{Guid.NewGuid():N}.ini");
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(_filePath);
        File.Delete(_filePath + ".backup");
    }

    [Test]
    public void Round_trips_keys_and_values_byte_exact()
    {
        (string Key, string Value)[] pairs =
        [
            ("DontConfirmAmend", "true"),
            ("Detailed.MergeLogMessagesCount", "42"),
            ("Appearance.RevisionGraph.ShowRemoteBranches", "false"),
            ("key with spaces", "plain value"),
            ("strange=key", "value"),
            ("multiline", "first\nsecond\r\nthird"),
            ("quoted", "he said \"hi\" and left"),
            ("backslashes", @"C:\temp\repo"),
            ("padded", "  keeps surrounding spaces  "),
            ("empty", ""),
            ("unicode", "héllo wörld ↓ 漢字"),
            ("hashy", "#not a comment"),
            ("equals", "a=b=c"),
            ("[weird.section", "unrepresentable prefix stays global"),
        ];

        using (IniSettingsCache writeCache = new(_filePath, autoSave: false))
        {
            foreach ((string key, string value) in pairs)
            {
                writeCache.SetValue(key, value);
            }

            writeCache.Save();
        }

        using IniSettingsCache readCache = new(_filePath, autoSave: false);
        foreach ((string key, string value) in pairs)
        {
            readCache.TryGetValue(key, out string? roundTripped).Should().BeTrue(because: $"'{key}' must exist");
            roundTripped.Should().Be(value, because: $"'{key}' must round-trip");
        }

        readCache.GetAllValues().Should().HaveCount(pairs.Length);
    }

    [Test]
    public void Writes_git_config_style_sections()
    {
        using (IniSettingsCache cache = new(_filePath, autoSave: false))
        {
            cache.SetValue("Confirmations.ConfirmBranchCheckout", "true");
            cache.SetValue("Confirmations.DontConfirmAmend", "false");
            cache.SetValue("Detailed.MergeLogMessagesCount", "20");
            cache.SetValue("TopLevelKey", "top");
            cache.Save();
        }

        File.ReadAllText(_filePath).Should().Be("""
            # Git Extensions settings
            TopLevelKey = top

            [Confirmations]
            ConfirmBranchCheckout = true
            DontConfirmAmend = false

            [Detailed]
            MergeLogMessagesCount = 20

            """.ReplaceLineEndings("\n"));
    }

    [Test]
    public void Xml_store_exposes_all_values_for_import()
    {
        string xmlPath = _filePath + ".settings";
        try
        {
            using (GitExtSettingsCache xmlCache = new(xmlPath, autoSave: false))
            {
                xmlCache.SetValue("DontConfirmAmend", "true");
                xmlCache.SetValue("Detailed.Greeting", "hello\nworld");
                xmlCache.Save();
            }

            using GitExtSettingsCache readCache = new(xmlPath, autoSave: false);
            readCache.GetAllValues().Should().BeEquivalentTo(
            [
                new KeyValuePair<string, string>("DontConfirmAmend", "true"),
                new KeyValuePair<string, string>("Detailed.Greeting", "hello\nworld"),
            ]);
        }
        finally
        {
            File.Delete(xmlPath);
            File.Delete(xmlPath + ".backup");
        }
    }

    [TestCase("plain", "plain")]
    [TestCase("has space", "has space")]
    [TestCase("a=b", "\"a=b\"")]
    [TestCase(" padded ", "\" padded \"")]
    [TestCase("line\nbreak", "\"line\\nbreak\"")]
    [TestCase("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [TestCase("#comment-like", "\"#comment-like\"")]
    [TestCase("", "")]
    public void Tokens_encode_as_expected(string token, string encoded)
    {
        IniSettingsCache.EncodeToken(token).Should().Be(encoded);

        (string decoded, _) = IniSettingsCache.DecodeToken(IniSettingsCache.EncodeToken(token), stopAtEquals: false);
        decoded.Should().Be(token);
    }
}
