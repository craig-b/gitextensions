using System.Diagnostics;
using System.Text;
using GitCommands;
using GitCommands.Git.Extensions;
using GitCommands.Logging;
using GitExtensions.Extensibility;
using GitExtUtils;
using GitExtUtils.GitUI.Theming;
using GitUI.Theming;
using Microsoft;
using Timer = System.Windows.Forms.Timer;

namespace GitUI.ConsoleEmulation.PlainText;

/// <summary>
///  Displays redirected process output in an edit box when no embedded terminal is being used.
/// </summary>
public sealed class PlainTextConsoleCommandRunner : ContainerControl, IPlainTextConsoleCommandRunner
{
    private readonly RichTextBox _editbox;

    private Process? _process;

    private IBridgedConsoleProcess? _bridgedProcess;

    private Action? _logProcessKilled;

    private ProcessOutputThrottle? _outputThrottle;

    private StreamWriter? _input;

    public PlainTextConsoleCommandRunner()
    {
        _editbox = new RichTextBox
        {
            BackColor = Application.IsDarkModeEnabled ? AppColor.EditorBackground.GetThemeColor() : SystemColors.Info,
            ForeColor = Application.IsDarkModeEnabled ? AppColor.AnsiTerminalWhiteForeNormal.GetThemeColor() : SystemColors.WindowText,
            BorderStyle = BorderStyle.FixedSingle,
            Dock = DockStyle.Fill,
            Font = AppSettings.MonospaceFont,
            ReadOnly = true
        };
        _editbox.LinkClicked += editbox_LinkClicked;
        Controls.Add(_editbox);

        _outputThrottle = new ProcessOutputThrottle(AppendMessage);

        void AppendMessage(string text)
        {
            DebugHelpers.Assert(text is not null, "text is not null");
            if (IsDisposed)
            {
                return;
            }

            DebugHelpers.Assert(!InvokeRequired, "!InvokeRequired");

            _editbox.Visible = true;

            // Insert at the end with the box's text colour set on the insertion point. Setting ForeColor
            // on an empty rich edit reformats nothing under Wine's riched20 (SCF_ALL never updates the
            // default style there), so text streamed in later came out black on the dark background.
            _editbox.SelectionStart = _editbox.TextLength;
            _editbox.SelectionLength = 0;
            _editbox.SelectionColor = _editbox.ForeColor;
            _editbox.SelectedText = text;
            _editbox.SelectionStart = _editbox.TextLength;
            _editbox.ScrollToCaret();
        }
    }

    public Control Control => this;

    public event EventHandler<ConsoleOutputEventArgs>? CommandOutputReceived;

    public event EventHandler<ConsoleProcessExitEventArgs>? CommandProcessExited;

    // Editbox-based output never terminates independently; event is required by the interface.
#pragma warning disable CS0067
    public event EventHandler? ConsoleHostTerminated;
#pragma warning restore CS0067

    public void WriteOutputText(string text)
    {
        _outputThrottle?.Append(text);
    }

    public void WriteCommandProcessInput(string text)
    {
        Validates.NotNull(_input);
        _input.Write(text);
    }

    public void KillCommandProcess()
    {
        if (InvokeRequired)
        {
            throw new InvalidOperationException("This operation is to be executed on the home thread.");
        }

        if (_bridgedProcess is { } bridged)
        {
            _bridgedProcess = null;
            _logProcessKilled?.Invoke();
            _logProcessKilled = null;
            try
            {
                bridged.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }

            bridged.Dispose();
            _input = null;
            CommandProcessExited?.Invoke(this, new ConsoleProcessExitEventArgs(-1));
            return;
        }

        if (_process is null)
        {
            return;
        }

        _logProcessKilled?.Invoke();

        try
        {
            _process.TerminateTree();
        }
        catch (Exception ex)
        {
            Trace.WriteLine(ex);
        }

        _process.Dispose();
        _process = null;
        _input?.Dispose();
        _input = null;
        CommandProcessExited?.Invoke(this, new ConsoleProcessExitEventArgs(-1));
    }

    public void ResetConsole()
    {
        KillCommandProcess();
        _outputThrottle?.Clear();
        _editbox.Text = "";
        _editbox.Visible = false;
    }

    public void StartCommand(string command, string arguments, string workDir, Dictionary<string, string> envVariables)
    {
        WriteOutputText($"{command.Quote()} {arguments}{Environment.NewLine}");

        if (NativeGitBridge.IsEnabled && NativeGitBridge.IsBridged(command))
        {
            StartBridgedCommand(command, arguments, workDir, envVariables);
            return;
        }

        ProcessOperation operation = CommandLog.LogProcessStart(command, arguments, workDir);

        try
        {
            EnvironmentConfiguration.SetEnvironmentVariables();

            KillCommandProcess();

            _logProcessKilled = () => operation.LogProcessEnd(new Exception("Process killed"));

            // process used to execute external commands
            Encoding outputEncoding = GitModule.SystemEncoding;
            ProcessStartInfo startInfo = new()
            {
                UseShellExecute = false,
                ErrorDialog = false,
                CreateNoWindow = !AppSettings.ShowGitCommandLine,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = outputEncoding,
                StandardErrorEncoding = outputEncoding,
                FileName = command,
                Arguments = arguments,
                WorkingDirectory = workDir
            };

            foreach ((string name, string value) in envVariables)
            {
                startInfo.EnvironmentVariables.Add(name, value);
            }

            _process = new() { StartInfo = startInfo, EnableRaisingEvents = true };

            AsyncStreamReader? outputReader = null;
            AsyncStreamReader? errorReader = null;

            _process.Exited += delegate
            {
                ThreadHelper.FileAndForget(async () =>
                    {
                        if (_process is null)
                        {
                            await this.SwitchToMainThreadAsync();
                            operation.LogProcessEnd(new Exception("Process instance is null in Exited event"));
                            return;
                        }

                        // The process is exited already, but this command waits also until all output is received.
                        // Only WaitForExit when someone is connected to the exited event. For some reason a
                        // null reference is thrown sometimes when staging/unstaging in the commit dialog when
                        // we wait for exit, probably a timing issue...
                        try
                        {
                            // WaitForExit[Async] blocks here for unknown reason if the process has already exited
                            if (!_process.HasExited)
                            {
                                await _process.WaitForExitAsync();
                            }

                            _logProcessKilled = null;

                            if (_process is null)
                            {
                                // The process has been killed meanwhile.
                                return;
                            }
                        }
                        catch (Exception ex)
                        {
                            await this.SwitchToMainThreadAsync();
                            operation.LogProcessEnd(ex);
                        }

                        int exitCode = _process.ExitCode;

                        using CancellationTokenSource eofTimeoutTokenSource = new(millisecondsDelay: 5000);

                        if (outputReader is not null)
                        {
                            await outputReader.WaitUntilEofAsync(eofTimeoutTokenSource.Token);
                            outputReader.Dispose();
                        }

                        if (errorReader is not null)
                        {
                            await errorReader.WaitUntilEofAsync(eofTimeoutTokenSource.Token);
                            errorReader.Dispose();
                        }

                        await this.SwitchToMainThreadAsync();
                        operation.LogProcessEnd(exitCode);
                        WriteOutputText("Done");
                        _process.Dispose();
                        _process = null;
                        await _input!.DisposeAsync();
                        _input = null;
                        _outputThrottle?.Stop(flush: true);
                        CommandProcessExited?.Invoke(this, new ConsoleProcessExitEventArgs(exitCode));
                    });
            };

            _process.Start();
            operation.SetProcessId(_process.Id);
            _input = _process.StandardInput;
            outputReader = new AsyncStreamReader(_process.StandardOutput, ForwardOutput);
            errorReader = new AsyncStreamReader(_process.StandardError, ForwardOutput);
        }
        catch (Exception ex)
        {
            operation.LogProcessEnd(ex);
            ex.Data.Add("command", command);
            ex.Data.Add("arguments", arguments);
            throw;
        }
    }

    /// <summary>
    ///  Runs a command through the native git bridge (Wine): the process logs itself, output is streamed from the socket.
    /// </summary>
    private void StartBridgedCommand(string command, string arguments, string workDir, Dictionary<string, string> envVariables)
    {
        EnvironmentConfiguration.SetEnvironmentVariables();
        KillCommandProcess();

        IBridgedConsoleProcess process = NativeGitBridge.StartConsole(command, arguments, workDir, envVariables, GitModule.SystemEncoding);
        _bridgedProcess = process;
        _input = process.StandardInput;
        _logProcessKilled = null;
        AsyncStreamReader outputReader = new(process.StandardOutput, ForwardOutput);
        AsyncStreamReader errorReader = new(process.StandardErrorReader, ForwardOutput);

        ThreadHelper.FileAndForget(async () =>
        {
            int exitCode;
            try
            {
                exitCode = await process.WaitForExitAsync();
            }
            catch (OperationCanceledException)
            {
                // killed or reset; KillCommandProcess has reported the exit
                outputReader.Dispose();
                errorReader.Dispose();
                return;
            }
            catch (Exception ex)
            {
                WriteOutputText(ex.Message + Environment.NewLine);
                exitCode = -1;
            }

            using CancellationTokenSource eofTimeoutTokenSource = new(millisecondsDelay: 5000);
            await outputReader.WaitUntilEofAsync(eofTimeoutTokenSource.Token);
            outputReader.Dispose();
            await errorReader.WaitUntilEofAsync(eofTimeoutTokenSource.Token);
            errorReader.Dispose();

            await this.SwitchToMainThreadAsync();
            if (_bridgedProcess != process)
            {
                // killed meanwhile
                return;
            }

            WriteOutputText("Done");
            _bridgedProcess = null;
            _input = null;
            process.Dispose();
            _outputThrottle?.Stop(flush: true);
            CommandProcessExited?.Invoke(this, new ConsoleProcessExitEventArgs(exitCode));
        });
    }

    private void ForwardOutput(string output)
    {
        output = output.Replace("\r\n", "\n");

        for (int startIndex = 0; startIndex < output.Length;)
        {
            int nextLineStart = output.IndexOfAny(Delimiters.LineFeedAndCarriageReturnSearchValues, startIndex) + 1;
            if (nextLineStart == 0)
            {
                nextLineStart = output.Length;
            }

            string outputLine = output[startIndex..nextLineStart];
            CommandOutputReceived?.Invoke(this, new ConsoleOutputEventArgs(outputLine));

            startIndex = nextLineStart;
        }
    }

    protected override void Dispose(bool disposing)
    {
        KillCommandProcess();
        if (disposing)
        {
            _outputThrottle?.Dispose();
            _outputThrottle = null;
            _process?.Dispose();
            _process = null;
            _bridgedProcess?.Dispose();
            _bridgedProcess = null;
            _input?.Dispose();
            _input = null;
        }

        base.Dispose(disposing);
    }

    private void editbox_LinkClicked(object? sender, LinkClickedEventArgs e)
    {
        try
        {
            OsShellUtil.OpenUrlInDefaultBrowser(e.LinkText);
        }
        catch (Exception ex)
        {
            MessageBoxes.ShowError(this, ex.Message);
        }
    }

    #region ProcessOutputThrottle

    private sealed class ProcessOutputThrottle : IDisposable
    {
        private readonly Lock _textToAddLock = new();
        private readonly StringBuilder _textToAdd = new();
        private readonly Timer _timer;
        private readonly Action<string> _doOutput;

        /// <param name="doOutput">Will be called on the UI thread.</param>
        public ProcessOutputThrottle(Action<string> doOutput)
        {
            _doOutput = doOutput;

            _timer = new Timer { Interval = 1 };
            _timer.Tick += delegate { FlushOutput(); };
            _timer.Start();
        }

        public void Stop(bool flush)
        {
            if (flush)
            {
                FlushOutput();
            }

            _timer.Stop();
        }

        /// <remarks>Can be called on any thread.</remarks>
        public void Append(string text)
        {
            lock (_textToAddLock)
            {
                _textToAdd.Append(text);
            }
        }

        public void FlushOutput()
        {
            _timer.Stop();
            _timer.Interval = 100;
            _timer.Start();

            string textToAdd = "";
            lock (_textToAddLock)
            {
                if (_textToAdd.Length > 0)
                {
                    textToAdd = _textToAdd.ToString();
                    _textToAdd.Clear();
                }
            }

            if (textToAdd.Length > 0)
            {
                _doOutput?.Invoke(textToAdd);
            }
        }

        public void Clear()
        {
            lock (_textToAddLock)
            {
                _textToAdd.Clear();
            }
        }

        public void Dispose()
        {
            Stop(flush: false);

            // clear will lock, to prevent outputting to disposed object
            Clear();
            _timer.Dispose();
        }
    }

    #endregion
}
