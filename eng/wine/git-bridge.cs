#:property TargetFramework=net10.0
#:property UseWindowsForms=false
#:property EnableStyleCopAnalyzers=false
#:property EnableVisualStudioThreading=false
#:property GenerateDocumentationFile=false
#:property IsPublishable=true
#:property AllowUnsafeBlocks=true
#:property PublishAot=true
#:property InvariantGlobalization=true

// Runs native git on behalf of Git Extensions running under Wine.
//
// The daemon listens on 127.0.0.1, on GITEXT_GIT_BRIDGE_PORT or on a free port of its own choosing,
// and prints "<port> <token>" as its first line of output for the launcher to pass to the app; the
// token is GITEXT_GIT_BRIDGE_TOKEN or freshly generated. The app connects once per command and sends one JSON
// header line: {"token", "cwd", "program", "args", "env", "stdin"}. The program is git when
// absent, otherwise a name looked up on the daemon's PATH or a path; user tools and scripts
// come this way too. Paths in cwd, program and args arrive in Windows form (Z:\var\...) and are
// translated through the prefix's dosdevices links. Frames follow, both ways, as
// <byte type><uint32 length> plus payload (little endian).
//
//   client -> daemon: 0 stdin data, 1 stdin EOF, 2 kill, 3 resize (uint16 cols, uint16 rows)
//   daemon -> client: 1 stdout, 2 stderr, 3 exit (int32), 4 error text, 5 pid (int32)
//
// With "pty": true (plus "cols" and "rows") the program runs on a pseudo-terminal instead of
// pipes: stdin frames are keystrokes, stdout frames are the terminal's output, and a resize
// frame becomes TIOCSWINSZ. No program means the user's login shell. That is how the app's
// Console tab hosts a Linux shell.
//
// Closing the connection kills the git process. The daemon exits when its parent (the launcher)
// goes away. Build with `dotnet publish eng/wine/git-bridge.cs -o <dir>`; the result is a native
// Linux executable.

using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

[assembly: SupportedOSPlatform("linux")]

// A non-interactive shell starts a background job, which is what the launcher makes of us, with SIGINT and
// SIGQUIT ignored, and .NET hands children whatever it found at startup. Reset them first, before the runtime
// looks, so that Ctrl-C in a terminal session and a broken pipe in a git hook behave as they do anywhere else.
Libc.ResetSignalDispositions();

BridgeSettings settings = BridgeSettings.FromEnvironment();
DriveMap drives = DriveMap.FromPrefix(settings.WinePrefix);
Log log = new(settings.LogPath);
int parent = Libc.getppid();

// A session of our own, with no controlling terminal, so no git we start can prompt on the launcher's
// terminal. setsid fails only when we already lead a process group (a job of an interactive shell),
// and then every git gets the setsid prefix instead, at the cost of one more exec per command.
bool detached = Libc.setsid() != -1;

using CancellationTokenSource shutdown = new();
using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
using PosixSignalRegistration sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

TcpListener listener = new(IPAddress.Loopback, settings.Port);
listener.Start();
int port = ((IPEndPoint)listener.LocalEndpoint).Port;
log.Write($"listening on {port} for parent {parent}{(detached ? "" : ", not detached")}");

// the launcher reads this line and exports both for the app
Console.Out.WriteLine($"{port} {settings.Token}");
Console.Out.Flush();

_ = WatchParentAsync();

try
{
    while (true)
    {
        Socket client = await listener.AcceptSocketAsync(shutdown.Token);
        client.NoDelay = true;
        _ = Task.Run(() => new Session(client, settings, drives, log, detached).RunAsync(), CancellationToken.None);
    }
}
catch (OperationCanceledException)
{
    // shutting down
}
finally
{
    listener.Stop();
}

return 0;

void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    shutdown.Cancel();
}

async Task WatchParentAsync()
{
    using PeriodicTimer timer = new(TimeSpan.FromSeconds(3));
    while (await timer.WaitForNextTickAsync(shutdown.Token))
    {
        if (Libc.getppid() != parent)
        {
            shutdown.Cancel();
            return;
        }
    }
}

/// <summary>What the launcher passes in the environment; port 0 means any free port, and a missing token is generated.</summary>
internal sealed record BridgeSettings(string WinePrefix, int Port, string Token, string Git, string? Editor, string? LogPath)
{
    public static BridgeSettings FromEnvironment()
        => new(
            WinePrefix: Require("WINEPREFIX"),
            Port: Optional("GITEXT_GIT_BRIDGE_PORT") is { } port ? int.Parse(port) : 0,
            Token: Optional("GITEXT_GIT_BRIDGE_TOKEN") ?? Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)),
            Git: Programs.Resolve(Optional("GITEXT_GIT_BRIDGE_GIT") ?? "git") ?? "git",
            Editor: Optional("GITEXT_GIT_BRIDGE_EDITOR"),
            LogPath: Optional("GITEXT_GIT_BRIDGE_LOG"));

    private static string Require(string name)
        => Optional(name) ?? throw new InvalidOperationException($"{name} is not set");

    private static string? Optional(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}

internal static class Programs
{
    private static readonly string[] _path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A path as given, or a bare name found on PATH; null when there is no such program.</summary>
    public static string? Resolve(string program)
    {
        if (program.Contains('/'))
        {
            return File.Exists(program) ? program : null;
        }

        foreach (string dir in _path)
        {
            string candidate = Path.Combine(dir, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

/// <summary>The request header line the app sends.</summary>
internal sealed record Request(string? Token, string? Cwd, string? Program, string[]? Args, Dictionary<string, string>? Env, bool Stdin, bool Pty, ushort Cols, ushort Rows);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Request))]
internal sealed partial class BridgeJsonContext : JsonSerializerContext;

/// <summary>Frames the client sends.</summary>
internal enum Inbound : byte
{
    StdinData = 0,
    StdinEof = 1,
    Kill = 2,
    Resize = 3,
}

/// <summary>Frames the daemon sends.</summary>
internal enum Outbound : byte
{
    Stdout = 1,
    Stderr = 2,
    Exit = 3,
    Error = 4,
    Pid = 5,
}

internal sealed class Log(string? path)
{
    private readonly Lock _lock = new();

    public void Write(string message)
    {
        if (path is null)
        {
            return;
        }

        lock (_lock)
        {
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
        }
    }
}

/// <summary>Drive letters of the Wine prefix and the Unix roots they point at.</summary>
internal sealed partial class DriveMap
{
    /// <summary>Roots without their trailing slash, so the root of the filesystem is the empty string.</summary>
    private readonly Dictionary<char, string> _rootByLetter;

    /// <summary>Longest root first, so a nested drive wins over the one containing it when mapping back.</summary>
    private readonly (char Letter, byte[] Root)[] _byLength;

    private DriveMap(Dictionary<char, string> rootByLetter)
    {
        _rootByLetter = rootByLetter;
        _byLength = [.. rootByLetter
            .OrderByDescending(kv => kv.Value.Length)
            .Select(kv => (kv.Key, Encoding.UTF8.GetBytes(kv.Value)))];
    }

    public static DriveMap FromPrefix(string prefix)
    {
        Dictionary<char, string> roots = [];
        foreach (string link in Directory.EnumerateFileSystemEntries(Path.Combine(prefix, "dosdevices")))
        {
            if (Path.GetFileName(link) is [char letter, ':'] && char.IsAsciiLetter(letter) && Libc.RealPath(link) is { } root)
            {
                roots[char.ToLowerInvariant(letter)] = root.TrimEnd('/');
            }
        }

        return new DriveMap(roots);
    }

    /// <summary>Rewrites every X:\... or X:/... span in the string to its Unix form.</summary>
    public string ToUnix(string value)
    {
        if (!ContainsDrivePath(value))
        {
            return value;
        }

        string result = DrivePathRegex().Replace(
            value.Replace('\\', '/'),
            m => _rootByLetter.TryGetValue(char.ToLowerInvariant(m.ValueSpan[0]), out string? root) ? root + "/" : m.Value);
        return result.Replace("file:////", "file:///");
    }

    /// <summary>Writes an absolute Unix path back in drive form; a path under no drive is written as is.</summary>
    public void WriteAsWindows(ReadOnlySpan<byte> path, IBufferWriter<byte> output)
    {
        foreach ((char letter, byte[] root) in _byLength)
        {
            if (path.StartsWith(root) && (path.Length == root.Length || path[root.Length] == (byte)'/'))
            {
                output.Write([(byte)char.ToUpperInvariant(letter), (byte)':']);
                output.Write(path[root.Length..]);
                return;
            }
        }

        output.Write(path);
    }

    private bool ContainsDrivePath(string value)
    {
        foreach (ValueMatch m in DrivePathRegex().EnumerateMatches(value))
        {
            if (_rootByLetter.ContainsKey(char.ToLowerInvariant(value[m.Index])))
            {
                return true;
            }
        }

        return false;
    }

    // a drive-letter path anywhere in a token: not preceded by a letter or digit, a letter, a colon, then a separator
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/]")]
    private static partial Regex DrivePathRegex();
}

/// <summary>Rewrites the whole of a command's stdout into <paramref name="output"/>.</summary>
internal delegate void OutputTranslator(ReadOnlySpan<byte> input, IBufferWriter<byte> output);

/// <summary>Knows which git commands print absolute paths the app treats as paths, and maps them back to drive form.</summary>
internal static class OutputTranslation
{
    /// <summary>The git subcommand: the first token that is not an option or an option value.</summary>
    public static string? Subcommand(IReadOnlyList<string> args)
    {
        bool skip = false;
        foreach (string arg in args)
        {
            if (skip)
            {
                skip = false;
            }
            else if (arg is "-c" or "-C" or "--git-dir" or "--work-tree" or "--namespace" or "--exec-path" or "--config-env")
            {
                skip = true;
            }
            else if (!arg.StartsWith('-'))
            {
                return arg;
            }
        }

        return null;
    }

    /// <summary>A translator for the whole of stdout, or null when the command prints nothing the app treats as a path.</summary>
    public static OutputTranslator? For(IReadOnlyList<string> args, string? subcommand, DriveMap drives)
        => subcommand switch
        {
            "rev-parse" => (input, output) => TranslateFields(input, output, (byte)'\n', drives, leadingWordOnly: false),
            "worktree" when args.Contains("list") => (input, output) => TranslateFields(input, output, args.Contains("-z") ? (byte)'\0' : (byte)'\n', drives, leadingWordOnly: true),
            _ => null,
        };

    /// <summary>
    ///  Rewrites each separator-delimited field that starts with '/'. With <paramref name="leadingWordOnly"/> only the first
    ///  space-delimited word of such a field is a path, and a field of the form "worktree /path" holds one too.
    /// </summary>
    private static void TranslateFields(ReadOnlySpan<byte> input, IBufferWriter<byte> output, byte separator, DriveMap drives, bool leadingWordOnly)
    {
        ReadOnlySpan<byte> worktreeKeyword = "worktree "u8;
        bool first = true;
        foreach (Range range in input.Split(separator))
        {
            if (!first)
            {
                output.Write([separator]);
            }

            first = false;
            ReadOnlySpan<byte> field = input[range];
            if (leadingWordOnly && field.StartsWith(worktreeKeyword) && field[worktreeKeyword.Length..] is [(byte)'/', ..] path)
            {
                output.Write(worktreeKeyword);
                drives.WriteAsWindows(path, output);
            }
            else if (field is [(byte)'/', ..])
            {
                int wordEnd = leadingWordOnly ? field.IndexOf((byte)' ') : -1;
                if (wordEnd < 0)
                {
                    drives.WriteAsWindows(field, output);
                }
                else
                {
                    drives.WriteAsWindows(field[..wordEnd], output);
                    output.Write(field[wordEnd..]);
                }
            }
            else
            {
                output.Write(field);
            }
        }
    }
}

/// <summary>One connection: one git command.</summary>
internal sealed class Session(Socket socket, BridgeSettings settings, DriveMap drives, Log log, bool detached)
{
    private const int FrameHeaderSize = 5;

    /// <summary>The largest frame we send: a power of two, so the pooled buffer is exactly that size, and under the 85 KB large-object threshold. Also the pipe's own capacity, so one read drains it.</summary>
    private const int FrameSize = 64 * 1024;

    private readonly NetworkStream _stream = new(socket, ownsSocket: true);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Process? _process;

    public async Task RunAsync()
    {
        PipeReader reader = PipeReader.Create(_stream);
        try
        {
            Request? request = await ReadHeaderAsync(reader);
            if (request is null || request.Token != settings.Token)
            {
                return;
            }

            if (request.Pty)
            {
                await RunTerminalAsync(request, reader);
            }
            else
            {
                await RunGitAsync(request, reader);
            }
        }
        catch (Exception ex)
        {
            log.Write($"error {ex}");
            try
            {
                await SendFrameAsync(TextFrame(Outbound.Error, ex.Message));
            }
            catch (Exception)
            {
                // the client is gone; nobody to report to
            }
        }
        finally
        {
            Kill();
            await reader.CompleteAsync();
            await _stream.DisposeAsync();
        }
    }

    private async Task RunGitAsync(Request request, PipeReader reader)
    {
        string? cwd = request.Cwd is { Length: > 0 } ? drives.ToUnix(request.Cwd) : null;
        List<string> args = [.. (request.Args ?? []).Select(drives.ToUnix)];
        bool isGit = request.Program is null or "" or "git";
        string? subcommand = isGit ? OutputTranslation.Subcommand(args) : null;
        if (subcommand is "mergetool" or "difftool")
        {
            // nobody can answer "Hit return to start merge resolution tool" over the bridge
            args.InsertRange(0, ["-c", $"{subcommand}.prompt=false"]);
        }

        string? program = isGit ? settings.Git : Programs.Resolve(drives.ToUnix(request.Program!));
        log.Write($"run cwd={cwd} program={program ?? request.Program} args=[{string.Join(", ", args)}]");

        if (program is null)
        {
            // what a shell says; the app shows the text and handles the exit code
            await SendFrameAsync(TextFrame(Outbound.Stderr, $"{request.Program}: command not found\n"));
            await SendFrameAsync(Int32Frame(Outbound.Exit, 127));
            return;
        }

        if (cwd is not null && !Directory.Exists(cwd))
        {
            // what git itself says for -C on a missing directory; the app handles the exit code
            await SendFrameAsync(TextFrame(Outbound.Stderr, $"fatal: cannot change to '{cwd}': No such file or directory\n"));
            await SendFrameAsync(Int32Frame(Outbound.Exit, 128));
            return;
        }

        // when the daemon could not detach itself, setsid does it per git; it execs git in place, so the pid stays git's
        ProcessStartInfo startInfo = new(detached ? program : "setsid")
        {
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!detached)
        {
            startInfo.ArgumentList.Add(program);
        }

        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment["LC_MESSAGES"] = "C";
        if (settings.Editor is not null)
        {
            startInfo.Environment["GIT_EDITOR"] = settings.Editor;
            startInfo.Environment.Remove("GIT_SEQUENCE_EDITOR");
        }

        // the app's own variables win, e.g. the sed sequence editor that rewrites a rebase todo
        foreach ((string name, string value) in request.Env ?? [])
        {
            startInfo.Environment[name] = drives.ToUnix(value);
        }

        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{program} did not start");
        _process = process;
        if (!request.Stdin)
        {
            process.StandardInput.Close();
        }

        await SendFrameAsync(Int32Frame(Outbound.Pid, process.Id));

        Task feeder = FeedStdinAsync(reader, process, request.Stdin);
        await Task.WhenAll(
            PumpAsync(process.StandardOutput.BaseStream, Outbound.Stdout, isGit ? OutputTranslation.For(args, subcommand, drives) : null),
            PumpAsync(process.StandardError.BaseStream, Outbound.Stderr, translate: null));
        await process.WaitForExitAsync();
        await SendFrameAsync(Int32Frame(Outbound.Exit, process.ExitCode));

        // nothing more can arrive that matters; the feeder is blocked in a read
        reader.CancelPendingRead();
        await feeder;
    }

    /// <summary>
    ///  Runs a program, by default the user's login shell, on a pseudo-terminal and relays the terminal both ways.
    ///  The program is a session leader with the terminal as its controlling tty, so job control, signals and
    ///  window-size changes behave as in any terminal emulator; the client is expected to be one.
    /// </summary>
    private async Task RunTerminalAsync(Request request, PipeReader reader)
    {
        string? cwd = request.Cwd is { Length: > 0 } ? drives.ToUnix(request.Cwd) : null;
        if (cwd is not null && !Directory.Exists(cwd))
        {
            cwd = null;
        }

        string? program;
        List<string> args = [.. (request.Args ?? []).Select(drives.ToUnix)];
        bool isGit = request.Program == "git";
        if (string.IsNullOrEmpty(request.Program))
        {
            program = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell ? shell : "/bin/sh";
            if (args.Count == 0)
            {
                args.Add("-l");
            }
        }
        else if (isGit)
        {
            program = settings.Git;
            if (OutputTranslation.Subcommand(args) is ("mergetool" or "difftool") and string tool)
            {
                args.InsertRange(0, ["-c", $"{tool}.prompt=false"]);
            }
        }
        else
        {
            program = Programs.Resolve(drives.ToUnix(request.Program));
        }

        log.Write($"terminal cwd={cwd} program={program ?? request.Program} args=[{string.Join(", ", args)}] size={request.Cols}x{request.Rows}");

        if (program is null)
        {
            await SendFrameAsync(TextFrame(Outbound.Stdout, $"{request.Program}: command not found\r\n"));
            await SendFrameAsync(Int32Frame(Outbound.Exit, 127));
            return;
        }

        using Pty pty = Pty.Open(request.Cols is 0 ? (ushort)80 : request.Cols, request.Rows is 0 ? (ushort)24 : request.Rows);

        // setsid makes the program a session leader; opening the slave as its first terminal then makes that
        // terminal its controlling tty. The redirections happen in the child, where .NET cannot reach.
        ProcessStartInfo startInfo = new("setsid")
        {
            ArgumentList = { "sh", "-c", "p=$GITEXT_PTY; unset GITEXT_PTY; exec \"$0\" \"$@\" 0<>\"$p\" 1>&0 2>&0", program },
            WorkingDirectory = cwd ?? Environment.GetEnvironmentVariable("HOME") ?? Environment.CurrentDirectory,
            UseShellExecute = false,
        };
        foreach (string arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment["GITEXT_PTY"] = pty.SlavePath;
        startInfo.Environment["TERM"] = "xterm-256color";
        if (isGit)
        {
            // the app reads some of what git says in its progress dialogs; the user's own shell is left alone
            startInfo.Environment["LC_MESSAGES"] = "C";
        }

        foreach ((string name, string value) in request.Env ?? [])
        {
            startInfo.Environment[name] = drives.ToUnix(value);
        }

        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{program} did not start");
        _process = process;
        await SendFrameAsync(Int32Frame(Outbound.Pid, process.Id));

        using FileStream output = pty.OpenRead();
        using FileStream input = pty.OpenWrite();
        Task feeder = FeedTerminalAsync(reader, input, pty);
        Task pump = PumpTerminalAsync(output);
        Task exit = process.WaitForExitAsync();

        // the terminal closes when its last user goes (EIO on the master) or shortly after the program itself exits,
        // whichever is first; a background process that kept the slave does not keep the tab open
        await Task.WhenAny(pump, exit);
        if (!pump.IsCompleted)
        {
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromMilliseconds(200)));
        }

        pty.Dispose();
        await pump;
        await exit;
        await SendFrameAsync(Int32Frame(Outbound.Exit, process.ExitCode));

        reader.CancelPendingRead();
        await feeder;
    }

    /// <summary>The terminal's output, until the master reports EIO because its last user has gone, or is closed on exit.</summary>
    private async Task PumpTerminalAsync(FileStream output)
    {
        try
        {
            await PumpAsync(output, Outbound.Stdout, translate: null);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
        {
            // expected ends of a pseudo-terminal
        }
    }

    /// <summary>Keystrokes go to the terminal, resizes to the kernel; a kill frame or a dropped connection ends the session.</summary>
    private async Task FeedTerminalAsync(PipeReader reader, FileStream input, Pty pty)
    {
        byte[] size = new byte[4];
        try
        {
            while (true)
            {
                (Inbound kind, ReadOnlySequence<byte> payload) = await ReadFrameAsync(reader);
                try
                {
                    switch (kind)
                    {
                        case Inbound.StdinData:
                            foreach (ReadOnlyMemory<byte> segment in payload)
                            {
                                await input.WriteAsync(segment);
                            }

                            break;
                        case Inbound.Resize when payload.Length >= 4:
                            payload.Slice(0, 4).CopyTo(size);
                            pty.Resize(BinaryPrimitives.ReadUInt16LittleEndian(size), BinaryPrimitives.ReadUInt16LittleEndian(size.AsSpan(2)));
                            break;
                        case Inbound.Kill:
                            Kill();
                            break;
                    }
                }
                finally
                {
                    reader.AdvanceTo(payload.End);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // the session is over
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or SocketException or ObjectDisposedException)
        {
            // the terminal window went away: hang up
            Kill();
        }
    }

    /// <summary>Forwards a stream of git's output as frames, reading straight into the frame so each one is a single write.</summary>
    private async Task PumpAsync(Stream stream, Outbound kind, OutputTranslator? translate)
    {
        if (translate is not null)
        {
            using MemoryStream all = new();
            await stream.CopyToAsync(all, FrameSize);
            if (all.Length > 0)
            {
                ArrayBufferWriter<byte> frame = new((int)all.Length + 64);
                frame.Advance(FrameHeaderSize);
                translate(all.GetBuffer().AsSpan(0, (int)all.Length), frame);
                WriteHeader(MemoryMarshal.AsMemory(frame.WrittenMemory).Span, kind, frame.WrittenCount - FrameHeaderSize);
                await SendFrameAsync(frame.WrittenMemory);
            }

            return;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(FrameSize);
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(FrameHeaderSize, FrameSize - FrameHeaderSize))) > 0)
            {
                WriteHeader(buffer, kind, read);
                await SendFrameAsync(buffer.AsMemory(0, FrameHeaderSize + read));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Forwards stdin frames; a kill frame or a dropped connection kills git.</summary>
    private async Task FeedStdinAsync(PipeReader reader, Process process, bool wantStdin)
    {
        Stream? stdin = wantStdin ? process.StandardInput.BaseStream : null;
        try
        {
            while (true)
            {
                (Inbound kind, ReadOnlySequence<byte> payload) = await ReadFrameAsync(reader);
                try
                {
                    switch (kind)
                    {
                        case Inbound.StdinData when stdin is not null:
                            stdin = await WriteStdinAsync(stdin, payload);
                            break;
                        case Inbound.StdinEof:
                            stdin?.Close();
                            stdin = null;
                            break;
                        case Inbound.Kill:
                            Kill();
                            break;
                    }
                }
                finally
                {
                    reader.AdvanceTo(payload.End);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // the command is over
        }
        catch (Exception ex) when (ex is EndOfStreamException or IOException or SocketException or ObjectDisposedException)
        {
            // the client hung up: whatever git is still doing is of no interest to anyone
            Kill();
        }
    }

    /// <summary>Writes a frame to git's stdin; returns null once git has closed its end, after which the rest is dropped.</summary>
    private static async Task<Stream?> WriteStdinAsync(Stream stdin, ReadOnlySequence<byte> payload)
    {
        try
        {
            foreach (ReadOnlyMemory<byte> segment in payload)
            {
                await stdin.WriteAsync(segment);
            }

            await stdin.FlushAsync();
            return stdin;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task<Request?> ReadHeaderAsync(PipeReader reader)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;
            if (buffer.PositionOf((byte)'\n') is { } newline)
            {
                Request? request = ParseHeader(buffer.Slice(0, newline));
                reader.AdvanceTo(buffer.GetPosition(1, newline));
                return request;
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                return null;
            }
        }
    }

    private static Request? ParseHeader(ReadOnlySequence<byte> line)
    {
        Utf8JsonReader json = new(line);
        return JsonSerializer.Deserialize(ref json, BridgeJsonContext.Default.Request);
    }

    /// <summary>Reads one frame; the caller advances past <c>Payload.End</c> once it is done with the bytes.</summary>
    private static async Task<(Inbound Kind, ReadOnlySequence<byte> Payload)> ReadFrameAsync(PipeReader reader)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync();
            if (result.IsCanceled)
            {
                throw new OperationCanceledException();
            }

            ReadOnlySequence<byte> buffer = result.Buffer;
            if (TryParseFrame(buffer, out Inbound kind, out ReadOnlySequence<byte> payload))
            {
                return (kind, payload);
            }

            if (result.IsCompleted)
            {
                throw new EndOfStreamException();
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private static bool TryParseFrame(in ReadOnlySequence<byte> buffer, out Inbound kind, out ReadOnlySequence<byte> payload)
    {
        kind = default;
        payload = default;
        if (buffer.Length < FrameHeaderSize)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[FrameHeaderSize];
        buffer.Slice(0, FrameHeaderSize).CopyTo(header);
        long length = BinaryPrimitives.ReadUInt32LittleEndian(header[1..]);
        if (buffer.Length < FrameHeaderSize + length)
        {
            return false;
        }

        kind = (Inbound)header[0];
        payload = buffer.Slice(FrameHeaderSize, length);
        return true;
    }

    private static void WriteHeader(Span<byte> frame, Outbound kind, int payloadLength)
    {
        frame[0] = (byte)kind;
        BinaryPrimitives.WriteUInt32LittleEndian(frame[1..], (uint)payloadLength);
    }

    private static byte[] Int32Frame(Outbound kind, int value)
    {
        byte[] frame = new byte[FrameHeaderSize + sizeof(int)];
        WriteHeader(frame, kind, sizeof(int));
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(FrameHeaderSize), value);
        return frame;
    }

    private static byte[] TextFrame(Outbound kind, string text)
    {
        byte[] frame = new byte[FrameHeaderSize + Encoding.UTF8.GetByteCount(text)];
        WriteHeader(frame, kind, frame.Length - FrameHeaderSize);
        Encoding.UTF8.GetBytes(text, frame.AsSpan(FrameHeaderSize));
        return frame;
    }

    /// <summary>Writes a complete frame, header included, in one go; frames from the two pumps never interleave.</summary>
    private async Task SendFrameAsync(ReadOnlyMemory<byte> frame)
    {
        await _sendLock.WaitAsync();
        try
        {
            await _stream.WriteAsync(frame);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Kills git and everything it spawned, if it still runs.</summary>
    private void Kill()
    {
        if (_process is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // it exited in the meantime
            }
        }
    }
}

/// <summary>The master side of a pseudo-terminal; the slave is opened by the child by path.</summary>
internal sealed class Pty : IDisposable
{
    private readonly int _master;
    private bool _closed;

    private Pty(int master, string slavePath)
    {
        _master = master;
        SlavePath = slavePath;
    }

    public string SlavePath { get; }

    public static Pty Open(ushort cols, ushort rows)
    {
        int master = Libc.posix_openpt(Libc.O_RDWR | Libc.O_NOCTTY);
        if (master < 0 || Libc.grantpt(master) != 0 || Libc.unlockpt(master) != 0)
        {
            throw new IOException($"cannot open a pseudo-terminal: errno {Marshal.GetLastPInvokeError()}");
        }

        Span<byte> name = stackalloc byte[128];
        if (Libc.ptsname_r(master, name, (nuint)name.Length) != 0)
        {
            throw new IOException($"ptsname_r failed: errno {Marshal.GetLastPInvokeError()}");
        }

        Pty pty = new(master, Encoding.UTF8.GetString(name[..name.IndexOf((byte)0)]));
        pty.Resize(cols, rows);
        return pty;
    }

    public void Resize(ushort cols, ushort rows)
    {
        Libc.WinSize size = new() { Rows = rows, Cols = cols };
        _ = Libc.ioctl(_master, Libc.TIOCSWINSZ, ref size);
    }

    /// <summary>A read stream on the master; the handle stays ours, so disposing the terminal ends the reads with an error.</summary>
    public FileStream OpenRead()
        => new(new SafeFileHandle(_master, ownsHandle: false), FileAccess.Read, bufferSize: 0);

    public FileStream OpenWrite()
        => new(new SafeFileHandle(Libc.dup(_master), ownsHandle: true), FileAccess.Write, bufferSize: 0);

    public void Dispose()
    {
        if (!_closed)
        {
            _closed = true;
            _ = Libc.close(_master);
        }
    }
}

internal static partial class Libc
{
    public const int O_RDWR = 2;
    public const int O_NOCTTY = 0x100;
    public const nuint TIOCSWINSZ = 0x5414;

    [StructLayout(LayoutKind.Sequential)]
    public struct WinSize
    {
        public ushort Rows;
        public ushort Cols;
        public ushort XPixels;
        public ushort YPixels;
    }

    [LibraryImport("libc", SetLastError = true)]
    public static partial int posix_openpt(int flags);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int grantpt(int fd);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int unlockpt(int fd);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int ptsname_r(int fd, Span<byte> buffer, nuint length);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int ioctl(int fd, nuint request, ref WinSize size);

    [LibraryImport("libc")]
    public static partial int dup(int fd);

    private const int SIGHUP = 1;
    private const int SIGINT = 2;
    private const int SIGQUIT = 3;
    private const int SIGPIPE = 13;
    private const int SIGTERM = 15;

    public static void ResetSignalDispositions()
    {
        foreach (int signal in (ReadOnlySpan<int>)[SIGHUP, SIGINT, SIGQUIT, SIGPIPE, SIGTERM])
        {
            _ = Libc.signal(signal, 0);
        }
    }

    [LibraryImport("libc")]
    private static partial nint signal(int signal, nint handler);

    [LibraryImport("libc")]
    public static partial int close(int fd);

    [LibraryImport("libc")]
    public static partial int getppid();

    [LibraryImport("libc", SetLastError = true)]
    public static partial int setsid();

    /// <summary>The canonical path, with every symlink resolved, or null when it cannot be resolved.</summary>
    public static string? RealPath(string path)
    {
        nint resolved = realpath(path, 0);
        if (resolved == 0)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUTF8(resolved);
        }
        finally
        {
            free(resolved);
        }
    }

    [LibraryImport("libc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint realpath(string path, nint resolved);

    [LibraryImport("libc")]
    private static partial void free(nint ptr);
}
