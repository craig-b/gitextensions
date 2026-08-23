using System.Diagnostics;
using Microsoft.VisualStudio.Threading;

namespace GitUI;

public class TaskManager
{
    private static readonly CancellationTokenSequence _switchToMainThreadCancellationTokenSequence = new();

    private static CancellationToken _switchToMainThreadCancellationToken = _switchToMainThreadCancellationTokenSequence.Next();

    private readonly JoinableTaskCollection _joinableTaskCollection;

    /// <summary>
    ///  Reports an exception that escaped a fire-and-forget operation.
    /// </summary>
    /// <remarks>
    ///  <para>
    ///   This is the seam that keeps <see cref="TaskManager"/> platform-neutral: the WinForms host wires it to
    ///   <c>Application.OnThreadException</c> at startup, which routes to the bug reporter.
    ///  </para>
    ///  <para>
    ///   The default traces rather than throwing, so tests and non-UI hosts behave sanely without configuration.
    ///   A host that shows exceptions to users <b>must</b> set this, or they will be traced and not surfaced.
    ///  </para>
    /// </remarks>
    public static Action<Exception> UnhandledExceptionReporter { get; set; } = static ex => Trace.TraceError(ex.ToString());

    public TaskManager(JoinableTaskContext joinableTaskContext)
    {
        JoinableTaskContext = joinableTaskContext;
        _joinableTaskCollection = joinableTaskContext.CreateCollection();
        JoinableTaskFactory = joinableTaskContext.CreateFactory(_joinableTaskCollection);
    }

    public JoinableTaskContext JoinableTaskContext { get; init; }

    public JoinableTaskFactory JoinableTaskFactory { get; init; }

    /// <summary>
    /// Handle all exceptions from asynchronous execution of <paramref name="asyncAction"/> by calling <paramref name="handleExceptionAsync"/> except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    internal static async Task HandleExceptionsAsync(Func<Task> asyncAction, Func<Exception, Task> handleExceptionAsync)
    {
        try
        {
            await asyncAction();
        }
        catch (OperationCanceledException)
        {
            // Do not rethrow these
        }
        catch (Exception ex)
        {
            await handleExceptionAsync(ex);
        }
    }

    /// <summary>
    /// Handle all exceptions from synchronous execution of <paramref name="action"/> by calling <paramref name="handleException"/> except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public static void HandleExceptions(Action action, Action<Exception> handleException)
    {
        try
        {
            action();
        }
        catch (OperationCanceledException)
        {
            // Do not rethrow these
        }
        catch (Exception ex)
        {
            handleException(ex);
        }
    }

    internal static Func<Task> AsyncAction(Action action)
    {
        return () =>
            {
                action();
                return Task.CompletedTask;
            };
    }

    /// <summary>
    ///  Exposed for <c>ControlThreadingExtensions</c>, which owns the <c>Control</c> overloads.
    /// </summary>
    internal static CancellationToken SwitchToMainThreadToken => _switchToMainThreadCancellationToken;

    internal static void CancelSwitchToMainThread()
    {
        _switchToMainThreadCancellationToken = _switchToMainThreadCancellationTokenSequence.Next();
    }

    /// <summary>
    /// Asynchronously run <paramref name="asyncAction"/> on a background thread and forward all exceptions to <see cref="UnhandledExceptionReporter"/> except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public void FileAndForget(Func<Task> asyncAction)
    {
        _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await TaskScheduler.Default;
                await HandleExceptionsAsync(asyncAction, ReportExceptionOnMainThreadAsync);
            });
    }

    /// <summary>
    /// Asynchronously run <paramref name="action"/> on a background thread and forward all exceptions to <see cref="UnhandledExceptionReporter"/> except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public void FileAndForget(Action action)
    {
        FileAndForget(AsyncAction(action));
    }

    /// <summary>
    /// Asynchronously run <paramref name="task"/> on a background thread and forward all exceptions to <see cref="UnhandledExceptionReporter"/> except for <see cref="OperationCanceledException"/>, which is ignored.
    /// </summary>
    public void FileAndForget(Task task)
    {
        TimeSpan infiniteTimeout = new(-TimeSpan.TicksPerMillisecond);
        FileAndForget(() => task.WaitAsync(infiniteTimeout));
    }

    // Note: the InvokeAndForget(Control, ...) overloads live in ControlThreadingExtensions,
    // because they were the only members of this type that required System.Windows.Forms.

    public async Task JoinPendingOperationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _joinableTaskCollection.JoinTillEmptyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
        }
    }

    public void JoinPendingOperations()
    {
        const int maxWaitMilliseconds = 60_000;
        using CancellationTokenSource cancellationTokenSource = new(maxWaitMilliseconds);

        // Note that JoinableTaskContext.Factory must be used to bypass the default behavior of JoinableTaskFactory
        // since the latter adds new tasks to the collection and would therefore never complete.
        JoinableTaskContext.Factory.Run(() => JoinPendingOperationsAsync(cancellationTokenSource.Token));
    }

    /// <summary>
    /// Forward the exception <paramref name="ex"/> to <see cref="UnhandledExceptionReporter"/> on the main thread.
    /// </summary>
    /// The readability of the callstack is improved by calling <c>ExceptionExtensions.Demystify</c>.
    internal async Task ReportExceptionOnMainThreadAsync(Exception ex)
    {
        try
        {
            if (!JoinableTaskContext.IsOnMainThread)
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(_switchToMainThreadCancellationToken);
            }

            UnhandledExceptionReporter(ex.Demystify());
        }
        catch (Exception exceptionWhileReporting)
        {
            try
            {
                Trace.TraceError(exceptionWhileReporting.ToString());
                Trace.TraceError(ex.ToString());
                Trace.TraceError(ex.StackTrace);
            }
            catch
            {
                // Give up
            }
        }
    }
}
