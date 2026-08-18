#!/usr/bin/env bash
# Runs the Windows portable build of Git Extensions under Wine on Linux.
#
# What this establishes (first proven 2026-08-19): the app's Windows build is fully usable
# under Wine — FormBrowse, log graph, diff viewing. It is the stopgap "app on Linux" while the
# native client track (cross-platform-plan.md §19–§20) is built, and the practical oracle for
# the plan's "diff/blame rendering identical" acceptance checks without a Windows machine.
#
# The four traps this script exists to remember:
#   1. The portable build is framework-dependent: the prefix needs the *Windows* .NET desktop
#      runtime, zip-extracted to C:\Program Files\dotnet (installer not needed).
#   2. The host's DOTNET_ROOT (/usr/share/dotnet) leaks into Wine and hijacks the apphost's
#      runtime probe — it must be overridden to the Windows path for the wine invocation.
#   3. MinGit installed at C:\Program Files\Git includes its own etc/gitconfig recursively
#      (its [include] paths are designed for MinGit being embedded *elsewhere*) — git dies with
#      "exceeded maximum include depth" until those include lines are removed. Also set
#      core.autocrlf=false there: the work tree is a Linux LF checkout.
#   4. Jump lists: Wine's taskbar COM throws NotImplementedException — handled in product code
#      since a4b291bdd; builds before that crash FormBrowse on activation.
#
# Usage:
#   eng/wine/run-under-wine.sh setup <app-dir>   # one-time: runtime + MinGit into <app-dir>/wineprefix
#   eng/wine/run-under-wine.sh run   <app-dir> [repo-path]   # launch (detached systemd user unit)
#   eng/wine/run-under-wine.sh stop
#
# <app-dir> holds the extracted GitExtensions-Portable-*.zip (e.g. from the PR Build artifact).

set -euo pipefail

DOTNET_VERSION=10.0.11
DOTNET_BASE=https://builds.dotnet.microsoft.com/dotnet
UNIT=gitext-wine

cmd=${1:?setup|run|stop}

if [[ $cmd == stop ]]; then
    systemctl --user kill -s SIGKILL $UNIT 2>/dev/null || true
    systemctl --user reset-failed $UNIT 2>/dev/null || true
    exit 0
fi

appdir=$(realpath "${2:?app dir with extracted portable build}")
prefix="$appdir/wineprefix"

if [[ $cmd == setup ]]; then
    export WINEPREFIX="$prefix"
    WINEDEBUG=-all wine wineboot -i

    dotnet_dir="$prefix/drive_c/Program Files/dotnet"
    mkdir -p "$dotnet_dir"
    curl -sL "$DOTNET_BASE/Runtime/$DOTNET_VERSION/dotnet-runtime-$DOTNET_VERSION-win-x64.zip" -o /tmp/ge-rt.zip
    curl -sL "$DOTNET_BASE/WindowsDesktop/$DOTNET_VERSION/windowsdesktop-runtime-$DOTNET_VERSION-win-x64.zip" -o /tmp/ge-wdr.zip
    unzip -qo /tmp/ge-rt.zip -d "$dotnet_dir"
    unzip -qo /tmp/ge-wdr.zip -d "$dotnet_dir"

    git_dir="$prefix/drive_c/Program Files/Git"
    mkdir -p "$git_dir"
    mingit_url=$(curl -s https://api.github.com/repos/git-for-windows/git/releases/latest |
        python3 -c "import json,sys; print(next(a['browser_download_url'] for a in json.load(sys.stdin)['assets'] if a['name'].startswith('MinGit-') and a['name'].endswith('-64-bit.zip') and 'busybox' not in a['name']))")
    curl -sL "$mingit_url" -o /tmp/ge-mingit.zip
    unzip -qo /tmp/ge-mingit.zip -d "$git_dir"

    # Trap 3: neutralize the self-referential include and the CRLF default.
    python3 - "$git_dir/etc/gitconfig" <<'EOF'
import re, sys
p = sys.argv[1]
s = open(p).read()
s = re.sub(r'\[include\][^\[]*', '', s)
s = s.replace('autocrlf = true', 'autocrlf = false')
open(p, 'w').write(s)
EOF

    # Preseed the portable settings so the "locate git" dialog never shows.
    printf '\xef\xbb\xbf<?xml version="1.0" encoding="utf-8"?>\n<dictionary>\n  <item>\n    <key>\n      <string>gitcommand</string>\n    </key>\n    <value>\n      <string>C:\\Program Files\\Git\\cmd\\git.exe</string>\n    </value>\n  </item>\n  <item>\n    <key>\n      <string>TelemetryEnabled</string>\n    </key>\n    <value>\n      <string>false</string>\n    </value>\n  </item>\n</dictionary>\n' > "$appdir/GitExtensions.settings"

    echo "setup done: $prefix"
    exit 0
fi

if [[ $cmd == run ]]; then
    repo=${3:-}
    winrepo=""
    if [[ -n $repo ]]; then
        winrepo="Z:$(realpath "$repo" | tr / '\\')"
    fi
    systemctl --user kill -s SIGKILL $UNIT 2>/dev/null || true
    systemctl --user reset-failed $UNIT 2>/dev/null || true
    # Trap 2: DOTNET_ROOT must point INSIDE the prefix, not at the Linux dotnet.
    systemd-run --user --collect --unit=$UNIT \
        --setenv=WINEPREFIX="$prefix" \
        --setenv=WINEDEBUG=-all \
        --setenv=DOTNET_ROOT='C:\Program Files\dotnet' \
        --working-directory="$appdir" \
        wine GitExtensions.exe ${winrepo:+browse "$winrepo"}
    echo "launched as user unit '$UNIT' (stop with: $0 stop)"
    exit 0
fi

echo "unknown command: $cmd" >&2
exit 1
