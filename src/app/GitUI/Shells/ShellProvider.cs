using ConEmu.WinForms;

namespace GitUI.Shells;

public class ShellProvider : IShellProvider
{
    // Under Wine with the git bridge the Console tab is a Linux terminal; the BusyBox bash of the prefix is not offered then.
    private static readonly IShellDescriptor DefaultShell = new LinuxShell() is { HasExecutable: true } linux ? linux : new BashShell();
    private static readonly IShellDescriptor[] Shells = [DefaultShell, new CmdShell(), new PwshShell(), new PowerShellShell()];

    public IReadOnlyList<IShellDescriptor> GetShells() => Shells;

    public IShellDescriptor GetShell(string? name) => Shells.FirstOrDefault(s => s.Name == name) ?? DefaultShell;

    public string GetShellCommandLine(string? shellType)
    {
        IShellDescriptor shell = GetShell(shellType);

        if (!shell.HasExecutable || shell.ExecutableCommandLine is null)
        {
            // Fallback to default if ExecutableCommandLine is not set
            return ConEmuConstants.DefaultConsoleCommandLine;
        }

        return shell.ExecutableCommandLine;
    }
}
