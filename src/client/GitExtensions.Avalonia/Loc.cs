using System.IO;
using GitCommands;
using GitCommands.Localization;

namespace GitExtensions.Avalonia;

/// <summary>
///  The client's §11 source-join localization: captions are translated by joining their
///  English text against the shipped xlf catalog for the configured language.
/// </summary>
internal static class Loc
{
    private static SourceJoinTranslator _translator = SourceJoinTranslator.Identity;

    public static string T(string english) => _translator.T(english);

    public static int CatalogSize => _translator.Count;

    /// <summary>The translation directory next to the client binaries.</summary>
    public static string TranslationDir
        => Path.Join(Path.GetDirectoryName(typeof(Loc).Assembly.Location)!, "Translation");

    public static void Reload()
        => _translator = SourceJoinTranslator.Load(TranslationDir, AppSettings.Translation);
}
