using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace GitExtensions.Extensibility;

/// <summary>
///  Set of DEBUG-only helpers.
/// </summary>
public static class DebugHelpers
{
    [Conditional("DEBUG")]
    public static void Assert([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            Fail(message);
        }
    }

    [Conditional("DEBUG")]
    public static void Fail(string message)
    {
        if (Debugger.IsAttached || IsTestRunning)
        {
            Debug.Fail(message);
        }
        else
        {
            Debugger.Launch();

            if (!Debugger.IsAttached)
            {
                throw new InvalidOperationException(message);
            }
        }
    }

    [Conditional("DEBUG")]
    public static void Trace(string message, [CallerMemberName] string caller = "")
    {
        // colon and noBreakSpace are used to detect such messages in order to show them in the Output History
        const char noBreakSpace = '\u00a0';
        Debug.WriteLine($"{caller}:{noBreakSpace}{message}");
    }

    [Conditional("DEBUG")]
    public static void TraceIf(bool condition, string message, [CallerMemberName] string caller = "")
    {
        if (condition)
        {
            Trace(message, caller);
        }
    }

    // Environment.ProcessPath rather than Application.ExecutablePath: this type is otherwise
    // platform-neutral, and detecting a test host does not need WinForms. The process-name match
    // covers Windows' testhost.exe apphost; off Windows `dotnet test` runs testhost.dll inside
    // the muxer, so the process is named "dotnet" and the entry assembly carries the signal.
    private static bool IsTestRunning
        => Path.GetFileNameWithoutExtension(Environment.ProcessPath)
               ?.Equals("testhost", StringComparison.OrdinalIgnoreCase) is true
           || Assembly.GetEntryAssembly()?.GetName().Name?.Equals("testhost", StringComparison.OrdinalIgnoreCase) is true;
}
