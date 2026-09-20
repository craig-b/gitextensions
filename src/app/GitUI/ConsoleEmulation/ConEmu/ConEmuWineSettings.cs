using System.Xml;
using ConEmu.WinForms;
using GitCommands.Utils;

namespace GitUI.ConsoleEmulation.ConEmu;

/// <summary>
///  ConEmu settings that differ under Wine.
/// </summary>
internal static class ConEmuWineSettings
{
    /// <summary>ConEmu's own settings dialog defaults to this scrollback depth; its code default is the 32,766-row maximum.</summary>
    private const int BufferHeight = 1000;

    /// <summary>
    ///  Keeps the real console's buffer at a normal scrollback depth under Wine.
    /// </summary>
    /// <remarks>
    ///  ConEmu implements the alternate screen that full-screen programs such as nano and less switch to by asking its
    ///  server for a dump of the whole console buffer and writing it back when the program exits. With ConEmu's code
    ///  default of 32,766 rows that copy took about three seconds per switch under Wine, felt as a long pause before the
    ///  prompt reappeared after quitting the program. A thousand rows keeps it below notice.
    /// </remarks>
    public static void Apply(ConEmuStartInfo startInfo)
    {
        if (!EnvUtils.RunningUnderWine)
        {
            return;
        }

        XmlDocument configuration = startInfo.BaseConfiguration;
        if (configuration.SelectSingleNode("/key/key/key") is not XmlNode settings)
        {
            return;
        }

        XmlElement value = (XmlElement)(settings.SelectSingleNode("value[@name='DefaultBufferHeight']") ?? settings.AppendChild(configuration.CreateElement("value"))!);
        value.SetAttribute("name", "DefaultBufferHeight");
        value.SetAttribute("type", "dword");
        value.SetAttribute("data", BufferHeight.ToString("x8"));
        startInfo.BaseConfiguration = configuration;
    }
}
