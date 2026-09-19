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
- **No apphost, no native code.** The Linux build produces no
  `GitExtensions.exe`, and the shell extension and SSH askpass helpers are C++
  projects that need MSVC. Take `GitExtensions.exe`, `BugReporter.exe`, the
  `GitExtensionsShellEx*.dll` files and the `ConEmu` folder from the official
  portable zip of the same version and overlay the managed build output on
  top. The apphost only launches `GitExtensions.dll`, so it does not care that
  the DLL was rebuilt.
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
- **Existing installs elsewhere.** A copy installed through Bottles or another
  prefix has its own prefix, config and menu entry. Nothing configured in your
  prefix applies to it, and the two look identical in a window list.

## 4. Git inside the prefix

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

## 6. Using native Linux tools from git-under-Wine

With the git bridge from section 8, `difftool` and the app's diff and blame
run through native git, which launches native tools directly. The plumbing
below is what the Windows git still needs, for `mergetool` and the Console tab.

You will want the native kdiff3, meld or whatever rather than a Windows port.
The plumbing:

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
Linux side, `eng/wine/git-bridge.py`, which runs native git. One git command
is then a localhost round trip of a few milliseconds instead of a 0.3 s
process start; the nine commands the app fires when it opens a repository took
56 ms together.

- **Wiring.** The launcher picks a free port and a random token, starts the
  daemon with them in `GITEXT_GIT_BRIDGE_PORT` and `GITEXT_GIT_BRIDGE_TOKEN`,
  runs the app, and kills the daemon afterwards. The daemon also exits when its
  parent goes away. The app bridges only the git executable, only when it would
  not create a console window, and only when both variables are set;
  `GITEXT_GIT_BRIDGE=0` in the launcher's environment turns it off.
- **Protocol.** One JSON header line with the token, working directory,
  argument list, forwarded environment and whether stdin follows, then framed
  bytes both ways (a type byte plus a little-endian length). The argument
  string is split with `CommandLineToArgvW`, the rules git.exe would have
  applied. Closing the connection kills the git process.
- **Paths.** The daemon translates the working directory and any argument that
  looks like `X:\...` or `--opt=X:\...` through the prefix's `dosdevices`
  links, and maps absolute paths back to drive form in the output of
  `rev-parse` and `worktree`. Relative paths pass through untouched. Nothing
  else in git's output is translated, so far.
- **Environment.** `GIT_*` and `DFT_*` variables set by the app are forwarded,
  except those that carry Windows paths (`GIT_SSH`, `GIT_EDITOR`,
  `GIT_SEQUENCE_EDITOR`, `GIT_ASKPASS`, `GIT_EXEC_PATH`). `HOME` is not, so
  native git reads your Linux configuration, identity and credential helpers.
  The daemon sets `GIT_EDITOR` to `git-bridge-editor`, which converts the file
  path and runs the app's own editor under Wine, and `LC_MESSAGES=C` so the
  messages the app parses stay in English.
- **What still runs the Windows git.** Calls that need a console window
  (`add --patch`, `checkout -p`, `mergetool`) and the Console tab. Direct
  `CreateProcess` of a Linux binary is not an alternative: Wine's
  `fork_and_exec` in `ntdll/unix/process.c` returns no process handle, closes
  stdin and stdout when the parent has no console or passes
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

With the git bridge from section 8, fetch, pull and push run through native
git with your Linux credential helpers, SSH keys and known hosts. The notes
below apply to the Windows git in the prefix.

- **Git Credential Manager does not work.** MinGit's system config sets
  `credential.helper=manager`, which talks to the Windows credential store.
  Under Wine that fails with "Failed to enumerate credentials [0x3ec]" and
  git falls back to a username prompt it cannot show. Override the helper in
  the prefix's global config: an empty first `credential.helper` entry resets
  the list, then add one that works. A tiny script that answers with a token
  from a file on the Linux side keeps the secret out of the prefix.
- **SSH works.** The msys `ssh.exe` that MinGit ships runs under Wine, reads a
  key straight off the Z: drive, and `git ls-remote` over SSH succeeds. Set
  `core.sshCommand` to `ssh -i Z:/path/to/key` in the prefix's global config.
  Known hosts live in the Wine user's profile, not your Linux `~/.ssh`.

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
| Git LFS | works | `git-lfs.exe` (release zip, checksum verified) dropped into `Git\cmd` |
| Open, Open with... | works | routed through `winebrowser` to `xdg-open` in code (Wine only) |
| Show in folder | expected to work | Wine's explorer implements `/select,` |
| Batch user scripts (`cmd`) | works | Wine cmd handles the generated `.cmd` |
| Git GUI, GitK (Tools menu) | missing | MinGit ships neither, and no Tcl/Tk |
| GPG tab on signed commits | error only | no `gpg.exe` in MinGit; unsigned commits show nothing, correctly |
| PowerShell user scripts | silently do nothing | Wine's `powershell.exe` is a stub that exits 0 |
| Convert workspace file to LF / CRLF scripts | works | edited in the portable settings: command `sh.exe`, arguments `dos2unix` / `unix2dos` without `.exe` (BusyBox resolves applets by bare name) |
| Open in VS Code script | fails | calls `bash`, and there is no `code` in the prefix |
| Repository hooks | depend on the hook | they run under BusyBox ash, not bash |
| Gource, AutoCompileSubmodules plugins | missing programs | need `gource.exe` / msbuild in the prefix |
| Credential manager | unusable | see section 9 |

Smoke checklist after a Wine, prefix or MinGit upgrade: open the Console tab
and switch windows; Edit commit on a throwaway branch; Open with... on a
file; `git lfs version` from the Console tab; resolve a conflict with kdiff3;
clone over SSH; the dashboard's recent repositories.

## 12. A refresh script

Keep the whole rebuild-and-overlay sequence in one script: stamp the version,
build with Windows targeting, `rsync` the output over the install excluding
the translation tool, XML docs, PDBs and the Linux apphosts, flip `IsPortable`
back to `True`, copy `eng/wine/git-bridge.py` and `git-bridge-editor` next to
the launcher, then restore the stamped files so the tree stays clean. Running
that after every change is what made the iteration loop bearable.
