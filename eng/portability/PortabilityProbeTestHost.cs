// Host-only test infrastructure for PortabilityProbe.Tests. Not part of any ProbeSource glob, so
// it needs no Remove entry and carries no debt-list annotation.
//
// WHY THIS EXISTS
//   tests/CommonTestUtils/ConfigureJoinableTaskFactoryAttribute.cs sets up the ambient
//   GitUI.ThreadHelper.JoinableTaskContext that ThreadHelper.FileAndForget/JoinPendingOperationsAsync
//   need - and a large slice of GitCommands (Executable, GitCommandRunner, GitModule, AsyncLoader,
//   RevisionReader, CommandLog, ...) uses those. Without it, any included test that exercises this
//   code throws "ThreadHelper.JoinableTaskContext has not been initialized".
//
//   That file can't be source-included here: its STA branch news up a System.Windows.Forms.Form
//   and wires System.Windows.Forms.Application.ThreadException, neither available with
//   UseWindowsForms=false. Editing the shared file to strip that branch is out of scope (it would
//   touch test infrastructure shared with the real, Windows-targeted projects).
//
//   The STA branch only matters to test classes marked [Apartment(ApartmentState.STA)]. Exactly
//   one file in the probed test suites needs that: AsyncLoaderTests.cs, which is excluded below
//   for that reason (a Windows STA/COM apartment-threading requirement). So only the non-STA
//   ("background thread") branch of ConfigureJoinableTaskFactoryAttribute is needed here, and that
//   branch is already 100% portable - it is reproduced as-is below, renamed to avoid colliding
//   with the excluded type.
//
//   tests/CommonTestUtils/TestAppSettingsAttribute.cs is excluded for a similar, unrelated
//   reason: it opens `new Semaphore(1, 1, "GitExtensionsTestAssemblySerializer")` - a NAMED
//   semaphore, to serialize AppSettings file access across concurrently-running test PROCESSES.
//   Named synchronization primitives are Windows-only in .NET; constructing it throws
//   PlatformNotSupportedException on Linux, aborting the whole test host before a single test
//   runs. There is only ever one process for a `dotnet test` run of this host, so an
//   unnamed/process-local Semaphore gives the same effective serialization here.
//
//   NOTE ON HISTORY: this file used to also carry a substantial workaround for AppSettings'
//   static constructor unconditionally touching the Windows registry (a module initializer that
//   pre-seeded the settings file, plus try/catch degradation in PortableTestAppSettingsAttribute
//   below). That was a genuine product bug, found by this host, and it has since been FIXED in
//   product code by commit 89e02fc6e ("fix: guard AppSettings registry access off Windows") -
//   the registry helpers and GetSettingsFromRegistry are now guarded with
//   OperatingSystem.IsWindows(). The workaround was removed once the fix landed; see that commit
//   for the bug's full story, not this file.
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CommonTestUtils;
using GitCommands;
using GitUI;
using Microsoft.VisualStudio.Threading;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using PortabilityProbeTests;

// Mirrors tests/app/UnitTests/GitCommands.Tests/Properties/AssemblyInfo.cs and
// tests/app/UnitTests/GitExtensions.Extensibility.Tests/Properties/AssemblyInfo.cs. Those files
// themselves are excluded from the merged compile (three copies of these assembly-level
// attributes - CommonTestUtils, GitCommands.Tests, GitExtensions.Extensibility.Tests - would
// collide as soon as everything lands in one assembly), so this is the single surviving copy.
[assembly: Epilogue]
[assembly: PortableTestAppSettings]
[assembly: Category("UnitTests")]
[assembly: PortableJoinableTaskFactory]

namespace PortabilityProbeTests;

// Host-local replacement for CommonTestUtils.TestAppSettingsAttribute. A faithful copy - the only
// difference is the cross-process named Semaphore swapped for an unnamed (process-local) one, per
// this file's header comment: named synchronization primitives are Windows-only in .NET, and
// there is only ever one process for a `dotnet test` run of this host to serialize against.
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PortableTestAppSettingsAttribute : Attribute, ITestAction
{
    private readonly Semaphore _semaphore = new(initialCount: 1, maximumCount: 1);

    public ActionTargets Targets => ActionTargets.Suite;

    public void BeforeTest(ITest test)
    {
        _semaphore.WaitOne();

        File.Delete(AppSettings.SettingsContainer.SettingsCache.SettingsFilePath);
        AppSettings.SettingsContainer.SettingsCache.Load();

        AppSettings.CheckForUpdates = false;
        AppSettings.ShowAvailableDiffTools = false;

        AppSettings.SettingsContainer.SettingsCache.Save();
    }

    public void AfterTest(ITest test)
    {
        AppSettings.SettingsContainer.SettingsCache.Dispose();

        _semaphore.Release();
    }
}

[AttributeUsage(AttributeTargets.Assembly)]
public sealed class PortableJoinableTaskFactoryAttribute : Attribute, ITestAction
{
    private DenyExecutionSynchronizationContext? _denyExecutionSynchronizationContext;
    private ExceptionDispatchInfo? _threadException;

    public ActionTargets Targets => ActionTargets.Test;

    public void BeforeTest(ITest test)
    {
        ThreadHelper.HasJoinableTaskContext.Should().BeFalse("Tests with joinable tasks must not be run in parallel!");

        for (ITest? scope = test; scope is not null; scope = scope.Parent)
        {
            System.Collections.IList apartmentState = scope.Properties[nameof(System.Threading.ApartmentState)];
            if (apartmentState.Count > 0)
            {
                if (apartmentState.Contains(System.Threading.ApartmentState.STA))
                {
                    throw new NotSupportedException(
                        $"{nameof(PortableJoinableTaskFactoryAttribute)} does not support [Apartment(ApartmentState.STA)] " +
                        "(no System.Windows.Forms host is available off Windows). Exclude the test from PortabilityProbe.Tests instead.");
                }

                break;
            }
        }

        _denyExecutionSynchronizationContext = new DenyExecutionSynchronizationContext(SynchronizationContext.Current);
        ThreadHelper.JoinableTaskContext = new JoinableTaskContext(_denyExecutionSynchronizationContext.MainThread, _denyExecutionSynchronizationContext);
    }

    public void AfterTest(ITest test)
    {
        try
        {
            try
            {
                using CancellationTokenSource cts = new(AsyncTestHelper.UnexpectedTimeout);
                try
                {
                    ThreadHelper.CancelSwitchToMainThread();
                    ThreadHelper.JoinableTaskContext.Factory.Run(() => ThreadHelper.JoinPendingOperationsAsync(cts.Token));
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw;
                }
            }
            finally
            {
                ThreadHelper.JoinableTaskContext = null!;
                if (_denyExecutionSynchronizationContext is not null)
                {
                    SynchronizationContext.SetSynchronizationContext(_denyExecutionSynchronizationContext.UnderlyingContext);
                }
            }

            _denyExecutionSynchronizationContext?.ThrowIfSwitchOccurred();
        }
        catch (Exception ex) when (_threadException is not null)
        {
            StoreThreadException(ex);
        }
        finally
        {
            Interlocked.Exchange(ref _threadException, null)?.Throw();
        }
    }

    private void StoreThreadException(Exception ex)
    {
        if (_threadException is not null)
        {
            ex = new AggregateException([_threadException.SourceException, ex]);
        }

        _threadException = ExceptionDispatchInfo.Capture(ex);
    }

    // Copied from CommonTestUtils.ConfigureJoinableTaskFactoryAttribute's private nested type of
    // the same name: it has no WinForms dependency, only the outer file does.
    private sealed class DenyExecutionSynchronizationContext : SynchronizationContext
    {
        private readonly SynchronizationContext? _underlyingContext;
        private readonly Thread _mainThread;
        private readonly StrongBox<ExceptionDispatchInfo> _failedTransfer;

        public DenyExecutionSynchronizationContext(SynchronizationContext? underlyingContext)
            : this(underlyingContext, mainThread: null, failedTransfer: null)
        {
        }

        private DenyExecutionSynchronizationContext(SynchronizationContext? underlyingContext, Thread? mainThread, StrongBox<ExceptionDispatchInfo>? failedTransfer)
        {
            _underlyingContext = underlyingContext;
            _mainThread = mainThread ?? new Thread(MainThreadStart);
            _failedTransfer = failedTransfer ?? new StrongBox<ExceptionDispatchInfo>();
        }

        internal SynchronizationContext? UnderlyingContext => _underlyingContext;

        internal Thread MainThread => _mainThread;

        private static void MainThreadStart() => throw new InvalidOperationException("This thread should never be started.");

        internal void ThrowIfSwitchOccurred()
        {
            _failedTransfer.Value?.Throw();
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                if (_failedTransfer.Value is null)
                {
                    ThrowFailedTransferExceptionForCapture();
                }
            }
            catch (InvalidOperationException e)
            {
                _failedTransfer.Value = ExceptionDispatchInfo.Capture(e);
            }

            (_underlyingContext ?? new SynchronizationContext()).Post(d, state);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            try
            {
                if (_failedTransfer.Value is null)
                {
                    ThrowFailedTransferExceptionForCapture();
                }
            }
            catch (InvalidOperationException e)
            {
                _failedTransfer.Value = ExceptionDispatchInfo.Capture(e);
            }

            (_underlyingContext ?? new SynchronizationContext()).Send(d, state);
        }

        public override SynchronizationContext CreateCopy()
        {
            return new DenyExecutionSynchronizationContext(_underlyingContext?.CreateCopy(), _mainThread, _failedTransfer);
        }

        private static void ThrowFailedTransferExceptionForCapture()
        {
            throw new InvalidOperationException("Tests cannot use SwitchToMainThreadAsync unless they are marked with ApartmentState.STA.");
        }
    }
}
