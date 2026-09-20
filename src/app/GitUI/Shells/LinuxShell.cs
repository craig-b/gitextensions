using GitCommands;
using GitCommands.Utils;
using GitUI.Properties;

namespace GitUI.Shells;

/// <summary>
///  The user's Linux login shell, for the Console tab under Wine. The tab hosts the bridge's terminal relay
///  (eng/wine/bridge-tty.c), which puts a pseudo-terminal session from the git bridge daemon into the console;
///  ConEmu renders it. The launcher passes the relay's path in <c>GITEXT_GIT_BRIDGE_TTY</c>. Opened as a window,
///  from the Tools menu, it is the user's own terminal emulator, through the <c>open-terminal</c> script installed
///  next to the relay.
/// </summary>
public class LinuxShell : ShellDescriptor
{
    public const string ShellName = "linux";

    public LinuxShell()
    {
        Name = ShellName;
        Icon = Images.Console;
        ExecutableName = "bridge-tty.exe";

        if (NativeGitBridge.TerminalRelay is { } relay)
        {
            string openTerminal = Path.Combine(Path.GetDirectoryName(relay) ?? "", "open-terminal");
            ExecutablePath = File.Exists(openTerminal) ? openTerminal : relay;
            ExecutableCommandLine = relay.Quote();
        }
    }

    /// <summary>The directory in its Linux form, quoted for a POSIX shell.</summary>
    public override string GetChangeDirCommand(string path)
        => $"cd '{WinePaths.ToHostPath(path).Replace("'", "'\\''")}'";
}
