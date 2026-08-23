using System.Text;
using GitCommands.Config;
using GitExtensions.Extensibility.Settings;

namespace GitCommands.Settings.Pages;

/// <summary>
///  Presentation model for the main git-config settings page: identity, editor, commit
///  template, files encoding, and line endings over the host-selected config level.
///  The diff/merge-tool configuration flow and the credential-helper handling (which
///  needs multi-value introspection) stay view-side — their managers are portable.
///  Git-typed values are saved only while <c>canSaveGitSettings</c> holds, mirroring the
///  page's "silently do not save when git is misconfigured" rule; the encoding is
///  app-managed and always saves.
/// </summary>
public sealed class GitConfigPageModel : SettingsPageModel
{
    /// <summary>The stored core.autocrlf value per choice index; the last choice unsets it.</summary>
    private static readonly AutoCRLFType?[] _autoCrlfValues =
        [AutoCRLFType.@true, AutoCRLFType.input, AutoCRLFType.@false, null];

    private readonly List<Encoding> _encodings;

    public GitConfigPageModel(Func<SettingsSource> currentSettings, Func<bool> canSaveGitSettings)
        : base("Config")
    {
        _encodings = [.. AppSettings.AvailableEncodings.Values];

        Groups =
        [
            new SettingsGroup("Config",
                FilesEncoding = new ChoiceSettingsEntry("Files content encoding",
                    [.. _encodings.Select(encoding => encoding.EncodingName)],
                    () => _encodings.FindIndex(encoding => Equals(encoding, new GitEncodingSettingsGetter(currentSettings()).FilesEncoding)),
                    index => new GitEncodingSettingsSetter(currentSettings()).FilesEncoding = index >= 0 ? _encodings[index] : null),
                UserName = new StringSettingsEntry("User name",
                    () => currentSettings().GetValue(SettingKeyString.UserName) ?? string.Empty,
                    Gated<string>(value => currentSettings().SetValue(SettingKeyString.UserName, value))),
                UserEmail = new StringSettingsEntry("User email",
                    () => currentSettings().GetValue(SettingKeyString.UserEmail) ?? string.Empty,
                    Gated<string>(value => currentSettings().SetValue(SettingKeyString.UserEmail, value))),
                Editor = new StringSettingsEntry("Editor",
                    () => currentSettings().GetValue("core.editor") ?? string.Empty,
                    Gated<string>(value => currentSettings().SetValue("core.editor", value.ConvertPathToGitSetting()))),
                CommitTemplatePath = new StringSettingsEntry("Path to commit template",
                    () => currentSettings().GetValue("commit.template") ?? string.Empty,
                    Gated<string>(value => currentSettings().SetValue("commit.template", value)))),

            new SettingsGroup("Line endings",
                LineEndings = new ChoiceSettingsEntry("Line endings",
                    [
                        "Checkout Windows-style, commit Unix-style line endings (\"core.autocrlf\"  is set to \"true\")",
                        "Checkout as-is, commit Unix-style line endings (\"core.autocrlf\"  is set to \"input\")",
                        "Checkout as-is, commit as-is (\"core.autocrlf\"  is set to \"false\")",
                        "Not set",
                    ],
                    () => Array.IndexOf(_autoCrlfValues, ((ISettingsValueGetter)currentSettings()).GetValue<AutoCRLFType>("core.autocrlf")),
                    Gated<int>(index => currentSettings().SetValue("core.autocrlf", _autoCrlfValues[index]?.ToString())))),
        ];

        Action<T> Gated<T>(Action<T> save)
            => value =>
            {
                if (canSaveGitSettings())
                {
                    save(value);
                }
            };
    }

    public ChoiceSettingsEntry FilesEncoding { get; }
    public StringSettingsEntry UserName { get; }
    public StringSettingsEntry UserEmail { get; }
    public StringSettingsEntry Editor { get; }
    public StringSettingsEntry CommitTemplatePath { get; }
    public ChoiceSettingsEntry LineEndings { get; }

    public override IReadOnlyList<SettingsGroup> Groups { get; }
}
