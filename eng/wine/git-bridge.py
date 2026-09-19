#!/usr/bin/env python3
"""Runs native git on behalf of Git Extensions running under Wine.

The app connects to 127.0.0.1:$GITEXT_GIT_BRIDGE_PORT once per git command and
sends one JSON header line: {"token", "cwd", "args", "env", "stdin"}. Paths in
cwd and args arrive in Windows form (Z:\\var\\...) and are translated through
the prefix's dosdevices links. Frames follow, both ways, as <B type><I length>
plus payload (little endian).

  client -> daemon: 0 stdin data, 1 stdin EOF, 2 kill
  daemon -> client: 1 stdout, 2 stderr, 3 exit (int32), 4 error text, 5 pid (int32)

Closing the connection kills the git process. The daemon exits when its parent
(the launcher) goes away.
"""
import asyncio
import json
import os
import re
import signal
import struct
import sys
import time

PREFIX = os.environ["WINEPREFIX"]
PORT = int(os.environ["GITEXT_GIT_BRIDGE_PORT"])
TOKEN = os.environ["GITEXT_GIT_BRIDGE_TOKEN"]
GIT = os.environ.get("GITEXT_GIT_BRIDGE_GIT", "git")
EDITOR = os.environ.get("GITEXT_GIT_BRIDGE_EDITOR")
LOG = os.environ.get("GITEXT_GIT_BRIDGE_LOG")
PARENT = os.getppid()

FRAME = struct.Struct("<BI")

# drive letter -> unix root, longest root first for the reverse mapping
DRIVES = {}
for name in os.listdir(os.path.join(PREFIX, "dosdevices")):
    if len(name) == 2 and name[1] == ":" and name[0].isalpha():
        DRIVES[name[0].lower()] = os.path.realpath(os.path.join(PREFIX, "dosdevices", name)).rstrip("/")
DRIVES_BY_LEN = sorted(DRIVES.items(), key=lambda kv: -len(kv[1]))

# a drive-letter path anywhere in a token: not preceded by a letter or digit, known letter, then a separator
DRIVE_RE = re.compile(r"(?<![A-Za-z0-9])([" + "".join(DRIVES) + "".join(DRIVES).upper() + r"]):[\\/]")


def log(msg):
    if LOG:
        with open(LOG, "a") as f:
            f.write(f"{time.strftime('%H:%M:%S')}.{int(time.time() * 1000) % 1000:03d} {msg}\n")


def to_unix(s):
    """Rewrites every X:\\... or X:/... span in the string to its Unix form."""
    if not DRIVE_RE.search(s):
        return s
    s = s.replace("\\", "/")
    s = DRIVE_RE.sub(lambda m: DRIVES[m.group(1).lower()] + "/", s)
    return s.replace("file:////", "file:///")


def to_windows(path):
    for letter, root in DRIVES_BY_LEN:
        if path == root or path.startswith(root + "/"):
            return letter.upper() + ":" + path[len(root):]
    return path


def subcommand(args):
    """The git subcommand: the first token that is not an option or an option value."""
    skip = False
    for a in args:
        if skip:
            skip = False
        elif a in ("-c", "-C", "--git-dir", "--work-tree", "--namespace", "--exec-path", "--config-env"):
            skip = True
        elif not a.startswith("-"):
            return a
    return None


def prepare_args(args):
    args = [to_unix(a) for a in args]
    cmd = subcommand(args)
    if cmd in ("mergetool", "difftool"):
        # nobody can answer "Hit return to start merge resolution tool" over the bridge
        args = ["-c", f"{cmd}.prompt=false", *args]
    return args, cmd


def output_translator(args, cmd):
    """Returns a function that maps absolute Unix paths in git's output back to drive form,
    or None when the command does not print paths the app treats as paths."""
    if cmd == "rev-parse":
        def rev_parse(data):
            lines = data.decode("utf-8", "surrogateescape").split("\n")
            lines = [to_windows(line) if line.startswith("/") else line for line in lines]
            return "\n".join(lines).encode("utf-8", "surrogateescape")
        return rev_parse
    if cmd == "worktree" and "list" in args:
        sep = "\0" if "-z" in args else "\n"

        def worktree(data):
            fields = data.decode("utf-8", "surrogateescape").split(sep)
            fields = [("worktree " + to_windows(f[9:])) if f.startswith("worktree /")
                      else (to_windows(f.split(" ", 1)[0]) + f[len(f.split(" ", 1)[0]):]) if f.startswith("/")
                      else f for f in fields]
            return sep.join(fields).encode("utf-8", "surrogateescape")
        return worktree
    return None


class Conn:
    def __init__(self, reader, writer):
        self.reader = reader
        self.writer = writer
        self.lock = asyncio.Lock()

    async def send(self, kind, payload=b""):
        async with self.lock:
            self.writer.write(FRAME.pack(kind, len(payload)) + payload)
            await self.writer.drain()

    async def recv(self):
        header = await self.reader.readexactly(FRAME.size)
        kind, length = FRAME.unpack(header)
        payload = await self.reader.readexactly(length) if length else b""
        return kind, payload


async def pump(stream, conn, kind, translate):
    if translate is not None:
        data = await stream.read()
        if data:
            await conn.send(kind, translate(data))
        return
    while True:
        chunk = await stream.read(65536)
        if not chunk:
            return
        await conn.send(kind, chunk)


async def feed_stdin(conn, proc, want_stdin):
    """Forwards stdin frames; a kill frame or a dropped connection kills git."""
    try:
        while True:
            kind, payload = await conn.recv()
            if kind == 0 and want_stdin and proc.stdin:
                proc.stdin.write(payload)
                await proc.stdin.drain()
            elif kind == 1:
                if want_stdin and proc.stdin:
                    proc.stdin.close()
            elif kind == 2:
                kill(proc)
    except (asyncio.IncompleteReadError, ConnectionError, BrokenPipeError):
        pass


def kill(proc):
    if proc.returncode is None:
        try:
            os.killpg(proc.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass


async def handle(reader, writer):
    conn = Conn(reader, writer)
    proc = None
    feeder = None
    try:
        line = await reader.readline()
        req = json.loads(line)
        if req.get("token") != TOKEN:
            return
        cwd = to_unix(req.get("cwd") or "") or None
        args, cmd = prepare_args(req.get("args", []))
        want_stdin = bool(req.get("stdin"))
        env = dict(os.environ)
        env["LC_MESSAGES"] = "C"
        if EDITOR:
            env["GIT_EDITOR"] = EDITOR
            env.pop("GIT_SEQUENCE_EDITOR", None)
        # the app's own variables win, e.g. the sed sequence editor that rewrites a rebase todo
        env.update({k: to_unix(v) for k, v in (req.get("env") or {}).items()})
        log(f"run cwd={cwd} args={args}")
        if cwd and not os.path.isdir(cwd):
            # what git itself says for -C on a missing directory; the app handles the exit code
            await conn.send(2, f"fatal: cannot change to '{cwd}': No such file or directory\n".encode())
            await conn.send(3, struct.pack("<i", 128))
            return
        proc = await asyncio.create_subprocess_exec(
            GIT, *args, cwd=cwd, env=env,
            stdin=asyncio.subprocess.PIPE if want_stdin else asyncio.subprocess.DEVNULL,
            stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE,
            start_new_session=True)
        await conn.send(5, struct.pack("<i", proc.pid))
        feeder = asyncio.ensure_future(feed_stdin(conn, proc, want_stdin))
        await asyncio.gather(
            pump(proc.stdout, conn, 1, output_translator(args, cmd)),
            pump(proc.stderr, conn, 2, None))
        code = await proc.wait()
        await conn.send(3, struct.pack("<i", code))
    except Exception as ex:  # noqa: BLE001 - report to the client, keep serving
        log(f"error {ex!r}")
        try:
            await conn.send(4, str(ex).encode())
        except Exception:
            pass
    finally:
        if feeder:
            feeder.cancel()
        if proc is not None:
            kill(proc)
        writer.close()


async def watch_parent(server):
    while True:
        await asyncio.sleep(3)
        if os.getppid() != PARENT:
            server.close()
            return


async def main():
    server = await asyncio.start_server(handle, "127.0.0.1", PORT)
    log(f"listening on {PORT} for parent {PARENT}")
    watcher = asyncio.ensure_future(watch_parent(server))
    async with server:
        try:
            await server.serve_forever()
        except asyncio.CancelledError:
            pass
    watcher.cancel()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        pass
    sys.exit(0)
