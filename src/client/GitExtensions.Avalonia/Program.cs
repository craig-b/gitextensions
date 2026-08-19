using Avalonia;
using GitCommands;
using GitUI;
using Microsoft.VisualStudio.Threading;

namespace GitExtensions.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Host decisions the portable core cannot make for itself, set before any AppSettings
        // access: this client is not a portable installation (settings live in the user profile),
        // decided here rather than via the trim-hostile ConfigurationManager mechanism.
        HostPortability.IsPortableOverride = false;

        // The engine's executable layer awaits through ThreadHelper's JoinableTaskFactory; every
        // host initializes the context on its main thread (WinForms Program.cs, the probe test
        // host) - so does this one.
        ThreadHelper.JoinableTaskContext = new JoinableTaskContext();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
