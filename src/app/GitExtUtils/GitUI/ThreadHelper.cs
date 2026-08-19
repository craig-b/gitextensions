using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GitExtensions.Extensibility;
using Microsoft.VisualStudio.Threading;

namespace GitUI;

public static class ThreadHelper
{
    private const int RPC_E_WRONG_THREAD = unchecked((int)0x8001010E);

    private static TaskManager? _taskManager;

    private static TaskManager TaskManager =>
        _taskManager ?? throw new InvalidOperationException($"{nameof(ThreadHelper)}.{nameof(JoinableTaskContext)} has not been initialized.");

    /// <summary>
    ///  The ambient task manager. Exposed to <c>ControlThreadingExtensions</c> only, which owns the
    ///  <c>InvokeAndForget(this Control, ...)</c> overloads that used to live here.
    /// </summary>
    internal static TaskManager AmbientTaskManager => TaskManager;

    public static bool HasJoinableTaskContext => _taskManager is not null;

    public static JoinableTaskContext JoinableTaskContext
    {
        get => TaskManager.JoinableTaskContext;

        // Public since the vertical slice: initializing the context is a HOST responsibility
        // (the WinForms Program, the test hosts, the Avalonia client) - not an internal detail.
        set => _taskManager = value is null ? null : new(value);
    }

    public static JoinableTaskFactory JoinableTaskFactory => TaskManager.JoinableTaskFactory;

    internal static void CancelSwitchToMainThread()
        => TaskManager.CancelSwitchToMainThread();

    public static ExclusiveTaskRunner CreateExclusiveTaskRunner()
        => new(TaskManager);

    public static TaskManager CreateTaskManager()
        => new(TaskManager.JoinableTaskContext);

    public static void ThrowIfNotOnUIThread([CallerMemberName] string callerMemberName = "")
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            return;
        }

        if (!JoinableTaskContext.IsOnMainThread)
        {
            string message = string.Format(CultureInfo.CurrentCulture, "{0} must be called on the UI thread.", callerMemberName);
            throw new COMException(message, RPC_E_WRONG_THREAD);
        }
    }

    [Conditional("DEBUG")]
    [DebuggerStepThrough]
    public static void AssertOnUIThread()
    {
        if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
        {
            return;
        }

        DebugHelpers.Assert(JoinableTaskContext.IsOnMainThread, "Must be on the UI thread.");
    }

    public static void ThrowIfOnUIThread([CallerMemberName] string callerMemberName = "")
    {
        if (JoinableTaskContext.IsOnMainThread)
        {
            string message = string.Format(CultureInfo.CurrentCulture, "{0} must be called on a background thread.", callerMemberName);
            throw new COMException(message, RPC_E_WRONG_THREAD);
        }
    }

    /// <summary>
    /// Asynchronously run <paramref name="asyncAction"/> on a background thread and forward all exceptions to the application's unhandled-exception handler except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public static void FileAndForget(Func<Task> asyncAction)
        => TaskManager.FileAndForget(asyncAction);

    /// <summary>
    /// Asynchronously run <paramref name="action"/> on a background thread and forward all exceptions to the application's unhandled-exception handler except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public static void FileAndForget(Action action)
        => TaskManager.FileAndForget(action);

    /// <summary>
    /// Asynchronously run <paramref name="joinableTask"/> on a background thread and forward all exceptions to the application's unhandled-exception handler except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public static void FileAndForget(this JoinableTask joinableTask)
        => TaskManager.FileAndForget(joinableTask.Task);

    /// <summary>
    /// Asynchronously run <paramref name="task"/> on a background thread and forward all exceptions to the application's unhandled-exception handler except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public static void FileAndForget(this Task task)
        => TaskManager.FileAndForget(task);

    // Note: the InvokeAndForget(this Control, ...) overloads live in ControlThreadingExtensions,
    // because they are the only members of this type that require System.Windows.Forms.

    public static async Task JoinPendingOperationsAsync(CancellationToken cancellationToken)
        => await TaskManager.JoinPendingOperationsAsync(cancellationToken);

    public static T CompletedResult<T>(this Task<T> task)
    {
        if (!task.IsCompleted)
        {
            throw new InvalidOperationException();
        }

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
        return task.Result;
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
    }

    public static T? CompletedOrDefault<T>(this Task<T> task)
    {
        if (!task.IsCompleted)
        {
            return default;
        }

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
        return task.Result;
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
    }
}
