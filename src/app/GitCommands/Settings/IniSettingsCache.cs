using System.Buffers;
using System.Text;

namespace GitCommands.Settings;

/// <summary>
///  The client-native settings store: the same flat key/value dictionary the XML store
///  holds, serialized as a git-config-style INI file. A key's text before its first dot
///  becomes the section header; keys without a dot (or with an unrepresentable prefix)
///  live at the top of the file. Keys and values round-trip byte-exact — anything that
///  plain INI cannot carry (newlines, quotes, surrounding whitespace, '=') is written as
///  a quoted string with git-config-style backslash escapes.
/// </summary>
public sealed class IniSettingsCache : GitExtSettingsCache
{
    private static readonly SearchValues<char> _tokenSpecials = SearchValues.Create("\"\\\n\r\t=");
    private static readonly SearchValues<char> _sectionSpecials = SearchValues.Create("[]#;\"\n\r");

    public IniSettingsCache(string settingsFilePath, bool autoSave = true)
        : base(settingsFilePath, autoSave)
    {
    }

    protected override void WriteSettings(string fileName)
    {
        StringBuilder builder = new();
        builder.Append("# Git Extensions settings\n");

        List<(string Section, string Key, string Value)> rows = [];
        foreach ((string fullKey, string value) in NameMap)
        {
            (string section, string key) = SplitSectionKey(fullKey);
            rows.Add((section, key, value));
        }

        string currentSection = "";
        foreach ((string section, string key, string value) in rows
            .OrderBy(row => row.Section, StringComparer.Ordinal)
            .ThenBy(row => row.Key, StringComparer.Ordinal))
        {
            if (section != currentSection)
            {
                builder.Append('\n').Append('[').Append(section).Append("]\n");
                currentSection = section;
            }

            builder.Append(EncodeToken(key)).Append(" = ").Append(EncodeToken(value)).Append('\n');
        }

        File.WriteAllText(fileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    protected override void ReadSettings(string fileName)
    {
        string section = "";
        int lineNumber = 0;
        foreach (string line in File.ReadLines(fileName))
        {
            lineNumber++;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] is '#' or ';')
            {
                continue;
            }

            if (trimmed[0] == '[')
            {
                if (trimmed[^1] != ']')
                {
                    throw new FormatException($"Malformed section header at line {lineNumber} of \"{fileName}\".");
                }

                section = trimmed[1..^1].Trim();
                continue;
            }

            (string key, string rest) = DecodeToken(trimmed, stopAtEquals: true);
            rest = rest.TrimStart();
            if (rest.Length == 0 || rest[0] != '=')
            {
                // tolerate unparseable lines the way the XML reader tolerates a broken file: skip
                continue;
            }

            (string value, _) = DecodeToken(rest[1..].TrimStart(), stopAtEquals: false);

            string fullKey = section.Length == 0 ? key : $"{section}.{key}";
            NameMap[fullKey] = value;
        }
    }

    private static (string Section, string Key) SplitSectionKey(string fullKey)
    {
        int dotIndex = fullKey.IndexOf('.');
        if (dotIndex <= 0 || dotIndex == fullKey.Length - 1)
        {
            return ("", fullKey);
        }

        string section = fullKey[..dotIndex];
        if (section.Trim() != section || section.AsSpan().ContainsAny(_sectionSpecials))
        {
            // an unrepresentable section header; keep the full key at the top of the file
            return ("", fullKey);
        }

        return (section, fullKey[(dotIndex + 1)..]);
    }

    /// <summary>Quotes and escapes a key or value when plain INI could not round-trip it.</summary>
    internal static string EncodeToken(string token)
    {
        bool needsQuoting = token.Length != 0
            && (token.Trim() != token
                || token.AsSpan().ContainsAny(_tokenSpecials)
                || token[0] is '#' or ';' or '[');

        if (!needsQuoting)
        {
            return token;
        }

        StringBuilder builder = new(token.Length + 8);
        builder.Append('"');
        foreach (char c in token)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default: builder.Append(c); break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>
    ///  Reads one token from the start of <paramref name="text"/>: a quoted string with
    ///  escapes, or a plain (trimmed) run - up to '=' for keys, to the end for values.
    ///  Returns the token and the rest.
    /// </summary>
    internal static (string Token, string Remainder) DecodeToken(string text, bool stopAtEquals)
    {
        if (text.Length == 0 || text[0] != '"')
        {
            int stopIndex = stopAtEquals ? text.IndexOf('=') : -1;
            return stopIndex < 0
                ? (text.Trim(), "")
                : (text[..stopIndex].Trim(), text[stopIndex..]);
        }

        StringBuilder builder = new(text.Length);
        int index = 1;
        while (index < text.Length)
        {
            char c = text[index];
            if (c == '"')
            {
                return (builder.ToString(), text[(index + 1)..]);
            }

            if (c == '\\' && index + 1 < text.Length)
            {
                index++;
                builder.Append(text[index] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    char escaped => escaped,
                });
            }
            else
            {
                builder.Append(c);
            }

            index++;
        }

        throw new FormatException("Unterminated quoted token.");
    }
}
