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
// The app connects to 127.0.0.1:$GITEXT_GIT_BRIDGE_PORT once per git command and sends one
// JSON header line: {"token", "cwd", "args", "env", "stdin"}. Paths in cwd and args arrive in
// Windows form (Z:\var\...) and are translated through the prefix's dosdevices links. Frames
// follow, both ways, as <byte type><uint32 length> plus payload (little endian).
//
//   client -> daemon: 0 stdin data, 1 stdin EOF, 2 kill
//   daemon -> client: 1 stdout, 2 stderr, 3 exit (int32), 4 error text, 5 pid (int32)
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
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

[assembly: SupportedOSPlatform("linux")]

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
log.Write($"listening on {settings.Port} for parent {parent}{(detached ? "" : ", not detached")}");

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

/// <summary>What the launcher passes in the environment.</summary>
internal sealed record BridgeSettings(string WinePrefix, int Port, string Token, string Git, string? Editor, string? LogPath)
{
    public static BridgeSettings FromEnvironment()
        => new(
            WinePrefix: Require("WINEPREFIX"),
            Port: int.Parse(Require("GITEXT_GIT_BRIDGE_PORT")),
            Token: Require("GITEXT_GIT_BRIDGE_TOKEN"),
            Git: ResolveExecutable(Optional("GITEXT_GIT_BRIDGE_GIT") ?? "git"),
            Editor: Optional("GITEXT_GIT_BRIDGE_EDITOR"),
            LogPath: Optional("GITEXT_GIT_BRIDGE_LOG"));

    private static string Require(string name)
        => Optional(name) ?? throw new InvalidOperationException($"{name} is not set");

    private static string? Optional(string name)
        => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    /// <summary>Searches PATH once, so that every spawn does not; a name that is not found is left for exec to complain about.</summary>
    private static string ResolveExecutable(string name)
    {
        if (name.Contains('/'))
        {
            return name;
        }

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(dir, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return name;
    }
}

/// <summary>The request header line the app sends.</summary>
internal sealed record Request(string? Token, string? Cwd, string[]? Args, Dictionary<string, string>? Env, bool Stdin);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Request))]
internal sealed partial class BridgeJsonContext : JsonSerializerContext;

/// <summary>Frames the client sends.</summary>
internal enum Inbound : byte
{
    StdinData = 0,
    StdinEof = 1,
    Kill = 2,
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

            await RunGitAsync(request, reader);
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
        string? subcommand = OutputTranslation.Subcommand(args);
        if (subcommand is "mergetool" or "difftool")
        {
            // nobody can answer "Hit return to start merge resolution tool" over the bridge
            args.InsertRange(0, ["-c", $"{subcommand}.prompt=false"]);
        }

        log.Write($"run cwd={cwd} args=[{string.Join(", ", args)}]");

        if (cwd is not null && !Directory.Exists(cwd))
        {
            // what git itself says for -C on a missing directory; the app handles the exit code
            await SendFrameAsync(TextFrame(Outbound.Stderr, $"fatal: cannot change to '{cwd}': No such file or directory\n"));
            await SendFrameAsync(Int32Frame(Outbound.Exit, 128));
            return;
        }

        // when the daemon could not detach itself, setsid does it per git; it execs git in place, so the pid stays git's
        ProcessStartInfo startInfo = new(detached ? settings.Git : "setsid")
        {
            WorkingDirectory = cwd ?? Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (!detached)
        {
            startInfo.ArgumentList.Add(settings.Git);
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

        Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start");
        _process = process;
        if (!request.Stdin)
        {
            process.StandardInput.Close();
        }

        await SendFrameAsync(Int32Frame(Outbound.Pid, process.Id));

        Task feeder = FeedStdinAsync(reader, process, request.Stdin);
        await Task.WhenAll(
            PumpAsync(process.StandardOutput.BaseStream, Outbound.Stdout, OutputTranslation.For(args, subcommand, drives)),
            PumpAsync(process.StandardError.BaseStream, Outbound.Stderr, translate: null));
        await process.WaitForExitAsync();
        await SendFrameAsync(Int32Frame(Outbound.Exit, process.ExitCode));

        // nothing more can arrive that matters; the feeder is blocked in a read
        reader.CancelPendingRead();
        await feeder;
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

internal static partial class Libc
{
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
