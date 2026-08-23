using System.Diagnostics;
using System.Text;
using System.Xml;

namespace GitCommands.Settings;

[DebuggerDisplay("{_byNameMap.Count} cached {" + nameof(SettingsFilePath) + ",nq}")]
public class GitExtSettingsCache : FileSettingsCache
{
    private readonly XmlSerializableDictionary<string, string> _encodedNameMap = [];

    public GitExtSettingsCache(string settingsFilePath, bool autoSave = true)
        : base(settingsFilePath, autoSave)
    {
    }

    /// <summary>The raw stored pairs; the host-selected serialization subclass reads and writes these.</summary>
    private protected IDictionary<string, string> NameMap => _encodedNameMap;

    public static GitExtSettingsCache FromCache(string settingsFilePath)
    {
        Lazy<GitExtSettingsCache> createSettingsCache = new(
            () => HostSettingsStore.CreateCache(settingsFilePath, autoSave: true));

        return FromCache(settingsFilePath, createSettingsCache);
    }

    public static GitExtSettingsCache Create(string settingsFilePath, bool useSharedCache = true)
    {
        if (useSharedCache)
        {
            return FromCache(settingsFilePath);
        }
        else
        {
            return HostSettingsStore.CreateCache(settingsFilePath, autoSave: false);
        }
    }

    /// <summary>All stored pairs, e.g. for a one-shot import into another store.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> GetAllValues()
        => LockedAction<IReadOnlyList<KeyValuePair<string, string>>>(() =>
        {
            EnsureSettingsAreUpToDate();
            return [.. _encodedNameMap];
        });

    protected override void ClearImpl()
    {
        _encodedNameMap.Clear();
    }

    protected override void WriteSettings(string fileName)
    {
        using XmlTextWriter xtw = new(fileName, Encoding.UTF8) { Formatting = Formatting.Indented };
        xtw.WriteStartDocument();
        xtw.WriteStartElement("dictionary");
        _encodedNameMap.WriteXml(xtw);
        xtw.WriteEndElement();
    }

    protected override void ReadSettings(string fileName)
    {
        XmlReaderSettings readerSettings = new()
        {
            IgnoreWhitespace = true,
            CheckCharacters = false
        };

        // StreamReader required for \\wsl$\ that is an illegal URI
        using StreamReader sr = new(fileName);
        using XmlReader xr = XmlReader.Create(sr, readerSettings);
        try
        {
            _encodedNameMap.ReadXml(xr);
        }
        catch (Exception e)
        {
            throw new Exception($"Exception reading XML file \"{fileName}\": {e.Message}", e);
        }
    }

    protected override void SetValueImpl(string key, string? value)
    {
        if (value is null)
        {
            _encodedNameMap.Remove(key);
        }
        else
        {
            _encodedNameMap[key] = value;
        }
    }

    protected override string? GetValueImpl(string key)
    {
        _encodedNameMap.TryGetValue(key, out string? value);
        return value;
    }
}
