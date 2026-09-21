#!/bin/sh
# Installs Git Extensions for Wine from a release of github.com/craig-b/gitextensions:
# the portable app, the Linux-side pieces (native git bridge daemon, terminal relay, launcher,
# helper scripts), a slim font configuration, a Wine prefix with the .NET desktop runtime and
# the registry entries the app needs, a launcher on PATH, a menu entry and icons.
#
# Everything lives under the user's home; nothing needs root. Re-running updates or repairs.

set -eu

# Held here rather than read back out of the file with sed, because the documented way to run this
# is `curl ... | sh -s -- --help`, where the script is a pipe and $0 is not a readable file.
usage() {
  cat <<'USAGE'
Usage: sh install.sh [options] [command]

Commands
  install       download the latest wine-v* release and install or update everything (default)
  prefix        only create or repair the Wine prefix (fonts, registry, .NET runtime)
  uninstall     remove the install, the launcher, the menu entry and icons; --purge removes the prefix too

Options
  --tag TAG         install this release tag instead of the latest, e.g. wine-v3
  --from DIR        install from a directory holding the two release archives and SHA256SUMS (offline)
  --dir DIR         install directory      (default: ~/.local/opt/gitext-wine)
  --prefix DIR      Wine prefix            (default: ~/.local/share/wineprefixes/gitext)
  --bin DIR         directory for the launcher symlink (default: ~/.local/bin)
  --no-desktop      do not write the menu entry and icons
  --force           reinstall even when this release is already installed
  --purge           with uninstall: also delete the Wine prefix

Needs: wine 10 or later, curl or wget, unzip or bsdtar, tar, fontconfig (fc-list), sha256sum.
USAGE
}

REPO=${GITEXT_REPO:-craig-b/gitextensions}
INSTALL_DIR=${GITEXT_INSTALL_DIR:-$HOME/.local/opt/gitext-wine}
PREFIX=${GITEXT_WINEPREFIX:-${XDG_DATA_HOME:-$HOME/.local/share}/wineprefixes/gitext}
BIN_DIR=${GITEXT_BIN_DIR:-$HOME/.local/bin}
CACHE_DIR=${XDG_CACHE_HOME:-$HOME/.cache}/gitext-wine
WINE_MIN_MAJOR=10
TAG=
FROM_DIR=
DESKTOP=1
FORCE=0
PURGE=0
COMMAND=install

# ---- plumbing ---------------------------------------------------------------------------------

say()  { printf '%s\n' "$*"; }
step() { printf '\n==> %s\n' "$*"; }
die()  { printf 'install.sh: %s\n' "$*" >&2; exit 1; }
have() { command -v "$1" >/dev/null 2>&1; }

download() { # url file
  if have curl; then curl -fsSL --retry 3 -o "$2" "$1"
  elif have wget; then wget -q -O "$2" "$1"
  else die "need curl or wget"
  fi
}

redirect_target() { # url -> prints the Location a request is redirected to
  if have curl; then curl -fsSI -o /dev/null -w '%{redirect_url}' "$1"
  else wget -q --max-redirect=0 -S -O /dev/null "$1" 2>&1 | sed -n 's/^ *Location: *//p' | head -1
  fi
}

extract_zip() { # zip dir
  if have unzip; then unzip -q -o "$1" -d "$2"
  elif have bsdtar; then bsdtar -xf "$1" -C "$2"
  else die "need unzip or bsdtar"
  fi
}

wine_major() {
  wine --version 2>/dev/null | sed -n 's/^wine-\([0-9][0-9]*\).*/\1/p'
}

check_tools() {
  have wine || die "wine is not installed. Debian and Ubuntu ship old versions; WineHQ's repository (https://wiki.winehq.org/Download) has current ones."
  major=$(wine_major)
  [ -n "$major" ] || die "cannot read the Wine version from 'wine --version'"
  if [ "$major" -lt "$WINE_MIN_MAJOR" ] && [ -z "${GITEXT_WINE_ANY:-}" ]; then
    die "Wine $major found; $WINE_MIN_MAJOR or later is needed (only 11 has been tested). Set GITEXT_WINE_ANY=1 to try anyway."
  fi
  have curl || have wget || die "need curl or wget"
  have unzip || have bsdtar || die "need unzip or bsdtar"
  have tar || die "need tar"
  have fc-list || die "need fontconfig (fc-list)"
  have sha256sum || have shasum || die "need sha256sum"
  have readlink || die "need readlink"
}

# True while any process still holds something in the install. Matching on the name and the working
# directory was not enough: pgrep is not among the tools checked for above, so where it is missing the
# guard used to vanish silently and let the install overwrite a running app; Wine shortens command
# lines; and an instance started by git-bridge-editor has the repository as its working directory, not
# the app. Asking /proc what is mapped, executing and current catches every one of those, including
# the bridge daemon, whose running binary is what makes the copy below fail with ETXTBSY.
app_running() {
  ROOT=$INSTALL_DIR
  # One pass over every maps file, then one over the symlinks, rather than a grep and three readlinks
  # per process: with several hundred processes the forks dominate and a poll takes seconds, which is
  # how a wait bound stops meaning what it says.
  grep -lsF -- " $ROOT/" /proc/[0-9]*/maps >/dev/null 2>&1 && return 0
  ls -l /proc/[0-9]*/cwd /proc/[0-9]*/exe /proc/[0-9]*/root 2>/dev/null | grep -qF " -> $ROOT/" && return 0
  return 1
}

# ---- release ----------------------------------------------------------------------------------

resolve_tag() {
  if [ -n "$TAG" ]; then return; fi
  target=$(redirect_target "https://github.com/$REPO/releases/latest")
  TAG=${target##*/}
  case "$TAG" in wine-v*) ;; *) die "the latest release of $REPO is '$TAG', not a wine-v* release; pass --tag" ;; esac
}

# Downloads SHA256SUMS for the tag (a fixed name, so no API is needed), then the two archives it lists,
# into the cache, and verifies them. Sets APP_ZIP and LINUX_TAR.
fetch_release() {
  RELEASE_DIR=$CACHE_DIR/$TAG
  mkdir -p "$RELEASE_DIR"
  base="https://github.com/$REPO/releases/download/$TAG"
  if [ -n "$FROM_DIR" ]; then
    RELEASE_DIR=$FROM_DIR
  else
    [ -s "$RELEASE_DIR/SHA256SUMS" ] || download "$base/SHA256SUMS" "$RELEASE_DIR/SHA256SUMS"
  fi
  [ -s "$RELEASE_DIR/SHA256SUMS" ] || die "no SHA256SUMS in $RELEASE_DIR"
  APP_ZIP=$RELEASE_DIR/$(awk '/GitExtensions-Portable-x64-.*\.zip$/ {print $2; exit}' "$RELEASE_DIR/SHA256SUMS")
  LINUX_TAR=$RELEASE_DIR/$(awk '/gitext-wine-linux-x64-.*\.tar\.gz$/ {print $2; exit}' "$RELEASE_DIR/SHA256SUMS")
  for f in "$APP_ZIP" "$LINUX_TAR"; do
    name=$(basename "$f")
    if [ ! -s "$f" ]; then
      [ -z "$FROM_DIR" ] || die "$name is missing from $FROM_DIR"
      say "downloading $name"
      download "$base/$name" "$f.part" && mv "$f.part" "$f"
    fi
  done
  (cd "$RELEASE_DIR" && if have sha256sum; then sha256sum -c --quiet SHA256SUMS; else shasum -a 256 -c SHA256SUMS >/dev/null; fi) \
    || die "checksum mismatch in $RELEASE_DIR; delete it and run again"
  say "release $TAG verified"
}

# ---- files ------------------------------------------------------------------------------------

install_files() {
  mkdir -p "$INSTALL_DIR"
  tmp=$(mktemp -d "${TMPDIR:-/tmp}/gitext-install.XXXXXX")
  trap 'rm -rf "$tmp"' EXIT

  say "unpacking the Linux pieces"
  tar -xzf "$LINUX_TAR" -C "$tmp"
  # Copy beside each file and rename over it, rather than copying onto it. cp opens the destination
  # truncating, which fails with ETXTBSY on a running binary (the bridge daemon) and corrupts a running
  # /bin/sh script (this script, the launcher), since sh reads its source by offset as it goes.
  # rename(2) over a running file is always safe: it replaces the name, never the open inode.
  (cd "$tmp/gitext-wine" && find . -type f -print) | while IFS= read -r rel; do
    rel=${rel#./}
    mkdir -p "$INSTALL_DIR/$(dirname "$rel")"
    cp -p "$tmp/gitext-wine/$rel" "$INSTALL_DIR/$rel.new"
    mv -f "$INSTALL_DIR/$rel.new" "$INSTALL_DIR/$rel"
  done

  say "unpacking the app"
  rm -rf "$INSTALL_DIR/app.new"
  mkdir -p "$INSTALL_DIR/app.new"
  extract_zip "$APP_ZIP" "$INSTALL_DIR/app.new"
  # the user's own files survive an update
  for keep in GitExtensions.settings GitExtensions.settings.backup WindowPositions.xml; do
    [ -f "$INSTALL_DIR/app/$keep" ] && cp -p "$INSTALL_DIR/app/$keep" "$INSTALL_DIR/app.new/$keep"
  done
  rm -rf "$INSTALL_DIR/app.old"
  [ -d "$INSTALL_DIR/app" ] && mv "$INSTALL_DIR/app" "$INSTALL_DIR/app.old"
  # Between these two renames there is no app directory at all. Put the old one back if the second
  # fails, because a plain re-run cannot recover from it: it deletes app.new, finds no app to copy the
  # settings out of, and then deletes app.old with them still in it.
  if ! mv "$INSTALL_DIR/app.new" "$INSTALL_DIR/app"; then
    [ -d "$INSTALL_DIR/app.old" ] && mv "$INSTALL_DIR/app.old" "$INSTALL_DIR/app"
    die "could not put the new app in place; the previous one is still installed"
  fi
  rm -rf "$INSTALL_DIR/app.old"

  seed_settings
  printf '%s\n' "$TAG" > "$INSTALL_DIR/VERSION"
  rm -rf "$tmp"
  trap - EXIT
}

# A first install gets sensible portable settings; an existing file is never touched.
seed_settings() {
  f=$INSTALL_DIR/app/GitExtensions.settings
  [ -f "$f" ] && return
  mono=$(pick_mono_font)
  cat > "$f" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<dictionary>
  <item><key><string>gitcommand</string></key><value><string>git</string></value></item>
  <item><key><string>TelemetryEnabled</string></key><value><string>false</string></value></item>
  <item><key><string>translation</string></key><value><string>English</string></value></item>
  <item><key><string>monospacefont</string></key><value><string>$mono;9;_IC_;0;0</string></value></item>
  <item><key><string>difffont</string></key><value><string>$mono;9.75;_IC_;0;0</string></value></item>
  <item><key><string>conemuconsolefont</string></key><value><string>$mono;10;_IC_;0;0</string></value></item>
</dictionary>
EOF
  say "seeded portable settings (git on the bridge, $mono for code)"
}

install_launcher() {
  # the launcher defaults to the same prefix as this script; a different one is recorded for it
  default_prefix=${XDG_DATA_HOME:-$HOME/.local/share}/wineprefixes/gitext
  if [ "$PREFIX" != "$default_prefix" ]; then
    printf 'GITEXT_WINEPREFIX=%s\nexport GITEXT_WINEPREFIX\n' "$PREFIX" > "$INSTALL_DIR/gitext-wine.env"
  else
    rm -f "$INSTALL_DIR/gitext-wine.env"
  fi
  mkdir -p "$BIN_DIR"
  ln -sf "$INSTALL_DIR/gitext-wine" "$BIN_DIR/gitext-wine"
  case ":$PATH:" in *":$BIN_DIR:"*) ;; *) say "note: $BIN_DIR is not on PATH in this shell; log in again or add it" ;; esac
}

# An update has to reproduce the choices this install was made with. Only the prefix was recorded,
# and only for the launcher, so updating an install made with --dir used to put a second copy in the
# default location and leave this one untouched. Record all of them where an updater can read them.
record_install() {
  cat > "$INSTALL_DIR/install.env" <<EOF
GITEXT_INSTALL_DIR=$INSTALL_DIR
GITEXT_WINEPREFIX=$PREFIX
GITEXT_BIN_DIR=$BIN_DIR
GITEXT_DESKTOP=$DESKTOP
EOF
}

install_desktop() {
  [ "$DESKTOP" = 1 ] || return 0
  apps=${XDG_DATA_HOME:-$HOME/.local/share}/applications
  icons=${XDG_DATA_HOME:-$HOME/.local/share}/icons/hicolor
  mkdir -p "$apps"
  # the launcher is named by its absolute path so the entry works before PATH is updated
  if [ -f "$INSTALL_DIR/gitext-wine.desktop" ]; then
    sed "s|^Exec=gitext-wine |Exec=$INSTALL_DIR/gitext-wine |" "$INSTALL_DIR/gitext-wine.desktop" > "$apps/gitext-wine.desktop"
  else
    # a release from before the entry was shipped
    cat > "$apps/gitext-wine.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=Git Extensions (Wine)
Comment=Git Extensions running under Wine, with native git
Exec=$INSTALL_DIR/gitext-wine browse %f
Icon=gitext-wine
Terminal=false
Categories=Development;RevisionControl;
MimeType=inode/directory;
StartupWMClass=gitextensions.exe
EOF
  fi
  for png in "$INSTALL_DIR"/icons/gitext-wine-*.png; do
    [ -f "$png" ] || continue
    size=${png##*-}; size=${size%.png}
    mkdir -p "$icons/${size}x${size}/apps"
    cp "$png" "$icons/${size}x${size}/apps/gitext-wine.png"
  done
  have update-desktop-database && update-desktop-database "$apps" 2>/dev/null || true
  have gtk-update-icon-cache && gtk-update-icon-cache -q "$icons" 2>/dev/null || true
  say "menu entry and icons installed"
}

# ---- fonts ------------------------------------------------------------------------------------

font_dirs() { # prints the directories fontconfig has the wanted families in
  for family in "DejaVu Sans" "DejaVu Sans Mono" "Liberation Sans" "Liberation Mono" "Hack"; do
    fc-list --format='%{file}\n' ":family=$family" 2>/dev/null
  done | sed 's|/[^/]*$||' | sort -u
}

pick_mono_font() {
  if [ -n "$(fc-list ':family=Hack' 2>/dev/null)" ]; then printf 'Hack'; else printf 'DejaVu Sans Mono'; fi
}

pick_ui_font_link() { # file,family for the Tahoma font link
  if [ -n "$(fc-list ':family=DejaVu Sans' 2>/dev/null)" ]; then printf 'DejaVuSans.ttf,DejaVu Sans'; else printf 'LiberationSans-Regular.ttf,Liberation Sans'; fi
}

write_fonts_conf() {
  dirs=$(font_dirs)
  [ -n "$dirs" ] || die "fontconfig has none of DejaVu, Liberation or Hack; install fonts-dejavu (or ttf-dejavu) first"
  {
    printf '<?xml version="1.0"?>\n<!DOCTYPE fontconfig SYSTEM "fonts.dtd">\n'
    printf '<!-- Written by install.sh. Every Windows process under Wine enumerates fontconfig fonts at startup;\n     only these directories are visible to the Git Extensions prefix. -->\n<fontconfig>\n'
    printf '%s\n' "$dirs" | sed 's|.*|  <dir>&</dir>|'
    printf '  <cachedir>%s</cachedir>\n' "${XDG_CACHE_HOME:-$HOME/.cache}/fontconfig-gitext-wine"
    printf '  <include ignore_missing="yes">/etc/fonts/conf.d</include>\n</fontconfig>\n'
  } > "$INSTALL_DIR/fonts.conf"
  say "fonts.conf lists $(printf '%s\n' "$dirs" | wc -l) font directories"
}

# ---- prefix -----------------------------------------------------------------------------------

# Wine sees only the slim font set from the very first command, so the prefix never registers the full set.
wine_env() {
  export WINEPREFIX="$PREFIX" WINEARCH=win64 WINEDEBUG=-all FONTCONFIG_FILE="$INSTALL_DIR/fonts.conf"
  export WINEDLLOVERRIDES="mscoree,mshtml="   # no Mono or Gecko prompts; the app brings its own runtime
}

bootstrap_prefix() {
  wine_env
  if [ ! -f "$PREFIX/system.reg" ]; then
    say "creating the Wine prefix at $PREFIX"
    mkdir -p "$PREFIX"
    wineboot -i >/dev/null 2>&1
    wineserver -w
  fi

  say "font registry: reset, Tahoma glyph link, Consolas substitute"
  # entries for fonts fontconfig no longer lists are never pruned by Wine; a prefix that ever saw the
  # full set keeps them and every process pays for loading them
  wine reg delete 'HKCU\Software\Wine\Fonts\External Fonts' /f >/dev/null 2>&1 || true
  wine reg delete 'HKLM\Software\Microsoft\Windows NT\CurrentVersion\Fonts' /f >/dev/null 2>&1 || true
  wine reg delete 'HKLM\Software\Microsoft\Windows\CurrentVersion\Fonts' /f >/dev/null 2>&1 || true
  # the UI font is Wine's Tahoma, which lacks the arrows the app draws; borrow missing glyphs, keep the look
  wine reg add 'HKLM\Software\Microsoft\Windows NT\CurrentVersion\FontLink\SystemLink' /v Tahoma /t REG_MULTI_SZ /d "$(pick_ui_font_link)" /f >/dev/null 2>&1
  # the app defaults its code fonts to Consolas, which is absent; a proportional stand-in wrecks ConEmu's cell width
  mono=$(pick_mono_font)
  wine reg add 'HKLM\Software\Microsoft\Windows NT\CurrentVersion\FontSubstitutes' /v Consolas /t REG_SZ /d "$mono" /f >/dev/null 2>&1
  wine reg add 'HKLM\Software\Microsoft\Windows NT\CurrentVersion\FontSubstitutes' /v Consolas /t REG_SZ /d "$mono" /f /reg:32 >/dev/null 2>&1
  wineserver -k >/dev/null 2>&1 || true
  wineserver -w

  install_dotnet
}

# The desktop runtime, unpacked from Microsoft's zip into the default location the apphost looks in.
# Running the Windows installer under Wine works too, but this has no installer to go wrong.
install_dotnet() {
  channel=$(sed -n 's/.*"version": *"\([0-9]*\.[0-9]*\)\.[0-9]*".*/\1/p' "$INSTALL_DIR/app/GitExtensions.runtimeconfig.json" | head -1)
  [ -n "$channel" ] || channel=10.0
  dotnet_root="$PREFIX/drive_c/Program Files/dotnet"
  if [ -d "$dotnet_root/shared/Microsoft.WindowsDesktop.App" ] && ls "$dotnet_root/shared/Microsoft.WindowsDesktop.App" | grep -q "^$channel\."; then
    say ".NET desktop runtime $channel present"
    return
  fi
  version=${GITEXT_DOTNET_VERSION:-}
  if [ -z "$version" ]; then
    mkdir -p "$CACHE_DIR"
    download "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$channel/releases.json" "$CACHE_DIR/releases-$channel.json"
    version=$(sed -n 's/.*"latest-runtime": *"\([^"]*\)".*/\1/p' "$CACHE_DIR/releases-$channel.json" | head -1)
  fi
  [ -n "$version" ] || die "cannot determine the latest .NET $channel runtime version; set GITEXT_DOTNET_VERSION"
  zip="$CACHE_DIR/windowsdesktop-runtime-$version-win-x64.zip"
  if [ ! -s "$zip" ]; then
    say "downloading the .NET desktop runtime $version (about 60 MB)"
    download "https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/$version/windowsdesktop-runtime-$version-win-x64.zip" "$zip.part" && mv "$zip.part" "$zip"
  fi
  mkdir -p "$dotnet_root"
  extract_zip "$zip" "$dotnet_root"
  say ".NET desktop runtime $version installed into the prefix"
}

# ---- commands ---------------------------------------------------------------------------------

do_install() {
  check_tools
  app_running && die "Git Extensions is running from $INSTALL_DIR; close it first"
  step "Release"
  resolve_tag
  if [ -f "$INSTALL_DIR/VERSION" ] && [ "$(cat "$INSTALL_DIR/VERSION")" = "$TAG" ] && [ "$FORCE" = 0 ]; then
    say "$TAG is already installed in $INSTALL_DIR (use --force to reinstall); checking the rest"
  else
    fetch_release
    step "Files"
    install_files
  fi
  step "Fonts"
  write_fonts_conf
  step "Prefix"
  bootstrap_prefix
  step "Launcher and menu"
  install_launcher
  install_desktop
  record_install
  step "Done"
  say "Git Extensions $TAG for Wine is installed."
  say "  run:      gitext-wine [browse] [repository]"
  say "  install:  $INSTALL_DIR"
  say "  prefix:   $PREFIX"
  say "  guide:    $INSTALL_DIR/running-under-wine.md"
}

do_prefix() {
  check_tools
  [ -f "$INSTALL_DIR/app/GitExtensions.runtimeconfig.json" ] || die "no app in $INSTALL_DIR; run install first"
  app_running && die "Git Extensions is running from $INSTALL_DIR; close it first"
  step "Fonts"
  write_fonts_conf
  step "Prefix"
  bootstrap_prefix
}

do_uninstall() {
  app_running && die "Git Extensions is running from $INSTALL_DIR; close it first"
  rm -f "$BIN_DIR/gitext-wine"
  rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/applications/gitext-wine.desktop"
  rm -f "${XDG_DATA_HOME:-$HOME/.local/share}"/icons/hicolor/*/apps/gitext-wine.png
  rm -rf "$INSTALL_DIR"
  say "removed $INSTALL_DIR, the launcher and the menu entry"
  if [ "$PURGE" = 1 ]; then
    WINEPREFIX="$PREFIX" wineserver -k >/dev/null 2>&1 || true
    rm -rf "$PREFIX"
    say "removed the prefix $PREFIX"
  else
    say "the prefix $PREFIX was kept (--purge removes it)"
  fi
}

# ---- main -------------------------------------------------------------------------------------

while [ $# -gt 0 ]; do
  case "$1" in
    --tag) TAG=$2; shift ;;
    --from) FROM_DIR=$2; shift ;;
    --dir) INSTALL_DIR=$2; shift ;;
    --prefix) PREFIX=$2; shift ;;
    --bin) BIN_DIR=$2; shift ;;
    --no-desktop) DESKTOP=0 ;;
    --force) FORCE=1 ;;
    --purge) PURGE=1 ;;
    install|prefix|uninstall) COMMAND=$1 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown argument '$1' (try --help)" ;;
  esac
  shift
done

case "$COMMAND" in
  install) do_install ;;
  prefix) do_prefix ;;
  uninstall) do_uninstall ;;
esac
