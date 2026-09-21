# Running Git Extensions under Wine

Lessons learnt from getting the Windows build of Git Extensions running as a
day-to-day tool on Linux under Wine, including building it on Linux, fixing the
parts of Wine that get in the way, and making it fast enough to use.

Everything here was worked out against Git Extensions 7.2.x, Wine 11, .NET 10
and a KDE Wayland session. Paths below use `$HOME` placeholders; substitute
your own.

## 1. The crash that starts it all

The stock Windows build crashes on every window activation under Wine:

```
System.NotImplementedException: The method or operation is not implemented.
   at Microsoft.WindowsAPICodePack.Taskbar.ICustomDestinationList.SetAppID(String pszAppID)
   at ... WindowsJumpListManager.CreateJumpList(...)
   at ... FormBrowse.OnActivated(EventArgs e)
```

Wine's shell provides the taskbar COM objects but `ICustomDestinationList::SetAppID`
returns `E_NOTIMPL`, which the interop layer surfaces as `NotImplementedException`.
`WindowsJumpListManager.SafeInvoke` already swallows a list of "taskbar not
available" exception types collected from Windows bug reports; adding
`NotImplementedException` to that filter is the whole fix. Because the toolbar
buttons never get created, the retry fires on every activation, so without the
fix the bug-report dialog comes back each time the window gains focus.

There is no Wine-side fix short of patching Wine, and no application setting
that turns jump lists off.

## 2. Building the Windows app on Linux

The full release pipeline needs WiX, `vswhere` and MSVC, so a complete portable
zip cannot be produced on Linux. A plain build of the managed code can:

```
dotnet build GitExtensions.slnx -c Release -p:EnableWindowsTargeting=true -p:NuGetAudit=false
```

Things that bite:

- **Resource file case.** Several `.resx` entries reference icons with a
  different letter case than the file on disk (`Branch.png` vs `branch.png`,
  and one whole directory as `resources/icons`). Windows does not care, Linux
  does. Fix the `.resx` references to the on-disk spelling.
- **Version stamping.** The tree carries placeholder versions (`33.33.33`
  assembly version, `1.0.0` package version). CI stamps the real version with
  `eng/set_version_to.cs`. If you intend to mix your DLLs with files from an
  official release, stamp the same version the release used; .NET refuses to
  load an assembly older than the one a reference was compiled against.
  `dotnet run set_version_to.cs` trips over the NuGet audit like the build
  does; set `NuGetAudit=false` in the environment for it.
- **The apphost needs a runtime identifier.** A plain build produces a Linux
  apphost named `GitExtensions` next to the DLL, not `GitExtensions.exe`.
  Building with `-r win-x64 --self-contained false` (and
  `-p:AppendRuntimeIdentifierToOutputPath=false` to keep the output path)
  fetches the Windows host pack and produces a proper `GitExtensions.exe` on
  Linux, with the icon, version resource and manifest embedded; the SDK's
  resource updater is managed code and runs anywhere.
- **`dotnet publish` produces the portable archive on Linux.** With the two
  changes above in the tree, `dotnet publish GitExtensions.slnx -c Release
  --no-build -p:EnableWindowsTargeting=true` after the build runs the same
  publish targets as the Windows pipeline and writes
  `artifacts/Release/publish/GitExtensions-Portable-x64-<version>-<commit>.zip`:
  the app with its own executables, plugins, the plugin manager, translations,
  ConEmu and the portable flag set. The plugin manager step and the runtime
  config patch are file-based C# scripts in `eng`, run with `dotnet run`, in
  place of the PowerShell they were; the installer's manifest check and the MSI
  itself run on Windows only. The plugin manager download uses the GitHub API,
  which allows sixty anonymous requests an hour; set `GITHUB_TOKEN` when that
  bites. What the archive lacks against the official one is the shell
  extension and the SSH askpass helper, neither of which does anything under
  Wine.
- **The native code needs ATL.** The shell extension and the SSH askpass
  helper are C++ projects, and what stops them building here is not the
  compiler, MinGW and clang can both target Windows, but ATL, which ships only
  with Visual Studio. Until they are rewritten in plain COM, take the
  `GitExtensionsShellEx*.dll` files (and `BugReporter.exe` and the `ConEmu`
  folder) from the official portable zip of the same version and overlay the
  managed build output on top. The apphost only launches `GitExtensions.dll`,
  so it does not care that the DLL was rebuilt.
- **Plugins layout.** The published zip flattens plugins; the dev build keeps
  each plugin in its own folder with its dependencies and rewrites the probing
  path in `GitExtensions.dll.config` to match. Use the dev layout wholesale and
  drop the zip's `Plugins` folder rather than merging the two.
- **Portable mode.** The dev build ships `GitExtensions.dll.config` with
  `IsPortable` set to `False`; the release flips it to `True` during publish.
  With it off, the app silently reads and writes settings under the Wine
  user's AppData and ignores the `GitExtensions.settings` next to the exe.
  Every overlay of a fresh build resets this flag, so flip it back each time.

## 3. The Wine prefix

- **.NET desktop runtime.** The official installer works with
  `wine windowsdesktop-runtime-<ver>-win-x64.exe /install /quiet /norestart`.
- **Hide the Linux .NET.** The Windows apphost reads `DOTNET_ROOT` from the
  environment and, under Wine, happily follows a Linux path into
  `Z:\usr\share\dotnet` and fails to find `hostfxr.dll`. Unset every `DOTNET_*`
  variable in the launcher.
- **Never build the Wine command line with `eval`.** Backslashes in `Z:\...`
  paths get eaten. Rebuild the positional parameters instead. Likewise, `echo`
  in zsh interprets `\v` and friends, which makes a correct path look mangled
  when you print it; use `printf '%s'` when checking.
- **Scrub editor variables.** Wine passes the whole Linux environment to
  every Windows process, and git prefers `GIT_EDITOR` and `VISUAL` over the
  `EDITOR` the app sets. A session `EDITOR=nano` does not exist in the prefix
  and fails; a `GIT_EDITOR=true` left by tooling returns instantly, so an
  interactive rebase runs with an unedited todo list and looks as if the
  dialog never waited. Unset `GIT_EDITOR`, `GIT_SEQUENCE_EDITOR`, `VISUAL`
  and `EDITOR` in the launcher and set `core.editor` in the prefix's global
  git config to `"<app dir>/GitExtensions.exe" fileeditor`.
- **Missing glyphs show as boxes.** The app's font is the system message font,
  which under Wine is Wine's own Tahoma, and that font lacks whole blocks:
  the ahead/behind arrows on branch labels come out as empty rectangles.
  Windows would borrow the glyph from another font through font linking;
  Wine implements the same mechanism, but its default chain for every font
  is Microsoft Sans Serif, Tahoma and the East Asian fonts the registry
  links to those, none of which the prefix has. Point the link at a font
  fontconfig provides and every font in the app inherits it, while the UI
  font itself stays as it was:

  ```
  wine reg add 'HKLM\Software\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink' \
    /v Tahoma /t REG_MULTI_SZ /d 'DejaVuSans.ttf,DejaVu Sans' /f
  ```

  `WINEDEBUG=+font wine cmd /c exit` traces `load_system_links` and shows
  whether the entry resolved to a file. Setting the application font to
  DejaVu Sans in the app's settings also shows the arrows, but changes the
  look of the whole grid.
- **Consolas does not exist, and its stand-in is not monospace.** The app
  defaults the console font, the diff font and the monospace font to Consolas.
  Wine substitutes whatever it finds, which is a proportional face: GDI then
  measures a 5-pixel average cell while the glyphs drawn are 9 pixels wide.
  ConEmu believes the console has hundreds of columns and squeezes every
  glyph into the cell, which is the "smushed" text in the Console tab and, in
  the past, in the progress dialogs it hosted. Give Consolas a real monospace
  substitute in the prefix and every request for it agrees with itself:

  ```
  wine reg add 'HKLM\Software\Microsoft\Windows NT\CurrentVersion\FontSubstitutes' \
    /v Consolas /t REG_SZ /d 'Hack' /f
  ```

  Hack over DejaVu Sans Mono because it ships the Powerline glyphs (U+E0A0
  to U+E0B3) that shell prompts draw their branch symbol and separators with;
  with DejaVu the branch symbol is a box. Use DejaVu Sans Mono when Hack is
  not installed, and put whichever you choose in `fonts.conf`. Pick a
  different console font in Settings, Console style, if you prefer one; the
  substitute only covers the default. Nerd Font icons are a different matter:
  no font here has them, so a prompt that uses them shows boxes in every
  terminal on this machine, not only under Wine.
- **Existing installs elsewhere.** A copy installed through Bottles or another
  prefix has its own prefix, config and menu entry. Nothing configured in your
  prefix applies to it, and the two look identical in a window list.

## 4. Git inside the prefix

With the bridge (section 8) the prefix needs no git at all: every git call,
the Console tab, the patch commands, user scripts and tools run the Linux
programs. The `gitcommand` setting can simply be `git`; the app resolves it
by name through the daemon, and the settings checklist knows that `sh` is the
Linux one. The notes below apply only when running without the bridge
(`GITEXT_GIT_BRIDGE=0`), which then needs a git in the prefix.

- **Use the BusyBox flavour of MinGit.** The msys flavour's `sh.exe`
  segfaults under Wine every time git runs `git-difftool--helper` or
  `git-mergetool--lib` (msys fork emulation does not survive Wine). Simple
  `!alias` shell commands work, which is misleading. The BusyBox variant's ash
  runs those helpers fine. It still ships the msys OpenSSH, which works.
- **MinGit's system config includes itself.** `etc/gitconfig` ends with an
  `[include]` of `C:/Program Files/Git/etc/gitconfig` to inherit a full Git for
  Windows install's settings. Installed at that exact path, it includes itself
  and every git command dies with "exceeded maximum include depth". Strip the
  include block. Set `core.autocrlf=false` there too if your repositories are
  Linux checkouts.
- **Git Extensions' "Linux tools (sh)" check** looks for `usr\bin\sh.exe`.
  The BusyBox variant has no such file; a copy of `busybox.exe` named `sh.exe`
  satisfies it and works, since BusyBox picks its applet from the exe name.
- **The Console tab starts in the home directory.** The bash shell descriptor
  finds no `bash.exe` and runs that `sh.exe` copy with `--login -i`. BusyBox's
  ash changes to `$HOME` whenever it is a login shell, before it reads any
  profile, so the tab opens at `C:/users/<you>` (shown as `~`) instead of the
  repository ConEmu was told to start in. Git for Windows' real bash keeps the
  directory. ConEmu exports the start folder as `ConEmuWorkDir` (BusyBox shows
  it upper-cased), so a `.profile` in the prefix home undoes the move:

  ```sh
  _d="${ConEmuWorkDir:-$CONEMUWORKDIR}"
  if [ -n "$_d" ] && [ -d "$_d" ]; then
    cd "$_d"
  fi
  unset _d
  ```
- **The Console tab shows the previous tab after a window switch.** Wine
  gives a child window owned by another process no back buffer: ConEmu paints
  straight into the app's top-level X window, and the app's own buffer still
  holds whatever it last drew there. Activation changes repaint the grid and
  the status controls, and the flush of that dirty span copies the stale
  buffer over the terminal until a click makes ConEmu paint again. Fixed in
  code: `FormBrowse` asks the hosted window to repaint a few times in the
  second after activation or deactivation (`HostedTerminalRepaint`, Wine only).
- **Finding git.** Git Extensions checks the configured `gitcommand` and the
  `PATH` at startup, and only probes `Program Files\Git` when you press
  "Find git". Seed `gitcommand` and `gitbindir` in the portable settings file
  and put `Git\cmd` and `Git\usr\bin` on `WINEPATH` in the launcher.
- **Identity.** The Wine user has no global git config, so the checklist
  flags the missing name and email until you set them in the prefix.

## 5. The settings checklist

- **Shell extension.** `wine regsvr32 GitExtensionsShellEx64.dll` (and the
  32-bit one) succeeds and turns the row green. Nothing uses it under Wine.
- **Diff tool.** The checklist and the settings page read `diff.guitool`, not
  `diff.tool`. Set both.
- **Tool commands must start with an `.exe`.** The resolve-conflicts dialog
  does not go through `git mergetool`. It splits `mergetool.<tool>.cmd` at the
  first `.exe`, runs that path directly with the rest as arguments, and falls
  back to the configured `path` if there is no `.exe`. A shell-script wrapper
  as the path gives "Error starting mergetool". Prefix the command with
  `"C:/Program Files/Git/usr/bin/sh.exe"` and the wrapper works for both the
  dialog and `git mergetool`.
- **Telemetry, language, default clone destination** can all be pre-seeded in
  `GitExtensions.settings` (`TelemetryEnabled`, `translation`,
  `defaultclonedestinationpath`). The keys are case-sensitive and mostly
  lower-case; check `AppSettings.cs` for the exact string.

## 6. Native Linux tools

With the git bridge from section 8, `difftool`, `mergetool` and the app's own
diff and blame run through native git, which launches native tools directly:
no `start.exe`, no marker files, no path conversion. The tool comes from your
Linux git configuration (`merge.guitool`, then `merge.tool`, then git's own
candidate list), not from the prefix's, so the app's settings pages show no
tool until `merge.tool`, `diff.tool` and their `guitool` twins are set in your
Linux global config. The conflicts dialog hands the file to `git mergetool`
rather than running the tool itself, so git stages the result when the tool
reports success. Repository hooks run under your Linux shell for the same
reason.

Before the bridge, the Windows git had to reach a Linux program by itself,
through a wrapper script in the prefix's git config. That plumbing is retired,
but the facts are worth keeping for the next time a Windows-side script needs
a Linux program:

- **Neither msys nor BusyBox can exec a Linux binary.** Both check the file
  header and refuse with "Exec format error". Wine's own loader can
  (`wine /usr/bin/true` works), and so can `start.exe /unix <path> [args]`,
  which any Windows-side script can call.
- **`start /wait` does not wait for Unix children.** It returns at once, git
  thinks the tool finished, deletes its temp files, and the tool exits quietly
  on missing inputs. Have the Linux side write a marker file with its exit
  code when done and make the Windows-side script poll for it.
- **Paths.** With msys, `MSYS2_ARG_CONV_EXCL` controls which arguments get
  converted to Windows form; exempt `/unix`, `/wait` and the Linux script path
  and let it convert the files. With BusyBox nothing is converted, and git
  hands over `C:\...` and repo-relative paths. On the Linux side convert
  `X:...` arguments with `winepath -u` and leave relative ones alone.
- **Working directory.** The Unix child does not inherit git's working
  directory. Pass it explicitly and `cd` to it, or relative paths from
  `git mergetool` break.
- **Environment.** The Unix child inherits the Linux environment Wine was
  started with, so `DISPLAY`, `WAYLAND_DISPLAY`, `HOME` and `DBUS_SESSION_BUS_ADDRESS`
  are all present and GUI tools launch normally.

## 7. ListView quirks

Wine's comctl32 is missing two things the dashboard's recent-repositories
list relies on. Both are worked around in code, gated on a Wine check
(`ntdll.dll` exports `wine_get_version`; Windows never does):

- **No tile view.** Tiles collapse to icon size and hit-testing fails, so
  clicks select nothing. Under Wine the list runs as a single-column details
  view with the same owner drawing. Row height comes from a placeholder small
  image list. That image list must hold as many (blank) entries as the real
  icon list, because `ListViewItem.ImageIndex` reports `-1` once the index is
  beyond the assigned list, and the tile renderer indexes with it.
- **Wine paints over owner drawing in details view.** WinForms answers the
  item pre-paint stage with `CDRF_NOTIFYSUBITEMDRAW` and expects sub-item
  notifications it can answer with `CDRF_SKIPDEFAULT`. Wine sends the item
  stage once per column and no sub-item stages, then paints the plain text.
  Returning `CDRF_SKIPDEFAULT` from the item stage yourself (a `WndProc`
  override on the reflected `WM_NOTIFY`) keeps it out.
- **No `NM_CLICK`.** WinForms raises `ListView.MouseClick` and `Click` from
  the native `NM_CLICK` notification, which Wine never sends. `MouseDown`,
  `MouseUp`, `SelectedIndexChanged` and `ItemActivate` all arrive. Open the
  repository from `ItemActivate` (the list is already set to one-click
  activation).

Neither the 5.80 comctl32 that winetricks can fetch (it predates tile view)
nor Wine's own is a fix; only a comctl32 6 taken from a real Windows install
would be, with its own problems.

## 8. Performance

The lag is process creation, not the filesystem. Every git command is a new
Windows process, and on the machine this was measured on Wine takes 0.16 s to
start a process that imports only `kernel32`, and 0.29 s for anything that
imports `user32`, which includes `git.exe` and `cmd.exe`. Native spawn is
under a millisecond. Git Extensions runs one or more git commands per click.

The fix is to not start a Windows process at all. The app routes every git
call through `IExecutable`, so under Wine that seam is swapped for a socket
client (`NativeGitBridge` in GitCommands) that talks to a small daemon on the
Linux side, `eng/wine/git-bridge.cs`, which runs native git. The daemon is a
single-file .NET 10 program published as a native AOT executable
(`dotnet publish eng/wine/git-bridge.cs -o <dir>`), so it needs no runtime and
starts in a few milliseconds. One git command is then a localhost round trip
of a few milliseconds instead of a 0.3 s process start; the nine commands the
app fires when it opens a repository took 56 ms together.

- **Wiring.** The launcher starts the daemon with its output on a pipe; the
  daemon binds a free port, generates a token, and prints both as its first
  line. The launcher reads that line, exports `GITEXT_GIT_BRIDGE_PORT` and
  `GITEXT_GIT_BRIDGE_TOKEN` for the app, runs it, and kills the daemon
  afterwards. Setting either variable before the daemon starts makes it use
  that value instead. The daemon also exits when its
  parent goes away. The daemon puts itself in its own session at startup, so
  no git it runs has a controlling terminal to prompt on, and a kill takes
  git's whole process tree with it. The app bridges only the git executable, only when it would
  not create a console window, and only when both variables are set;
  `GITEXT_GIT_BRIDGE=0` in the launcher's environment turns it off.
- **Protocol.** One JSON header line with the token, working directory,
  argument list, forwarded environment and whether stdin follows, then framed
  bytes both ways (a type byte plus a little-endian length). The argument
  string is split with `CommandLineToArgvW`, the rules git.exe would have
  applied. Closing the connection kills the process. The header names a
  program only when it is not git; the daemon looks a bare name up on the
  Linux `PATH` and reports `command not found` with exit code 127 like a
  shell when there is none.
- **Everything else is native too.** Under the bridge the app treats any
  program as a Linux program unless its name ends in `.exe`, `.bat`, `.cmd`
  or `.com`. User scripts, external tools, `gitk` and `git gui` therefore run
  on the Linux side with the same path translation as git; write them as you
  would on Linux, with bare command names or `Z:` paths. Programs that open
  their own window need no console, so the "run in console" restriction below
  applies to git only.
- **Paths.** The daemon rewrites every `X:\\...` or `X:/...` span in the
  working directory, the arguments and the forwarded environment values
  through the prefix's `dosdevices` links, including `--opt=X:\\...`,
  `-c key=X:\\...` and `file:///X:/...`. Coming back, absolute paths in the
  output of `rev-parse` and `worktree list` (both the `-z` and the line form)
  are mapped to `Z:/...` form, which is what the Windows git printed. Nothing
  else in git's output is translated; relative paths pass through untouched.
- **Environment.** `GIT_*` and `DFT_*` variables set by the app are forwarded,
  except those that carry Windows paths (`GIT_SSH`, `GIT_EDITOR`,
  `GIT_SEQUENCE_EDITOR`, `GIT_ASKPASS`, `GIT_EXEC_PATH`). `HOME` is not, so
  native git reads your Linux configuration, identity and credential helpers.
  The daemon sets `GIT_EDITOR` to `git-bridge-editor`, which converts the file
  path and runs the app's own editor under Wine, and `LC_MESSAGES=C` so the
  messages the app parses stay in English.
- **Progress dialogs.** With the console emulator on, `FormProcess` hosts
  the command in ConEmu as it does on Windows, through the terminal relay
  described below, so the command runs on a real pseudo-terminal: a terminal
  sequence editor such as the interactive rebase tool, a terminal commit
  editor and coloured progress all work, and the relay forwards the dialog's
  own `GIT_*` variables, such as the sed expression that rewrites a rebase todo
  for Edit commit. Git in a dialog keeps `LC_MESSAGES=C` because the app reads
  some of what it says; the editors come from your Linux git configuration,
  not the app's. With the emulator off, or without the relay, the plain-text
  runner streams stdout and stderr from the socket instead, and the daemon's
  own editor default applies there.
- **Tools with windows.** The app gives `mergetool` and `difftool` a console
  window; the bridge takes them anyway, since the tools open their own windows
  and native git then launches native tools. The daemon adds
  `-c mergetool.prompt=false` because nobody can answer "Hit return to start
  merge resolution tool" over a socket.
- **Stage and unstage by patch.** `add --patch` and `checkout -p` need
  something to ask their questions on. Under the bridge the process dialog is
  that something: the hunks and prompts stream into it and the answer goes
  into its input line, with single-key reading and colours turned off because
  a pipe is not a terminal. `e` opens the hunk in the app's own editor through
  the bridge's `GIT_EDITOR`.
- **The Console tab is a Linux terminal.** ConEmu can only host a Windows
  program, so it hosts `bridge-tty.exe` (`eng/wine/bridge-tty.c`, built with
  MinGW by the refresh script), which asks the daemon for a pseudo-terminal
  session running your login shell in the repository directory and is then a
  wire: terminal output goes to the console untouched with virtual terminal
  processing on, key events become the byte sequences an xterm sends, and
  console resizes become `TIOCSWINSZ`. Your prompt, dotfiles, SSH agent and
  credential helpers are all there, and `git` in the tab is native git. The
  launcher passes the relay's path in `GITEXT_GIT_BRIDGE_TTY`; without it the
  tab falls back to the BusyBox shell. The daemon resets the signal
  dispositions it inherits from the launcher's background job, or Ctrl+C
  would be ignored in the session.
- **Full-screen programs pause on exit.** nano, less and vim switch to the
  alternate screen, which ConEmu implements by dumping the whole console
  buffer and writing it back when the program quits. ConEmu's code default
  for that buffer is 32,766 rows and the copy took about three seconds under
  Wine. The app sets the buffer to 1000 rows under Wine, ConEmu's own dialog
  default, which keeps the switch below notice and still leaves a thousand
  lines of scrollback.
- **What still runs the Windows git.** `notes edit` with a foreign editor,
  and anything you configure with an explicit Windows extension. Those never write the index in normal use, so the tree is in
  practice owned by native git. Do not commit files with the execute bit: the
  Windows git cannot see it and reports them as modified. Direct `CreateProcess` of a Linux binary is not an alternative:
  Wine's `fork_and_exec` in `ntdll/unix/process.c` returns no process handle,
  closes stdin and stdout when the parent has no console or passes
  `CREATE_NO_WINDOW`, never wires stderr, and execs with the Linux environment.

What still helped, for the Windows git that remains and for running
without the bridge, in order of effect:

- **Turn off the background git calls.** `showgitstatusinbrowsetoolbar`,
  `showgitstatusforartificialcommits` and `showaheadbehinddata` each fire git
  on focus changes and file-watcher events, and a `git status` walk through
  Wine's filesystem layer costs about a second more. Over the bridge each is
  a few milliseconds and the walk runs in native git, so leave them on; set
  them to `false` only when running without the bridge. The
  uncommitted-change counts on the working-directory rows then go away; the
  rows still work on demand.
- **Fonts.** Every Windows process enumerates fonts at startup. With a full
  Noto installation that is about 2,300 file opens plus a registry value read
  per font per process, and the prefix registers all of them in
  `HKCU\Software\Wine\Fonts\External Fonts` and the system font key. Give
  Wine a minimal fontconfig file through `FONTCONFIG_FILE` in the launcher
  and reset those registry keys once so it re-registers only that set. Worth
  about ten percent per spawn; not the main cost.
- **CPU power profile.** A powersave profile stretches short bursts. The
  performance profile was worth about twenty percent per spawn.
- **Not worth doing:** `WINEESYNC`, `WINEFSYNC`, the kernel's ntsync driver,
  a tmpfs prefix, or git's `core.fscache`. None moved the numbers.

## 9. Remotes and credentials

Fetch, pull and push run through native git (section 8) with your Linux
credential helpers, SSH keys and known hosts, and so do all the other
progress dialogs: rebase, merge, cherry-pick, checkout, reset and the rest of
`FormProcess`. Under the bridge those dialogs use the plain-text output view
instead of the console emulator, because a bridged process has no console to
host; the emulator setting is ignored. The notes below apply only to the
Windows git, which now runs only in the Console tab and for the interactive
patch commands.

- **Git Credential Manager does not work.** MinGit's system config sets
  `credential.helper=manager`, which talks to the Windows credential store.
  Under Wine that fails with "Failed to enumerate credentials [0x3ec]" and
  git falls back to a username prompt it cannot show. Override the helper in
  the prefix's global config: an empty first `credential.helper` entry resets
  the list, then add one that works, such as a script that answers with a
  token from a file on the Linux side.
- **SSH works.** The msys `ssh.exe` that MinGit ships runs under Wine and
  reads a key straight off the Z: drive with `core.sshCommand` set to
  `ssh -i Z:/path/to/key`. Known hosts live in the Wine user's profile, not
  your Linux `~/.ssh`.

## 10. Diagnostics that actually work

- **Window listing on Wayland.** Wine windows are X11 clients under XWayland.
  `xprop -root _NET_CLIENT_LIST` lists them and `xprop -id <w> WM_NAME
  WM_CLASS _NET_WM_PID` gives the title, class and owning process. Map
  windows to process ids before drawing conclusions; two copies of the app
  share a class name.
- **Screenshots.** `spectacle -b -n -o file.png` captures the current screen
  without a dialog. Poll window titles and capture on change rather than
  sleeping a fixed time; dialogs otherwise sit unseen.
- **Crashes.** `coredumpctl list` shows `wine-preloader` segfaults with the
  Windows command line, which is how the msys `sh` crashes were pinned down.
- **Wine noise.** `fixme:file:NtSetInformationFile unsupported flags: 0x3` is
  .NET deleting files with POSIX semantics; Wine ignores the flag and deletes
  normally. Harmless. `WINEDEBUG=-all` in the launcher hides it.
- **Registry reads are wineserver round trips.** `strace -f -c` on a process
  start shows six-figure syscall counts; most are `rt_sigprocmask`, `read`
  and `write` around server calls. Large registry keys read at startup, such
  as the font list, show up here.
- **`grep` may be `ugrep`.** It behaves differently on binary input such as
  `/proc/<pid>/cmdline`. Match on `WM_CLASS` output instead.

## 11. What is not there, and a smoke checklist

Everything below was found by sweeping every place the app shells out
(2026-09-19) and testing what could be tested without the GUI. The prefix is
the deployment; keep this table current when it changes.

| Feature | Status under Wine | Why |
|---|---|---|
| Edit commit, Reword | works | POSIX sed expression in code (BusyBox sed ignores GNU `0,/re/`) |
| Git LFS | works | native `git-lfs` through the bridge, in the Console tab too |
| Open, Open with... | works | routed through `winebrowser` to `xdg-open` in code (Wine only) |
| Copy path(s) | works | "native" is the Linux path in code (Wine only): drive letters resolved through `winepath -u`, once per drive; the Windows form is its own item; WSL and Cygwin are hidden |
| Show in folder | expected to work | Wine's explorer implements `/select,` |
| Batch user scripts (`cmd`) | works | Wine cmd handles the generated `.cmd` |
| Git GUI, GitK (Tools menu) | work | native `gitk` and `git gui` through the bridge; they need Tk on the Linux side (git's optional dependency) |
| User scripts and external tools | run natively | a program without a Windows extension is a Linux program to the bridge; arguments with `Z:` paths are translated |
| Stage / unstage by patch (context menu) | work | native git in the process dialog, answers typed into its input line (section 8) |
| Console tab | Linux login shell | the terminal relay over the bridge (section 8); the BusyBox shell notes in section 4 apply only without it |
| Edit notes | works | native git with the app's editor through the bridge |
| Tools, Linux terminal (Git bash on Windows) | works | opens your terminal emulator in the repository through `open-terminal` next to the relay: `TERMINAL`, then `xdg-terminal-exec`, then common emulators |
| Tools, PuTTY | hidden | SSH is the Linux one under the bridge |
| GPG tab on signed commits | expected to work | native git finds the Linux `gpg` through the bridge |
| PowerShell user scripts | silently do nothing | Wine's `powershell.exe` is a stub that exits 0 |
| Convert workspace file to LF / CRLF scripts | work | command `sh` so they run natively; they need `dos2unix` on the Linux side |
| Open in VS Code script | works | `bash` runs natively, so it finds the Linux `code` |
| Repository hooks | work | run by native git under your Linux shell |
| Gource, AutoCompileSubmodules plugins | missing programs | need `gource.exe` / msbuild in the prefix |
| Merge tool, diff tool | work | native tools through the bridge; set them in your Linux git config (section 6) |
| Credential manager | not needed | remotes go through native git with your Linux helpers (section 9) |

Smoke checklist after a Wine, prefix or git upgrade: open the
Console tab and switch windows; Edit commit on a throwaway branch; Open
with... on a file; `git lfs version` from the Console tab; resolve a conflict
with the merge tool and open a file with the diff tool; fetch from a remote;
the dashboard's recent repositories. `GITEXT_GIT_BRIDGE_LOG=<file>` in the
launcher's environment lists every command the bridge ran.

## 12. Releases, updating, and a refresh script

`.github/workflows/wine-release.yml` builds the whole Wine distribution on a
Linux runner: a push to `wine-support` builds the portable app archive and a
Linux tarball (the daemon, the relay, the launcher and helper scripts, this
guide) and keeps them as workflow artifacts; a tag `wine-v<n>` publishes them
as a GitHub release with a checksum file.

The tag carries only `<n>`, the fork's build number. The rest of the version
comes from `BUILD_VERSION_BASE` in `.github/workflows/_app-build-core.yml`,
which is upstream's own source of truth for it, so a rebase onto upstream
carries it forward instead of leaving a number behind that nobody remembers to
change: `wine-v3` on a 7.3.0 base builds 7.3.0.3. A tag that is not a bare
number fails the job rather than guessing. The numeric version stays four
plain numbers, while the informational version and the archive names also
carry `-dev` for as long as the upstream base is one upstream has not
released, and `-wine` always, because the build is never plain upstream --
`7.3.0.3-dev-wine`. The release notes are generated from the patch series
itself, the commits between the upstream base and the tag, because GitHub's
generated notes infer a range from the previous tag and that stops meaning
anything once the branch has been rebased past it. Nothing in it comes from an
official archive and nothing needs Windows. The launcher, `eng/wine/gitext-wine`, finds the app,
the daemon and the scripts next to itself, so the tarball unpacks into one
directory and a symlink on `PATH` is the install. The tarball also carries
the desktop entry (`gitext-wine.desktop`, which declares `inode/directory` so
file managers offer "Open with Git Extensions" on folders) and the logo in
the hicolor sizes; they go to `~/.local/share/applications` and
`~/.local/share/icons/hicolor/<size>x<size>/apps/gitext-wine.png`.

### Installing from a release

`eng/wine/install.sh`, also shipped in the Linux tarball, does the whole
setup from a release and needs only Wine 10 or later, `curl` or `wget`,
`unzip` or `bsdtar`, `tar`, fontconfig and `sha256sum`; nothing needs root:

```
curl -fsSL https://raw.githubusercontent.com/craig-b/gitextensions/wine-support/eng/wine/install.sh | sh
```

It finds the latest `wine-v*` release through the `releases/latest` redirect
and the fixed-name `SHA256SUMS` file, so it never touches the GitHub API;
downloads and verifies both archives into `~/.cache/gitext-wine`; unpacks
them into `~/.local/opt/gitext-wine`, keeping your settings and window
positions across updates; writes `fonts.conf` from the directories fontconfig
reports for DejaVu, Liberation and Hack; creates the prefix with the Mono and
Gecko prompts suppressed and the slim fonts visible from the first Wine
command; resets the font registry keys, adds the Tahoma glyph link and the
Consolas substitute (Hack when present, else DejaVu Sans Mono); unpacks the
.NET desktop runtime from Microsoft's zip into `Program Files\dotnet`, where
the apphost looks by default, so no installer runs under Wine; links the
launcher into `~/.local/bin`; and writes the menu entry and icons. Re-running
it updates to the latest release or repairs what is missing; `install.sh
prefix` redoes only the prefix; `install.sh uninstall` removes everything but
the prefix, `--purge` that too. `--tag`, `--dir`, `--prefix`, `--bin`,
`--from DIR` (offline, from a directory holding the archives) and
`--no-desktop` cover the rest.

### Updating from inside the app

Help &rarr; Check for updates asks this fork which release is newest, by
following the redirect from `releases/latest` exactly as `install.sh` does. It
is one request and no API call, which matters: the check this replaced spent
five unauthenticated GitHub API calls against a limit of sixty an hour, and
once that limit was reached it left the dialog searching for updates for good.
The installed release is whatever `install.sh` wrote into `VERSION`, so the
comparison is two build numbers.

Where the install came from a release and the bridge is up, the dialog offers
to perform the update. It hands off to `eng/wine/update` and closes, because
`install.sh` refuses to run while the app is up -- it replaces the directory
the app runs from, and its prefix step kills every Wine process in the prefix.
The helper waits for every window to go, runs `install.sh` from a copy taken
outside the install, and starts the app again.

Two details make that work, and both are easy to get wrong. The bridge daemon
kills the whole process tree of anything it starts as soon as its connection
closes, and the app closes it on the way out, so the helper forks itself out
of that tree rather than merely calling `setsid`, which changes the session
but not the parent. And the update overlays every file in the install, this
helper and `install.sh` among them, so both run from copies: overwriting a
running `/bin/sh` script corrupts it, because the shell reads its source by
offset as it goes.

It will not kill a window to get on with the job. If something stays open it
waits five minutes, then gives up having changed nothing, because the
alternative is discarding a half-written commit message. If `install.sh` fails
and leaves nothing whole it starts nothing and prints the command that repairs
it. Progress goes to `~/.cache/gitext-wine/update.log`.

An install made by `refresh.sh` has no `VERSION`, so it is reported as not
being from a release and is never offered an update, which is right: it would
compare as older than everything and no release could apply to it.

### The refresh script, for development


Keep the whole rebuild-and-overlay sequence in one script: stamp the version,
build with Windows targeting, `dotnet publish` the solution, `rsync` the
published portable app over the install, flip `IsPortable` back to `True`,
publish `eng/wine/git-bridge.cs`, compile the relay and copy the scripts next
to the launcher, then restore the stamped files so the tree stays clean. Running
that after every change is what made the iteration loop bearable.
