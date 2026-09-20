using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using GitCommands.Logging;
using GitCommands.Utils;
using GitExtensions.Extensibility;
using GitUI;
using Microsoft.VisualStudio.Threading;

namespace GitCommands;

/// <summary>
///  A bridged git process whose error output is streamed live, for the progress dialogs.
/// </summary>
public interface IBridgedConsoleProcess : IProcess
{
    StreamReader StandardErrorReader { get; }
}

/// <summary>
///  Runs git through the Linux-side bridge daemon when the app runs under Wine.
/// </summary>
/// <remarks>
///  <para>
///   Every Windows process Wine creates costs about a quarter of a second, and the app runs git
///   once or more per click. The launcher starts the <c>git-bridge</c> daemon (built from eng/wine/git-bridge.cs)
///   next to the install and passes its port and token in <c>GITEXT_GIT_BRIDGE_PORT</c> and
///   <c>GITEXT_GIT_BRIDGE_TOKEN</c>; the daemon runs native git and translates paths between the two worlds.
///   See docs/running-under-wine.md.
///  </para>
///  <para>
///   Protocol: one JSON header line, then frames of a byte type and a little-endian length.
///   Client to daemon: 0 stdin data, 1 stdin EOF, 2 kill. Daemon to client: 1 stdout, 2 stderr,
///   3 exit code, 4 error text, 5 process id. The header names the program only when it is not
///   git; the daemon then looks it up on the Linux PATH, so user tools and scripts run natively too.
///  </para>
/// </remarks>
public static class NativeGitBridge
{
    private static readonly Lazy<(int Port, string Token)?> _endpoint = new(ReadEndpoint);

    public static bool IsEnabled => EnvUtils.RunningUnderWine && _endpoint.Value is not null;

    public static bool IsGit(string fileName)
        => Path.GetFileName(fileName) is { } name
           && (name.Equals("git.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("git", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///  Whether a program runs on the Linux side: git always, and everything else unless it is
    ///  explicitly a Windows program by extension. User tools and scripts are Linux programs by default.
    /// </summary>
    public static bool IsBridged(string fileName)
        => IsGit(fileName) || !IsWindowsProgram(fileName);

    private static bool IsWindowsProgram(string fileName)
        => Path.GetExtension(fileName) is { Length: > 0 } extension
           && (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".com", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    ///  Whether a command the app would give a console window can go over the bridge anyway.
    ///  A Linux program other than git opens its own window or needs none. For git, the merge and
    ///  diff tools and the Tk GUIs open their own windows, and native git then launches native tools.
    ///  Anything interactive on the console (add --patch) stays with Windows git.
    /// </summary>
    public static bool AllowsWindow(string fileName, string arguments)
    {
        if (!IsGit(fileName))
        {
            return true;
        }

        bool skipNext = false;
        foreach (string token in SplitArguments(arguments))
        {
            if (skipNext)
            {
                skipNext = false;
            }
            else if (token is "-c" or "-C" or "--git-dir" or "--work-tree")
            {
                skipNext = true;
            }
            else if (!token.StartsWith('-'))
            {
                return token is "mergetool" or "difftool" or "gui" or "citool";
            }
        }

        return false;
    }

    public static IProcess Start(string fileName,
                                 string prefixArguments,
                                 string arguments,
                                 string workDir,
                                 bool redirectInput,
                                 bool redirectOutput,
                                 Encoding? outputEncoding,
                                 bool throwOnErrorExit,
                                 CancellationToken cancellationToken)
    {
        (int port, string token) = _endpoint.Value!.Value;
        return new BridgeProcess(port, token, fileName, prefixArguments, arguments, workDir, redirectInput, redirectOutput, outputEncoding, throwOnErrorExit, cancellationToken, extraEnvironment: null, liveErrorOutput: false);
    }

    /// <summary>
    ///  Starts git for a progress dialog: stdin open, stdout and stderr streamed live, the exit code reported rather than thrown.
    /// </summary>
    /// <param name="environment">Variables for this process only, such as the sequence editor for an interactive rebase; not filtered.</param>
    public static IBridgedConsoleProcess StartConsole(string fileName, string arguments, string workDir, IReadOnlyDictionary<string, string> environment, Encoding outputEncoding)
    {
        (int port, string token) = _endpoint.Value!.Value;
        arguments = arguments.Replace("$QUOTE$", "\\\"");
        return new BridgeProcess(port, token, fileName, prefixArguments: "", arguments, workDir, redirectInput: true, redirectOutput: true, outputEncoding, throwOnErrorExit: false, CancellationToken.None, environment, liveErrorOutput: true);
    }

    private static (int, string)? ReadEndpoint()
    {
        string? port = Environment.GetEnvironmentVariable("GITEXT_GIT_BRIDGE_PORT");
        string? token = Environment.GetEnvironmentVariable("GITEXT_GIT_BRIDGE_TOKEN");
        return int.TryParse(port, out int p) && !string.IsNullOrEmpty(token) ? (p, token) : null;
    }

    /// <summary>
    ///  Splits a command line into arguments with the Windows rules git.exe would have applied.
    /// </summary>
    private static string[] SplitArguments(string commandLine)
    {
        // The first token of a command line is the program and follows different quoting rules.
        nint argv = NativeMethods.CommandLineToArgvW($"git {commandLine}", out int count);
        if (argv == 0)
        {
            throw new InvalidOperationException("CommandLineToArgvW failed");
        }

        try
        {
            string[] args = new string[Math.Max(count - 1, 0)];
            for (int i = 1; i < count; i++)
            {
                args[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * nint.Size)) ?? "";
            }

            return args;
        }
        finally
        {
            NativeMethods.LocalFree(argv);
        }
    }

    /// <summary>
    ///  Environment variables the app sets for git that make sense on the Linux side.
    ///  Editor and ssh variables carry Windows paths and stay behind; the daemon sets its own.
    /// </summary>
    private static Dictionary<string, string> ForwardedEnvironment(IReadOnlyDictionary<string, string>? extra)
    {
        Dictionary<string, string> env = [];
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            string name = (string)entry.Key;
            bool forward = (name.StartsWith("GIT_", StringComparison.Ordinal) || name.StartsWith("DFT_", StringComparison.Ordinal))
                && name is not ("GIT_SSH" or "GIT_EDITOR" or "GIT_SEQUENCE_EDITOR" or "GIT_ASKPASS" or "GIT_EXEC_PATH" or "GIT_TEMPLATE_DIR" or "GIT_CONFIG_SYSTEM");
            if (forward && entry.Value is string value)
            {
                env[name] = value;
            }
        }

        if (extra is not null)
        {
            foreach ((string name, string value) in extra)
            {
                env[name] = value;
            }
        }

        return env;
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", SetLastError = true)]
        public static extern nint CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        [DllImport("kernel32.dll")]
        public static extern nint LocalFree(nint hMem);
    }

    private sealed class BridgeProcess : IBridgedConsoleProcess
    {
        private const byte StdinData = 0;
        private const byte StdinEof = 1;
        private const byte KillRequest = 2;
        private const byte StdoutFrame = 1;
        private const byte StderrFrame = 2;
        private const byte ExitFrame = 3;
        private const byte ErrorFrame = 4;
        private const byte PidFrame = 5;

        private readonly TaskCompletionSource<int> _exitTaskCompletionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _cancellationToken;
        private readonly Encoding? _errorEncoding;
        private readonly MemoryStream _errorOutputStream = new();
        private readonly ProcessOperation _logOperation;
        private readonly Socket _socket;
        private readonly NetworkStream _stream;
        private readonly Lock _sendLock = new();
        private readonly Pipe? _stdoutPipe;
        private readonly Pipe? _stderrPipe;
        private readonly StreamReader? _standardOutput;
        private readonly StreamReader? _standardError;
        private readonly StreamWriter? _standardInput;
        private readonly string _command;
        private readonly string _arguments;
        private readonly string _workDir;
        private readonly bool _throwOnErrorExit;

        private string? _errorOutput;
        private bool _disposed;
        private bool _exited;

        public BridgeProcess(int port,
                             string token,
                             string fileName,
                             string prefixArguments,
                             string arguments,
                             string workDir,
                             bool redirectInput,
                             bool redirectOutput,
                             Encoding? outputEncoding,
                             bool throwOnErrorExit,
                             CancellationToken cancellationToken,
                             IReadOnlyDictionary<string, string>? extraEnvironment,
                             bool liveErrorOutput)
        {
            _command = $"{fileName} {prefixArguments}".Trim();
            _arguments = arguments;
            _workDir = workDir;
            _throwOnErrorExit = throwOnErrorExit;
            _cancellationToken = cancellationToken;
            _errorEncoding = outputEncoding ?? (throwOnErrorExit ? Encoding.Default : null);

            _logOperation = CommandLog.LogProcessStart($"{_command} [bridge]", arguments, workDir);

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                _socket.Connect(System.Net.IPAddress.Loopback, port);
                _stream = new NetworkStream(_socket, ownsSocket: true);

                string header = System.Text.Json.JsonSerializer.Serialize(new
                {
                    token,
                    cwd = workDir,
                    program = IsGit(fileName) ? null : fileName,
                    args = SplitArguments($"{prefixArguments}{arguments}"),
                    env = ForwardedEnvironment(extraEnvironment),
                    stdin = redirectInput
                });
                byte[] headerBytes = Encoding.UTF8.GetBytes(header + "\n");
                _stream.Write(headerBytes);

                if (redirectInput)
                {
                    _standardInput = new StreamWriter(new StdinStream(this), new UTF8Encoding(false)) { AutoFlush = true };
                }
                else
                {
                    SendFrame(StdinEof, ReadOnlySpan<byte>.Empty);
                }

                if (redirectOutput)
                {
                    _stdoutPipe = new Pipe();
                    _standardOutput = new StreamReader(_stdoutPipe.Reader.AsStream(), outputEncoding ?? Encoding.Default);
                }

                if (liveErrorOutput)
                {
                    _stderrPipe = new Pipe();
                    _standardError = new StreamReader(_stderrPipe.Reader.AsStream(), outputEncoding ?? Encoding.Default);
                }

                _ = Task.Run(ReceiveLoopAsync);
            }
            catch (Exception ex)
            {
                _logOperation.LogProcessEnd(ex);
                Dispose();
                throw new ExternalOperationException(_command, arguments, workDir, innerException: ex);
            }
        }

        private void SendFrame(byte kind, ReadOnlySpan<byte> payload)
        {
            Span<byte> header = stackalloc byte[5];
            header[0] = kind;
            BinaryPrimitives.WriteUInt32LittleEndian(header[1..], (uint)payload.Length);
            lock (_sendLock)
            {
                if (_disposed)
                {
                    return;
                }

                try
                {
                    _stream.Write(header);
                    if (payload.Length > 0)
                    {
                        _stream.Write(payload);
                    }
                }
                catch (IOException)
                {
                    // The daemon has gone; the receive loop reports it.
                }
            }
        }

        private async Task ReceiveLoopAsync()
        {
            byte[] header = new byte[5];
            try
            {
                while (true)
                {
                    await _stream.ReadExactlyAsync(header);
                    byte kind = header[0];
                    int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(1));
                    byte[] payload = length > 0 ? new byte[length] : [];
                    if (length > 0)
                    {
                        await _stream.ReadExactlyAsync(payload);
                    }

                    switch (kind)
                    {
                        case StdoutFrame:
                            if (_stdoutPipe is not null)
                            {
                                await _stdoutPipe.Writer.WriteAsync(payload);
                            }

                            break;
                        case StderrFrame:
                            if (_stderrPipe is not null)
                            {
                                await _stderrPipe.Writer.WriteAsync(payload);
                            }
                            else
                            {
                                _errorOutputStream.Write(payload);
                            }

                            break;
                        case PidFrame:
                            _logOperation.SetProcessId(BinaryPrimitives.ReadInt32LittleEndian(payload));
                            break;
                        case ExitFrame:
                            await CompleteStdoutAsync(null);
                            HandleProcessExit(BinaryPrimitives.ReadInt32LittleEndian(payload));
                            return;
                        case ErrorFrame:
                            throw new InvalidOperationException($"git bridge: {Encoding.UTF8.GetString(payload)}");
                    }
                }
            }
            catch (Exception ex)
            {
                await CompleteStdoutAsync(ex);
                lock (_sendLock)
                {
                    if (_exited)
                    {
                        return;
                    }

                    _exited = true;
                }

                Exception reported = _disposed && _cancellationToken.IsCancellationRequested
                    ? new OperationCanceledException("Process killed", ex)
                    : new ExternalOperationException(_command, _arguments, _workDir, innerException: ex);
                _logOperation.LogProcessEnd(reported);
                _exitTaskCompletionSource.TrySetException(reported);
            }
        }

        private async Task CompleteStdoutAsync(Exception? ex)
        {
            if (_stdoutPipe is not null)
            {
                await _stdoutPipe.Writer.CompleteAsync(ex);
            }

            if (_stderrPipe is not null)
            {
                await _stderrPipe.Writer.CompleteAsync(ex);
            }
        }

        private void HandleProcessExit(int exitCode)
        {
            lock (_sendLock)
            {
                if (_exited)
                {
                    return;
                }

                _exited = true;
            }

            string? errorOutput = _errorEncoding is null ? null : _errorEncoding.GetString(_errorOutputStream.GetBuffer(), 0, (int)_errorOutputStream.Length).Trim();
            _errorOutput = errorOutput;
            _logOperation.LogProcessEnd(exitCode, errorOutput);

            if (_throwOnErrorExit && exitCode != 0)
            {
                string errorMessage = errorOutput?.Length is > 0 ? errorOutput : "External program returned non-zero exit code.";
                _exitTaskCompletionSource.TrySetException(new ExternalOperationException(_command, _arguments, _workDir, exitCode, new Exception(errorMessage)));
                return;
            }

            _exitTaskCompletionSource.TrySetResult(exitCode);
        }

        public StreamWriter StandardInput => _standardInput ?? throw new InvalidOperationException("Process was not created with redirected input.");

        public StreamReader StandardOutput => _standardOutput ?? throw new InvalidOperationException("Process was not created with redirected output.");

        public string StandardError => _errorOutput ?? throw new InvalidOperationException("Process was not created with redirected output.");

        public StreamReader StandardErrorReader => _standardError ?? throw new InvalidOperationException("Process was not created with live error output.");

        public void Kill(bool entireProcessTree) => SendFrame(KillRequest, ReadOnlySpan<byte>.Empty);

        public void WaitForInputIdle()
        {
        }

#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks
        public Task<int> WaitForExitAsync() => _exitTaskCompletionSource.Task;
#pragma warning restore VSTHRD003 // Avoid awaiting foreign Tasks

        public Task<int> WaitForExitAsync(CancellationToken token) => WaitForExitAsync().WithCancellation(token);

        public int WaitForExit() => ThreadHelper.JoinableTaskFactory.Run(WaitForExitAsync);

        public void Dispose()
        {
            lock (_sendLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                if (!_exited)
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        _exited = true;
                        OperationCanceledException ex = new("Process killed");
                        _logOperation.LogProcessEnd(ex);
                        _exitTaskCompletionSource.TrySetException(ex);
                    }
                    else
                    {
                        _exited = true;
                        _exitTaskCompletionSource.TrySetCanceled();
                    }
                }
            }

            // Closing the connection makes the daemon kill git if it still runs.
            try
            {
                _stream?.Dispose();
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }

            _logOperation.NotifyDisposed();
        }

        /// <summary>Write side of git's stdin: each write is one frame, closing sends EOF.</summary>
        private sealed class StdinStream(BridgeProcess owner) : Stream
        {
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (buffer.Length > 0)
                {
                    owner.SendFrame(StdinData, buffer);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    owner.SendFrame(StdinEof, ReadOnlySpan<byte>.Empty);
                }

                base.Dispose(disposing);
            }
        }
    }
}
