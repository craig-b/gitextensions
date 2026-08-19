using Avalonia;
using GitUI;
using Microsoft.VisualStudio.Threading;

namespace GitExtensions.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
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
