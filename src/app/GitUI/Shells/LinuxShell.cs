using GitCommands;
using GitCommands.Utils;
using GitUI.Properties;

namespace GitUI.Shells;

/// <summary>
///  The user's Linux login shell, for the Console tab under Wine. The tab hosts the bridge's terminal relay
///  (eng/wine/bridge-tty.c), which puts a pseudo-terminal session from the git bridge daemon into the console;
///  ConEmu renders it. The launcher passes the relay's path in <c>GITEXT_GIT_BRIDGE_TTY</c>.
/// </summary>
public class LinuxShell : ShellDescriptor
{
    public const string ShellName = "linux";

    public LinuxShell()
    {
        Name = ShellName;
        Icon = Images.Console;
        ExecutableName = "bridge-tty.exe";

        if (NativeGitBridge.IsEnabled
            && Environment.GetEnvironmentVariable("GITEXT_GIT_BRIDGE_TTY") is { Length: > 0 } relay
            && File.Exists(relay))
        {
            ExecutablePath = relay;
            ExecutableCommandLine = relay.Quote();
        }
    }

    /// <summary>The directory in its Linux form, quoted for a POSIX shell.</summary>
    public override string GetChangeDirCommand(string path)
        => $"cd '{WinePaths.ToHostPath(path).Replace("'", "'\\''")}'";
}
