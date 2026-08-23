using System.Text;
using GitCommands;
using GitCommands.Logging;
using GitExtensions.Extensibility;
using GitUI;
using Microsoft.VisualStudio.Threading;

namespace GitCommandsTests;

public sealed class ExecutableTests
{
    // These tests need a real external process that keeps running for a controllable duration
    // (there's no built-in "sleep" on Windows), so they shell out to "ping -n/-c <count>
    // 127.0.0.1" - a trick that works on both OSes, just with a different executable name and
    // count switch. Resolving to the platform's own executable/switch here is the same fix as
    // "ping.exe" -> an OS-conditional that yields "ping.exe" on Windows, applied consistently.
    private static string PingExecutable => OperatingSystem.IsWindows() ? "ping.exe" : "ping";
    private static string PingCountSwitch => OperatingSystem.IsWindows() ? "-n" : "-c";

    [SetUp]
    public void SetUp()
    {
    }

    [TearDown]
    public void TearDown()
    {
    }

    [Test]
    public async Task Process_shall_be_killed_on_cancellation()
    {
        TimeSpan cancelDelay = TimeSpan.FromSeconds(1);

        await TaskScheduler.Default;

        using CancellationTokenSource cts = new();

        // start a process running for seconds
        IExecutable executable = new Executable(PingExecutable);
        IProcess process = executable.Start($"{PingCountSwitch} {cancelDelay.TotalSeconds + 60} 127.0.0.1", cancellationToken: cts.Token);
        DateTime startedAt = DateTime.Now;

        // cancel after delay
        await Task.Delay(cancelDelay);
        await cts.CancelAsync();

        // the process is killed on dispose but not before
        using (CancellationTokenSource ctsWaitExit = new())
        {
            try
            {
                ctsWaitExit.CancelAfter(cancelDelay);
                int exitCode = await process.WaitForExitAsync(ctsWaitExit.Token);
                Assert.Fail($"should not have exited, received: {exitCode}");
            }
            catch (OperationCanceledException)
            {
            }
        }

        process.Dispose();

        TimeSpan durationWaitTimeout = DateTime.Now - startedAt;
        durationWaitTimeout.Should().BeGreaterThan(1.5 * cancelDelay).And.BeLessThan(4 * cancelDelay);

        CommandLogEntry? cmd = CommandLog.Commands.LastOrDefault();
        (cmd?.Exception?.Message).Should().Be("Process killed");
    }

    [Test]
    public async Task WaitForProcessExitAsync_shall_return_latest_after_timeout()
    {
        TimeSpan halfRuntime = TimeSpan.FromSeconds(3);

        await TaskScheduler.Default;

        // start a process running for seconds
        IExecutable executable = new Executable(PingExecutable);
        using IProcess process = executable.Start($"{PingCountSwitch} {(halfRuntime.TotalSeconds * 2) + 1} 127.0.0.1");

        // wait for process exit, but cancel the wait while the process is still running
        using CancellationTokenSource cts = new();
        DateTime startedAt = DateTime.Now;
        cts.CancelAfter(halfRuntime);
        try
        {
            await process.WaitForExitAsync(cts.Token); // may or may not throw on cancel
        }
        catch (OperationCanceledException)
        {
            // ignore
        }

        TimeSpan durationWaitTimeout = DateTime.Now - startedAt;
        durationWaitTimeout.Should().BeGreaterThan(0.5 * halfRuntime).And.BeLessThan(1.5 * halfRuntime);

        // wait for process exit without cancellation
        await process.WaitForExitAsync(CancellationToken.None);

        TimeSpan durationExit = DateTime.Now - startedAt;
        durationExit.Should().BeGreaterThan(1.5 * halfRuntime).And.BeLessThan(2.5 * halfRuntime);
    }

    [Test]
    public async Task ExecuteAsync_shall_return_latest_after_timeout([Values("cmd.exe", "ping.exe")] string exeFile)
    {
        const int cancelDelay = 1000;
        const int exitDelay = cancelDelay;
        const int minRuntime = cancelDelay + exitDelay;
        int pingCount = (minRuntime / 1000) + 2;

        // cmd.exe with no arguments exits immediately when stdin is not a terminal (e.g., on CI
        // runners). Run a subcommand that blocks for the required duration instead. Off Windows,
        // "cmd.exe" and "ping.exe" as literal executables don't exist; resolve exeFile to the
        // platform's own shell/ping and matching count switch, which is the same intent (a shell
        // wrapping a ping call, or a direct ping call) translated per OS.
        (string resolvedExeFile, string arguments) = (exeFile, OperatingSystem.IsWindows()) switch
        {
            ("ping.exe", true) => ("ping.exe", $"-n {pingCount} 127.0.0.1"),
            ("ping.exe", false) => ("ping", $"-c {pingCount} 127.0.0.1"),
            ("cmd.exe", true) => ("cmd.exe", $"/c ping -n {pingCount} 127.0.0.1"),
            ("cmd.exe", false) => ("/bin/sh", $"-c \"ping -c {pingCount} 127.0.0.1\""),
            _ => (exeFile, "")
        };

        using CancellationTokenSource cancellationTokenSource = new();
        CancellationToken cancellationToken = cancellationTokenSource.Token;
        IExecutable executable = new Executable(resolvedExeFile);

        Exception? exception = null;
        ExecutionResult? executionResult = null;
        async Task ExecuteAsync()
        {
            try
            {
                await TaskScheduler.Default.SwitchTo(alwaysYield: true);
                executionResult = await executable.ExecuteAsync(arguments, outputEncoding: Encoding.Default, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        }

        _ = ThreadHelper.JoinableTaskFactory.RunAsync(ExecuteAsync, JoinableTaskCreationOptions.LongRunning);

        await Task.Delay(cancelDelay);
        await cancellationTokenSource.CancelAsync();
        await Task.Delay(exitDelay);

        exception.Should().NotBeNull();
        exception.GetType().Should().Be<OperationCanceledException>();
        executionResult.Should().BeNull();
    }
}
